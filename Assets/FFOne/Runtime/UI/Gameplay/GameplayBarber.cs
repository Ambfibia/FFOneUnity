using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Creation;
using FFOne.Integration;
using FFOne.Network;
using UnityEngine;
using UnityEngine.UIElements;
using CharacterInfo = FFOne.Network.CharacterInfo;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        CharacterInfo barberCharacter, barberDraft;
        WorldEntity barberNpc;
        PreviewStage barberPreview;
        Action barberCommitted;
        CreationCatalog barberCatalog;
        int[] barberPrices;
        int barberPage;
        uint barberPending;
        float barberDeadline;
        string barberStatus;
        static readonly string[] BarberFields = { "gender", "height", "body", "skin", "hair", "hair_color", "face", "eye" };

        internal void ConfigureBarber(CharacterInfo character, PreviewStage preview, Action committed)
        { barberCharacter = character; barberPreview = preview; barberCommitted = committed; }

        void OpenBarber(WorldEntity npc)
        {
            if (barberCharacter == null || barberPending != 0) return;
            barberNpc = npc;
            barberDraft = NativeAvatarPreview.Snapshot(barberCharacter);
            barberPrices = null;
            barberStatus = BarberText("waiting");
            barberPage = 0;
            barberCatalog ??= new CreationCatalog();
            Windows.Open(GameplayWindow.Barber);
            _ = SendBarber(ProtocolBarber.Quote, () => travelClient.OpenBarberAsync(npc.Id, travelToken));
        }

        async Task SendBarber(uint expected, Func<Task> request)
        {
            barberPending = expected;
            barberDeadline = Time.unscaledTime + 12;
            try { await request(); }
            catch (Exception ex) { if (!disposed) { barberPending = 0; barberStatus = ex.Message; RefreshBarber(); } }
        }

        void TickBarber()
        {
            if (barberPending == 0 || Time.unscaledTime < barberDeadline) return;
            barberDeadline = float.PositiveInfinity;
            barberStatus = BarberText(barberPending == ProtocolBarber.Result ? "pending" : "unavailable");
            // A commit may already have been accepted. Only its reply or a new world entry can resolve it.
            if (barberPending == ProtocolBarber.Quote) barberPending = 0;
            RefreshBarber();
        }

        internal void ReceiveBarber(ProtocolFrame frame)
        {
            if (frame.PacketType != barberPending || barberPending == 0) return;
            if (frame.PacketType == ProtocolBarber.Quote)
            {
                var prices = ProtocolBarber.Prices(frame.Payload);
                barberPending = 0;
                if (Windows.Current != GameplayWindow.Barber) return;
                barberPrices = prices;
                barberPage = prices[0] == 1 ? 0 : 1;
                barberStatus = "";
                RefreshBarber();
                return;
            }
            var data = frame.Payload;
            if (data.Length != 56) throw new ProtocolException("Invalid barber receipt length.");
            int error = Protocol0104.Read32(data, 0);
            barberPending = 0;
            if (error != 0)
            {
                barberStatus = BarberText(error == 1 ? "inventory_full" : "failed");
                RefreshBarber();
                return;
            }
            var state = session.Snapshot.WithBarber(barberDraft.Style.Gender, data);
            barberCharacter.Style = barberDraft.Style;
            GameplayEquipmentAppearance.Apply(barberCharacter, state);
            session.AcceptBarber(state);
            barberCommitted?.Invoke();
            if (Windows.Current == GameplayWindow.Barber) Windows.Close();
        }

        string BarberText(string key) => LocalizationCatalog.Lookup("ui.barber." + key, language);
        void RefreshBarber() { if (Windows.Current == GameplayWindow.Barber) Windows.Refresh(); }

        IEnumerable<(int field, int price)> Charges()
        {
            var a = barberCharacter.Style;
            var b = barberDraft.Style;
            bool gender = a.Gender != b.Gender;
            var changes = new[] { gender, a.Height != b.Height || a.Body != b.Body, a.SkinColor != b.SkinColor,
                gender || a.HairStyle != b.HairStyle, a.HairColor != b.HairColor, gender || a.FaceStyle != b.FaceStyle, a.EyeColor != b.EyeColor };
            int[] fields = { 0, 2, 3, 4, 5, 6, 7 };
            for (int i = 0; i < changes.Length; i++) if (changes[i]) yield return (fields[i], barberPrices[i + 1]);
        }

        int[] BarberChoices(int field, int gender) => field switch {
            0 => new[] { 1, 2 }, 1 => Enumerable.Range(0, 5).ToArray(), 2 => Enumerable.Range(0, 3).ToArray(),
            4 or 6 => barberCatalog.Choices(BarberFields[field], gender).Select(row => (int)row["value"]).Distinct().ToArray(),
            _ => Enumerable.Range(1, barberCatalog.Palette(field == 5 ? "hair" : BarberFields[field]).Length).ToArray()
        };
        static int BarberValue(CharacterStyle style, int field) => field switch {
            0 => style.Gender, 1 => style.Height, 2 => style.Body, 3 => style.SkinColor, 4 => style.HairStyle,
            5 => style.HairColor, 6 => style.FaceStyle, _ => style.EyeColor
        };
        void ChangeBarber(int field, int delta)
        {
            var s = barberDraft.Style;
            var values = BarberChoices(field, s.Gender);
            if (values.Length == 0) return;
            int index = Math.Max(0, Array.IndexOf(values, BarberValue(s, field)));
            int next = values[(index + values.Length + delta) % values.Length];
            switch (field)
            {
                case 0:
                    int Corresponding(int f) {
                        var a = barberCharacter.Style;
                        var candidates = BarberChoices(f, next);
                        return candidates[Mathf.Clamp(Array.IndexOf(BarberChoices(f, a.Gender), BarberValue(a, f)), 0, candidates.Length - 1)];
                    }
                    s.HairStyle = Corresponding(4); s.FaceStyle = Corresponding(6); s.Gender = next;
                    break;
                case 1: s.Height = next; break;
                case 2: s.Body = next; break;
                case 3: s.SkinColor = next; break;
                case 4: s.HairStyle = next; break;
                case 5: s.HairColor = next; break;
                case 6: s.FaceStyle = next; break;
                case 7: s.EyeColor = next; break;
            }
            barberStatus = "";
            RefreshBarber();
        }

        VisualElement BuildBarber()
        {
            var root = FFView.InstantiateClass(NpcMissionTree, "ff-barber-screen");
            var image = FFView.RequireClass<Image>(root, "barber-avatar");
            if (barberPreview)
            {
                var visible = NativeAvatarPreview.Snapshot(barberDraft);
                for (int slot = 1; slot <= 3; slot++)
                {
                    var item = visible.Equipment[slot];
                    int required = GameplayUiCatalog.Load().Item(item.ItemType, item.ItemId)?.RequiredGender ?? 0;
                    if (required > 0 && required != visible.Style.Gender) visible.Equipment[slot] = default;
                }
                barberPreview.ShowDraft(visible, image);
            }
            FFView.RequireClass<Label>(root, "barber-title").text = BarberText("title");
            FFView.RequireClass<Label>(root, "barber-status").text = barberStatus;
            var locked = barberPrices == null || barberPending == ProtocolBarber.Result;
            var options = FFView.RequireClass<VisualElement>(root, "barber-options");
            var previous = FFView.RequireClass<Button>(root, "barber-back");
            var reset = FFView.RequireClass<Button>(root, "barber-reset");
            var proceed = FFView.RequireClass<Button>(root, "barber-continue");
            int firstPage = barberPrices?[0] == 1 ? 0 : 1;
            previous.text = barberPage <= firstPage ? T("Cancel", "Отмена") : T("Back", "Назад");
            previous.SetEnabled(barberPending != ProtocolBarber.Result);
            previous.clicked += () => { if (barberPage <= firstPage || barberPrices == null) Windows.Close(); else { barberPage--; RefreshBarber(); } };
            reset.text = BarberText("reset");
            reset.SetEnabled(!locked);
            reset.clicked += () => { barberDraft = NativeAvatarPreview.Snapshot(barberCharacter); barberStatus = ""; barberPage = firstPage; RefreshBarber(); };
            proceed.text = barberPage < 2 ? BarberText("continue") : T("Confirm", "Подтвердить");
            proceed.SetEnabled(!locked);
            proceed.clicked += () => {
                if (barberPage < 2) { barberPage++; RefreshBarber(); return; }
                barberStatus = BarberText("waiting");
                _ = SendBarber(ProtocolBarber.Result, () => travelClient.ConfirmBarberAsync(barberDraft, travelToken));
                RefreshBarber();
            };
            if (barberPrices == null) return root;
            FFView.RequireClass<Label>(root, "barber-step").text = BarberText(barberPage == 0 ? "choose_body" : barberPage == 1 ? "choose_features" : "review");
            var charges = Charges().ToArray();
            long total = charges.Sum(row => (long)row.price);
            FFView.RequireClass<Label>(root, "barber-total").text = BarberText("total").Replace("{cost}", total.ToString());
            IEnumerable<int> fields = barberPage < 2 ? Enumerable.Range(barberPage * 4, 4) : charges.Select(c => c.field);
            foreach (int field in fields)
            {
                var row = FFView.InstantiateClass(NpcMissionTree, "barber-option");
                row.AddToClassList("barber-" + BarberFields[field]);
                FFView.RequireClass<Label>(row, "barber-field").text = BarberText(BarberFields[field]);
                FFView.RequireClass<Label>(row, "barber-value").text = (barberPage == 2 ? charges.First(c => c.field == field).price : BarberValue(barberDraft.Style, field)).ToString();
                foreach (int delta in new[] { -1, 1 })
                {
                    var button = FFView.RequireClass<Button>(row, delta < 0 ? "barber-prev" : "barber-next");
                    if (barberPage == 2) button.style.display = DisplayStyle.None;
                    button.SetEnabled(!locked);
                    button.clicked += () => ChangeBarber(field, delta);
                }
                options.Add(row);
            }
            return root;
        }
    }

    public sealed partial class GameplayUiState
    {
        internal GameplayUiState WithBarber(int gender, byte[] data)
        {
            var equip = Equipment.ToArray();
            var items = Inventory.ToArray();
            int balance = Protocol0104.Read32(data, 4);
            if (balance < 0) throw new ProtocolException("Invalid barber balance.");
            for (int i = 0; i < 3; i++)
            {
                int slot = Protocol0104.Read32(data, 8 + 4 * i);
                if (slot == -1) continue;
                var item = ProtocolServices.Item(data, 20 + i * 12);
                if (slot < 0 || slot >= items.Length || items[slot].ItemId != 0 || item.ItemId <= 0 || equip[i+1].ItemId != item.ItemId || equip[i+1].ItemType != item.ItemType)
                    throw new ProtocolException("Invalid barber wardrobe destination.");
                items[slot] = new GameplayItemState(slot, item.ItemType, item.ItemId, item.Option, item.TimeLimit);
                equip[i + 1] = new GameplayItemState(i + 1, 0, 0, 0, 0);
            }
            var next = (GameplayUiState)MemberwiseClone();
            next.Gender = gender; next.Taros = balance;
            next.Equipment = Array.AsReadOnly(equip); next.Inventory = Array.AsReadOnly(items);
            return next;
        }
    }
    public sealed partial class GameplayUiSession
    {
        internal void AcceptBarber(GameplayUiState next) => Publish(next);
    }
}
