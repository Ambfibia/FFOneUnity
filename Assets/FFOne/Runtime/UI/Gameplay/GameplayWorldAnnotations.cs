using System;
using System.Collections.Generic;
using System.Globalization;
using FFOne.Content;
using FFOne.Network;
using FFOne.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        GameplayWorldAnnotations worldAnnotations;
        bool showMyName, showPlayerNames, showGroupNames, showNpcNames, showMonsterNames;
        bool showDamageNumbers, showCombatChat;
        bool showBalloons = true;

        internal void ApplyWorldDisplayPreferences(JObject preferences)
        {
            var display = preferences?["display"];
            showMyName = (bool?)display?["my_name"] ?? false;
            showPlayerNames = (bool?)display?["other_name"] ?? false;
            showGroupNames = (bool?)display?["group_name"] ?? false;
            showNpcNames = (bool?)display?["npc_name"] ?? false;
            showMonsterNames = (bool?)display?["monster_name"] ?? false;
            showDamageNumbers = (bool?)display?["floating_numbers"] ?? false;
            showCombatChat = (bool?)display?["combat_in_chat"] ?? false;
            showBalloons = (bool?)display?["balloon"] ?? true;
            NanoHud.SetAnimated((bool?)display?["animated_nanos"] ?? true);
            worldAnnotations?.ApplyToggles(showDamageNumbers, showBalloons);
        }

        internal void UpdateWorldAnnotations(NetworkWorldController world, bool blocked)
        {
            UpdateGroupMapMarkers(world);
            bool hasNames = showMyName || showPlayerNames || showGroupNames || showNpcNames || showMonsterNames;
            if (worldAnnotations == null)
            {
                if ((!hasNames && !showBalloons) || !world || !world.IsReady) return;
                GetWorldAnnotations();
            }

            var state = session.Snapshot;
            worldAnnotations.Tick(world, blocked, state.PlayerId, state.DisplayName,
                ActorNameRole, showMyName, hasNames, language);
        }

        string ActorNameRole(NetworkEntityView actor)
        {
            if (actor.Entity.Kind == WorldEntityKind.Player)
            {
                if (actor.Entity.Id == session.Snapshot.PlayerId) return null;
                if (IsGroupMember(actor.Entity.Id)) return showGroupNames ? "group" : null;
                return showPlayerNames ? "player" : null;
            }
            if (actor.Entity.Kind != WorldEntityKind.Npc) return null;
            if (actor.Hostile) return showMonsterNames ? "hostile" : null;
            return showNpcNames ? "npc" : null;
        }

        bool IsGroupMember(int id)
        {
            foreach (var member in social.Members)
                if (member.Id == id) return true;
            return false;
        }

        GameplayWorldAnnotations GetWorldAnnotations()
        {
            return worldAnnotations ??= new GameplayWorldAnnotations(Hud.View, session.MaxHpAtLevel);
        }

        void RecordCombatPresentation(CombatUpdate combat, bool outgoing)
        {
            if (!showDamageNumbers && !showCombatChat) return;
            int playerId = session.Snapshot.PlayerId;
            bool chatChanged = false;
            foreach (var hit in combat.Hits)
            {
                if (hit.Damage <= 0) continue;
                bool incoming = hit.Kind == WorldEntityKind.Player && hit.Id == playerId;
                if (!incoming && !outgoing) continue;

                if (showDamageNumbers) GetWorldAnnotations().ShowDamage(hit, incoming);
                if (!showCombatChat) continue;
                string other = incoming ? ResolveCombatName(combat.AttackerKind, combat.AttackerId) : ResolveCombatName(hit.Kind, hit.Id);
                string template = incoming
                    ? T("{0} dealt {1} damage to you.", "{0} наносит вам {1} урона.")
                    : T("You dealt {1} damage to {0}.", "Ваш урон по цели «{0}» — {1}.");
                AppendChat(ChatChannel.All, string.Format(CultureInfo.InvariantCulture, template, other, hit.Damage), incoming ? "receive" : "attack");
                chatChanged = true;
            }
            if (chatChanged) RefreshChat(chatView);
        }

        string ResolveCombatName(WorldEntityKind kind, int id)
        {
            var entity = worldAnnotations?.FindActor(kind, id)?.Entity;
            if (entity == null && socialPeers != null)
                foreach (var peer in socialPeers())
                    if (peer.Kind == kind && peer.Id == id) { entity = peer; break; }
            string title = GameplayWorldAnnotations.ActorName(entity, language);
            return !string.IsNullOrEmpty(title) ? title : kind == WorldEntityKind.Player ? T("Player ", "Игрок ") + id : T("NPC ", "NPC ") + id;
        }

        internal void DisposeWorldAnnotations()
        {
            worldAnnotations?.Dispose();
            worldAnnotations = null;
        }
    }

    internal sealed class GameplayWorldAnnotations : IDisposable
    {
        const string HudAddress = "ui/views/Retrobution/GameplayHud";
        const float NpcDistance = 30, PlayerHeight = 1.6f, MinSpeechTime = 7, DamageLifetime = 1;
        const int MaxDamagePopups = 24, MaxBubbles = 32;
        readonly VisualElement overlay, nameLayer, bubbleLayer, damageLayer;
        readonly Dictionary<(WorldEntityKind, int), Mark> names = new();
        readonly Dictionary<(WorldEntityKind, int), Mark> bubbles = new();
        readonly List<Mark> damage = new();
        readonly Stack<Mark> namePool = new();
        readonly HashSet<(WorldEntityKind, int)> active = new();
        readonly List<(WorldEntityKind, int)> removed = new();
        NetworkWorldController lastWorld;
        NetworkWorldEntities replicaSet;
        int mapNumber;

        sealed class Mark
        {
            public VisualElement View;
            public Label Text, PlayerLevel;
            public VisualElement HealthFill;
            public WorldEntityKind Kind;
            public int ActorId, NpcType;
            public float Start, Expiry;
            public Vector3 LastPoint;
            public bool HasPoint;
            public bool NpcWasClose;
        }

        readonly Func<int, int?> maxHealth;
        internal GameplayWorldAnnotations(VisualElement host, Func<int, int?> maxHealth)
        {
            this.maxHealth = maxHealth;
            overlay = FFView.InstantiateClass(HudAddress, "world-annotations");
            host.Insert(0, overlay);
            nameLayer = AddLayer();
            bubbleLayer = AddLayer();
            damageLayer = AddLayer();
        }

        VisualElement AddLayer()
        {
            var layer = new VisualElement { pickingMode = PickingMode.Ignore };
            layer.AddToClassList("world-annotations");
            overlay.Add(layer);
            return layer;
        }

        static Mark CreateMark(string role, VisualElement layer)
        {
            var view = FFView.InstantiateClass(HudAddress, role);
            layer.Add(view);
            var mark = new Mark { View = view, Text = FFView.RequireClass<Label>(view, role + "-copy") };
            if (role == "world-nameplate")
            {
                mark.PlayerLevel = FFView.RequireClass<Label>(view, "world-nameplate-level");
                mark.HealthFill = FFView.RequireClass<VisualElement>(view, "world-nameplate-hp-fill");
            }
            return mark;
        }

        internal void ApplyToggles(bool showDamage, bool showSpeech)
        {
            if (!showDamage) ClearDamage();
            if (!showSpeech) ClearBubbles();
        }

        internal void ShowDamage(CombatHit hit, bool incoming)
        {
            if (damage.Count == MaxDamagePopups)
            {
                damage[0].View.RemoveFromHierarchy();
                damage.RemoveAt(0);
            }
            var mark = CreateMark("world-damage", damageLayer);
            mark.View.EnableInClassList("world-damage--incoming", incoming);
            mark.Text.text = hit.Damage.ToString(CultureInfo.InvariantCulture);
            mark.Kind = hit.Kind;
            mark.ActorId = hit.Id;
            mark.Start = Time.unscaledTime;
            mark.Expiry = mark.Start + DamageLifetime;
            damage.Add(mark);
        }

        internal void ShowPlayerBubble(int player, string message)
        {
            if (player <= 0) return;
            ShowBubble((WorldEntityKind.Player, player), player, 0, message);
        }

        internal bool ShowQuestBubble(int npcType, string message)
        {
            if (npcType <= 0 || string.IsNullOrWhiteSpace(message)) return false;
            var actor = replicaSet && lastWorld && lastWorld.Player ? NearestNpc(npcType, lastWorld.Player) : null;
            if (!actor) return false;
            ShowBubble((WorldEntityKind.Npc, actor.Entity.Id), actor.Entity.Id, npcType, message, true);
            return true;
        }

        internal void ShowNpcBubble(WorldEntity npc, string message)
            => ShowBubble((WorldEntityKind.Npc, npc.Id), npc.Id, npc.NpcType, message);

        void ShowBubble((WorldEntityKind, int) key, int actor, int npcType, string message, bool quest = false)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            if (!bubbles.TryGetValue(key, out var mark))
            {
                if (bubbles.Count == MaxBubbles)
                {
                    var oldest = key;
                    float start = float.PositiveInfinity;
                    foreach (var entry in bubbles)
                        if (entry.Value.Start < start) { start = entry.Value.Start; oldest = entry.Key; }
                    bubbles[oldest].View.RemoveFromHierarchy();
                    bubbles.Remove(oldest);
                }
                bubbles[key] = mark = CreateMark("world-speech", bubbleLayer);
            }
            mark.ActorId = actor;
            mark.NpcType = npcType;
            mark.Kind = key.Item1;
            mark.NpcWasClose = false;
            mark.Text.text = message;
            mark.Start = Time.unscaledTime;
            mark.Expiry = mark.Start + MinSpeechTime;
            mark.View.EnableInClassList("world-speech--ordinary", key.Item1 == WorldEntityKind.Npc && !quest);
            mark.View.EnableInClassList("world-speech--quest", quest);
        }

        internal NetworkEntityView FindActor(WorldEntityKind kind, int id) => FindActor(replicaSet, kind, id);

        static NetworkEntityView FindActor(NetworkWorldEntities replicas, WorldEntityKind kind, int id)
        {
            if (!replicas) return null;
            foreach (var view in replicas.Actors)
                if (view && view.Entity != null && view.Entity.Kind == kind && view.Entity.Id == id) return view;
            return null;
        }

        static bool VisibleActor(NetworkEntityView actor) => actor && actor.isActiveAndEnabled && actor.Entity != null &&
            !actor.Entity.Hidden && actor.Definition?.HiddenMarker != true;

        static Vector3 Head(NetworkEntityView actor, float fraction) => actor.transform.position +
            Vector3.up * ((actor.Definition?.Height ?? PlayerHeight) * actor.transform.lossyScale.y * fraction);

        NetworkEntityView NearestNpc(int type, Transform player)
        {
            NetworkEntityView nearest = null;
            float distance = float.PositiveInfinity;
            foreach (var view in replicaSet.Actors)
            {
                if (!VisibleActor(view) || view.Entity.Kind != WorldEntityKind.Npc || view.Entity.NpcType != type || view.Entity.Hp <= 0) continue;
                float next = (player.position - view.transform.position).sqrMagnitude;
                if (next >= distance) continue;
                nearest = view;
                distance = next;
            }
            return nearest;
        }

        internal void Tick(NetworkWorldController world, bool blocked, int localId, string localName,
            Func<NetworkEntityView, string> nameRole, bool self, bool hasNames, string language)
        {
            if (lastWorld != world || replicaSet != world?.Replicas || world && mapNumber != world.MapNumber)
            {
                if (lastWorld)
                {
                    ClearDamage();
                    ClearBubbles();
                }
                lastWorld = world;
                replicaSet = world?.Replicas;
                mapNumber = world ? world.MapNumber : 0;
            }

            bool ready = world && world.IsReady && world.View && world.Player && replicaSet;
            overlay.EnableInClassList("world-annotations--hidden", !ready || blocked);
            float now = Time.unscaledTime;
            if (ready && !blocked && hasNames) TickNames(world, localId, localName, nameRole, self, language);
            else ClearNames();

            for (int i = damage.Count - 1; i >= 0; i--)
            {
                var mark = damage[i];
                if (now >= mark.Expiry)
                {
                    mark.View.RemoveFromHierarchy();
                    damage.RemoveAt(i);
                    continue;
                }
                if (!ready) continue;
                var actor = FindActor(mark.Kind, mark.ActorId);
                if (mark.ActorId == localId && mark.Kind == WorldEntityKind.Player)
                {
                    mark.LastPoint = world.Player.position + Vector3.up * PlayerHeight;
                    mark.HasPoint = true;
                }
                else if (VisibleActor(actor))
                {
                    mark.LastPoint = Head(actor, 1);
                    mark.HasPoint = true;
                }
                float age = Mathf.Clamp01((now - mark.Start) / DamageLifetime);
                Draw(mark.View, mark.HasPoint && !blocked, world.View, mark.LastPoint, 28 * age);
                mark.View.style.opacity = 1 - age * age;
            }
            removed.Clear();
            foreach (var item in bubbles)
            {
                var mark = item.Value;
                if (mark.View.resolvedStyle.height > 0)
                    mark.Expiry = mark.Start + Mathf.Max(MinSpeechTime, mark.View.resolvedStyle.height / 5);
                if (now >= mark.Expiry)
                {
                    mark.View.RemoveFromHierarchy();
                    removed.Add(item.Key);
                    continue;
                }
                if (!ready) continue;
                bool selfSpeech = mark.Kind == WorldEntityKind.Player && mark.ActorId == localId;
                var actor = selfSpeech ? null : FindActor(mark.Kind, mark.ActorId);
                bool visible = selfSpeech || VisibleActor(actor);
                Vector3 point = selfSpeech ? world.Player.position + Vector3.up * PlayerHeight * .9f
                    : visible ? Head(actor, mark.Kind == WorldEntityKind.Npc ? .8f : .9f) : default;
                string role = actor ? nameRole(actor) : null;
                if (mark.Kind == WorldEntityKind.Npc)
                {
                    float limit = NpcDistance + (mark.NpcWasClose ? 1 : 0);
                    mark.NpcWasClose = visible && (point - world.Player.position).sqrMagnitude <= limit * limit;
                    visible &= mark.NpcWasClose;
                }
                if (visible && !selfSpeech)
                    visible = !Physics.Linecast(world.View.transform.position, point, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                Draw(mark.View, visible && !blocked, world.View, point, role != null || selfSpeech && self ? 15 : 0);
            }
            foreach (var key in removed) bubbles.Remove(key);
        }

        void TickNames(NetworkWorldController world, int localId, string localName, Func<NetworkEntityView, string> nameRole,
            bool self, string language)
        {
            active.Clear();
            if (self) AddName((WorldEntityKind.Player, localId), localName, "self", world.View,
                world.Player.position + Vector3.up * PlayerHeight * .9f);
            foreach (var actor in replicaSet.Actors)
            {
                if (!VisibleActor(actor) || actor.Entity.Hp <= 0 ||
                    (actor.transform.position - world.Player.position).sqrMagnitude > NpcDistance * NpcDistance) continue;
                string role = nameRole(actor);
                if (role == null) continue;
                Vector3 point = Head(actor, actor.Entity.Kind == WorldEntityKind.Player ? .9f : .8f);
                if (!TryProject(world.View, point, out _) ||
                    Physics.Linecast(world.View.transform.position, point, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                AddName((actor.Entity.Kind, actor.Entity.Id), ActorName(actor.Entity, language, actor.Definition), role, world.View, point, actor.Entity);
            }
            removed.Clear();
            foreach (var item in names)
            {
                if (active.Contains(item.Key)) continue;
                removed.Add(item.Key);
                item.Value.View.RemoveFromHierarchy();
                namePool.Push(item.Value);
            }
            foreach (var key in removed) names.Remove(key);
        }

        void AddName((WorldEntityKind, int) key, string name, string role, Camera camera, Vector3 point, WorldEntity actor = null)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            active.Add(key);
            if (!names.TryGetValue(key, out var nameplate))
            {
                if (namePool.TryPop(out nameplate)) nameLayer.Add(nameplate.View);
                else nameplate = CreateMark("world-nameplate", nameLayer);
                names.Add(key, nameplate);
            }
            if (nameplate.Text.text != name) nameplate.Text.text = name;
            bool player = actor?.Kind == WorldEntityKind.Player && actor.Character?.Level > 0;
            nameplate.View.EnableInClassList("world-nameplate--player", player);
            if (player)
            {
                int level = actor.Character.Level;
                nameplate.PlayerLevel.text = level.ToString(CultureInfo.InvariantCulture);
                int maximum = maxHealth(level) ?? 0;
                nameplate.HealthFill.style.width = Length.Percent(maximum > 0 ? 100 * Mathf.Clamp01((float)actor.Hp / maximum) : 0);
            }
            nameplate.View.EnableInClassList("world-nameplate--group", role == "group");
            nameplate.View.EnableInClassList("world-nameplate--hostile", role == "hostile");
            Draw(nameplate.View, true, camera, point, 0);
        }

        internal static string ActorName(WorldEntity entity, string language, WorldNpcDefinition definition = null)
        {
            if (entity == null) return null;
            if (entity.Kind == WorldEntityKind.Player) return entity.Character?.DisplayName;
            if (entity.Kind != WorldEntityKind.Npc) return null;
            string key = "content.npc." + entity.NpcType + ".name";
            string name = LocalizationCatalog.Lookup(key, language);
            return name == key ? (definition ?? WorldNpcCatalog.Load().Find(entity.NpcType))?.Name : name;
        }

        static bool TryProject(Camera camera, Vector3 point, out Vector3 screen)
        {
            screen = camera.WorldToViewportPoint(point);
            return screen.z > 0 && screen.x >= 0 && screen.x <= 1 && screen.y >= 0 && screen.y <= 1;
        }

        void Draw(VisualElement view, bool visible, Camera camera, Vector3 position, float lift)
        {
            Vector3 screen = default;
            visible = visible && TryProject(camera, position, out screen);
            view.EnableInClassList("world-annotation--visible", visible);
            if (!visible) return;
            float width = overlay.resolvedStyle.width, height = overlay.resolvedStyle.height;
            if (width > 0 && height > 0)
            {
                view.style.left = screen.x * width;
                view.style.top = (1 - screen.y) * height - lift;
            }
        }

        void ClearNames()
        {
            foreach (var mark in names.Values)
            {
                mark.View.RemoveFromHierarchy();
                namePool.Push(mark);
            }
            names.Clear();
        }

        void ClearBubbles()
        {
            foreach (var mark in bubbles.Values) mark.View.RemoveFromHierarchy();
            bubbles.Clear();
        }

        void ClearDamage()
        {
            foreach (var mark in damage) mark.View.RemoveFromHierarchy();
            damage.Clear();
        }

        public void Dispose() => overlay.RemoveFromHierarchy();
    }
}
