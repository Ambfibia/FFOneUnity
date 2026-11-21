using System;
using System.Collections.Generic;
using System.Linq;
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
        WorldEntity serviceMenuNpc;

        string ServiceText(string key) => FFOne.Content.LocalizationCatalog.Lookup("ui.mission.npc.service." + key,language);

        VisualElement BuildNpcServices()
        {
            const string quest="ui/en/gameplay/mission/npc/", journal="ui/en/gameplay/journal/";
            var npc=serviceMenuNpc;
            var definition=WorldNpcCatalog.Load().Find(npc.NpcType);
            var rows=new List<(string id,string label,string icon,Action run)>();
            if(definition.IsVendor)
                rows.Add(("npc-store",ServiceText("store"),journal+"npcicon_vendor",()=>OpenNpcService(npc,definition)));
            switch(definition.Category)
            {
                case 7: case 8: case 9:
                    rows.Add(("npc-use-service",ServiceText("nano_station"),journal+"npcicon_nanotune",OpenNanoStation)); break;
                case 13: case 14:
                    if(definition.Category==13)
                        rows.Add(("race-start",raceRunning?ServiceText("race_cancel"):ServiceText("race"),journal+"npcicon_ep",
                            ()=> {Windows.Close();_=raceRunning?CancelRace():SendRace(0x310000a4,()=>travelClient.RaceStartAsync(npc.Id,true,0,travelToken));}));
                    rows.Add(("race-rank",ServiceText("race_rank"),journal+"npcicon_08",()=>{
                        raceScreen=RaceScreen.Rankings;rankIndex=rankPage=rankPeriod=0; Windows.Open(GameplayWindow.Race);
                    }));break;
                case 28:
                    rows.Add(("npc-barber", ServiceText("barber"), "ui/en/gameplay/interaction/icons/auction", () => OpenBarber(npc))); break;
                case 26:
                    rows.Add(("npc-use-service",ServiceText("combine"),journal+"npcicon_20",()=>OpenCroc(npc)));break;
                default:
                    if (transportHub == null) break;
                    rows.Add(("npc-use-service",ServiceText("transportation"),journal+(transportHub.Type==2?"npcicon_10":"npcicon_11"),()=>{
                        travelNpc=npc;crocView=false;travelStatus="";Windows.Open(GameplayWindow.Travel);
                    }));break;
            }
            var missions=WorldMissionCatalog.Load();
            if(missions.Tasks.Any(m=>m.GiverNpc==npc.NpcType && MissionOffered(m,missions,session.Snapshot)))
                rows.Add(("npc-missions",T("MISSIONS","ЗАДАНИЯ"),MissionServiceIcon,()=>OpenNpcConversation(npc)));
            rows.Add(("npc-dismiss",T("CLOSE","ЗАКРЫТЬ"),quest+"npcicon_exit",()=>Windows.Close()));

            return BuildServiceRows(definition, rows);
        }

        const string ServiceMenuView = "ui/views/Retrobution/MissionJournal";
        internal const string MissionServiceIcon = "ui/en/gameplay/mission/npc/current_mission";
        VisualElement serviceMenuPanel;

        internal static Texture2D LoadServiceIcon(string address, string locale)
        {
            if (locale == "ru" && address == MissionServiceIcon)
            {
                var localized = Texture("ui/ru/gameplay/mission/npc/current_mission");
                if (localized) return localized;
            }
            return Texture(address);
        }

        VisualElement BuildServiceRows(WorldNpcDefinition definition, List<(string id,string label,string icon,Action run)> rows, Dictionary<string, Button> actions = null)
        {
            var root = FFView.InstantiateClass(ServiceMenuView, "npc-service-root");
            var panel = FFView.RequireClass<VisualElement>(root, "npc-service-window");
            serviceMenuPanel = panel;
            panel.EnableInClassList("npc-service-window--compact", rows.Count <= 2);
            var title = FFView.RequireClass<Label>(panel, "npc-service-name");
            title.text = FFStyle.Caption(definition.Name);
            AutoFit(title);
            var list = FFView.RequireClass<VisualElement>(panel, "npc-service-rows");
            foreach (var choice in rows)
            {
                var hit = (Button)FFView.InstantiateClass(ServiceMenuView, "npc-service-action");
                hit.name = choice.id;
                hit.text = FFStyle.Caption(choice.label);
                FFView.RequireClass<Image>(hit, "npc-service-icon").image = LoadServiceIcon(choice.icon, language);
                hit.clicked += () => {
                    if (!TravelAvailable) return;
                    playSound?.Invoke("button");
                    choice.run();
                };
                actions?.Add(choice.id, hit);
                list.Add(hit);
                AutoFit(hit);
            }
            FFView.RequireClass<Button>(panel, "npc-service-close").clicked += () => Windows.Close();
            root.RegisterCallback<GeometryChangedEvent>(_ => ScaleServiceMenu(panel, root.resolvedStyle.height));
            return root;
        }

        static void ScaleServiceMenu(VisualElement panel, float height)
        {
            if (panel == null || !float.IsFinite(height) || height <= 0) return;
            float scale = RetrobutionLayout.Scale(height);
            panel.style.scale = new Scale(new Vector3(scale, scale, 1));
        }
    }
}
