using System.Linq;
using FFOne.Content;
using FFOne.Network;
using FFOne.World;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        const string NpcMissionTree = "ui/views/Retrobution/MissionJournal";
        WorldEntity missionMenuNpc;
        VisualElement nextMissionMenu;

        internal void OpenNpcConversation(WorldEntity npc)
        {
            if (npc == null || npc.Kind != WorldEntityKind.Npc || npc.Hp <= 0 || AdvanceIntermediateMission(npc)) return;
            nextMissionMenu = FFView.OptionalClass(NpcMissionTree, "npc-mission-screen");
            if (nextMissionMenu == null) { OpenNpcMissions(npc); return; }
            missionMenuNpc = npc;
            Windows.Open(GameplayWindow.NpcMissions);
        }

        VisualElement BuildNpcMissionMenu()
        {
            var root = nextMissionMenu ?? FFView.InstantiateClass(NpcMissionTree, "npc-mission-screen");
            nextMissionMenu = null;
            var npc = missionMenuNpc;
            var panel = FFView.RequireClass<VisualElement>(root, "npc-mission-panel");
            var name = LocalizationCatalog.Lookup("content.npc." + npc.NpcType + ".name",language);
            if (name.StartsWith("content.npc.")) name = WorldNpcCatalog.Load().Find(npc.NpcType)?.Name ?? "";
            FFView.RequireClass<Label>(panel,"npc-mission-name").text = name;
            var catalog = GameplayUiCatalog.Load();
            var rules = WorldMissionCatalog.Load();
            var state = session.Snapshot;
            var active = state.RunningQuests.Select(q => rules.Find(q.TaskId))
                .Where(r => r != null && (r.EndNpc == npc.NpcType || r.GiverNpc == npc.NpcType));
            var offers = rules.Tasks.Where(r => r.GiverNpc == npc.NpcType && MissionOffered(r,rules,state));
            Populate("npc-mission-active", "npc-mission-active-heading", T("Active missions:", "Активные задания:"), active);
            Populate("npc-mission-offers", "npc-mission-offer-heading", T("Available missions:", "Доступные задания:"), offers);
            var close = FFView.RequireClass<Button>(panel,"npc-mission-close");
            close.text = T("CLOSE","ЗАКРЫТЬ");
            close.clicked += () => Windows.Close();
            root.RegisterCallback<GeometryChangedEvent>(_ => ScaleServiceMenu(panel,root.resolvedStyle.height));
            return root;

            void Populate(string listClass, string titleClass, string caption, System.Collections.Generic.IEnumerable<WorldMissionDefinition> entries)
            {
                var list = FFView.RequireClass<VisualElement>(panel,listClass);
                var heading = FFView.RequireClass<Label>(panel,titleClass);
                heading.text = caption;
                foreach (var rule in entries)
                {
                    var row = (Button)FFView.InstantiateClass(NpcMissionTree,"npc-mission-option");
                    FFView.RequireClass<Label>(row,"npc-mission-option-title").text = catalog.Mission(rule.TaskId,language)?.Title ?? "";
                    FFView.RequireClass<Image>(row,"npc-mission-option-icon").image = FFWidgets.Texture("ui/en/gameplay/journal/world_icon");
                    row.clicked += () => { playSound?.Invoke("button"); OpenMissionReward(rule.TaskId,npc); };
                    list.Add(row);
                }
                heading.style.display = list.childCount == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }
    }
}
