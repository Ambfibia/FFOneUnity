using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Network;
using FFOne.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        FusionClient travelClient;
        CancellationToken travelToken;
        WorldEntity travelNpc;
        WorldTravelCatalog travelCatalog;
        uint travelPending;
        float travelDeadline;
        bool travelFaulted, crocView;
        bool travelRiding;
        int warpFlags, looksSlot = -1, statsSlot = -1, selectedRecallPoint;
        long skyway0, skyway1;
        string travelStatus = "";
        Button crocButton;
        public bool TravelBusy => travelPending != 0 || travelFaulted || travelRiding;
        public void ConfigureTravel(FusionClient client, CancellationToken token, WorldEntry entry)
        {
            travelClient = client; travelToken = token; travelCatalog = WorldTravelCatalog.Load();
            if (entry.RawLoadData?.Length >= 2524)
            { warpFlags = Protocol0104.Read32(entry.RawLoadData, 2504); skyway0 = Protocol0104.Read64(entry.RawLoadData, 2508); skyway1 = Protocol0104.Read64(entry.RawLoadData, 2516); }
            crocButton = ServiceButton(parent, "croc-open", "CROC POT", 18, 170, 200, OpenCroc);
        }
        bool TravelAvailable => travelClient != null && !disposed && !interactionSuspended && !TravelBusy && !MailBusy && !ServiceActive && !serviceFaulted && !inventoryPending && !TradeWindowActive &&
            trade?.Snapshot.Phase != GameplayTradePhase.OutgoingOffer && trade?.Snapshot.Phase != GameplayTradePhase.IncomingOffer && session.Snapshot.Hp > 0;
        public bool OpenNpcTravel(WorldEntity npc, WorldNpcDefinition definition)
        {
            if (npc == null || definition == null || !TravelAvailable) return false;
            if (!travelCatalog.Locations.Any(l => l.Npc == npc.NpcType) && !travelCatalog.Warps.Any(w => w.Npc == npc.NpcType) && !definition.IsGroupNpc && definition.Category != 17) return false;
            travelNpc = npc; crocView = false; travelStatus = ""; Windows.Open(GameplayWindow.Travel); return true;
        }
        public void OpenCroc()
        {
            if (!TravelAvailable) return;
            crocView = true; travelNpc = null; looksSlot = statsSlot = -1; travelStatus = ""; Windows.Open(GameplayWindow.Travel);
        }
        async Task BeginTravel(uint expected, Func<Task> action)
        {
            if (!TravelAvailable) return;
            travelPending = expected; travelDeadline = Time.unscaledTime + 12; travelStatus = T("Waiting for server…", "Ожидание сервера…"); RefreshTravel();
            try { await action(); }
            catch (Exception e) { if (!disposed) FaultTravel(e.Message); }
        }
        void FaultTravel(string text)
        { travelPending = 0; travelFaulted = true; travelStatus = text + T(" Re-enter the world to synchronize.", " Войдите в мир заново для синхронизации."); RefreshTravel(); Error?.Invoke(travelStatus); }
        void TickTravel()
        {
            if (travelPending != 0 && Time.unscaledTime >= travelDeadline) FaultTravel(T("No acknowledgement.", "Нет подтверждения операции."));
            if (crocButton != null) crocButton.style.display = session.Snapshot.IsActive && !session.Snapshot.IsTutorial && session.Snapshot.Hp > 0 && !Windows.IsOpen && !interactionSuspended && prompt == null ? DisplayStyle.Flex : DisplayStyle.None;
        }
        public void ReceiveTravel(ProtocolFrame f)
        {
            if (ProtocolRecall.TryDecode(f, out var recall))
            {
                if (!recall.Failed && travelPending == ProtocolRecall.Success && recall.NpcId != travelNpc?.Id)
                    throw new ProtocolException("Recall reply does not match the selected NPC.");
                if (!recall.Failed) selectedRecallPoint = recall.NpcId;
                if (travelPending == ProtocolRecall.Success)
                {
                    travelPending = 0;
                    travelStatus = recall.Failed ? T("Unable to register this Recall Point.", "Не удалось зарегистрировать точку возврата.") : T("Recall Point registered!", "Точка возврата зарегистрирована!");
                    RefreshTravel();
                }
            }
            if (ProtocolTravel.TryDecode(f, out var u))
            {
                if (u.Type == ProtocolTravel.WarpOk) selectedRecallPoint = 0;
                if ((u.Type == ProtocolTravel.Riding || u.Type == ProtocolTravel.PeerRiding) && u.PlayerId == session.Snapshot.PlayerId) travelRiding = u.Ride != 0;
                if (u.Type == ProtocolTravel.RegisterOk) { warpFlags = u.WarpFlags; skyway0 = u.SkywayFlags0; skyway1 = u.SkywayFlags1; }
                uint failure = travelPending == ProtocolTravel.WarpOk ? ProtocolTravel.WarpFail : travelPending - 1;
                if (travelPending == 0 || (u.Type != travelPending && u.Type != failure)) return;
                travelPending = 0; travelStatus = u.Failed ? T("Server rejected the trip: ", "Сервер отклонил переход: ") + u.Error : T("Confirmed.", "Подтверждено.");
                if (!u.Failed && u.Type != ProtocolTravel.RegisterOk) Windows.Close(); else RefreshTravel();
            }
            if (ProtocolCroc.TryDecode(f, out var c) && travelPending == ProtocolCroc.Ok)
            {
                if (c.LooksSlot != looksSlot || c.StatsSlot != statsSlot) throw new ProtocolException("Croc Pot reply does not match the selected slots.");
                travelPending = 0;
                travelStatus = c.Failed ? T("Server rejected the combination.", "Сервер отклонил совмещение.") : c.Combined ? T("Combination succeeded.", "Совмещение удалось.") : T("Attempt failed. Both items returned; Taros spent.", "Попытка не удалась. Предметы возвращены; Taros списаны.");
                if (c.Combined) looksSlot = statsSlot = -1;
                RefreshTravel();
            }
        }
        bool Unlocked(int type, int id) => id > 0 && (type == 1 ? id <= 31 && ((uint)warpFlags & (1u << (id - 1))) != 0 : id <= 128 &&
            ((unchecked((ulong)(id <= 64 ? skyway0 : skyway1)) >> ((id - 1) % 64)) & 1) != 0);
        VisualElement BuildTravelView()
        {
            var root = SocialRoot("travel", 880, 630, crocView ? "CROC POT" : WorldNpcCatalog.Load().Find(travelNpc.NpcType)?.Name ?? "TRAVEL");
            var panel = root.Q("travel-window");
            FFWidgets.Label(panel, "travel-status", travelStatus, 18, 48, 840, 48, true, 14, false);
            var list = new ScrollView { name = "travel-list" }; FFWidgets.Rect(list, 18, 100, 840, 510); panel.Add(list);
            FillTravel(root); return root;
        }
        void RefreshTravel() { if (Windows.Current == GameplayWindow.Travel) FillTravel(Windows.View); }
        void FillTravel(VisualElement root)
        {
            if (root == null) return; root.Q<Label>("travel-status").text = travelStatus;
            var list = root.Q<ScrollView>("travel-list"); list.Clear();
            if (crocView) { FillCroc(list); return; }
            bool ready = !TravelBusy;
            if (WorldNpcCatalog.Load().Find(travelNpc.NpcType)?.Category == 17)
            {
                var row = TravelRow(list, T("Register this point to return with your Recall Nano.", "Зарегистрируйте точку, чтобы вернуться с помощью Nano возврата."));
                ServiceButton(row, "recall-register", selectedRecallPoint == travelNpc.Id ? T("REGISTERED", "ЗАРЕГИСТРИРОВАНО") : T("REGISTER", "ЗАРЕГИСТРИРОВАТЬ"), 565, 4, 240,
                    () => _ = BeginTravel(ProtocolRecall.Success, () => travelClient.RegisterRecallPointAsync(travelNpc.Id, travelToken))).SetEnabled(ready && selectedRecallPoint != travelNpc.Id);
                return;
            }
            foreach (var loc in travelCatalog.Locations.Where(l => l.Npc == travelNpc.NpcType))
            {
                var row = TravelRow(list, (loc.Type == 1 ? "S.C.A.M.P.E.R. — " : "Monkey Skyway — ") + loc.Name);
                ServiceButton(row, "transport-register-" + loc.Type + "-" + loc.Id, Unlocked(loc.Type, loc.Id) ? T("REGISTERED", "ОТКРЫТО") : T("REGISTER", "ОТКРЫТЬ"), 565, 4, 240,
                    () => _ = BeginTravel(ProtocolTravel.RegisterOk, () => travelClient.RegisterTransportAsync(travelNpc.Id, loc.Type, loc.Id, travelToken))).SetEnabled(ready && !Unlocked(loc.Type, loc.Id));
                foreach (var trip in travelCatalog.Trips.Where(t => t.Type == loc.Type && t.From == loc.Id && t.Npc == travelNpc.NpcType))
                {
                    var dest = travelCatalog.Locations.FirstOrDefault(l => l.Type == trip.Type && l.Id == trip.To); if (dest == null) continue;
                    bool covered = NetworkWorldController.HasCoverage(FFOne.Content.ContentAssets.Load<WorldCatalog>("World/Catalog"), 0, WorldCoordinates.FromServer(dest.X, dest.Y, dest.Z));
                    var r = TravelRow(list, dest.Name + $"   {trip.Cost} Taros" + (!Unlocked(trip.Type, trip.To) ? T(" [locked]", " [закрыто]") : !covered ? T(" [scene unavailable]", " [нет сцены]") : ""));
                    ServiceButton(r, "transport-trip-" + trip.Id, T("TRAVEL", "ОТПРАВИТЬСЯ"), 565, 4, 240,
                        () => ConfirmTravel(dest.Name + $" — {trip.Cost} Taros", () => BeginTravel(ProtocolTravel.TripOk, () => travelClient.TravelAsync(travelNpc.Id, trip.Id, travelToken))))
                        .SetEnabled(ready && covered && Unlocked(trip.Type, trip.From) && Unlocked(trip.Type, trip.To) && session.Snapshot.Taros >= trip.Cost);
                }
            }
            foreach (var warp in travelCatalog.Warps.Where(w => w.Npc == travelNpc.NpcType))
            {
                bool covered = NetworkWorldController.HasCoverage(FFOne.Content.ContentAssets.Load<WorldCatalog>("World/Catalog"), warp.Map, WorldCoordinates.FromServer(warp.X, warp.Y, warp.Z));
                var row = TravelRow(list, warp.Name + (warp.Group ? T(" [group]", " [группа]") : "") + $"   Lv {warp.Level}   {warp.Cost} Taros" + (!covered ? T(" [scene unavailable]", " [нет сцены]") : ""));
                ServiceButton(row, "npc-warp-" + warp.Id, T("ENTER", "ВОЙТИ"), 565, 4, 240, () => ConfirmTravel(warp.Name + (warp.Group ? T(" — nearby group members travel together.", " — участники группы рядом перейдут вместе.") : ""), () => SendNpcWarp(warp)))
                    .SetEnabled(ready && covered && session.Snapshot.Level >= warp.Level && session.Snapshot.Taros >= warp.Cost);
            }
            if (WorldNpcCatalog.Load().Find(travelNpc.NpcType)?.IsGroupNpc == true)
            {
                var row = TravelRow(list, T("NPC companion", "NPC в группе")); bool member = social.Npcs.Any(n => n.Id == travelNpc.Id);
                ServiceButton(row, "npc-group-action", member ? T("REMOVE", "УБРАТЬ") : T("INVITE", "ПРИГЛАСИТЬ"), 565, 4, 240,
                    () => _ = SocialSend(() => member ? travelClient.KickNpcAsync(travelNpc.Id, travelToken) : travelClient.InviteNpcAsync(travelNpc.Id, travelToken))).SetEnabled(ready && !socialSending);
            }
            var missions = TravelRow(list, T("NPC missions", "Задания NPC"));
            ServiceButton(missions, "travel-missions", T("MISSIONS", "ЗАДАНИЯ"), 565, 4, 240, () => OpenNpcMissions(travelNpc)).SetEnabled(ready);
        }
        VisualElement TravelRow(VisualElement list, string text)
        {
            var row = new VisualElement(); row.style.height = 57; row.style.flexShrink = 0; list.Add(row);
            var label = FFWidgets.Label(row, "name", text, 0, 0, 550, 52, true, 14, false); label.style.whiteSpace = WhiteSpace.Normal; return row;
        }
        void ConfirmTravel(string text, Func<Task> action) => ShowPrompt(T("CONFIRM", "ПОДТВЕРЖДЕНИЕ"), text, null, _ => action(), () => Task.CompletedTask);
        Task SendNpcWarp(NpcWarp w)
        {
            var required = (w.RequiredType == 8 ? session.Snapshot.QuestInventory : session.Snapshot.Inventory).FirstOrDefault(i => i.ItemType == w.RequiredType && i.ItemId == w.RequiredItem);
            var consumed = session.Snapshot.Inventory.FirstOrDefault(i => i.ItemType == w.ConsumedType && i.ItemId == w.ConsumedItem);
            if (w.RequiredItem > 0 && required.IsEmpty || w.ConsumedItem > 0 && consumed.IsEmpty) { travelStatus = T("Required item is missing.", "Нет нужного предмета."); RefreshTravel(); return Task.CompletedTask; }
            return BeginTravel(ProtocolTravel.WarpOk, () => travelClient.WarpNpcAsync(travelNpc.Id, w.Id, w.RequiredItem > 0 ? (w.RequiredType == 8 ? 2 : 1) : 4,
                w.RequiredItem > 0 ? required.Slot : 0, w.ConsumedItem > 0 ? consumed.Slot : -1, travelToken));
        }
        void FillCroc(VisualElement list)
        {
            var items = session.Snapshot.Inventory.Where(i => !i.IsEmpty && i.ItemType <= 3 && i.MetadataComplete).ToArray();
            foreach (var item in items)
            {
                var row = TravelRow(list, $"[{item.Slot + 1}] " + GameplayUiCatalog.Load().Item(item.ItemType, item.ItemId, language)?.Name);
                ServiceButton(row, "croc-looks-" + item.Slot, looksSlot == item.Slot ? T("LOOKS ✓", "ВИД ✓") : T("LOOKS", "ВИД"), 565, 4, 115,
                    () => { looksSlot = item.Slot; RefreshTravel(); }).SetEnabled(!TravelBusy);
                ServiceButton(row, "croc-stats-" + item.Slot, statsSlot == item.Slot ? T("STATS ✓", "СТАТЫ ✓") : T("STATS", "СТАТЫ"), 690, 4, 115,
                    () => { statsSlot = item.Slot; RefreshTravel(); }).SetEnabled(!TravelBusy);
            }
            if (looksSlot < 0 || statsSlot < 0 || looksSlot == statsSlot) { TravelRow(list, T("Select two matching equipment items: appearance and stats.", "Выберите два предмета одного типа: внешний вид и характеристики.")); return; }
            var look = session.Snapshot.Inventory[looksSlot]; var stat = session.Snapshot.Inventory[statsSlot];
            var a = GameplayUiCatalog.Load().Item(look.ItemType, look.ItemId); var b = GameplayUiCatalog.Load().Item(stat.ItemType, stat.ItemId);
            if (a == null || b == null || look.ItemType != stat.ItemType || !travelCatalog.Croc.TryGetValue(Math.Abs(a.RequiredLevel - b.RequiredLevel), out var odds) || Math.Abs(a.Rarity - b.Rarity) > 3) return;
            long cost = (long)a.BuyPrice * odds.LooksCost + (long)b.BuyPrice * odds.StatsCost;
            string detail = $"{cost} Taros   {odds.Base * odds.Rarity[Math.Abs(a.Rarity - b.Rarity)] * 100:0.##}%";
            var confirm = TravelRow(list, detail);
            ServiceButton(confirm, "croc-combine", T("COMBINE", "СОВМЕСТИТЬ"), 565, 4, 240,
                () => ConfirmTravel(detail + T(". Failure still costs Taros. Success consumes the stats item.", ". При неудаче Taros списываются. При успехе предмет характеристик расходуется."),
                    () => BeginTravel(ProtocolCroc.Ok, () => travelClient.CombineItemsAsync(looksSlot, statsSlot, travelToken))))
                .SetEnabled(!TravelBusy && session.Snapshot.Taros >= cost && !look.IsEmpty && !stat.IsEmpty);
        }
    }
}
