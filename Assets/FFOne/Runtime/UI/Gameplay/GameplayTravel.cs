using System;
using System.Collections.Generic;
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
        public bool TravelBusy => travelPending != 0 || travelFaulted || travelRiding || crocWaiting || transportDeparting;
        internal Func<int, NetworkEntityView> ResolveServiceNpc;
        internal Action<WorldEntity,string> ServiceVoice;
        internal Func<WorldEntity, CancellationToken, Task<bool>> SyncWarpPosition;
        public void ConfigureTravel(FusionClient client, CancellationToken token, WorldEntry entry)
        {
            travelClient = client; travelToken = token; travelCatalog = WorldTravelCatalog.Load();
            RequestMentor = mentor => client.ChangeMentorAsync(mentor, token);
            LoadMapPreferences();
            if (entry.RawLoadData?.Length >= 2524)
            { warpFlags = Protocol0104.Read32(entry.RawLoadData, 2504); skyway0 = Protocol0104.Read64(entry.RawLoadData, 2508); skyway1 = Protocol0104.Read64(entry.RawLoadData, 2516); }
        }
        bool TravelAvailable => travelClient != null && !disposed && !interactionSuspended && !TravelBusy && !MailBusy && !ServiceActive && !serviceFaulted && !inventoryPending && !TradeWindowActive &&
            trade?.Snapshot.Phase != GameplayTradePhase.OutgoingOffer && trade?.Snapshot.Phase != GameplayTradePhase.IncomingOffer && session.Snapshot.Hp > 0;
        public bool OpenNpcTravel(WorldEntity npc, WorldNpcDefinition definition)
        {
            if (npc == null || definition == null || !TravelAvailable) return false;
            if (definition.Category is 13 or 14) { OpenRaceNpc(npc,definition.Category); return true; }
            transportHub = travelCatalog.Locations.FirstOrDefault(l => l.Npc == npc.NpcType);
            selectedTrip = null; turboTravel = false;
            if (definition.Category is >=7 and <=9 or 26 or 28 || transportHub!=null || npc.NpcType is >=643 and <=650)
            {
                serviceMenuNpc=npc; Windows.Open(GameplayWindow.NpcServices);return true;
            }
            if (!travelCatalog.Locations.Any(l => l.Npc == npc.NpcType) && !travelCatalog.Warps.Any(w => w.Npc == npc.NpcType) && !definition.IsGroupNpc && definition.Category != 17 && !IsMentorCategory(definition.Category)) return false;
            travelNpc = npc; crocView = false; travelStatus = ""; Windows.Open(GameplayWindow.Travel); return true;
        }
        public void OpenCroc(WorldEntity npc = null)
        {
            if (!TravelAvailable) return;
            crocView = true; travelNpc = npc; looksSlot = statsSlot = crocCarried = crocResult = -1; travelStatus = ""; Windows.Open(GameplayWindow.Travel);
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
            if(stationReply!=0 && Time.unscaledTime>=stationDeadline)StationFailure(T("No Nano station acknowledgement.","Сервер не подтвердил действие Nano-станции."));
            if (mentorPending != 0 && !travelFaulted && Time.unscaledTime >= mentorReplyDeadline)
                FaultTravel(T("No guide change acknowledgement.", "Сервер не подтвердил смену гида."));
            if (travelPending != 0 && Time.unscaledTime >= travelDeadline) FaultTravel(T("No acknowledgement.", "Нет подтверждения операции."));
        }
        public void ReceiveTravel(ProtocolFrame f)
        {
            if (ReceiveMentor(f)) return;
            if (ReceiveMapPresence(f)) return;
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
                if(travelPending==ProtocolTravel.TripOk)playSound?.Invoke(u.Failed?"action_failure01":"action_sucess");
                travelPending = 0; travelStatus = u.Failed ? T("Server rejected the trip: ", "Сервер отклонил переход: ") + u.Error : T("Confirmed.", "Подтверждено.");
                if (u.Type == ProtocolTravel.RegisterOk)
                    travelStatus = FFOne.Content.LocalizationCatalog.Lookup(
                        u.Transport == 1 ? "ui.transportation.registered.warp" : "ui.transportation.registered.hub", language);
                if (!u.Failed && u.Type != ProtocolTravel.RegisterOk) Windows.Close(); else RefreshTravel();
            }
            if (ProtocolCroc.TryDecode(f, out var c) && travelPending == ProtocolCroc.Ok)
            {
                if (c.LooksSlot != looksSlot || c.StatsSlot != statsSlot) throw new ProtocolException("Croc Pot reply does not match the selected slots.");
                travelPending = 0;
                travelStatus = c.Failed ? T("Server rejected the combination.", "Сервер отклонил совмещение.") : c.Combined ? T("Combination succeeded.", "Совмещение удалось.") : T("Attempt failed. Both items returned; Taros spent.", "Попытка не удалась. Предметы возвращены; Taros списаны.");
                if (c.Combined) { crocResult = looksSlot; looksSlot = statsSlot = -1; }
                else if (!c.Failed) crocResult = -2;
                RefreshTravel();
            }
        }
        bool Unlocked(int type, int id) => id > 0 && (type == 1 ? id <= 31 && ((uint)warpFlags & (1u << (id - 1))) != 0 : id <= 128 &&
            ((unchecked((ulong)(id <= 64 ? skyway0 : skyway1)) >> ((id - 1) % 64)) & 1) != 0);
        VisualElement BuildTravelView()
        {
            if (crocView) return BuildCrocView();
            if (transportHub != null) return BuildTransportView();
            var npc = travelNpc;
            var definition = WorldNpcCatalog.Load().Find(npc.NpcType);
            var rows = new List<(string id,string label,string icon,Action run)>();
            const string quest = "ui/en/gameplay/mission/npc/";
            int category = definition.Category;
            if (IsMentorCategory(category))
            {
                string text = category == 23 ? MentorText("warp.action") : MentorText("action." + (session.Snapshot.Guide == 5 ? "choose" : "change"));
                rows.Add(("npc-mentor", text, category == 23 ? quest+"npcicon_warp" : "ui/en/gameplay/journal/npcicon_guide",
                    () => OpenMentor(category, travelClient.PaymentFlag)));
            }
            if (category == 17)
                rows.Add(("recall-register", T("REGISTER", "ЗАРЕГИСТРИРОВАТЬ"), quest+"npcicon_warp",
                    () => _ = BeginTravel(ProtocolRecall.Success, () => travelClient.RegisterRecallPointAsync(npc.Id, travelToken))));
            var warps = travelCatalog.Warps.Where(w => w.Npc == npc.NpcType && !(category == 23 && w.Id == 76)).ToArray();
            var destinations = FFOne.Content.ContentAssets.Load<WorldCatalog>("World/Catalog");
            var availability = new Dictionary<NpcWarp,bool>();
            foreach (var warp in warps)
            {
                availability.Add(warp,NetworkWorldController.HasCoverage(destinations,warp.Map,WorldCoordinates.FromServer(warp.X,warp.Y,warp.Z)));
                rows.Add(("npc-warp-"+warp.Id, warp.Name, quest+"npcicon_warp", () => ConfirmTravel(warp.Name +
                    (warp.Group ? T(" — nearby group members travel together.", " — участники группы рядом перейдут вместе.") : ""), () => SendNpcWarp(warp))));
            }
            if (definition.IsGroupNpc)
                rows.Add(("npc-group-action", T("INVITE","ПРИГЛАСИТЬ"), "ui/en/gameplay/system/messicon_group", () =>
                    _ = SocialSend(() => social.Npcs.Any(n => n.Id == npc.Id) ? travelClient.KickNpcAsync(npc.Id, travelToken) : travelClient.InviteNpcAsync(npc.Id, travelToken))));
            if (category != 17)
                rows.Add(("travel-missions", T("MISSIONS","ЗАДАНИЯ"), MissionServiceIcon, () => OpenNpcConversation(npc)));
            rows.Add(("npc-dismiss", T("CLOSE","ЗАКРЫТЬ"), quest+"npcicon_exit", () => Windows.Close()));
            var controls = new Dictionary<string, Button>();
            var root = BuildServiceRows(definition, rows, controls);
            var status = FFView.RequireClass<Label>(root, "npc-service-status");
            FFWidgets.AutoFit(status);
            void UpdateActions()
            {
                status.text = string.IsNullOrEmpty(travelStatus) && warps.Length > 0 && warps.All(w => session.Snapshot.Level < w.Level)
                    ? FFOne.Content.LocalizationCatalog.Lookup("content.tabledata.message.message.111.sz_string", language)
                    : travelStatus;
                foreach (var row in rows) controls[row.id].SetEnabled(!TravelBusy);
                if (IsMentorCategory(category)) controls["npc-mentor"].SetEnabled(!TravelBusy && session.Snapshot.Guide is >= 1 and <= 5);
                if (category == 17)
                {
                    var register = controls["recall-register"];
                    bool selected = selectedRecallPoint == npc.Id;
                    register.text = selected ? T("REGISTERED","ЗАРЕГИСТРИРОВАНО") : T("REGISTER","ЗАРЕГИСТРИРОВАТЬ");
                    register.SetEnabled(!TravelBusy && !selected);
                }
                foreach (var warp in warps) controls["npc-warp-" + warp.Id].SetEnabled(!TravelBusy && availability[warp] &&
                    session.Snapshot.Level >= warp.Level && session.Snapshot.Taros >= warp.Cost);
                if (definition.IsGroupNpc)
                {
                    var button=controls["npc-group-action"];
                    button.text=social.Npcs.Any(n=>n.Id==npc.Id) ? T("REMOVE","УБРАТЬ") : T("INVITE","ПРИГЛАСИТЬ");
                    button.SetEnabled(!TravelBusy && !socialSending);
                }
            }
            root.userData = (Action)UpdateActions;
            UpdateActions();
            return root;
        }
        void RefreshTravel()
        {
            if (Windows.Current != GameplayWindow.Travel || Windows.View == null) return;
            if (crocView) RefreshCroc(Windows.View);
            else if (transportHub != null) FillTransport(Windows.View);
            else (Windows.View.userData as Action)?.Invoke();
        }
        void ConfirmTravel(string text, Func<Task> action) => ShowPrompt(T("CONFIRM", "ПОДТВЕРЖДЕНИЕ"), text, null, _ => action(), () => Task.CompletedTask);
        Task SendNpcWarp(NpcWarp w)
        {
            var required = (w.RequiredType == 8 ? session.Snapshot.QuestInventory : session.Snapshot.Inventory).FirstOrDefault(i => i.ItemType == w.RequiredType && i.ItemId == w.RequiredItem);
            var consumed = session.Snapshot.Inventory.FirstOrDefault(i => i.ItemType == w.ConsumedType && i.ItemId == w.ConsumedItem);
            if (w.RequiredItem > 0 && required.IsEmpty || w.ConsumedItem > 0 && consumed.IsEmpty) { travelStatus = T("Required item is missing.", "Нет нужного предмета."); RefreshTravel(); return Task.CompletedTask; }
            var npc = travelNpc;
            return BeginTravel(ProtocolTravel.WarpOk, async () =>
            {
                if (SyncWarpPosition != null && !await SyncWarpPosition(npc, travelToken))
                {
                    travelPending = 0;
                    travelStatus = T("Move closer to the portal.", "Подойдите ближе к порталу.");
                    RefreshTravel();
                    return;
                }
                await travelClient.WarpNpcAsync(npc.Id, w.Id, w.RequiredItem > 0 ? (w.RequiredType == 8 ? 2 : 1) : 4,
                    w.RequiredItem > 0 ? required.Slot : 0, w.ConsumedItem > 0 ? consumed.Slot : -1, travelToken);
            });
        }
    }
}
