using System;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Network;
using FFOne.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        FusionClient vehicleClient;
        CancellationToken vehicleToken;
        Func<bool> vehicleWorldGate;
        Button vehicleButton;
        uint vehiclePending;
        float vehicleDeadline;
        string vehicleStatus = "";
        public bool VehicleBusy => vehiclePending != 0;
        public void ConfigureVehicle(FusionClient client, CancellationToken token, Func<bool> worldGate)
        {
            vehicleClient = client; vehicleToken = token; vehicleWorldGate = worldGate;
            vehicleButton = ServiceButton(parent, "vehicle-toggle", "VEHICLE", 18, 210, 200, () => _ = ToggleVehicle());
            RefreshVehicle();
        }
        bool VehicleAvailable => vehicleClient != null && !disposed && !interactionSuspended && !VehicleBusy && !TravelBusy && !MailBusy &&
            !ServiceActive && !serviceFaulted && !inventoryPending && !TradeWindowActive && !Windows.IsOpen && prompt == null &&
            trade?.Snapshot.Phase != GameplayTradePhase.OutgoingOffer && trade?.Snapshot.Phase != GameplayTradePhase.IncomingOffer &&
            session.Snapshot.Hp > 0 && vehicleWorldGate?.Invoke() == true;
        public async Task ToggleVehicle()
        {
            if (!VehicleAvailable) return;
            var state = session.Snapshot; bool mounted = !state.VehicleMounted;
            var item = state.Equipment[ProtocolVehicle.EquipmentSlot];
            if (mounted && (state.MapNumber != 0 || state.IsTutorial || item.IsEmpty || item.ItemType != 10))
            { vehicleStatus = T("Equip a vehicle in the overworld first.", "Сначала наденьте транспорт в открытом мире."); RefreshVehicle(); return; }
            vehiclePending = mounted ? ProtocolVehicle.OnSuccess : ProtocolVehicle.OffSuccess;
            vehicleDeadline = Time.unscaledTime + 3; vehicleStatus = T("Waiting for server…", "Ожидание сервера…"); RefreshVehicle();
            try { await vehicleClient.SetVehicleAsync(mounted, vehicleToken); }
            catch (Exception e) { if (!disposed) { vehiclePending = 0; vehicleStatus = e.Message; RefreshVehicle(); Error?.Invoke(e.Message); } }
        }
        public void ReceiveVehicle(ProtocolFrame frame)
        {
            if (!ProtocolVehicle.TryDecode(frame, out var v)) { if (frame.PacketType == ProtocolVehicle.ExpiredItems) RefreshVehicle(); return; }
            if (v.Type == ProtocolVehicle.StateChanged)
            {
                if (v.PlayerId != session.Snapshot.PlayerId) return;
                // State broadcasts are authoritative too; a missing direct reply must not keep the key locked.
                if (vehiclePending == (v.Mounted ? ProtocolVehicle.OnSuccess : ProtocolVehicle.OffSuccess)) vehiclePending = 0;
            }
            else if (vehiclePending != 0 && (v.Type == vehiclePending || v.Type == vehiclePending + 1 || v.Type == ProtocolVehicle.OffSuccess)) vehiclePending = 0;
            vehicleStatus = v.Failed ? T("Server rejected vehicle toggle: ", "Сервер отклонил транспорт: ") + v.Error : "";
            RefreshVehicle();
        }
        void TickVehicle()
        {
            if (vehiclePending != 0 && Time.unscaledTime >= vehicleDeadline)
            { vehiclePending = 0; vehicleStatus = T("No vehicle acknowledgement; confirmed state retained.", "Нет подтверждения транспорта; сохранено подтверждённое состояние."); }
            RefreshVehicle();
        }
        void RefreshVehicle()
        {
            if (vehicleButton == null) return;
            var state = session.Snapshot;
            vehicleButton.text = state.VehicleMounted ? T("DISMOUNT", "СЛЕЗТЬ") : T("VEHICLE", "ТРАНСПОРТ");
            vehicleButton.tooltip = vehicleStatus.Length > 0 ? vehicleStatus : T("Vehicle toggle", "Транспорт") + " (V)";
            vehicleButton.style.display = state.IsActive && !state.IsTutorial && !Windows.IsOpen && !interactionSuspended && prompt == null ? DisplayStyle.Flex : DisplayStyle.None;
            var item = state.Equipment.Count > ProtocolVehicle.EquipmentSlot ? state.Equipment[ProtocolVehicle.EquipmentSlot] : default;
            vehicleButton.SetEnabled(VehicleAvailable && (state.VehicleMounted || state.MapNumber == 0 && !item.IsEmpty && item.ItemType == 10));
        }
        void DisposeVehicle() { vehicleButton?.RemoveFromHierarchy(); vehicleButton = null; vehicleClient = null; vehiclePending = 0; }
    }
    public sealed partial class FrontendController
    {
        void RefreshPersonalVehicle()
        {
            if (!networkWorld || gameplayState == null) return;
            var state = gameplayState.Snapshot;
            int id = state.VehicleMounted && state.MapNumber == 0 && state.Hp > 0 && !networkWorld.Riding && networkWorld.SliderId == 0
                ? state.Equipment[ProtocolVehicle.EquipmentSlot].ItemId : 0;
            networkWorld.SetPersonalVehicle(id);
        }
    }
}
