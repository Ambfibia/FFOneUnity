using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Linq;
using FFOne.Network;
using NUnit.Framework;

namespace FFOne.Tests
{
    public sealed class NetworkProtocolTests
    {
        // Independent accepted vectors: FFOneClient ffone-protocol tests, verified against RustyFusion.
        [Test]
        public void KeyDerivationMatchesAccepted0104Vectors()
        {
            Assert.AreEqual(0x51217e5472243e62UL, Protocol0104.DefaultKey);
            Assert.AreEqual(0x043f235c29964200UL, Protocol0104.DeriveKey(0x1122334455667788UL, 3, 7));
            Assert.AreEqual(0x3989a24fb8c56068UL, Protocol0104.FrontendKey);
        }

        [TestCase(0, "0C000000737EE6605C792754663D2667")]
        [TestCase(7, "0C000000730EE6605C792754663D2667")]
        public void CharacterSelectMatchesGoldenEncryptedFrame(int sequence, string golden)
        {
            byte[] payload = { 8, 7, 6, 5, 4, 3, 2, 1 };
            var encoded = Protocol0104.EncodeFrame(0x12000005, payload, Protocol0104.DefaultKey, (ushort)sequence);
            Assert.AreEqual(golden, BitConverter.ToString(encoded).Replace("-", ""));
            var decoded = Protocol0104.DecodeFrame(encoded, Protocol0104.DefaultKey, (ushort)sequence);
            Assert.AreEqual(0x12000005U, decoded.PacketType);
            CollectionAssert.AreEqual(payload, decoded.Payload);
        }

        [Test]
        public void CodecRejectsMalformedLengthTruncationTrailingDataAndChecksum()
        {
            foreach (uint size in new[] { 0U, 3U, 4097U, uint.MaxValue })
            {
                byte[] header = new byte[4]; Protocol0104.Write32(header, 0, unchecked((int)size));
                Assert.Throws<ProtocolException>(() => Protocol0104.DecodeFrame(header, Protocol0104.DefaultKey));
            }
            byte[] valid = Protocol0104.EncodeFrame(0x21000016, new byte[] { 1, 2, 3, 4 }, Protocol0104.DefaultKey);
            Assert.Throws<ProtocolException>(() => Protocol0104.DecodeFrame(valid.Take(valid.Length - 1).ToArray(), Protocol0104.DefaultKey));
            Assert.Throws<ProtocolException>(() => Protocol0104.DecodeFrame(valid.Concat(new byte[1]).ToArray(), Protocol0104.DefaultKey));
            valid[valid.Length - 1] ^= 0x20;
            Assert.Throws<ProtocolException>(() => Protocol0104.DecodeFrame(valid, Protocol0104.DefaultKey));
        }

        [Test]
        public void FullCookieUsesByteFieldsWithoutTruncatingIntoPasswordBuffer()
        {
            string cookie = new string('x', 254);
            byte[] payload = Protocol0104.LoginPayload("canonical", cookie, true);
            Assert.AreEqual(468, payload.Length);
            Assert.AreEqual(2, Protocol0104.Read32(payload, 144));
            Assert.IsTrue(payload.Take(132).All(b => b == 0));
            Assert.IsTrue(payload.Skip(212).Take(254).All(b => b == 'x'));
            Assert.AreEqual(0, payload[466]);
            Assert.Throws<ArgumentException>(() => Protocol0104.LoginPayload("canonical", cookie + "x", true));
            Assert.Throws<ArgumentException>(() => Protocol0104.LoginPayload("canonical", "bad\0secret", true));
        }

        [Test]
        public void CharacterSnapshotKeepsAuthoritativeUidAppearanceEquipmentAndSignedCoordinates()
        {
            byte[] payload = new byte[204]; payload[0] = 3;
            Protocol0104.Write16(payload, 2, 17); Protocol0104.Write64(payload, 4, 0x0102030405060708);
            Protocol0104.WriteUtf16(payload, 14, 9, "Юлия"); Protocol0104.WriteUtf16(payload, 32, 17, "Fusion");
            payload[66] = 2; payload[67] = 6; payload[68] = 25; payload[69] = 54; payload[70] = 36;
            payload[80] = 1; payload[81] = 1;
            Protocol0104.Write32(payload, 84, -123); Protocol0104.Write32(payload, 88, 456); Protocol0104.Write32(payload, 92, 789);
            Protocol0104.Write16(payload, 108, 1); Protocol0104.Write16(payload, 110, 67);
            var character = Protocol0104.DecodeCharacter(payload);
            Assert.AreEqual(0x0102030405060708L, character.Uid);
            Assert.AreEqual("Юлия Fusion", character.DisplayName);
            Assert.AreEqual(3, character.Slot); Assert.AreEqual(17, character.Level);
            Assert.AreEqual(2, character.Style.Gender); Assert.AreEqual(25, character.Style.HairStyle);
            Assert.AreEqual(67, character.Equipment[1].ItemId);
            Assert.AreEqual(-123, character.X); Assert.AreEqual(456, character.Y); Assert.AreEqual(789, character.Z);
            CollectionAssert.AreEqual(payload, character.RawData);
        }

        [Test]
        public void WorldSnapshotUses2700ByteLayoutAndChecksSelectedUid()
        {
            byte[] payload = new byte[2700];
            Protocol0104.Write32(payload, 0, 42); Protocol0104.Write64(payload, 8, 1234);
            Protocol0104.Write16(payload, 4, 50);
            Protocol0104.Write32(payload, 124, -100); Protocol0104.Write32(payload, 128, 200); Protocol0104.Write32(payload, 132, 300);
            Protocol0104.Write32(payload, 112, 987); Protocol0104.Write64(payload, 2692, 0x0102030405060708);
            var entry = Protocol0104.DecodeWorldEntry(payload, 1234);
            Assert.AreEqual(42, entry.PlayerId); Assert.AreEqual(-100, entry.X); Assert.AreEqual(200, entry.Y); Assert.AreEqual(300, entry.Z);
            Assert.AreEqual(987, entry.FusionMatter); Assert.AreEqual(2688, entry.RawLoadData.Length);
            Assert.AreEqual(50, entry.UserLevel);
            Assert.Throws<ProtocolException>(() => Protocol0104.DecodeWorldEntry(payload, 5678));
            Assert.Throws<ProtocolException>(() => Protocol0104.DecodeWorldEntry(new byte[2699], 1234));
        }

        [Test]
        public void LauncherOriginRequiresHttpsExceptExplicitLoopbackFixtures()
        {
            Assert.AreEqual("https", FusionClient.ValidateApiOrigin("https://api.slavicfall.ru").Scheme);
            Assert.AreEqual("http", FusionClient.ValidateApiOrigin("http://127.0.0.1:24302").Scheme);
            Assert.Throws<ArgumentException>(() => FusionClient.ValidateApiOrigin("http://example.com"));
            Assert.Throws<ArgumentException>(() => FusionClient.ValidateApiOrigin("https://user:password@example.com"));
        }

        [Test]
        public void LegacyMovementKeepsUnscaledMoveFloatsAndPackedJumpLaunchData()
        {
            // FFOneClient movement/tests.rs accepted vector: Unity velocity (1.5,-2,3.25), heading 90.5.
            var move = Protocol0104.MovementPayload(125, 375, -250, -90, 1.5f, 3.25f, -2f, 600, 8, false);
            Assert.AreEqual(44, move.Length);
            Assert.AreEqual(0, Protocol0104.Read64(move, 0));
            Assert.AreEqual(125, Protocol0104.Read32(move, 8));
            Assert.AreEqual(375, Protocol0104.Read32(move, 12));
            Assert.AreEqual(-250, Protocol0104.Read32(move, 16));
            Assert.AreEqual(0x3fc00000, Protocol0104.Read32(move, 20)); // 1.5f, not 150f
            Assert.AreEqual(0x40500000, Protocol0104.Read32(move, 24)); // 3.25f
            Assert.AreEqual(unchecked((int)0xc0000000), Protocol0104.Read32(move, 28)); // -2f
            Assert.AreEqual(-90, Protocol0104.Read32(move, 32));
            Assert.AreEqual(8, move[36]);
            CollectionAssert.AreEqual(new byte[3], move.Skip(37).Take(3).ToArray());
            Assert.AreEqual(600, Protocol0104.Read32(move, 40));

            var jump = Protocol0104.JumpPayload(125, 375, -250, -90, 150, 325, -200, 700, 8, 7.25f, 0x456789ab);
            Assert.AreEqual(44, jump.Length);
            Assert.AreEqual(0x000002d5456789abL, Protocol0104.Read64(jump, 0));
            Assert.AreEqual(150, Protocol0104.Read32(jump, 20));
            Assert.AreEqual(325, Protocol0104.Read32(jump, 24));
            Assert.AreEqual(-200, Protocol0104.Read32(jump, 28));
            Assert.AreEqual(700, Protocol0104.Read32(jump, 40));
            var stop = Protocol0104.MovementPayload(125, 375, -250, 0, 0, 0, 0, 0, 0, true);
            Assert.AreEqual(20, stop.Length);
            CollectionAssert.AreEqual(move.Take(20).ToArray(), stop);
            Assert.Throws<ArgumentException>(() => Protocol0104.MovementPayload(0, 0, 0, 0, float.NaN, 0, 0, 600, 1, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => Protocol0104.JumpPayload(0, 0, 0, 0, 0, 0, 0, 568, 1, float.PositiveInfinity, 0));
        }

        [Test]
        public void ServerGotoRequiresExactSignedPositionPayload()
        {
            byte[] payload = { 0xff, 0xff, 0xff, 0xff, 0, 0, 0, 0x80, 0xff, 0xff, 0xff, 0x7f };
            var position = Protocol0104.DecodePlayerPosition(payload);
            Assert.AreEqual(-1, position.X); Assert.AreEqual(int.MinValue, position.Y); Assert.AreEqual(int.MaxValue, position.Z);
            Assert.Throws<ProtocolException>(() => Protocol0104.DecodePlayerPosition(new byte[11]));
            Assert.Throws<ProtocolException>(() => Protocol0104.DecodePlayerPosition(new byte[16]));
        }
    }
}
