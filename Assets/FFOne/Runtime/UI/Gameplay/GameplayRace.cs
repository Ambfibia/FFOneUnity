using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Network;
using FFOne.World;
using UnityEngine;
using UnityEngine.UIElements;
using static FFOne.UI.FFWidgets;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        enum RaceScreen { Rankings, Result }
        RaceScreen raceScreen;
        WorldEntity raceNpc;
        int raceLimit, raceMap, racePods;
        uint racePending;
        float raceStarted, raceDeadline;
        bool raceRunning, raceTimeoutRequested;
        byte[] raceResult;
        readonly Dictionary<string,int> raceRingIds = new();
        readonly HashSet<int> raceTaken = new();
        readonly HashSet<int> raceInFlight = new();

        void OpenRaceNpc(WorldEntity npc, int category)
        {
            if (racePending != 0) return;
            raceNpc = npc;
            if (category == 14 && raceRunning)
            {
                _ = SendRace(0x310000a6, () => travelClient.RaceEndAsync(npc.Id, 0, travelToken));
                return;
            }
            serviceMenuNpc=npc;
            Windows.Open(GameplayWindow.NpcServices);
        }

        VisualElement BuildRaceWindow()
        {
            var root = new VisualElement {name = "race-root"};
            root.StretchToParentSize();
            var shell = Group(root, "inventory-window");
            Rect(shell, 0, 0, 1036, 653);
            if (raceScreen == RaceScreen.Rankings) BuildRaceRankings(root, shell);
            else BuildRaceResult(shell);
            return root;
        }

        Task CancelRace() => SendRace(0x310000a8, () => travelClient.RaceCancelAsync(raceStartNpc,travelToken));
        int raceStartNpc;
        async Task SendRace(uint expected, Func<Task> send)
        {
            if (racePending!=0 || disposed || travelClient==null) return;
            if (expected==0x310000a4) raceStartNpc=raceNpc.Id;
            racePending=expected; raceDeadline=Time.unscaledTime+12;
            try { await send(); }
            catch(Exception error) { if (!disposed) { racePending=0; Error?.Invoke(error.Message); } }
        }

        internal void ReceiveRace(ProtocolFrame frame)
        {
            uint type=frame.PacketType;
            if(type<0x310000a4 || type>0x310000ab) return;
            var p=frame.Payload;
            if(type==0x310000aa)
            {
                Protocol0104.RequireSize(p,8);
                int id=Protocol0104.Read32(p,0);
                if (raceRunning && raceInFlight.Remove(id))
                {
                    raceTaken.Add(id); racePods=Protocol0104.Read32(p,4);
                    lastPickup=Time.unscaledTime; playSound?.Invoke("ring");
                }
                return;
            }
            if(type==0x310000ab) { raceInFlight.Clear(); return; }
            if(racePending==0 || type!=racePending && type!=racePending+1) return;
            racePending=0;
            if ((type&1)!=0)
            {
                if(type==0x310000a7) StopRace();
                Error?.Invoke(T("The race request was declined.","Сервер отклонил запрос гонки.")); return;
            }
            if(type==0x310000a4)
            {
                Protocol0104.RequireSize(p,12);
                raceLimit=Protocol0104.Read32(p,8); raceMap=session.Snapshot.MapNumber;
                raceStarted=Time.unscaledTime; racePods=0; raceRunning=true; raceTimeoutRequested=false;
                BuildRaceHud();
                playSound?.Invoke("racestart");
                ServiceVoice?.Invoke(raceNpc,"ClickStart");
            }
            else if(type==0x310000a6)
            {
                Protocol0104.RequireSize(p,72);
                session.ApplyRaceReward(p);
                raceResult=(byte[])p.Clone(); StopRace(); raceScreen=RaceScreen.Result;
                playSound?.Invoke("racefinish"); Windows.Open(GameplayWindow.Race);
                ServiceVoice?.Invoke(raceNpc,"RaceFinished");
            }
            else StopRace();
        }

        async Task CollectRing(int id)
        {
            raceInFlight.Add(id);
            try { await travelClient.RaceRingAsync(id,travelToken); }
            catch(Exception error) { raceInFlight.Remove(id); if(!disposed) Error?.Invoke(error.Message); }
        }

        void TickRace()
        {
            if(racePending!=0 && Time.unscaledTime>=raceDeadline)
            {
                racePending=0;
                Error?.Invoke(T("No race acknowledgement.","Сервер не подтвердил действие гонки."));
            }
            if(!raceRunning) return;
            var state=session.Snapshot;
            if(!state.IsActive || state.MapNumber!=raceMap) {StopRace();return;}
            float elapsed=Time.unscaledTime-raceStarted;
            int cs=(int)(elapsed*100), remaining=Mathf.Max(0,raceLimit-(int)elapsed);
            UpdateRaceHud(cs);
            if (remaining<=0 && !raceTimeoutRequested && racePending==0)
            { raceTimeoutRequested=true; _=CancelRace(); }
            foreach (var ring in WorldBehaviour.RaceRings)
            {
                if(!raceRingIds.TryGetValue(ring.Key,out int id)) raceRingIds.Add(ring.Key,id=raceRingIds.Count+1);
                bool available=!raceTaken.Contains(id) && !raceInFlight.Contains(id);
                ring.Show(available);
                if (!available || remaining==0) continue;
                var pos=state.WorldPosition;
                pos.y=Mathf.Clamp(ring.Center.y,pos.y+.35f,pos.y+1.45f);
                if ((pos-ring.Center).sqrMagnitude <= Mathf.Pow(ring.Radius+.35f,2)) _=CollectRing(id);
            }
        }

        void StopRace()
        {
            raceRunning=false; raceHud?.RemoveFromHierarchy(); raceHud=null;
            raceRingIds.Clear(); raceTaken.Clear(); raceInFlight.Clear();
            foreach(var ring in WorldBehaviour.RaceRings) ring.Show(false);
        }

        void ReleaseRace() {StopRace();CancelRankRequest();}

        void BuildRaceResult(VisualElement shell)
        {
            const string art="ui/en/race/result/";
            int Read(int offset) => Protocol0104.Read32(raceResult,offset);
            string Copy(string key) => LocalizationCatalog.Lookup("ui.race.mode."+key,language);
            var panel=Group(shell,"race-result"); Rect(panel,319,109.5f,398,434);
            FFStyle.Background(panel,art+"background");
            int rank=Read(16);
            for(int i=0;i<5;i++) Image(panel,"race-star-"+i,art+(i<6-rank?"star":"star_empty"),158-22*i,14,19,17);
            string[] ratings={"","genius","awesome","good","not_bad","bleh"};
            if(rank>=1 && rank<ratings.Length) Label(panel,"race-rating",Copy("rating."+ratings[rank]),202,14,120,20);
            Label(panel,"race-best",Copy("my_best_record"),200,45,190,24);
            void Score(int x,int y,string key,int value)
            {
                Label(panel,key+"-label-"+x,Copy(key),x,y,80,20,false,12,false);
                Label(panel,key+"-value-"+x,key=="time"?$"{value/60:00}:{value%60:00}":value.ToString(),x+80,y-2,77,20);
            }
            Score(20,81,"time",Read(4));Score(20,101,"pods",Read(8));Score(20,130,"score",Read(12));
            Score(210,70,"time",Read(32));Score(210,90,"pods",Read(36));Score(210,119,"score",Read(24));
            Label(panel,"reward",Copy("reward"),10,195,90,20,false,14,false);
            var item=ProtocolServices.Item(raceResult,44);
            if(item.ItemId>0)
            {
                Image(panel,"reward-item-bar",art+"item_bar",13,216,367,76);
                var entry=GameplayUiCatalog.Load().Item(item.ItemType,item.ItemId,language);
                if(entry!=null) { Image(panel,"reward-item",entry.IconAddress,20,224,62,62);
                    Label(panel,"reward-name",entry.Name,92,228,250,46); }
            }
            int shift=item.ItemId>0?85:0;
            Image(panel,"reward-fm-bar",art+"item_bar",13,216+shift,367,76);
            Image(panel,"reward-fm",art+"fusion_matter",20,224+shift,62,62);
            Label(panel,"reward-fm-copy",Copy("fusion_matter_reward").Replace("{amount}",Read(20).ToString()),100,224+shift,200,40,false,14,false);
            Button(panel,"race-accept",Copy("accept"),230,393,154,26,art+"button_normal",art+"button_hover",()=>{
                playSound?.Invoke("button"); Windows.Close(); raceResult=null;
            });
        }
    }

    public sealed partial class GameplayUiSession
    {
        internal void ApplyRaceReward(byte[] payload)
        {
            var next=Snapshot.WithReportedMatter(Protocol0104.Read32(payload,40));
            if(ProtocolServices.Item(payload,44).ItemId>0)
            {
                var grant=ProtocolTravel.Words(Protocol0104.Read32(payload,56),Protocol0104.Read32(payload,60),0,0,0);
                Array.Copy(payload,44,grant,8,12); next=next.WithGivenItem(grant);
            }
            Publish(next);
        }
    }
}
