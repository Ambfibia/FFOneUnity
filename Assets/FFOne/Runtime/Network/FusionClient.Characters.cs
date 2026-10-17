using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FFOne.Network
{
    public sealed partial class FusionClient
    {
        /// <summary>Reserve the name at the end of the name screen; appearance remains resumable.</summary>
        public async Task<CharacterInfo> ReserveCharacterNameAsync(CharacterCreation request, CancellationToken cancellationToken = default)
        {
            if (request == null || request.Style == null || request.Slot < 1 || request.Slot > 4 ||
                string.IsNullOrWhiteSpace(request.FirstName) || request.Style.Gender < 1 || request.Style.Gender > 2)
                throw new ArgumentException("Choose a valid slot, name and gender.");
            byte[] check = NameCheckPayload(request);
            int slot = request.Slot, gender = request.Style.Gender;
            long uid = request.ExistingUid;
            using (var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
            {
                await operations.WaitAsync(token.Token).ConfigureAwait(false);
                try { return await ReserveNameCoreAsync(check, slot, gender, uid, token.Token).ConfigureAwait(false); }
                catch (ProtocolException) { login?.Dispose(); throw; }
                finally { operations.Release(); }
            }
        }

        private async Task<CharacterInfo> ReserveNameCoreAsync(byte[] check, int slot, int gender, long uid, CancellationToken cancellation)
        {
            RequireLogin();
            if (IsInWorld) throw new InvalidOperationException("Return to character selection first.");
            if (uid != 0)
            {
                var pending = OwnedCharacter(uid);
                if (pending.Style.AppearanceComplete || pending.Slot != slot)
                    throw new InvalidOperationException("Only this slot's unfinished character can resume creation.");
                if (pending.FirstName != Protocol0104.ReadUtf16(check, 12, 9) || pending.LastName != Protocol0104.ReadUtf16(check, 30, 17))
                    throw new InvalidOperationException("Resume creation with the reserved character name.");
                return pending;
            }
            if (characters.Any(c => c.Slot == slot)) throw new InvalidOperationException("This character slot is occupied.");
            var response = await login.RequestAsync(PacketId.CheckName, check,
                new[] { PacketId.CheckNameSuccess, PacketId.CheckNameFailure }, ReplyTimeout, cancellation).ConfigureAwait(false);
            Reject(response, "Character name check", PacketId.CheckNameFailure);
            Expect(response, PacketId.CheckNameSuccess, 52);
            RequireName(response.Payload, 0, 18, check, 12, 30);
            byte[] save = new byte[68]; save[0] = (byte)slot; save[1] = (byte)gender;
            Buffer.BlockCopy(check, 0, save, 4, 64);
            response = await login.RequestAsync(PacketId.SaveName, save,
                new[] { PacketId.SaveNameSuccess, PacketId.SaveNameFailure, PacketId.CheckNameFailure, PacketId.ShardSelectFailure }, ReplyTimeout, cancellation).ConfigureAwait(false);
            Reject(response, "Character name save", PacketId.SaveNameFailure, PacketId.CheckNameFailure, PacketId.ShardSelectFailure);
            Expect(response, PacketId.SaveNameSuccess, 64);
            RequireName(response.Payload, 10, 28, check, 12, 30);
            if ((sbyte)response.Payload[8] != slot) throw new ProtocolException("Name-save reply slot mismatch.");
            uid = Protocol0104.Read64(response.Payload, 0);
            if (characters.Any(c => c.Uid == uid)) throw new ProtocolException("Name-save reply reused an existing UID.");
            var reserved = new CharacterInfo
            {
                Uid = uid, Slot = slot, FirstName = Protocol0104.ReadUtf16(response.Payload, 10, 9),
                LastName = Protocol0104.ReadUtf16(response.Payload, 28, 17),
                Style = new CharacterStyle { Gender = (sbyte)response.Payload[9], AppearanceComplete = false }
            };
            characters = characters.Concat(new[] { reserved }).OrderBy(c => c.Slot).ToArray();
            SelectedSlot = slot;
            return reserved;
        }

        public async Task<CharacterInfo> CreateCharacterAsync(CharacterCreation request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            ValidateCreation(request);
            // Snapshot UI intent before yielding; later control edits cannot alter the transaction.
            byte[] check = NameCheckPayload(request), appearance = AppearancePayload(request);
            int slot = request.Slot, gender = request.Style.Gender;
            long uid = request.ExistingUid;
            using (var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
            {
                await operations.WaitAsync(token.Token).ConfigureAwait(false);
                try
                {
                    var pendingCharacter = await ReserveNameCoreAsync(check, slot, gender, uid, token.Token).ConfigureAwait(false);
                    uid = pendingCharacter.Uid;
                    Protocol0104.Write64(appearance, 0, uid);
                    var created = await login.RequestAsync(PacketId.Create, appearance,
                        new[] { PacketId.CreateSuccess, PacketId.CreateFailure, PacketId.ShardSelectFailure }, ReplyTimeout, token.Token).ConfigureAwait(false);
                    Reject(created, "Character creation", PacketId.CreateFailure, PacketId.ShardSelectFailure);
                    Expect(created, PacketId.CreateSuccess, 100);
                    if (Protocol0104.Read64(created.Payload, 4) != uid) throw new ProtocolException("Character-create reply UID mismatch.");
                    RequireName(created.Payload, 14, 32, check, 12, 30);
                    // All visible state comes from the success packet, including server-approved style.
                    pendingCharacter.Level = Protocol0104.Read16(created.Payload, 0);
                    pendingCharacter.Style = Protocol0104.ReadStyle(created.Payload, 4, 80);
                    pendingCharacter.Equipment = new EquipmentItem[9];
                    for (int i = 0; i < 7; i++) pendingCharacter.Equipment[i] = new EquipmentItem
                    { ItemType = i, ItemId = Protocol0104.Read16(created.Payload, 84 + i * 2) };
                    SelectedSlot = slot;
                    return pendingCharacter;
                }
                catch (ProtocolException) { login?.Dispose(); throw; }
                finally { operations.Release(); }
            }
        }

        public async Task DeleteCharacterAsync(long uid, CancellationToken cancellationToken = default)
        {
            using (var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
            {
                await operations.WaitAsync(token.Token).ConfigureAwait(false);
                try
                {
                    RequireLogin();
                    if (IsInWorld) throw new InvalidOperationException("Return to character selection before deleting a character.");
                    int slot = OwnedCharacter(uid).Slot;
                    var response = await login.RequestAsync(PacketId.Delete, UidPayload(uid),
                        new[] { PacketId.DeleteSuccess, PacketId.DeleteFailure, PacketId.ShardSelectFailure }, ReplyTimeout, token.Token).ConfigureAwait(false);
                    Reject(response, "Character deletion", PacketId.DeleteFailure, PacketId.ShardSelectFailure);
                    Expect(response, PacketId.DeleteSuccess, 1);
                    if ((sbyte)response.Payload[0] != slot) throw new ProtocolException("Character-delete reply slot mismatch.");
                    characters = characters.Where(c => c.Uid != uid).ToArray();
                    if (SelectedSlot == slot) SelectedSlot = characters.Count == 0 ? 0 : characters[0].Slot;
                }
                catch (ProtocolException) { login?.Dispose(); throw; }
                finally { operations.Release(); }
            }
        }

        private static void RequireName(byte[] actual, int first, int last, byte[] expected, int expectedFirst, int expectedLast)
        {
            // Compare fixed buffers byte-for-byte: stale/cross-request echoes must not advance creation.
            for (int i = 0; i < 18; i++) if (actual[first + i] != expected[expectedFirst + i]) throw new ProtocolException("Character reply first-name mismatch.");
            for (int i = 0; i < 34; i++) if (actual[last + i] != expected[expectedLast + i]) throw new ProtocolException("Character reply last-name mismatch.");
        }

        private static byte[] NameCheckPayload(CharacterCreation r)
        {
            byte[] p = new byte[64]; Protocol0104.Write32(p, 0, r.FirstNameCode);
            Protocol0104.Write32(p, 4, r.LastNameCode); Protocol0104.Write32(p, 8, r.MiddleNameCode);
            Protocol0104.WriteUtf16(p, 12, 9, r.FirstName); Protocol0104.WriteUtf16(p, 30, 17, r.LastName);
            return p;
        }

        private static byte[] AppearancePayload(CharacterCreation r)
        {
            byte[] p = new byte[100];
            Protocol0104.WriteUtf16(p, 10, 9, r.FirstName); Protocol0104.WriteUtf16(p, 28, 17, r.LastName);
            int[] values = { r.Style.Gender, r.Style.FaceStyle, r.Style.HairStyle, r.Style.HairColor,
                r.Style.SkinColor, r.Style.EyeColor, r.Style.Height, r.Style.Body };
            for (int i = 0; i < values.Length; i++) p[62 + i] = (byte)values[i];
            Protocol0104.Write32(p, 72, r.Style.Class);
            Protocol0104.Write16(p, 78, r.UpperBodyId); Protocol0104.Write16(p, 80, r.LowerBodyId); Protocol0104.Write16(p, 82, r.FootId);
            int[] indices = { r.UpperBodyIndex, r.LowerBodyIndex, r.FootIndex, r.FaceStyleIndex, r.HairStyleIndex };
            for (int i = 0; i < indices.Length; i++) Protocol0104.Write16(p, 90 + i * 2, indices[i]);
            return p;
        }

        private static void ValidateCreation(CharacterCreation r)
        {
            if (r.Style == null || r.Slot < 1 || r.Slot > 4) throw new ArgumentException("Choose a valid character slot and appearance.");
            if (string.IsNullOrWhiteSpace(r.FirstName)) throw new ArgumentException("A character needs a first name.");
            int[] bytes = { r.Style.Gender, r.Style.FaceStyle, r.Style.HairStyle, r.Style.HairColor,
                r.Style.SkinColor, r.Style.EyeColor, r.Style.Height, r.Style.Body };
            if (bytes.Any(v => v < 0 || v > 127)) throw new ArgumentException("Appearance value exceeds its signed wire byte.");
            int[] ids = { r.UpperBodyId, r.LowerBodyId, r.FootId };
            if (ids.Any(v => v <= 0 || v > short.MaxValue)) throw new ArgumentException("Select valid starter equipment IDs.");
            int[] indices = { r.UpperBodyIndex, r.LowerBodyIndex, r.FootIndex, r.FaceStyleIndex, r.HairStyleIndex };
            if (indices.Any(v => v < 0 || v > short.MaxValue)) throw new ArgumentException("Invalid appearance selection index.");
        }
    }
}
