using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Network;
using UnityEngine;

namespace FFOne.World
{
    public sealed class NetworkEntityView : MonoBehaviour
    {
        enum NpcMotion { Unset, Idle, Walk, Run, Ready, Attack, Hit, Stun, Sleep, Death }

        public WorldEntity Entity { get; internal set; }
        public WorldNpcDefinition Definition { get; internal set; }
        internal Vector3 Destination;
        internal float Speed;
        internal Animation LegacyAnimation;
        internal NativeNpcAnimator NpcAnimator;
        internal NativeAvatarPreview Avatar;
        internal WorldNanoCompanion Nano;
        internal string LastClip;
        Coroutine avatarFade;
        static readonly int AvatarVisibility = Shader.PropertyToID("_AvatarVisibility");
        MaterialPropertyBlock fadedMaterial;
        float avatarOpacity;
        internal void FadeAvatar(bool visible)
        {
            if (avatarFade != null) StopCoroutine(avatarFade);
            avatarFade = StartCoroutine(Fade());
            IEnumerator Fade()
            {
                var renderers = GetComponentsInChildren<Renderer>(true);
                fadedMaterial ??= new MaterialPropertyBlock();
                while (true)
                {
                    foreach (var renderer in renderers)
                        if (renderer)
                        {
                            renderer.GetPropertyBlock(fadedMaterial);
                            fadedMaterial.SetFloat(AvatarVisibility, avatarOpacity);
                            renderer.SetPropertyBlock(fadedMaterial);
                        }
                    float target = visible ? 1 : 0;
                    if (avatarOpacity == target) break;
                    yield return null;
                    avatarOpacity = Mathf.MoveTowards(avatarOpacity, target, Time.unscaledDeltaTime / .35f);
                }
                avatarFade = null;
                if (!visible) Destroy(gameObject);
            }
        }
        NpcMotion npcMotion;
        bool readyAfterOverlay, spellPreparation, spellStarted;
        string spellMotion;
        float spellUntil;
        float npcActionUntil;
        internal float NpcMovingUntil;
        int idleCycles;
        string humanIdle;
        float humanIdleStarted, humanIdleDuration;
        bool humanIdleSoundPending, remoteJumping;
        internal void PresentJump(bool airborne)
        {
            if (remoteJumping == airborne) return;
            remoteJumping = airborne;
            var world = GetComponentInParent<NetworkWorldController>();
            if (Avatar && world) Avatar.PlayJumpVoice(airborne, (string)world.Preferences["voice_locale"],
                FFOne.UI.FrontendController.AudioGain(world.Preferences, "voice"));
        }
        NetworkWorldController humanWorld;
        public bool IsNpc => Entity?.Kind == WorldEntityKind.Npc;
        public bool Hostile => Definition?.Team == 2;
        internal void Animate(string clip)
        {
            bool attack = clip == "attack1";
            if (LastClip == clip && !attack) return;
            LastClip = clip;
            if (Avatar)
            {
                if (!Avatar.IsReady) return; // Replay the latest requested pose when the appearance arrives.
                if (attack)
                {
                    Avatar.CancelEmote();
                    Avatar.PlayerAnimation?.Play(WorldWeaponCatalog.AttackClip(WorldWeaponCatalog.AnimationEquipType(Entity.Character)), true);
                    return;
                }
                else if (Avatar.PersonalVehicleId == 0 && clip is "stand1" or "run" or "jumpstart")
                    clip = WorldWeaponCatalog.AnimationPrefix(WorldWeaponCatalog.AnimationEquipType(Entity.Character)) + clip;
                Avatar.PlayLocomotion(clip, attack);
                return;
            }
            if (!LegacyAnimation) return;
            var candidates = clip == "attack1" ? new[] { "melee1", "attack1" } : clip == "hit" ? new[] { "wound", "hit" } : new[] { clip };
            foreach (var name in candidates) if (LegacyAnimation[name]) { LegacyAnimation.CrossFade(name, .15f); break; }
        }

        NpcMotion NpcCondition => Entity.Hp <= 0 ? NpcMotion.Death
            : (Entity.ConditionFlags & (1 << 10)) != 0 ? NpcMotion.Sleep
            : (Entity.ConditionFlags & (1 << 9)) != 0 ? NpcMotion.Stun : NpcMotion.Unset;

        bool NpcModelReady => Avatar ? Avatar.IsReady : NpcAnimator;

        internal void RefreshNpcAnimation(float now)
        {
            if (!NpcModelReady) return;
            var desired = NpcCondition;
            if (desired != NpcMotion.Unset)
            {
                readyAfterOverlay = false;
                spellMotion = null;
            }
            if (desired == NpcMotion.Unset)
            {
                if (spellMotion != null && AdvanceSpell(now)) return;
                if ((npcMotion is NpcMotion.Attack or NpcMotion.Hit) &&
                    (NpcAnimator ? !NpcAnimator.ReachedEnd : now < npcActionUntil)) return;
                bool overlays = readyAfterOverlay && NpcAnimator &&
                    (NpcAnimator.ActionPlaying(NativeNpcAnimator.AttackLayer) || NpcAnimator.ActionPlaying(NativeNpcAnimator.WoundLayer));
                if (Speed > 0 && ((Destination - transform.position).sqrMagnitude > .0001f || now < NpcMovingUntil))
                    desired = Entity.NpcRunning ? NpcMotion.Run : NpcMotion.Walk;
                else if (overlays)
                {
                    if (npcMotion is NpcMotion.Idle or NpcMotion.Ready) return;
                    desired = NpcMotion.Ready;
                }
                else
                    desired = readyAfterOverlay || npcMotion is NpcMotion.Attack or NpcMotion.Hit or NpcMotion.Ready ? NpcMotion.Ready : NpcMotion.Idle;
                if (!overlays) readyAfterOverlay = false;
            }
            if (npcMotion == desired)
            {
                if (desired == NpcMotion.Idle && Avatar)
                {
                    float age = now - humanIdleStarted;
                    if (humanIdleSoundPending && age >= .25f)
                    {
                        humanIdleSoundPending = false;
                        if (!humanWorld) humanWorld = GetComponentInParent<NetworkWorldController>();
                        if (humanWorld && humanWorld.Player)
                            Avatar.PlayNpcIdleSounds(humanIdle, (string)humanWorld.Preferences["voice_locale"],
                                FFOne.UI.FrontendController.AudioGain(humanWorld.Preferences, "voice"),
                                (humanWorld.Player.position - transform.position).sqrMagnitude <= 100);
                    }
                    if (humanIdleDuration > 0 && age >= humanIdleDuration)
                        PlayNpcMotion(desired, false, out _);
                }
                else if (desired == NpcMotion.Idle && NpcAnimator && NpcAnimator.CurrentMotion &&
                    NpcAnimator.CurrentMotion.isLooping && NpcAnimator.CurrentMotion.length > 0)
                {
                    int completed = Mathf.FloorToInt(NpcAnimator.MotionTime / NpcAnimator.CurrentMotion.length);
                    if (completed > idleCycles)
                    {
                        idleCycles = completed;
                        PlayNpcMotion(desired, false, out _);
                    }
                }
                return;
            }
            PlayNpcMotion(desired, npcMotion == NpcMotion.Unset, out _);
            npcMotion = desired;
        }

        internal void PlayNpcAction(bool attack, float now)
        {
            if (!NpcModelReady || NpcCondition != NpcMotion.Unset) return;
            if (NpcAnimator && !attack && NpcAnimator.ActionPlaying(NativeNpcAnimator.WoundLayer)) return;
            if (attack && npcMotion == NpcMotion.Hit && (!NpcAnimator || NpcAnimator.Animator.GetLayerIndex(NativeNpcAnimator.AttackLayer) < 1)
                && (NpcAnimator ? !NpcAnimator.ReachedEnd : now < npcActionUntil))
                return;
            var action = attack ? NpcMotion.Attack : NpcMotion.Hit;
            if (!PlayNpcMotion(action, true, out float duration)) return;
            if (NpcAnimator && NativeNpcAnimator.ActionLayer(NpcAnimator.CurrentClip) is string layer && NpcAnimator.Animator.GetLayerIndex(layer) > 0)
            {
                readyAfterOverlay = true;
                return;
            }
            npcMotion = action;
            npcActionUntil = now + duration;
        }

        internal void SetNpcSpell(uint packet, int skill)
        {
            if (Entity.Hp <= 0) return;
            var previous = spellMotion;
            spellPreparation = packet is 0x31000020 or 0x31000023;
            spellMotion = packet switch
            {
                0x31000020 => Definition?.EruptionSkill == skill && WorldSkillCatalog.Load().Find(skill) is { Type: not 23 }
                    ? "megatak_readyspell" : "readyspell",
                0x31000023 => "corruptak_readyspell",
                ProtocolNpcSkill.Corruption => "corruptak",
                ProtocolNpcSkill.Hit => SkillMotion(skill),
                _ => null
            };
            if (spellMotion == null && previous == null) return;
            spellStarted = readyAfterOverlay = false;
            npcMotion = NpcMotion.Unset;
            RefreshNpcAnimation(Time.time);
        }

        string SkillMotion(int skill)
        {
            if (skill <= 0 || Definition == null) return null;
            foreach (var slot in Definition.SkillMotions)
                if (slot.Id == skill) return slot.Motion is >= 0 and <= 3 ? "skill" + slot.Motion : null;
            return null;
        }

        bool AdvanceSpell(float now)
        {
            if (spellStarted && now < spellUntil) return true;
            if (spellStarted && !spellPreparation) { spellMotion = null; return false; }
            bool played;
            if (NpcAnimator)
            {
                // Most recovered prepare clips are authored as once. Repeat them until hit/cancel.
                played = spellPreparation && NpcAnimator.Play(spellMotion, true, restart: true)
                    || NpcAnimator.Play(spellMotion, false, restart: true)
                    || spellPreparation && NpcAnimator.Play("ready", true);
                if (played) spellUntil = spellPreparation && NpcAnimator.CurrentMotion.isLooping
                    ? float.PositiveInfinity : now + Mathf.Max(.01f, NpcAnimator.CurrentMotion.length);
            }
            else
            {
                var animator = Avatar.PlayerAnimation;
                string supported = animator && animator.Supports(spellMotion) ? spellMotion : spellPreparation ? "ready" : null;
                played = supported != null && animator && animator.Supports(supported) && Avatar.PlayLocomotion(supported, true);
                if (played) spellUntil = now + Mathf.Max(.01f, animator.Duration(supported));
            }
            if (!played) spellMotion = null;
            spellStarted = played;
            return played;
        }

        bool PlayNpcMotion(NpcMotion motion, bool restart, out float duration)
        {
            duration = 0;
            var (clip, alternative, loop) = motion switch
            {
                NpcMotion.Walk => ("walk", "run", true),
                NpcMotion.Run => ("run", "walk", true),
                NpcMotion.Ready => ("ready", "stand1", true),
                NpcMotion.Attack => ("melee1", "attack1", false),
                NpcMotion.Hit => ("wound", "hit", false),
                NpcMotion.Stun => ("stun", "stand1", true),
                NpcMotion.Sleep => ("sleep", "stand1", true),
                NpcMotion.Death => ("death", "die", false),
                _ => ("stand1", "nif-default", true)
            };
            if (Avatar)
            {
                var animator = Avatar.PlayerAnimation;
                if (!animator) return false;
                if (motion == NpcMotion.Idle)
                {
                    int roll = UnityEngine.Random.Range(0, 100);
                    var choices = Definition?.IdleClips;
                    if (choices != null)
                    {
                        int index = roll < 34 ? 0 : roll < 67 ? 1 : 2;
                        clip = index != 0 && choices[index] == humanIdle ? choices[0] : choices[index];
                    }
                    else clip = NativeNpcAnimator.SelectStand(roll, humanIdle);
                    if (!animator.Supports(clip)) clip = "stand1";
                    humanIdle = clip;
                    humanIdleStarted = Time.time;
                    humanIdleDuration = animator.Duration(clip);
                    humanIdleSoundPending = true;
                    return Avatar.PlayLocomotion(clip, true);
                }
                humanIdle = null;
                humanIdleSoundPending = false;
                if (motion == NpcMotion.Attack) clip = WorldWeaponCatalog.AttackClip(0);
                if (!animator.Supports(clip)) clip = alternative;
                duration = animator.Duration(clip);
                return Avatar.PlayLocomotion(clip, restart);
            }
            if (motion == NpcMotion.Idle)
            {
                string previous = NpcAnimator.CurrentClip;
                if (!NpcAnimator.PlayWorldIdle(UnityEngine.Random.Range(0, 100), Definition?.Model, restart)) return false;
                if (NpcAnimator.CurrentClip != previous || restart) idleCycles = 0;
                return true;
            }
            if (motion is NpcMotion.Attack or NpcMotion.Hit)
            {
                if (motion == NpcMotion.Attack && UnityEngine.Random.Range(0, 2) == 1 && NpcAnimator.PlayWorldAction("melee2")) return true;
                return NpcAnimator.PlayWorldAction(clip) || NpcAnimator.PlayWorldAction(alternative);
            }
            if (motion is NpcMotion.Stun or NpcMotion.Sleep)
            {
                if (NpcAnimator.Play(clip, true, restart: restart)) return true;
                NpcAnimator.ClearWorldActions();
                return NpcAnimator.PlayWorldIdle(0, Definition?.Model, restart);
            }
            if (NpcAnimator.Play(clip, loop, restart: restart, speed: PlaybackSpeed(clip)) ||
                NpcAnimator.Play(alternative, loop, restart: restart, speed: PlaybackSpeed(alternative))) return true;
            return motion is NpcMotion.Walk or NpcMotion.Run or NpcMotion.Ready &&
                NpcAnimator.PlayWorldIdle(0, Definition?.Model, restart);
        }

        float PlaybackSpeed(string clip) => clip switch
        {
            "walk" => Definition?.WalkAnimationSpeed ?? 1,
            "run" => Definition?.RunAnimationSpeed ?? 1,
            _ => 1
        };
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
            if (frame.PacketType == 0x31000023) GetComponentInParent<NetworkWorldController>()?.ReceiveCorruptionReady(frame.Payload);
            if (frame.PacketType is 0x31000020 or ProtocolNpcSkill.Hit or 0x31000025)
                GetComponentInParent<NetworkWorldController>()?.ReceiveEruption(frame);
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
                GetComponentInParent<NetworkWorldController>()?.ClearEruptions();
                selectedRecallPoint = 0;
                foreach (var actor in views.Values)
                    actor.GetComponent<RecallPointPresentation>()?.SetSelected(actor.Entity.Id == selectedRecallPoint);
            }
            Session.Apply(frame);
            if (frame.PacketType is 0x31000020 or 0x31000023 or 0x31000025)
            {
                bool cancel = frame.PacketType == 0x31000025;
                Protocol0104.RequireSize(frame.Payload, cancel ? 4 : 20);
                if (views.TryGetValue((WorldEntityKind.Npc, Protocol0104.Read32(frame.Payload, 0)), out var casterView))
                    casterView.SetNpcSpell(frame.PacketType, cancel ? 0 : Protocol0104.Read16(frame.Payload, 4));
            }
            if (frame.PacketType == ProtocolGameplay.NanoActiveBroadcast && Protocol0104.Read32(frame.Payload, 16) != 0 &&
                views.TryGetValue((WorldEntityKind.Player, Protocol0104.Read32(frame.Payload, 0)), out var activeOwner))
                activeOwner.Nano?.ShowSkill(activeOwner.Entity.NanoId, activeOwner.Entity.NanoSkill);
            if (ProtocolNpcSkill.TryDecode(frame, out var npcEffects))
            {
                var controller = GetComponentInParent<NetworkWorldController>();
                int npc = Protocol0104.Read32(frame.Payload, 0);
                bool corruption = frame.PacketType == ProtocolNpcSkill.Corruption;
                if (views.TryGetValue((WorldEntityKind.Npc, npc), out var casterView))
                    casterView.SetNpcSpell(frame.PacketType, Protocol0104.Read16(frame.Payload, 4));
                if (corruption) controller?.CancelEruption(npc);
                controller?.PresentSkill(Protocol0104.Read16(frame.Payload, 4), WorldEntityKind.Npc, npc,
                    npcEffects, corruption ? Protocol0104.Read16(frame.Payload, 6) : 0);
                foreach (var effect in npcEffects) if (effect.Hp.HasValue && !effect.Resurrected) Session.SetHealth(effect.Kind, effect.Id, effect.Hp.Value);
            }
            if (frame.PacketType == ProtocolBuff.Tick && ProtocolBuff.TryEffects(frame, out var ticks) && Protocol0104.Read16(frame.Payload, 8) == 24)
                GetComponentInParent<NetworkWorldController>()?.PresentHealingTick(ticks);
            if (ProtocolNanoSkill.TryDecode(frame, out var skill))
            {
                if (skill.CasterId == Session.LocalPlayerId && frame.PacketType == ProtocolNanoSkill.Success)
                    GetComponentInParent<NetworkWorldController>()?.LocalNano?.ShowSkill(skill.NanoId, skill.SkillId);
                else if (frame.PacketType == ProtocolNanoSkill.Broadcast &&
                    views.TryGetValue((WorldEntityKind.Player, skill.CasterId), out var caster))
                    caster.Nano?.ShowSkill(skill.NanoId, skill.SkillId);
                GetComponentInParent<NetworkWorldController>()?.PresentSkill(skill.SkillId, WorldEntityKind.Player, skill.CasterId, skill.Effects);
                foreach (var effect in skill.Effects) if (effect.Hp.HasValue && !effect.Resurrected) Session.SetHealth(effect.Kind, effect.Id, effect.Hp.Value);
            }
            if (ProtocolGameplay.TryCombat(frame, out var combat))
            {
                GetComponentInParent<NetworkWorldController>()?.RecordPlayerCombatResult(combat, Session.LocalPlayerId);
                foreach (var hit in combat.Hits) Session.SetHealth(hit.Kind, hit.Id, hit.Hp);
                if (views.TryGetValue((combat.AttackerKind, combat.AttackerId), out var attacker))
                {
                    if (attacker.IsNpc) attacker.PlayNpcAction(true, Time.time);
                    else attacker.Animate("attack1");
                }
            }
        }
        void Changed(WorldUpdate update)
        {
            var entity = update.Entity; var key = (entity.Kind, entity.Id);
            if (update.Kind == WorldUpdateKind.Leave)
            {
                if (update.Entity.Kind == WorldEntityKind.Npc) GetComponentInParent<NetworkWorldController>()?.CancelEruption(update.Entity.Id);
                if (requests.Remove(key, out var request)) request.Dispose();
                if (views.Remove(key, out var old))
                {
                    if (entity.Kind == WorldEntityKind.Player && old.Avatar && old.Avatar.IsReady && !entity.Hidden)
                    { old.Nano?.Clear(); old.FadeAvatar(false); }
                    else { old.gameObject.SetActive(false); Destroy(old.gameObject); }
                }
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
                    else view.FadeAvatar(true);
                    view.Nano = go.AddComponent<WorldNanoCompanion>();
                    view.Nano.Initialize(this, GetComponentInParent<NetworkWorldController>()?.View, true);
                    view.Nano.SetWeapon(entity.Character.Equipment[0].ItemId);
                    if (!entity.Hidden) view.Nano.SetNano(entity.NanoId);
                }
                else
                {
                    view.Definition = catalog.Find(entity.NpcType);
                    if (view.Definition?.HnpcAppearance >= 0)
                    {
                        view.Avatar = go.AddComponent<NativeAvatarPreview>();
                        _ = LoadHnpcAsync(key, view);
                    }
                    else if (view.Definition?.HiddenMarker == true) { /* Source NpcMoveController location probe: keep its root, suppress its model. */ }
                    else if (string.IsNullOrEmpty(view.Definition?.Model)) ContentFailure($"NPC type {entity.NpcType}: accepted model route is missing.");
                    else StartCoroutine(LoadNpc(key, view));
                }
                var collider = go.AddComponent<CapsuleCollider>(); collider.isTrigger = true;
                collider.height = Mathf.Max(1, view.Definition?.Height ?? 1.8f); collider.radius = Mathf.Max(.3f, view.Definition?.Radius ?? .45f);
                collider.center = Vector3.up * collider.height * .5f;
                if (!view.IsNpc) view.Animate("stand1");
                go.AddComponent<NativeBuffPresentation>().SetCondition(entity.ConditionFlags, Camera.main, view.Avatar ? 1 : Mathf.Max(.01f, (view.Definition?.Radius ?? .5f) * 2));
                return;
            }
            if (!views.TryGetValue(key, out var current)) return;
            int previousHp = current.Entity.Hp;
            current.Entity = entity;
            if (update.Kind == WorldUpdateKind.Condition) current.GetComponent<NativeBuffPresentation>()?.SetCondition(entity.ConditionFlags, Camera.main, current.Avatar ? 1 : Mathf.Max(.01f, (current.Definition?.Radius ?? .5f) * 2));
            if (update.Kind == WorldUpdateKind.Settings)
            {
                current.transform.localScale = Vector3.one * entity.Scale;
                foreach (var renderer in current.GetComponentsInChildren<Renderer>(true)) renderer.enabled = !entity.Hidden;
            }
            if (current.Nano && (update.Kind == WorldUpdateKind.Nano || update.Kind == WorldUpdateKind.Settings))
            {
                if (entity.Hidden) current.Nano.Clear();
                else current.Nano.SetNano(entity.NanoId);
            }
            if (current.Nano && update.Kind == WorldUpdateKind.Equipment && update.EquipmentSlot == 0)
                current.Nano.SetWeapon(entity.Character.Equipment[0].ItemId);
            if (update.Kind is WorldUpdateKind.Equipment or WorldUpdateKind.Appearance) current.Avatar?.Apply(entity.Character);
            if ((update.Kind == WorldUpdateKind.State || update.Kind == WorldUpdateKind.Equipment) && current.Avatar)
            {
                int vehicle = (entity.StateFlags & 8) != 0 ? entity.Character.Equipment[8].ItemId : 0;
                bool vehicleChanged = current.Avatar.PersonalVehicleId != vehicle;
                current.Avatar.SetPersonalVehicle(vehicle);
                if (vehicleChanged || update.Kind == WorldUpdateKind.Equipment && update.EquipmentSlot == 0 && !current.Avatar.HasActiveEmote)
                {
                    current.LastClip = null;
                    current.Animate(entity.Jumping ? "jumpstart" : entity.Speed > 0 ? "run" : "stand1");
                }
            }
            if (update.Kind == WorldUpdateKind.Health)
            {
                if (entity.Hp <= 0 && current.Avatar) current.Avatar.SetZiplinePresentation(false);
                if (!current.IsNpc) current.Animate(entity.Hp == 0 ? "death" : "hit");
                else if (entity.Hp > 0 && entity.Hp < previousHp) current.PlayNpcAction(false, Time.time);
            }
            if (update.Kind == WorldUpdateKind.Move)
            {
                if (entity.Kind == WorldEntityKind.Player) current.PresentJump(entity.Jumping);
                if (current.Avatar) current.Avatar.SetZiplinePresentation(entity.Ziplining && entity.Hp > 0);
                if (current.Avatar && current.Avatar.CancelEmote()) current.LastClip = null;
                current.Destination = WorldCoordinates.FromServer(entity.X, entity.Y, entity.Z);
                current.Speed = entity.Kind == WorldEntityKind.Npc ? entity.Speed / 100f : Mathf.Max(6, entity.Speed / 100f);
                // Movement arrives at 8 Hz. A short gap after reaching one waypoint must not restart the pose on the next one.
                if (current.IsNpc) current.NpcMovingUntil = current.Speed > 0 ? Time.time + .21f : 0;
                if (update.HasHeading) current.transform.rotation = Quaternion.Euler(0, WorldCoordinates.FromServerHeading(entity.Angle), 0);
                if (!current.IsNpc && !entity.Ziplining) current.Animate(entity.Jumping ? "jumpstart" : entity.Speed > 0 ? "run" : "stand1");
            }
            if (current.IsNpc) current.RefreshNpcAnimation(Time.time);
        }
        async Task LoadHnpcAsync((WorldEntityKind, int) key, NetworkEntityView view)
        {
            var token = view.destroyCancellationToken;
            bool IsCurrent() => this && view && view.gameObject.activeInHierarchy &&
                views.TryGetValue(key, out var current) && current == view;
            try
            {
                var loading = NativeAvatarPreview.PrepareHnpcAsync(view.Definition.HnpcAppearance, token);
                // Even a fully resident appearance must not assemble inside a burst of interest packets.
                if (loading.IsCompletedSuccessfully) await Awaitable.NextFrameAsync(token);
                var appearance = await loading;
                if (!IsCurrent()) return;
                view.Avatar.ApplyHnpc(appearance);
                if (!view.Avatar.IsReady) { ContentFailure($"HNPC type {view.Entity.NpcType}: {view.Avatar.LastError}"); return; }
                AddServiceIcon(view);
                if (view.Entity.Hidden)
                    foreach (var renderer in view.Avatar.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                view.RefreshNpcAnimation(Time.time);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                if (IsCurrent()) ContentFailure($"HNPC type {view.Entity.NpcType}: {error.Message}");
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
                WorldNpcSkins.Apply(model, view.Definition.Type);
                var animators = model.GetComponentsInChildren<NativeNpcAnimator>(true);
                foreach (var animator in animators) animator.WorldEvents = true;
                if (animators.Length > 0) view.NpcAnimator = animators[0];
                AddServiceIcon(view);
                if (view.Definition.Type == 1386) view.gameObject.AddComponent<RecallPointPresentation>().Initialize(view.Definition, Camera.main, view.Entity.Id == selectedRecallPoint);
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true)) renderer.enabled = !view.Entity.Hidden;
                view.RefreshNpcAnimation(Time.time);
            }
            else ContentFailure($"NPC type {view.Entity.NpcType}: cannot load {view.Definition.Model}.");
            // Keep the handle alive until interest exit; a render clone still references prefab dependencies.
        }
        void AddServiceIcon(NetworkEntityView view)
        {
            var npc = view.Definition;
            if (npc == null) return;
            int id = npc.Effect;
            bool shop = npc.IsVendor && (id is >= 672 and <= 678 or 824 or 825 or 826);
            bool transport = npc.Category == 15 && id is 681 or 683;
            if (!shop && !transport) return;
            var socket = view.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "GameIcon");
            var camera = GetComponentInParent<NetworkWorldController>()?.View;
            var effect = NativeCombatEffect.Spawn(socket ? socket : view.transform, id, 1, false, camera, -1);
            effect.name = "NPC service icon ES" + id;
            if (!socket) effect.transform.localPosition = Vector3.up * (npc.Height * .85f);
            if (view.Entity.Hidden)
                foreach (var renderer in effect.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        }

        internal void ContentFailure(string message) { contentFailures.Add(message); ContentError?.Invoke(message); }
        void Update()
        {
            foreach (var view in views.Values)
            {
                if (view.Entity.Kind == WorldEntityKind.Player && view.Entity.TransportId > 0 &&
                    views.ContainsKey((WorldEntityKind.Transportation, view.Entity.TransportId))) continue;
                var delta = view.Destination - view.transform.position;
                if (delta.sqrMagnitude > 900) view.transform.position = view.Destination;
                else view.transform.position = Vector3.MoveTowards(view.transform.position, view.Destination, Mathf.Max(.1f, view.Speed) * Time.deltaTime);
                if (view.IsNpc)
                {
                    delta.y = 0; if (delta.sqrMagnitude > .001f) view.transform.rotation = Quaternion.LookRotation(delta);
                    view.RefreshNpcAnimation(Time.time);
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
