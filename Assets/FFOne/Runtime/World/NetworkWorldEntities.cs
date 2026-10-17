using System;
using System.Collections;
using System.Collections.Generic;
using FFOne.Content;
using FFOne.Network;
using UnityEngine;

namespace FFOne.World
{
    public sealed class NetworkEntityView : MonoBehaviour
    {
        public WorldEntity Entity { get; internal set; }
        public WorldNpcDefinition Definition { get; internal set; }
        internal Vector3 Destination;
        internal float Speed;
        internal Animation LegacyAnimation;
        internal NativeAvatarPreview Avatar;
        internal string LastClip;
        public bool Hostile => Definition?.Team == 2;
        internal void Animate(string clip)
        {
            if (LastClip == clip) return;
            LastClip = clip;
            if (Avatar) { Avatar.PlayLocomotion(clip == "attack1" ? WorldWeaponCatalog.AttackClip(WorldWeaponCatalog.Load().Find(Entity.Character.Equipment[0].ItemId)?.Mode ?? 0) : clip); return; }
            if (!LegacyAnimation) return;
            var candidates = clip == "attack1" ? new[] { "melee1", "attack1" } : clip == "hit" ? new[] { "wound", "hit" } : new[] { clip };
            foreach (var name in candidates) if (LegacyAnimation[name]) { LegacyAnimation.CrossFade(name, .15f); break; }
        }
    }

    /// <summary>Owns the active shard's visual actors and their asynchronous asset handles.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed partial class NetworkWorldEntities : MonoBehaviour
    {
        readonly Dictionary<(WorldEntityKind, int), NetworkEntityView> views = new();
        readonly Dictionary<(WorldEntityKind, int), NativeModelAssets.Request> requests = new();
        public WorldEntitySession Session { get; private set; }
        public int VisibleCount => views.Count;
        public IEnumerable<NetworkEntityView> Actors => views.Values;
        readonly List<string> contentFailures = new();
        public IReadOnlyList<string> ContentFailures => contentFailures.AsReadOnly();
        public event Action<string> ContentError;
        WorldNpcCatalog catalog;
        int selectedRecallPoint;
        public int SelectedRecallPointId => selectedRecallPoint;
        public void Begin(int playerId, IReadOnlyList<ProtocolFrame> prelude)
        {
            selectedRecallPoint = 0;
            Session = new WorldEntitySession(playerId); catalog = WorldNpcCatalog.Load(); Session.Changed += Changed;
            if (prelude != null) foreach (var frame in prelude) Receive(frame);
        }
        public void Receive(ProtocolFrame frame)
        {
            if (Session == null) return;
            if (ProtocolProjectiles.TryDecode(frame, out var projectile)) GetComponentInParent<NetworkWorldController>()?.ReceiveProjectile(projectile);
            // The original ReceivePacket stores REP_REGIST_RXCOM's first word in
            // iRXCom, despite the historic wire field being named iMapNum.
            if (ProtocolRecall.TryDecode(frame, out var recall) && !recall.Failed)
            {
                selectedRecallPoint = recall.NpcId;
                foreach (var actor in views.Values)
                    actor.GetComponent<RecallPointPresentation>()?.SetSelected(actor.Entity.Id == selectedRecallPoint);
            }
            if (frame.PacketType == ProtocolTravel.WarpOk)
            {
                ProtocolTravel.TryDecode(frame, out _);
                GetComponentInParent<NetworkWorldController>()?.ClearProjectiles();
                selectedRecallPoint = 0;
                foreach (var actor in views.Values)
                    actor.GetComponent<RecallPointPresentation>()?.SetSelected(actor.Entity.Id == selectedRecallPoint);
            }
            Session.Apply(frame);
            if (ProtocolNpcSkill.TryDecode(frame, out var npcEffects))
                foreach (var effect in npcEffects) if (effect.Hp.HasValue && !effect.Resurrected) Session.SetHealth(effect.Kind, effect.Id, effect.Hp.Value);
            if (ProtocolNanoSkill.TryDecode(frame, out var skill))
                foreach (var effect in skill.Effects) if (effect.Hp.HasValue && !effect.Resurrected) Session.SetHealth(effect.Kind, effect.Id, effect.Hp.Value);
            if (ProtocolGameplay.TryCombat(frame, out var combat))
            {
                foreach (var hit in combat.Hits) Session.SetHealth(hit.Kind, hit.Id, hit.Hp);
                if (views.TryGetValue((combat.AttackerKind, combat.AttackerId), out var attacker)) attacker.Animate("attack1");
            }
        }
        void Changed(WorldUpdate update)
        {
            var entity = update.Entity; var key = (entity.Kind, entity.Id);
            if (update.Kind == WorldUpdateKind.Leave)
            {
                if (requests.Remove(key, out var request)) request.Dispose();
                if (views.Remove(key, out var old)) { old.gameObject.SetActive(false); Destroy(old.gameObject); }
                return;
            }
            if (update.Kind == WorldUpdateKind.Enter)
            {
                if (views.TryGetValue(key, out var old)) { old.gameObject.SetActive(false); Destroy(old.gameObject); }
                if (requests.Remove(key, out var pending)) pending.Dispose();
                var go = new GameObject(entity.Kind + " " + entity.Id); go.transform.SetParent(transform, false);
                var view = go.AddComponent<NetworkEntityView>(); views[key] = view; view.Entity = entity;
                view.Destination = go.transform.position = WorldCoordinates.FromServer(entity.X, entity.Y, entity.Z);
                go.transform.rotation = entity.Kind == WorldEntityKind.Transportation ? Quaternion.identity : Quaternion.Euler(0, WorldCoordinates.FromServerHeading(entity.Angle), 0);
                if (entity.Kind == WorldEntityKind.Transportation)
                {
                    if (entity.TransportationType == 1) StartCoroutine(LoadSlider(key, view));
                    else ContentFailure($"Transportation type {entity.TransportationType}: no accepted model route.");
                    return;
                }
                if (entity.Kind == WorldEntityKind.Player)
                {
                    view.Avatar = go.AddComponent<NativeAvatarPreview>(); view.Avatar.Apply(entity.Character);
                    view.Avatar.SetPersonalVehicle((entity.StateFlags & 8) != 0 ? entity.Character.Equipment[8].ItemId : 0);
                    if (!view.Avatar.IsReady) ContentFailure($"Player {entity.Id}: {view.Avatar.LastError}");
                }
                else
                {
                    view.Definition = catalog.Find(entity.NpcType);
                    if (view.Definition?.HnpcAppearance >= 0)
                    {
                        view.Avatar = go.AddComponent<NativeAvatarPreview>(); view.Avatar.ApplyHnpc(view.Definition.HnpcAppearance);
                        if (!view.Avatar.IsReady) ContentFailure($"HNPC type {entity.NpcType}: {view.Avatar.LastError}");
                    }
                    else if (view.Definition?.HiddenMarker == true) { /* Source NpcMoveController location probe: keep its root, suppress its model. */ }
                    else if (string.IsNullOrEmpty(view.Definition?.Model)) ContentFailure($"NPC type {entity.NpcType}: accepted model route is missing.");
                    else StartCoroutine(LoadNpc(key, view));
                }
                var collider = go.AddComponent<CapsuleCollider>(); collider.isTrigger = true;
                collider.height = Mathf.Max(1, view.Definition?.Height ?? 1.8f); collider.radius = Mathf.Max(.3f, view.Definition?.Radius ?? .45f);
                collider.center = Vector3.up * collider.height * .5f;
                view.Animate("stand1");
                go.AddComponent<NativeBuffPresentation>().SetCondition(entity.ConditionFlags, Camera.main, view.Avatar ? 1 : Mathf.Max(.01f, (view.Definition?.Radius ?? .5f) * 2));
                return;
            }
            if (!views.TryGetValue(key, out var current)) return;
            current.Entity = entity;
            if (update.Kind == WorldUpdateKind.Condition) current.GetComponent<NativeBuffPresentation>()?.SetCondition(entity.ConditionFlags, Camera.main, current.Avatar ? 1 : Mathf.Max(.01f, (current.Definition?.Radius ?? .5f) * 2));
            if (update.Kind == WorldUpdateKind.Settings)
            {
                current.transform.localScale = Vector3.one * entity.Scale;
                foreach (var renderer in current.GetComponentsInChildren<Renderer>(true)) renderer.enabled = !entity.Hidden;
            }
            if (update.Kind == WorldUpdateKind.Equipment) current.Avatar?.Apply(entity.Character);
            if ((update.Kind == WorldUpdateKind.State || update.Kind == WorldUpdateKind.Equipment) && current.Avatar)
            {
                int vehicle = (entity.StateFlags & 8) != 0 ? entity.Character.Equipment[8].ItemId : 0;
                bool vehicleChanged = current.Avatar.PersonalVehicleId != vehicle;
                current.Avatar.SetPersonalVehicle(vehicle);
                if (vehicleChanged)
                {
                    current.LastClip = null;
                    current.Animate(entity.Jumping ? "jumpstart" : entity.Speed > 0 ? "run" : "stand1");
                }
            }
            if (update.Kind == WorldUpdateKind.Health) current.Animate(entity.Hp == 0 ? "death" : "hit");
            if (update.Kind == WorldUpdateKind.Move)
            {
                current.Destination = WorldCoordinates.FromServer(entity.X, entity.Y, entity.Z);
                current.Speed = entity.Kind == WorldEntityKind.Npc ? entity.Speed / 100f : Mathf.Max(6, entity.Speed / 100f);
                if (update.HasHeading) current.transform.rotation = Quaternion.Euler(0, WorldCoordinates.FromServerHeading(entity.Angle), 0);
                current.Animate(entity.Jumping ? "jumpstart" : entity.Speed > 0 ? "run" : "stand1");
            }
        }
        IEnumerator LoadNpc((WorldEntityKind, int) key, NetworkEntityView view)
        {
            var request = NativeModelAssets.LoadAsync(view.Definition.Model); requests[key] = request;
            yield return request;
            if (!views.TryGetValue(key, out var current) || current != view) yield break;
            if (request.asset is GameObject prefab)
            {
                var model = Instantiate(prefab, view.transform, false); model.transform.localScale = Vector3.one * view.Definition.Scale;
                view.LegacyAnimation = model.GetComponentInChildren<Animation>(); view.LastClip = null;
                if (view.Definition.Type == 1386) view.gameObject.AddComponent<RecallPointPresentation>().Initialize(view.Definition, Camera.main, view.Entity.Id == selectedRecallPoint);
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true)) renderer.enabled = !view.Entity.Hidden;
                view.Animate(view.Entity.Hp == 0 ? "death" : "stand1");
            }
            else ContentFailure($"NPC type {view.Entity.NpcType}: cannot load {view.Definition.Model}.");
            // Keep the handle alive until interest exit; a render clone still references prefab dependencies.
        }
        void ContentFailure(string message) { contentFailures.Add(message); ContentError?.Invoke(message); }
        void Update()
        {
            foreach (var view in views.Values)
            {
                if (view.Entity.Kind == WorldEntityKind.Player && view.Entity.TransportId > 0 &&
                    views.ContainsKey((WorldEntityKind.Transportation, view.Entity.TransportId))) continue;
                var delta = view.Destination - view.transform.position;
                if (delta.sqrMagnitude > 900) view.transform.position = view.Destination;
                else view.transform.position = Vector3.MoveTowards(view.transform.position, view.Destination, Mathf.Max(.1f, view.Speed) * Time.deltaTime);
                if (view.Entity.Kind == WorldEntityKind.Npc && delta.sqrMagnitude > .001f)
                {
                    delta.y = 0; if (delta.sqrMagnitude > .001f) view.transform.rotation = Quaternion.LookRotation(delta);
                }
            }
            // Interest packets can add a passenger before its platform. Resolve
            // attachments after movement so both use the current frame's pose.
            foreach (var view in views.Values)
                if (view.Entity.Kind == WorldEntityKind.Player && view.Entity.TransportId > 0 &&
                    views.TryGetValue((WorldEntityKind.Transportation, view.Entity.TransportId), out var platform))
                    view.transform.position = platform.transform.TransformPoint(WorldCoordinates.FromServer(view.Entity.LocalX, view.Entity.LocalY, view.Entity.LocalZ));
        }
        void OnDestroy()
        {
            if (Session != null) { Session.Changed -= Changed; Session.Clear(); }
            foreach (var request in requests.Values) request.Dispose(); requests.Clear(); views.Clear();
        }
    }
}
