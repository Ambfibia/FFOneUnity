using System;
using System.Collections.Generic;
using System.Linq;
using FFOne.Content;
using FFOne.Network;
using FFOne.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using CharacterInfo = FFOne.Network.CharacterInfo;

namespace FFOne.Tutorial
{
    public sealed partial class TutorialController : MonoBehaviour
    {
        public static readonly Vector3 StartPosition = new(547, -105.4f, 655);
        public bool IsReady { get; private set; }
        public string LastError { get; private set; }
        public Transform Player { get; private set; }
        public Camera ViewCamera { get; private set; }
        public Vector3 PlayerUnityPosition => Player ? Player.position : StartPosition;
        public event Action<Vector3, float> Moved;
        public TutorialStage Stage { get; private set; }
        public TutorialPanPlayer PanPlayer { get; private set; }
        public Transform NanoTransform => nano ? nano.transform : null;
        public bool IsCinematic => Stage.Scene != TutorialScene.None && cinematics != null && !cinematics.Finished;
        public bool Russian { get; private set; }
        public bool VoiceRussian { get; private set; }
        public bool ExternalInputBlocked { get; set; }
        public string Language => Russian ? "ru" : "en";
        readonly Dictionary<int, TutorialActor> actors = new();
        readonly List<AudioSource> loops = new();
        readonly List<Camera> previousCameras = new();
        readonly List<AudioListener> previousListeners = new();
        TutorialContent content;
        TutorialCinematics cinematics;
        TutorialCinematicEffects effects;
        CharacterController motor;
        readonly FFOne.World.LegacyGameplayCameraInput cameraInput = new();
        bool readCameraMotion;
        float mouseSensitivity = 1;
        bool invertMouseY, invertPadY;
        float padSensitivity = 5;
        NativeAvatarPreview avatar;
        GameObject worldRoot, playerRoot, cameraRoot, nano;
        AudioSource voice, sound, ambient, music;
        Action completed, cancelled;
        float yaw = 90, pitch = 12, vertical, stageTime, nextAttack, nextEnemyAttack;
        float attackAnimationEnd, jumpAge;
        bool nanoVoiceDisabled = true;
        float bars, fade, subtitleOpacity = 1, stageOriginYaw;
        Vector3 stageOrigin;
        string subtitle = "";
        readonly TutorialVoiceSubtitles voiceSubtitles = new();
        public string VoiceSubtitleText => voiceSubtitles.Text;
        public float VoiceSubtitleRemaining => voiceSubtitles.Remaining;
        public bool VoiceSubtitleVisible => !string.IsNullOrEmpty(VoiceSubtitleText) &&
            (eventScene ? IsCinematic && cinematicScreen && bars > .9f : Russian);
        public void TickVoiceSubtitles(float delta) => voiceSubtitles.Tick(delta);
        int subtitleCharacters = int.MaxValue;
        int audioCursor, deaths, damageNpc, damageUser;
        int interacting, currentTask;
        int playerHp = 1000, hiddenHud, tutorialEarnedFusionMatter;
        JToken picture;
        string spatialAudioTarget = "Camera";
        bool eventScene, cinematicScreen;
        bool jumped, nanoActive, nanoPowerUsed, ending, paused;
        bool mainMenu, journal, missionSelected, accepted, rewardClaimed, warped;
        TutorialActor target;
        Color oldAmbient;
        bool ambientCaptured;

        public async void Begin(CharacterInfo character, bool russian, Action onCompleted, Action onCancelled, bool? voiceRussian = null)
        {
            try { await BeginAsync(character, russian, onCompleted, onCancelled, voiceRussian); }
            catch (OperationCanceledException) { }
            catch (Exception) { /* BeginAsync records the failure for legacy callers. */ }
        }

        void Update()
        {
            try { UpdateTutorial(); }
            catch (Exception error) { LastError = error.Message; Debug.LogException(error, this); Cleanup(); }
        }
        void UpdateTutorial()
        {
            TickHud();
            readCameraMotion = cameraInput.Capture(IsReady && !ending && !ExternalInputBlocked && !paused &&
                !mainMenu && !journal && interacting == 0 && !eventScene && !IsCinematic);
            if (!IsReady || ending) return;
            if (worldStream && Player) worldStream.Anchor = Player.position;
            if (ExternalInputBlocked) return;
            var keyboard = Keyboard.current;
            if (keyboard?.backquoteKey.wasPressedThisFrame == true) { Finish(); return; }
            if (keyboard?.escapeKey.wasPressedThisFrame == true)
            {
                if (IsCinematic) { SkipScene(); return; }
                paused = !paused;
            }
            if (paused) return;
            if (IsCinematic && (keyboard?.spaceKey.wasPressedThisFrame == true ||
                Gamepad.current?.buttonSouth.wasPressedThisFrame == true)) { SkipScene(); return; }
            float delta = Mathf.Min(Time.deltaTime, .1f);
            stageTime += delta;
            if (IsCinematic)
            {
                cinematics.Tick(Time.deltaTime); PanPlayer?.Tick(Time.deltaTime);
                if (cinematics.Finished) yaw = Player.eulerAngles.y;
            }
            else
            {
                TickVoiceSubtitles(Time.deltaTime);
                HandleInput(delta); UpdateCamera(); UpdateTarget(); UpdateEnemies();
            }
            GroundActors(delta);
            Evaluate();
            TickAuxiliary(Time.deltaTime);
            if (!IsCinematic && nano && nanoActive) nano.transform.position = Vector3.Lerp(nano.transform.position, Player.position + Player.right * 1.3f + Vector3.up * 1.6f, delta * 5);
            TickHud();
        }

        void HandleInput(float delta)
        {
            var mouse = Mouse.current;
            bool journalPressed = GameplayInput.Pressed(GameplayAction.Journal);
            bool close = Gamepad.current?.buttonEast.wasPressedThisFrame == true;
            if (journal)
            {
                if (TutorialWindowCanClose && (journalPressed || close)) journal = false;
                return;
            }
            if (mainMenu)
            {
                if (journalPressed && Stage.Allows(11)) journal = true;
                else if (close || GameplayInput.Pressed(GameplayAction.Menu)) mainMenu = false;
                return;
            }
            if (interacting != 0 || eventScene || HudConsumedInput) return;
            if (Stage.Chapter == 3 && Stage.Step >= 8 && Stage.Step <= 12 && journalPressed)
            {
                journal = true;
                return;
            }
            bool interact = Gamepad.current?.buttonSouth.wasPressedThisFrame == true && Stage.Allows(5);
            if (interact && actors.TryGetValue(ObjectiveNpc, out var objective) && objective && !objective.Hostile &&
                objective.Hp > 0 && Vector3.Distance(Player.position, objective.transform.position) < 10)
                target = objective;
            if (Stage.Allows(17))
            {
                var motion = mouse?.delta.ReadValue() ?? Vector2.zero;
                var look = readCameraMotion ? FFOne.World.LegacyGameplayCameraInput.Look(motion, mouseSensitivity, invertMouseY) : Vector2.zero;
                var pad = GameplayInput.PadLook(delta, padSensitivity, invertPadY);
                yaw += look.x + pad.x;
                pitch = Mathf.Clamp(pitch + look.y + pad.y, -30, 70);
            }
            float forward = (Stage.Allows(0) && GameplayInput.Held(GameplayAction.Up) ? 1 : 0) - (Stage.Allows(1) && GameplayInput.Held(GameplayAction.Down) ? 1 : 0);
            float side = Stage.Allows(2) ? (GameplayInput.Held(GameplayAction.Right) ? 1 : 0) - (GameplayInput.Held(GameplayAction.Left) ? 1 : 0) : 0;
            if (Stage.Allows(3)) yaw += ((GameplayInput.Held(GameplayAction.RightTurn) ? 1 : 0) - (GameplayInput.Held(GameplayAction.LeftTurn) ? 1 : 0)) * 100 * delta;
            bool moving = forward != 0 || side != 0;
            bool fullBodyAttack = (avatar == null || avatar.PlayerAnimation == null) && Time.time < attackAnimationEnd;
            bool freeCamera = GameplayInput.Held(GameplayAction.FreeCamera) &&
                !(Stage.Allows(3) && (GameplayInput.Held(GameplayAction.LeftTurn) || GameplayInput.Held(GameplayAction.RightTurn)));
            float heading = freeCamera ? Player.eulerAngles.y : yaw;
            Vector3 displacement = Quaternion.Euler(0, heading, 0) * new Vector3(side, 0, forward).normalized * (forward < 0 ? 3 : 6);
            bool grounded = IsPlayerGrounded();
            if (grounded && vertical < 0) vertical = 0;
            bool armed = Player.GetComponent<TutorialPlayerEquipment>() != null;
            bool launched = Stage.Allows(4) && GameplayInput.Pressed(GameplayAction.Jump) && grounded && !(interact && target && !target.Hostile);
            if (launched) { vertical = 5.68f * NativePlayerMotor.JumpMultiplier; jumpAge = 0; jumped = true; }
            var collision = NativePlayerMotor.Step(motor, displacement, ref vertical, ref jumpAge, delta, GameplayInput.Held(GameplayAction.Jump), launched);
            if ((collision & CollisionFlags.Above) != 0 && vertical > 0 || (collision & CollisionFlags.Below) != 0 && vertical < 0) vertical = 0;
            grounded = !launched && IsPlayerGrounded();
            if (moving || launched || !grounded || Stage.Allows(3) && (GameplayInput.Held(GameplayAction.RightTurn) || GameplayInput.Held(GameplayAction.LeftTurn)))
                Player.rotation = Quaternion.Euler(0, heading, 0);
            if (avatar && avatar.PlayerAnimation)
            {
                bool ready = Time.time < attackAnimationEnd || TutorialCombatHudPresentation.IsActive(tutorialLastCombatResult, Time.realtimeSinceStartup);
                avatar.PlayerAnimation.UpdateLocomotion(side, forward, grounded, launched, armed ? "rifle" : "", ready, delta, vertical);
            }
            else if (launched) Animate(Player.gameObject, armed ? "riflejumpstart" : "jumpstart");
            else if (grounded && !fullBodyAttack)
                Animate(Player.gameObject, (armed ? "rifle" : "") + (moving ? forward < 0 ? "runback" : "run" : "stand1"));
            if (moving || Mathf.Abs(vertical) > 2) Moved?.Invoke(Player.position, Player.eulerAngles.y);
            if (Player.position.y < -285) WarpPlayer(stageOrigin);
            if (Stage.Allows(8) && GameplayInput.Pressed(GameplayAction.Menu)) mainMenu = true;
            if (Stage.Allows(11) && GameplayInput.Pressed(GameplayAction.Journal)) { journal = true; if (Stage.Chapter == 3 && Stage.Step == 6) mainMenu = true; }
            if (Stage.Allows(18) && GameplayInput.Pressed(GameplayAction.Nano1)) SummonNano();
            if (Stage.Allows(6) && GameplayInput.Pressed(GameplayAction.Fire2) && nanoActive && target && target.Hostile)
            { nanoPowerUsed = true; target.StunUntil = Time.time + 5; PlayVoice("Computress_Tut61"); }
            if (Stage.Allows(5) && (GameplayInput.Held(GameplayAction.Fire1) || target && !target.Hostile && interact) && Time.time >= nextAttack && target)
            {
                nextAttack = Time.time + 1;
                if (target.Hostile) Attack(target);
                else if (Vector3.Distance(Player.position, target.transform.position) < 10) interacting = target.RuntimeId;
            }
        }

        bool IsPlayerGrounded()
        {
            return motor && NativePlayerMotor.IsGrounded(motor, vertical);
        }

        internal void StepCinematicPlayer(float delta)
        {
            if (!motor || !motor.enabled || delta <= 0) return;
            // Event scenes take over input and the pose, while the capsule
            // continues to settle under the same gravity as gameplay.
            NativePlayerMotor.Step(motor, Vector3.zero, ref vertical, ref jumpAge,
                Mathf.Min(delta, .1f), false, false, false);
        }

        void UpdateCamera()
        {
            if (!ViewCamera || !Player) return;
            Vector3 focus = Player.position + Vector3.up * 1.6f;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
            Vector3 offset = rotation * new Vector3(0, .2f, -6);
            offset = NativePlayerCamera.ResolveOffset(ViewCamera, Player, focus, offset);
            ViewCamera.transform.SetPositionAndRotation(focus + offset, rotation);
        }
        void UpdateTarget()
        {
            target = null;
            if (interacting != 0 || mainMenu || journal) return;
            var position = Cursor.lockState == CursorLockMode.Locked ? new Vector2(Screen.width / 2f, Screen.height / 2f)
                : Mouse.current?.position.ReadValue() ?? new Vector2(Screen.width / 2f, Screen.height / 2f);
            var ray = ViewCamera.ScreenPointToRay(position);
            foreach (var hit in Physics.RaycastAll(ray, 80).OrderBy(h => h.distance))
            {
                var actor = hit.collider.GetComponentInParent<TutorialActor>();
                if (actor && actor.Hp > 0) { target = actor; break; }
            }
        }
        void Attack(TutorialActor actor)
        {
            if (Vector3.Distance(Player.position, actor.transform.position) > 16) return;
            RecordTutorialCombatResult();
            actor.Damaged = true; damageNpc++;
            actor.Hp = Mathf.Max(0, actor.Hp - VirtualDamage(nanoActive ? 1 : -1, actor.Style, actor.Invulnerable));
            FacePlayer(Quaternion.LookRotation(Vector3.ProjectOnPlane(actor.transform.position - Player.position, Vector3.up)));
            var playerAnimation = Player.GetComponentInChildren<NativePlayerAnimator>();
            if (playerAnimation) playerAnimation.Play("rifleattack1upper", true);
            else Animate(Player.gameObject, "rifleattack1");
            attackAnimationEnd = Time.time + (playerAnimation ? playerAnimation.Duration("rifleattack1upper") : 1);
            PlayCombatAnimation(actor, actor.Hp == 0 ? "death" : "wound",
                actor.Hp == 0 ? TutorialAnimationEffectReceiver.Completion.Hold : TutorialAnimationEffectReceiver.Completion.Ready);
            if (actor.Hp == 0)
            {
                deaths++;
                tutorialEarnedFusionMatter = Mathf.Min(220, tutorialEarnedFusionMatter + 30);
                if (actor.RuntimeId == 1100) AdvanceTutorialTask(2249);
            }
        }
        void UpdateEnemies()
        {
            if (Time.time < nextEnemyAttack) return;
            nextEnemyAttack = Time.time + 1;
            int id = Stage.Chapter switch {
                1 when Stage.Step == 6 || Stage.Step == 7 => 1005,
                3 when Stage.Step == 4 && Near(1100, 30) => 1100,
                4 when Stage.Step == 11 && Near(4000, 10) => 4000,
                5 when Stage.Step == 2 || Stage.Step == 4 || Stage.Step == 6 || Stage.Step == 7 => 5000,
                _ => 0
            };
            if (!actors.TryGetValue(id, out var actor) || !actor || actor.Hp <= 0 || actor.StunUntil > Time.time) return;
            if (actor.AttackRange <= 0 || Vector3.Distance(Player.position, actor.transform.position) > actor.AttackRange) return;
            RecordTutorialCombatResult();
            damageUser++; playerHp = Mathf.Max(100, playerHp - 50);
            PlayCombatAnimation(actor, "melee1", TutorialAnimationEffectReceiver.Completion.Ready);
        }
        public GameObject SpawnActor(int runtimeId, int npcType, Vector3 unityPosition, float yaw = 0)
        {
            RemoveActor(runtimeId);
            var definition = content.Actors.SingleOrDefault(a => a.NpcType == npcType);
            if (definition == null || !definition.Model) throw new InvalidOperationException("Tutorial actor is not imported: " + npcType);
            if (definition.Class < 100 && npcType != 2968) unityPosition.y += 1;
            var go = new GameObject("Tutorial NPC " + runtimeId + " " + definition.Name); go.transform.SetParent(worldRoot.transform, false);
            go.transform.SetPositionAndRotation(unityPosition, Quaternion.Euler(0, yaw, 0));
            var model = Instantiate(NativeModelAssets.Resolve(definition.Model), go.transform); model.transform.localScale = Vector3.one * definition.Scale;
            FFOne.World.WorldNpcSkins.Apply(model, npcType);
            foreach (var animation in model.GetComponentsInChildren<NativeNpcAnimator>(true))
                animation.gameObject.AddComponent<TutorialAnimationEffectReceiver>().Initialize(this, definition, effects);
            var actor = go.AddComponent<TutorialActor>(); actor.RuntimeId = runtimeId; actor.NpcType = npcType;
            actor.Hp = actor.MaxHp = Mathf.Max(1, definition.MaxHp); actor.DisplayName = definition.Name;
            actor.Style = definition.Style; actor.Class = definition.Class; actor.AttackRange = definition.AttackRange; actor.Height = definition.Height;
            actor.Hostile = npcType == 2674 || npcType == 2675 || npcType == 2676 || npcType == 2677 || npcType == 2678;
            if (!actor.IsScenery)
            {
                var capsule = go.AddComponent<CapsuleCollider>(); capsule.height = Mathf.Max(.01f, definition.Height); capsule.radius = Mathf.Max(.01f, definition.Radius);
                capsule.center = Vector3.up * definition.Height * .5f;
            }
            actors[runtimeId] = actor; Animate(go, "stand1"); return go;
        }
        public GameObject Actor(int runtimeId) => runtimeId == -1 ? nano : actors.TryGetValue(runtimeId, out var actor) && actor ? actor.gameObject : null;
        void GroundActors(float delta)
        {
            foreach (var actor in actors.Values)
            {
                if (!actor || actor.Hp <= 0 || actor.ForceUpdate || actor.Class >= 100 || actor.IsScenery) continue;
                Vector3 position = actor.transform.position;
                float? floor = null;
                foreach (var hit in Physics.RaycastAll(position + Vector3.up * actor.Height, Vector3.down, actor.Height + 10, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (!(hit.collider is MeshCollider || hit.collider is TerrainCollider) || worldStream == null || !worldStream.Owns(hit.collider)) continue;
                    floor = floor.HasValue ? Mathf.Max(floor.Value, hit.point.y) : hit.point.y;
                }
                var result = AdvanceGrounding(position.y, floor, actor.VerticalVelocity, delta);
                position.y = result.y; actor.VerticalVelocity = result.velocity; actor.transform.position = position;
            }
        }
        public static (float y, float velocity) AdvanceGrounding(float y, float? floor, float velocity, float delta)
        {
            if (!float.IsFinite(y) || !float.IsFinite(velocity) || !float.IsFinite(delta) || delta <= 0) return (y, velocity);
            if (floor.HasValue && floor.Value >= y) return (floor.Value, 0);
            velocity = Mathf.Max(-10, velocity - 10 * delta);
            float next = y + velocity * delta;
            return floor.HasValue && next <= floor.Value ? (floor.Value, 0) : (next, velocity);
        }
        public void RemoveActor(int runtimeId)
        {
            if (runtimeId == -1) { if (nano) nano.SetActive(false); return; }
            if (!actors.Remove(runtimeId, out var actor) || !actor) return;
            actor.gameObject.SetActive(false); Destroy(actor.gameObject);
        }
        public void WarpPlayer(Vector3 unityPosition)
        {
            if (!Player) return;
            if (motor) NativePlayerMotor.Teleport(motor, unityPosition);
            else Player.position = unityPosition;
            vertical = 0; jumpAge = 0;
            Moved?.Invoke(unityPosition, yaw);
        }
        public void FacePlayer(Quaternion rotation) { Player.rotation = rotation; yaw = rotation.eulerAngles.y; }
        void SkipScene() { cinematics.Skip(); yaw = Player.eulerAngles.y; }
        public static int VirtualDamage(int attackerStyle, int defenderStyle, bool dontKill)
        {
            if (dontKill || attackerStyle < -1 || attackerStyle > 2 || defenderStyle < -1 || defenderStyle > 2) return 0;
            // cnVirtualServer's four-by-four style matrix; no server combat mutation.
            int[,] damage = { {200,100,100,100}, {300,200,100,300}, {300,300,200,100}, {300,100,300,200} };
            return damage[attackerStyle + 1, defenderStyle + 1];
        }
        public static void Animate(GameObject actor, string clip)
        {
            if (!actor) return;
            var player = actor.GetComponentInChildren<NativePlayerAnimator>();
            if (player) { player.Play(clip); return; }
            var npc = actor.GetComponentInChildren<TutorialAnimationEffectReceiver>(true);
            if (npc)
            {
                npc.PlayNpc(clip, TutorialAnimationEffectReceiver.Completion.Loop, .15f,
                    clip == "stand1" ? TutorialNpcPose.Rest : TutorialNpcPose.Forced);
                return;
            }
            var animation = actor.GetComponentInChildren<Animation>();
            if (!animation || !animation.GetClip(clip)) return;
            if (!animation.IsPlaying(clip)) animation.CrossFade(clip, .15f);
        }

        static void PlayCombatAnimation(TutorialActor actor, string clip, TutorialAnimationEffectReceiver.Completion completion)
        {
            foreach (var receiver in actor.GetComponentsInChildren<TutorialAnimationEffectReceiver>(true))
                if (receiver.PlayNpc(clip, completion, .15f)) return;
        }
        public void PlayVoice(string cue)
        {
            var row = content?.Audio.FirstOrDefault(a => string.Equals(a.Cue, cue, StringComparison.OrdinalIgnoreCase));
            var clip = row == null ? null : VoiceRussian && row.Russian ? row.Russian : row.English;
            voiceSubtitles.Play(cue, Russian, Localized, VoiceRussian && clip ? clip.length : 0);
            if (voice) voice.Stop();
            if (!clip || !voice) { Debug.LogWarning("Missing tutorial voice " + cue); return; }
            voice.clip = clip; voice.Play();
        }
        public void PlaySound(string cue, bool loop = false)
        {
            var definition = content?.Audio.FirstOrDefault(a => string.Equals(a.Cue, cue, StringComparison.OrdinalIgnoreCase));
            var clip = definition?.English;
            if (!clip) return;
            if (!loop) { (definition.Category == "music" ? music : sound).PlayOneShot(clip); return; }
            if (loops.Any(existing => existing && existing.clip == clip)) return;
            var source = cameraRoot.AddComponent<AudioSource>(); source.clip = clip; source.loop = true; source.Play(); loops.Add(source);
        }
        public void StopLoops() { foreach (var source in loops) if (source) { source.Stop(); Destroy(source); } loops.Clear(); }
        public void PlaySpatialSound(string cue, Vector3 position)
        {
            var clip = content?.Audio.FirstOrDefault(a => string.Equals(a.Cue, cue, StringComparison.OrdinalIgnoreCase))?.English;
            if (!clip) return;
            PlayAnimationSound(clip, position);
        }
        public int AnimationSoundsPlayed { get; private set; }
        public void PlayAnimationSound(AudioClip clip, Vector3 position)
        {
            bool playerFocus = spatialAudioTarget == "Player";
            Vector3 focus = playerFocus ? Player.position : ViewCamera.transform.position;
            int active = worldRoot.GetComponentsInChildren<AudioSource>().Count(s => s.isPlaying);
            float range = playerFocus ? active < 6 ? 18 : 16 : active < 6 ? 40 : 30;
            if (Vector3.Distance(position, focus) > range) return;
            var go = new GameObject(clip.name); go.transform.SetParent(worldRoot.transform); go.transform.position = position;
            var source = go.AddComponent<AudioSource>(); source.clip = clip; source.spatialBlend = 1; source.maxDistance = range;
            source.Play(); AnimationSoundsPlayed++; Destroy(go, clip.length);
        }
        void PlayTimeline()
        {
            if (!TutorialAudioTimeline.Cues.TryGetValue(Stage.Scene, out var cues)) return;
            while (audioCursor < cues.Length && cues[audioCursor].Seconds <= stageTime)
            {
                var cue = cues[audioCursor++];
                if (cue.Kind == 0) PlayVoice(cue.Cue); else if (cue.Kind == 3) StopLoops(); else PlaySound(cue.Cue, cue.Kind == 2);
            }
        }
        public void Subtitle(string key)
        {
            subtitle = Localized(key);
            locationCaption = key is "tutorial.cinematic.tech_square_future" or "content.tutorial.scene.1.1";
            subtitleOpacity = 1; subtitleCharacters = int.MaxValue;
        }
        public void SetSubtitleCharacters(int visible, int canonicalTotal) => subtitleCharacters = canonicalTotal <= 0 ? subtitle.Length : Mathf.Clamp(Mathf.RoundToInt((float)visible / canonicalTotal * subtitle.Length), 0, subtitle.Length);
        public void SetFade(float value) => fade = Mathf.Clamp01(value);
        public void SetBars(float value) => bars = Mathf.Clamp01(value);
        public void SetSubtitleOpacity(float value) => subtitleOpacity = Mathf.Clamp01(value);
        public void SetPan(int oneBased) { if (oneBased <= 0) PanPlayer?.Stop(); else PanPlayer?.Begin(); }
        string Localized(string key) => string.IsNullOrEmpty(key) ? "" : LocalizationCatalog.Lookup(key, Language);
        void SummonNano() { nanoActive = true; if (nano) nano.SetActive(true); }
        public void ApplyCinematicAction(string kind, JToken value)
        {
            // Local tutorial presentation changes never grant persistent server inventory.
            if (kind == "Pan")
            {
                switch ((string)value?["type"])
                {
                    case "LoadEightTextures": PanPlayer.Preload(); break;
                    case "Start": PanPlayer.Start(); break;
                    case "Stop": PanPlayer.Stop(); break;
                    case "ReleaseEightTextures": PanPlayer.Release(); break;
                }
            }
            else if (kind == "Effect") effects.Execute(value);
            else if (kind == "Projectile")
            {
                if ((string)value["type"] == "NpcToPlayerPair")
                {
                    var actor = Actor((int)value["npc_id"]!);
                    if (!actor) throw new InvalidOperationException("Cinematic projectile source NPC is absent.");
                    effects.ProjectilePair(actor.transform.position, () => Player.position, false);
                }
                else
                {
                    var actor = Actor(4000);
                    if (!actor) throw new InvalidOperationException("Fusion Buttercup projectile target is absent.");
                    Vector3 destination = actor.transform.position + Vector3.up * 3;
                    effects.ProjectilePair(Player.position, () => destination, true);
                }
            }
            else if (kind == "VoiceOff") { if (voice) voice.Stop(); voiceSubtitles.Clear(); }
            else if (kind == "StopBgm") music.Stop();
            else if (kind == "Ambient")
            {
                if (value == null || value.Type == JTokenType.Null || string.IsNullOrEmpty((string)value["cue"]) || (string)value["cue"] == "none") ambient.Stop();
            }
            else if (kind == "Picture") picture = (string)value?["type"] == "HideAll" ? null : value;
            else if (kind == "Hud") hiddenHud = Mathf.Max(0, hiddenHud + ((string)value?["type"] == "PushHide" ? 1 : -1));
            else if (kind == "SpatialAudio") spatialAudioTarget = (string)value?["type"];
            else if (kind == "Cinematic")
            {
                cinematicScreen = (bool)value;
                if (!cinematicScreen) SetBars(0);
            }
            else if (kind == "EventScene") eventScene = (bool)value;
            else if (kind == "Player" && (string)value?["type"] == "SetTemporaryNanoAbsent") nanoActive = false;
            else if (kind == "Nano")
            {
                string action = (string)value?["type"];
                if (action == "Equip") { /* Slot 0 / Nano 1 / skill 1 remains tutorial-local. */ }
                else if (action == "Call") { SummonNano(); Animate(nano, "call"); }
                else if (action == "Happy") Animate(nano, "happy");
                else if (action == "Stand") Animate(nano, "stand1");
                else if (action == "Emote") { if (nano) nano.SetActive(true); Animate(nano, (string)value["args"]?[0]); }
                else if (action == "SetVoiceDisabled") nanoVoiceDisabled = (bool)value["args"]![0];
                else if (action == "DestroyPresentationObject" && nano) { nano.SetActive(false); nanoActive = false; }
            }
            else if (kind == "Progress") { /* The finite scene owns visuals; Evaluate owns chapter completion. */ }
            else if (kind == "Equipment") TutorialPlayerEquipment.Equip(Player);
        }
        public void Cancel()
        {
            if (ending) return; ending = true; Cleanup(); cancelled?.Invoke();
        }
        void Finish()
        {
            if (ending) return; ending = true; Cleanup(); completed?.Invoke();
        }
        public void Stop() { ending = true; Cleanup(); }
        void Cleanup()
        {
            cameraInput.Release();
            DetachHud();
            StopWorld();
            IsReady = false; StopAuxiliary(); StopLoops(); cinematics?.Dispose(); cinematics = null; PanPlayer?.Dispose(); PanPlayer = null;
            if (worldRoot) Destroy(worldRoot); if (playerRoot) Destroy(playerRoot); if (cameraRoot) Destroy(cameraRoot);
            foreach (var camera in previousCameras) if (camera) camera.enabled = true;
            foreach (var listener in previousListeners) if (listener) listener.enabled = true;
            previousListeners.Clear();
            previousCameras.Clear(); actors.Clear();
            if (ambientCaptured) { RenderSettings.ambientLight = oldAmbient; ambientCaptured = false; }
        }
        void OnDestroy() => Cleanup();
    }
}
