using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Network;
using UnityEngine;
using UnityEngine.UIElements;
using static FFOne.UI.FFWidgets;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        VisualElement stationRoot;
        int selectedStationNano, stationRequest, stationSlot;
        uint stationReply;
        float stationDeadline;

        void OpenNanoStation()
        {
            Windows.Open(GameplayWindow.Nanos);
            stationRoot = Windows.View;
        }

        void ShowNanoCard(VisualElement root, int id)
        {
            root.Q("nano-card-modal")?.RemoveFromHierarchy();
            var catalog=GameplayUiCatalog.Load();
            var entry=catalog.Nano(id,language);
            if (entry==null) return;
            selectedStationNano=id;
            detailOpener = root.Q<Button>("inventory-nano-" + id);
            var state=session.Snapshot;
            var nano=state.Nanos.FirstOrDefault(n=>n.Id==id);
            int equipped=Array.IndexOf(state.NanoSlots.ToArray(),id);
            var shell=root.Q("inventory-window");
            var modal=Group(shell,"nano-card-modal");
            Rect(modal,0,0,1036,653); modal.pickingMode=PickingMode.Position;
            var panel=Group(modal,"nano-viewer");
            var x=equipped>=0?115.5f:618f;
            Rect(panel,x,nano.IsOwned?(equipped>=0?11.5f:16.5f):96.5f,equipped>=0?370:360,nano.IsOwned?(equipped>=0?640:620):460);
            FFStyle.Background(panel,EquipArt+(equipped>=0?"nano-popup-equipped":nano.IsOwned?"nano-popup":"nano-popup-next"));
            var content=Group(panel,"nano-card-content");
            Rect(content,equipped>=0?2.5f:0,equipped>=0?18:0,360,620);
            if(equipped>=0)
            {
                FFStyle.Background(content,EquipArt+"nano-popup");
                Label(panel,"equipped-caption",LocalizationCatalog.Lookup("ui.inventory.equipped",language),13.5f,3,130,30,false,16,false).style.color=new Color(0,.2f,.4f);
            }
            Button(panel,"close-nano-card","",327,0,32,32,MapArtRoot+"controls/close",MapArtRoot+"controls/closeover",
                ()=> {playSound?.Invoke("button");CloseInventoryDetail();});
            Image(content,"nano-card-icon",nano.IsOwned?entry.IconAddress:entry.NanoReadyIconAddress,17,15,62,62);
            Label(content,"nano-name",entry.Name,88,15,200,20,false,14,false);
            string affinity=entry.NanoStyle switch {0=>T("ADAPTIUM","АДАПТИУМ"),1=>T("BLASTONS","БЛАСТОН"),2=>T("COSMIX","КОСМИКС"),_=>""};
            Label(content,"nano-affinity",affinity,88,47,180,20,false,14).style.color=Color.yellow;
            var description=Label(content,"nano-description",entry.Description,17,85,335,27,false,8,false);
            description.style.whiteSpace=WhiteSpace.Normal;description.style.color=Color.cyan;
            if(nano.IsOwned)Label(content,"current-power",LocalizationCatalog.Lookup("ui.inventory.nano.current_power",language),20,120,100,10,false,8).style.color=new Color(0,.2f,.4f);
            var tunes=catalog.Tunes(id).OrderByDescending(t=>t.Skill==nano.SkillId).ToArray();
            bool station=root==stationRoot && nano.IsOwned;
            for(int i=0;i<tunes.Length && i<3;i++)
            {
                var tune=tunes[i];
                int y=i==0?133:i==1?222:383;
                Image(content,"skill-"+i,catalog.SkillIcon(tune.Skill),18,y,35,35);
                string powerName = catalog.NanoPowerName(id, tune.Skill, language);
                Label(content,"skill-name-"+i,powerName,53,y+2,260,20,false,14,false);
                string desc=LocalizationCatalog.Lookup("content.tabledata.nano.nano_tune_string."+tune.TextIndex+".str_comment",language);
                if(desc.StartsWith("content.",StringComparison.Ordinal))desc=tune.Description;
                var skillInfo=Label(content,"skill-desc-"+i,desc,20,i==0?173:i==1?268:430,310,i==0?27:40,false,8,false);
                skillInfo.style.whiteSpace=WhiteSpace.Normal;skillInfo.style.color=Color.cyan;
                if(!station || tune.Skill==nano.SkillId) continue;
                var slots=new List<int>();
                int available=0;
                if(nano.SkillId>0)
                    foreach(var item in state.Inventory.Where(item=>item.ItemType==7 && item.ItemId==tune.Item))
                    { if(available>=tune.Count || slots.Count==10)break; slots.Add(item.Slot); available+=item.Quantity; }
                bool free=nano.SkillId<=0;
                int cost=free?0:catalog.TuningPrice(state.Level);
                if(!free)
                {
                    int barY=i==1?331:492;
                    for(int b=0;b<2;b++)
                    {
                        int yy=barY+b*17;
                        Image(content,"tuning-bar-"+i+"-"+b,EquipArt+(b==0?"nano-popup-fm-bar":"nano-popup-item-bar"),176,yy,126,9);
                        Label(content,"required-"+i+"-"+b,b==0?T("FUSION MATTER","МАТЕРИЯ ФЬЮЖН"):catalog.Item(7,tune.Item,language)?.Name??"",20,yy-3,140,13,false,8);
                        Label(content,"progress-"+i+"-"+b,b==0?$"{state.FusionMatter} / {cost}":$"{available} / {tune.Count}",176,yy-3,126,13,false,8);
                    }
                }
                var action=Button(content,"tune-"+tune.Id,T("TUNE","НАСТРОИТЬ"),219,i==0?194:i==1?358:521,110,22,
                    EquipArt+"button-normal",EquipArt+"button-hover",
                    ()=> ShowPrompt(entry.Name,powerName+(free?"":$"   {cost} FM"),null,
                        _=>StationSend(id,tune.Skill,0x31000029,()=>travelClient.TuneNanoAsync(id,tune.Id,slots.ToArray(),travelToken)), null));
                action.SetEnabled(stationReply==0 && (free || state.FusionMatter>=cost && available>=tune.Count));
            }
            if(!station || nano.SkillId<=0) return;
            if(equipped<0) Label(content,"equip-title",LocalizationCatalog.Lookup("ui.nano_station.equip",language),155,556,75,16,false,14);
            for(int i=0;i<3;i++)
            {
                int index=i;
                if(equipped>=0 && equipped!=i)continue;
                var label=LocalizationCatalog.Lookup(equipped>=0?"ui.nano_station.unequip":"ui.nano_station.slot",language).Replace("{slot}",(i+1).ToString());
                Button(content,"station-slot-"+i,label,equipped>=0?220:25+i*105,574,equipped>=0?120:101,26,
                    EquipArt+"button-normal",EquipArt+"button-hover",
                    ()=> _=StationSend(id,index,equipped>=0?ProtocolGameplay.NanoUnequipSuccess:ProtocolGameplay.NanoEquipSuccess,
                        ()=>equipped>=0?travelClient.UnequipNanoAsync(index,travelToken):travelClient.EquipNanoAsync(id,index,travelToken)))
                    .SetEnabled(stationReply==0 && (equipped>=0 || state.ActiveNanoSlot!=i));
            }
        }

        async Task StationSend(int id,int slot,uint reply,Func<Task> send)
        {
            if(stationReply!=0 || Windows.View!=stationRoot || disposed || !TravelAvailable)return;
            stationReply=reply;stationRequest=id;stationSlot=slot;playSound?.Invoke("button");
            stationDeadline=Time.unscaledTime+12;
            ShowNanoCard(stationRoot,id);
            try { await send(); }
            catch(Exception error){if(!disposed)StationFailure(error.Message);}
        }

        void StationFailure(string message)
        {
            stationReply=0;
            if(Windows.View==stationRoot && stationRoot?.Q("nano-card-modal")!=null)ShowNanoCard(stationRoot,selectedStationNano);
            Error?.Invoke(message);
        }

        internal void ReceiveNanoStation(ProtocolFrame frame)
        {
            var p=frame.Payload;
            if(frame.PacketType==0x31000029) session.ApplyNanoTune(p);
            ReceiveNanoReward(frame);
            if(stationReply==0)return;
            bool failed=frame.PacketType==0x31000055 && stationReply==0x31000029;
            if(!failed && frame.PacketType!=stationReply)return;
            if(!failed && stationReply==ProtocolGameplay.NanoEquipSuccess && (Protocol0104.Read16(p,0)!=stationRequest || Protocol0104.Read16(p,2)!=stationSlot))return;
            if(!failed && stationReply==ProtocolGameplay.NanoUnequipSuccess && Protocol0104.Read16(p,0)!=stationSlot)return;
            if(!failed && stationReply==0x31000029 && (Protocol0104.Read16(p,0)!=stationRequest || Protocol0104.Read16(p,2)!=stationSlot))return;
            stationReply=0;
            if(Windows.View==stationRoot && stationRoot!=null && stationRoot.Q("nano-card-modal")!=null) ShowNanoCard(stationRoot,selectedStationNano);
            if(failed) Error?.Invoke(T("Tuning failed.","Настройка не удалась."));
        }
    }

    public sealed partial class GameplayUiState
    {
        internal GameplayUiState WithReportedMatter(int amount)
        { var copy=(GameplayUiState)MemberwiseClone();copy.FusionMatter=Math.Max(0,amount);return copy; }
    }

    public sealed partial class GameplayUiSession
    {
        internal void ApplyNanoTune(byte[] p)
        {
            Protocol0104.RequireSize(p,168);
            int id=Protocol0104.Read16(p,0), skill=Protocol0104.Read16(p,2);
            var nano=Snapshot.Nanos.FirstOrDefault(n=>n.Id==id);
            if(id<=0 || skill<=0 || !nano.IsOwned) throw new ProtocolException("Unknown Nano in tuning acknowledgement.");
            var next=Snapshot.WithNanos(new[]{new GameplayNanoState(nano.BankIndex,id,skill,nano.Stamina)}).WithReportedMatter(Protocol0104.Read32(p,4));
            var seen=new HashSet<int>();
            for(int i=0;i<10;i++)
            {
                int slot=Protocol0104.Read32(p,8+4*i);
                if(slot==-1)continue;
                if(slot<0 || slot>=next.Inventory.Count || !seen.Add(slot))throw new ProtocolException("Invalid tuning item slot.");
                var item=ProtocolTravel.Words(1,slot,0,0,0);
                Array.Copy(p,48+i*12,item,8,12); next=next.WithGivenItem(item);
            }
            Publish(next);
        }
    }
}
