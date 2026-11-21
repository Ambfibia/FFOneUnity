using System;
using System.Globalization;
using System.Collections.Generic;
using System.Net.Http;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using FFOne.Content;
using FFOne.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using static FFOne.UI.FFWidgets;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        const string RankArt="ui/en/race/rank/";
        string rankUrl;
        int rankPage,rankPeriod,rankIndex;
        CancellationTokenSource rankRequest;
        JToken[] rankLocations;
        XElement rankScores;
        string rankStatus;
        static readonly HttpClient RankHttp = new HttpClient { Timeout=TimeSpan.FromSeconds(10), MaxResponseContentBufferSize=512*1024 };
        static readonly string[] RankSections={"day","week","month","alltime"};
        static readonly string[] PeriodKeys={"today","week","month","all_time"};
        string RankText(string key) => LocalizationCatalog.Lookup("ui.race.rank."+key,language);

        internal void ConfigureRankProvider(string host)
        {
            string custom=Environment.GetEnvironmentVariable("FFONE_RACE_RANK_URL");
            if(Uri.TryCreate(custom,UriKind.Absolute,out var uri) && (uri.Scheme=="https" || uri.Scheme=="http"))
                rankUrl=uri.AbsoluteUri;
            else if(string.Equals(host,"retrobution.xyz",StringComparison.OrdinalIgnoreCase) ||
                    host?.EndsWith(".retrobution.xyz",StringComparison.OrdinalIgnoreCase)==true)
                rankUrl="http://api.retrobution.xyz/getranks";
            else if(string.Equals(host,"api.slavicfall.ru",StringComparison.OrdinalIgnoreCase))
                rankUrl="https://api.slavicfall.ru/getranks";
            else if(Uri.TryCreate("http://"+host,UriKind.Absolute,out var local) && local.IsLoopback)
                rankUrl=new UriBuilder(local){Port=8888,Path="getranks"}.Uri.AbsoluteUri;
        }

        void CancelRankRequest()
        {
            rankRequest?.Cancel(); rankRequest?.Dispose(); rankRequest=null;
        }

        async Task FetchRankings(VisualElement right)
        {
            CancelRankRequest();
            rankScores=null;
            if(string.IsNullOrEmpty(rankUrl)) { rankStatus=T("Rankings unavailable.","Рейтинг недоступен."); DrawScores(right); return; }
            var request=rankRequest=new CancellationTokenSource();
            var token=request.Token;
            rankStatus=T("Loading…","Загрузка…"); DrawScores(right);
            try
            {
                using var form=new FormUrlEncodedContent(new Dictionary<string,string>{
                    ["PCUID"]=session.Snapshot.CharacterUid.ToString(CultureInfo.InvariantCulture),
                    ["EP_ID"]=((int)rankLocations[rankIndex]["ep_id"]).ToString(CultureInfo.InvariantCulture)
                });
                using var response=await RankHttp.PostAsync(rankUrl,form,token);
                response.EnsureSuccessStatusCode();
                string body=await response.Content.ReadAsStringAsync();
                if(token.IsCancellationRequested)return;
                var scores=new XElement("scores");
                if(body.Contains("SUCCESS"))
                {
                    foreach(var period in RankSections)
                        foreach(var section in new[]{"my"+period,period})
                        {
                            string open="<"+section+">", close="</"+section+">";
                            int from=body.IndexOf(open,StringComparison.Ordinal), to=body.IndexOf(close,StringComparison.Ordinal);
                            if(from<0 || to<from)throw new FormatException(section);
                            var rows=XElement.Parse(body.Substring(from,to+close.Length-from));
                            foreach(var score in rows.Descendants("score").Where(s=>!s.HasAttributes).ToArray())
                            {
                                // OFAPI writes the attribute list inside the score element.
                                var fields=XElement.Parse("<score "+System.Security.SecurityElement.Escape(score.Value)
                                    .Replace("&quot;","\"")+"/>");
                                score.ReplaceWith(fields);
                            }
                            scores.Add(rows);
                        }
                }
                rankScores=scores; rankStatus="";
            }
            catch(Exception) { if(token.IsCancellationRequested)return; rankStatus=T("Rankings unavailable.","Рейтинг недоступен."); }
            if(!token.IsCancellationRequested) DrawScores(right);
        }

        void BuildRaceRankings(VisualElement root,VisualElement shell)
        {
            rankLocations??=NativeData.Object("ui/en/race/rank/locations")["locations"].OrderBy(t=>(int)t["sort_index"]).ToArray();
            root.Insert(0,Image(root,"inventory-backdrop",RankArt+"skin/panel_backdrop",0,0,1920,1440));
            var background=Image(shell,"rank-shell",RankArt+"skin/shell",0,0,1035,653);
            var left=Group(shell,"rank-left"); Rect(left,8,7,490,632);
            var right=Group(shell,"rank-right"); Rect(right,511,7,472,632);
            Image(left,"rank-title",RankArt+"skin/title",0,0,481,87);
            Label(left,"rank-title-copy",RankText("title"),10,7,164,20,false,16,false);
            Label(left,"rank-npc-name",raceNpc==null?"":WorldNpcCatalog.Load().Find(raceNpc.NpcType)?.Name,10,26,190,20,false,14,false);
            var portrait=Image(left,"rank-portrait",null,230,0,200,150);
            new GameplayNpcPortrait(portrait,()=>raceNpc==null?null:ResolveServiceNpc?.Invoke(raceNpc.Id),200,150,.5f,1.2f,0);
            Image(left,"rank-locations-back",RankArt+"skin/location_back",0,137,477,492);
            Image(left,"rank-black",RankArt+"skin/black",2,168,473,434);
            Label(left,"rank-locations-title",RankText("locations"),9,143,270,25,false,16,false);
            var rows=Group(left,"rank-locations"); Rect(rows,7,172,486,432);
            var page=Label(left,"rank-page","",133,605,213,20,true,12);
            Button previous=null,next=null;
            previous=Button(left,"rank-previous","",1,603,130,25,RankArt+"controls/previous",RankArt+"controls/previous_hover",()=>ChangePage(-1));
            next=Button(left,"rank-next","",346,603,130,25,RankArt+"controls/next",RankArt+"controls/next_hover",()=>ChangePage(1));
            Image(left,"arrow-left",RankArt+"controls/arrow_left",11,611,6,11);
            Image(left,"arrow-right",RankArt+"controls/arrow_right",460,611,6,11);
            var close=Button(shell,"rank-close","",988,7,30,30,RankArt+"controls/close",RankArt+"controls/close_hover",()=>{playSound?.Invoke("button");Windows.Close();});
            var help=Button(shell,"rank-help","",993,599,30,30,RankArt+"controls/help",RankArt+"controls/help_hover",null);
            float opened=Time.unscaledTime;
            IVisualElementScheduledItem slide=null;
            slide=root.schedule.Execute(()=>{
                // ScrollWindow advances the same clock for each side in one GUI pass.
                float t=Mathf.Clamp01(2*(Time.unscaledTime-opened));
                float leftPhase=Mathf.Max(0,t-Time.unscaledDeltaTime);
                float margin=shell.worldBound.xMin/Mathf.Max(.1f,shell.resolvedStyle.scale.value.x);
                float dx=-(margin+498)*(1-Mathf.Sin((t==1?1:leftPhase)*Mathf.PI/2));
                float rightDx=(margin+517)*(1-Mathf.Sin(t*Mathf.PI/2));
                left.style.left=8+dx; background.style.left=dx;
                right.style.left=511+rightDx; close.style.left=988+rightDx; help.style.left=993+rightDx;
                if(t==1)slide.Pause();
            }).Every(16);
            void ChangePage(int step)
            {
                rankPage=Mathf.Clamp(rankPage+step,0,(rankLocations.Length-1)/5);
                rankIndex=rankPage*5; playSound?.Invoke("button"); DrawLocations(); _=FetchRankings(right);
            }
            void DrawLocations()
            {
                rows.Clear();
                previous.SetEnabled(rankPage>0); next.SetEnabled((rankPage+1)*5<rankLocations.Length);
                page.text=RankText("page").Replace("{first}",(rankPage*5+1).ToString()).Replace("{last}",Math.Min(rankLocations.Length,rankPage*5+5).ToString()).Replace("{total}",rankLocations.Length.ToString());
                for(int i=rankPage*5;i<Math.Min(rankPage*5+5,rankLocations.Length);i++)
                {
                    var loc=rankLocations[i];int index=i;
                    var row=Button(rows,"rank-location-"+(int)loc["ep_id"],"",0,(i%5)*86,460,80,RankArt+"skin/location_row",RankArt+"skin/location_row_selected",()=>{
                        rankIndex=index; playSound?.Invoke("button"); DrawLocations(); _=FetchRankings(right);
                    });
                    row.AddToClassList("ff-rank-location");
                    row.EnableInClassList("ff-rank-location--selected", i == rankIndex);
                    if(i==rankIndex)
                    {
                        Image(row,"selected",RankArt+"skin/location_row_selected",0,0,460,80);
                        Image(row,"selection-outline",RankArt+"skin/selection_outline",-3,-3,466,86);
                        Image(row,"selection-point",RankArt+"skin/selection_point",460,30,30,22);
                    }
                    Image(row,"icon",((string)loc["small_image"]).Replace(".png",""),5,5,68,68);
                    Label(row,"name",(string)loc["name"],80,17,300,20,false,14,false).AddToClassList("ff-rank-location-copy");
                    Label(row,"area",(string)loc["area_name"],80,42,300,20,true,12,false).AddToClassList("ff-rank-location-copy");
                }
            }
            DrawLocations(); _=FetchRankings(right);
            root.RegisterCallback<DetachFromPanelEvent>(_=>CancelRankRequest());
        }

        void DrawScores(VisualElement right)
        {
            right.Clear(); var loc=rankLocations[rankIndex];
            Image(right,"rank-right-back",RankArt+"skin/right_back",0,0,472,632);
            Label(right,"location-label",RankText("location_label"),8,8,220,15,false,12,false);
            Label(right,"location",(string)loc["name"],8,23,211,40,false,16,false);
            Label(right,"area",(string)loc["area_name"],8,63,220,18,true,12,false);
            Image(right,"preview",((string)loc["big_image"]).Replace(".png",""),230,3,239,127);
            int[] tabX={0,68,188,310},hitX={16,103,214,350};
            for(int i=0;i<4;i++)
            {
                string skin=i==0?"today":"wide";
                Image(right,"period-art-"+i,RankArt+"skin/"+skin+(i==rankPeriod?"_active":"_idle"),tabX[i],110,i==0?100:153,29);
            }
            Image(right,"tab-bar",RankArt+"skin/tab_bar",0,130,472,20);
            for(int i=0;i<4;i++)
            {
                int index=i;
                var tab=Button(right,"rank-period-"+i,RankText("period."+PeriodKeys[i]),hitX[i],112,i==0?62:92,17,null,null,()=>{
                    rankPeriod=index; playSound?.Invoke("tab_click01");DrawScores(right);
                });
                tab.style.backgroundColor=Color.clear;
                var visual=right.Q<Image>("period-art-"+i);
                string skin=RankArt+"skin/"+(i==0?"today":"wide");
                tab.RegisterCallback<PointerEnterEvent>(_=>{if(rankPeriod!=index)visual.image=Texture(skin+"_hover");});
                tab.RegisterCallback<PointerLeaveEvent>(_=>{if(rankPeriod!=index)visual.image=Texture(skin+"_idle");});
            }
            for(int group=0;group<2;group++)
            {
                int offset=group*110;
                Image(right,"header-"+group,RankArt+"skin/sky",2,150+offset,468,25);
                Label(right,"caption-"+group,RankText((group==0?"best.":"top.")+PeriodKeys[rankPeriod]),10,151+offset,450,26,false,14,false)
                    .AddToClassList("ff-rank-section-caption");
                string[] columns={"rank","player","top_score"};
                int[] x={15,165,367},w={89,180,103};
                for(int c=0;c<3;c++)Label(right,"heading-"+group+"-"+c,RankText("header."+columns[c]),x[c],178+offset,w[c],20,false,12,false);
                Image(right,"score-back-"+group,RankArt+"skin/black",2,200+offset,468,group==0?60:300);
                var scores=rankScores?.Element((group==0?"my":"")+RankSections[rankPeriod])?.Descendants("score").Take(group==0?1:10).ToArray();
                if(scores==null || scores.Length==0)
                    Label(right,"score-status-"+group,string.IsNullOrEmpty(rankStatus)?RankText("no_score"):rankStatus,15,213+offset,440,25,true,13);
                else for(int i=0;i<scores.Length;i++)
                {
                    var s=scores[i];int y=(group==0?217:313)+i*30;
                    bool highlighted=group==1 && (string)s.Attribute("PCUID")==session.Snapshot.CharacterUid.ToString(CultureInfo.InvariantCulture);
                    if(highlighted)
                        Image(right,"local-rank",RankArt+"skin/highlight",2,305+i*30,500,30);
                    string[] values={(string)s.Attribute("Rank"),((string)s.Attribute("FirstName")+" "+(string)s.Attribute("LastName")).Trim(),(string)s.Attribute("Score")};
                    for(int c=0;c<3;c++)
                    {
                        var label=Label(right,"score-"+group+"-"+i+"-"+c,values[c],x[c],y,w[c],20,true,13,false);
                        if(highlighted)label.style.color=new Color(0,0,.14919356f);
                    }
                }
            }
        }
    }
}
