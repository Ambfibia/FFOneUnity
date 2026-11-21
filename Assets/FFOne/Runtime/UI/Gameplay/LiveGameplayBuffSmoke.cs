#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Integration;
using FFOne.Content;
using FFOne.Network;
using FFOne.World;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
        internal async Task LiveBuffLogin(int port, string account = "unitybuffa")
        {
            await Session.LoginAsync("127.0.0.1", port, account, "test-only-password", lifetime.Token);
            await EnterLiveFixtureWorld();
        }
        internal void LiveCastActiveNano() => UseActiveNanoSkill();
    }
    public sealed partial class GameplayUiController
    {
        internal Task LiveUseBuffItem(int slot, int nanoSlot) => UseInventoryItem(slot, nanoSlot);
        internal bool LiveBuffItemPending => itemUsePending;
    }

    /// <summary>Real production packets drive HUD/conditions/replicas; closure rendering is reported separately.</summary>
    [DefaultExecutionOrder(1000)]
    public sealed class LiveGameplayBuffSmoke : MonoBehaviour
    {
        sealed class Plan { public Case[] passives, active; public Item[] items; public int[] effects, unresolvedSkills, unresolvedConsumers, unsupportedConsumers; }
        sealed class Case { public int nano, skill, buff, value, boostedValue; public bool group; }
        sealed class Item { public int slot, item, skill; public bool supported; }
        FrontendController frontend;
        FusionClient observer;
        WorldEntry peerEntry;
        WorldEntitySession peerWorld;
        BuffState peerBuffs;
        readonly List<ProtocolFrame> frames = new(), peerFrames = new();
        readonly List<string> checks = new(), failures = new();
        readonly HashSet<int> rendered = new();
        readonly List<object> transactions = new();
        readonly List<object> iconVisuals = new();
        readonly List<object> closureVisuals = new();
        readonly List<object> observerDeathReceipts = new();
        RenderTexture image, uiImage;
        PanelSettings uiPanel;
        string directory;
        Plan plan;
        bool observerHadAura;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Run()
        {
            if (DevelopmentConnection.Enabled && Environment.GetCommandLineArgs().Contains("--live-buff-smoke"))
                new GameObject("Native buff acceptance").AddComponent<LiveGameplayBuffSmoke>();
        }
        async void Start()
        {
            directory = Environment.GetEnvironmentVariable(DevelopmentConnection.ProfilesVariable);
            try
            {
                Directory.CreateDirectory(directory);
                plan = JsonConvert.DeserializeObject<Plan>(File.ReadAllText(Environment.GetEnvironmentVariable("FFONE_BUFF_PLAN")));
                var endpoint = LoginServer.Parse(Environment.GetEnvironmentVariable(DevelopmentConnection.ServerVariable)).Endpoint;
                if (!endpoint.IsLoopback) throw new InvalidOperationException("Buff acceptance requires loopback.");
                await Wait(() => (frontend = FindFirstObjectByType<FrontendController>()) && frontend.LiveFrontendReady, "native buff frontend ready");
                bool healthScenario = Environment.GetCommandLineArgs().Contains("--player-health");
                if (healthScenario && Environment.GetEnvironmentVariable("FFONE_TEST_PROFILE_BUNDLE") is string bundlePath)
                    await LoadInspectionProfiles(bundlePath);
                await frontend.LiveBuffLogin(endpoint.Port); frontend.NetworkEventReceived += Observe;
                Check(Protocol0104.Read16(frontend.Session.World.RawLoadData, 0) == 99, "native buffs retain normal account privilege99");
                frontend.LiveWorld.Replicas.ContentError += failures.Add;
                observer = new FusionClient(); var roster = await observer.LoginAsync("127.0.0.1", endpoint.Port, "unitybuffb", "test-only-password");
                peerEntry = await observer.EnterWorldAsync(roster.Single().Uid); await observer.CompleteLoadingAsync();
                peerWorld = new WorldEntitySession(peerEntry.PlayerId); peerBuffs = ProtocolBuff.FromEntry(peerEntry, 0);
                foreach (var frame in peerEntry.BootstrapPackets) ApplyPeer(frame);
                await Wait(() => frontend.LiveWorld.Replicas.Actors.Any(a => a.Entity.Kind == WorldEntityKind.Player && a.Entity.Id == peerEntry.PlayerId && a.Avatar && a.Avatar.IsReady), "native buff observer avatar enters real interest");
                image = new RenderTexture(1280, 720, 24); image.Create(); frontend.LiveWorld.View.targetTexture = image;
                var document = frontend.GetComponent<UIDocument>();
                uiImage = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32); uiImage.Create();
                uiPanel = Instantiate(document.panelSettings); uiPanel.targetTexture = uiImage; uiPanel.scaleMode = PanelScaleMode.ConstantPixelSize;
                uiPanel.scale = 1; uiPanel.clearColor = true; uiPanel.colorClearValue = Color.clear; document.panelSettings = uiPanel;
                await frontend.Session.InviteGroupAsync(peerEntry.PlayerId);
                await Wait(() => peerFrames.Any(f => f.PacketType == ProtocolSocial.Invite), "native buff group invitation reaches peer");
                await observer.AnswerGroupAsync(frontend.Session.World.PlayerId, true);
                await Wait(() => frames.Any(f => f.PacketType == ProtocolSocial.Join), "native buff group membership reaches frontend");
                if (healthScenario || Environment.GetCommandLineArgs().Contains("--nano-targets") || Environment.GetCommandLineArgs().Contains("--combat-audio"))
                {
                    if (healthScenario) await ExercisePlayerHealth();
                    else if (Environment.GetCommandLineArgs().Contains("--nano-targets")) await ExerciseNanoTargets();
                    else await ExerciseCombatAudio(endpoint.Port);
                    await observer.LeaveWorldAsync(); await frontend.LiveExit(); Write(true, null); Application.Quit(0); return;
                }
                await RenderClosures();
                foreach (var item in plan.passives) await Passive(item);
                await GroupLifecycle();
                foreach (var item in plan.active) await Active(item);
                await frontend.LiveMoveTo(632032, 187177, -5500); await Nano(1); await frontend.Session.ActivateNanoAsync(-1);
                foreach (var item in plan.items.Where(i => i.item != 126).OrderBy(i => i.item == 122 ? 1 : 0)) await UseItem(item);
                await Nano(33);
                await Wait(() => frontend.LiveWorld.Buffs.Value(15) == 500, "real stronger CashItem RewardBlob500 wins over weaker active Nano33 source");
                Check(!string.IsNullOrEmpty(frontend.Root.Q<VisualElement>("buff-15")?.Q<Label>(className: "gameplay-buff-timer")?.text),
                    "real authored timed item source displays its server duration in the native buff HUD");
                await CheckVisibleIcon(15, "reward-strong-cash-item-hud");
                Capture("reward-strong-cash-item-stack");
                await Wait(() => !frontend.LiveWorld.Buffs.Has(20), "native timed Invulnerable expires only on real server removal", 110);
                var reward = plan.passives.Single(c => c.nano == 33);
                await Wait(() => frontend.LiveWorld.Buffs.Has(15) && frontend.LiveWorld.Buffs.Value(15) == (frontend.LiveWorld.Buffs.Has(21) ? reward.boostedValue : reward.value),
                    "real stronger item expiry applies Change to surviving authored Nano value", 20);
                Check(BuffIcon(15)?.image != null,
                    "stronger source expiry retains the weaker source buff HUD icon");
                await CheckVisibleIcon(15, "reward-surviving-nano-hud");
                Capture("reward-surviving-nano-stack");
                await frontend.Session.ActivateNanoAsync(-1);
                await Wait(() => !frontend.LiveWorld.Buffs.Has(15) && frontend.Root.Q<VisualElement>("buff-15") == null,
                    "last weaker source removal clears real RewardBlob flag and icon");
                await UseItem(plan.items.Single(i => i.item == 126));
                await Phoenix(endpoint.Port);
                Check(rendered.SetEquals(plan.effects), "all15 original buff effect closures have emitted supported rendered geometry");
                Check(!frontend.LiveWorld.Player.GetComponent<NativeBuffPresentation>().MissingEffectIds.Any(), "real condition presentation reports no missing original effect IDs");
                foreach (var actor in frontend.LiveWorld.Replicas.Actors)
                    Check(!(actor.GetComponent<NativeBuffPresentation>()?.MissingEffectIds.Any() ?? false), "replica " + actor.Entity.Id + " reports no missing buff closure");
                Check(string.IsNullOrEmpty(frontend.LiveWorld.LastError), "native buff world has no content failure");
                await observer.LeaveWorldAsync(); await frontend.LiveExit(); Write(true, null); Application.Quit(0);
            }
            catch (Exception error) { Write(false, error.ToString()); Debug.LogError("NATIVE BUFF ACCEPTANCE FAILED: " + error); Application.Quit(1); }
            finally { observer?.Dispose(); }
        }
        async Task Nano(int id)
        {
            int start = frames.Count; await frontend.Session.ActivateNanoAsync(-1);
            await Wait(() => frames.Skip(start).Any(f => f.PacketType == ProtocolGameplay.NanoActiveSuccess), "native buff Nano deactivation ACK");
            // Normal-account Nano changes require the actual source station.
            await frontend.LiveMoveTo(632032, 187177, -5500);
            start = frames.Count; await frontend.Session.EquipNanoAsync(id, 0);
            await Wait(() => frames.Skip(start).Any(f => f.PacketType == ProtocolGameplay.NanoEquipSuccess), "native Nano" + id + " equip at real Nano station");
            start = frames.Count; await frontend.Session.ActivateNanoAsync(0);
            await Wait(() => frames.Skip(start).Any(f => f.PacketType == ProtocolGameplay.NanoActiveSuccess), "native Nano" + id + " activation ACK");
            // The station's authored model occupies the equip coordinate. Step
            // into its clear western approach before motor/VFX acceptance.
            await frontend.LiveMoveTo(631632, 187177, -5500);
        }
        Image BuffIcon(int id) => frontend.Root.Q<VisualElement>("buff-" + id)?.Q<Image>(className: "gameplay-buff-icon");

        async Task ExerciseNanoTargets()
        {
            var panel = frontend.Root.Q<VisualElement>(className: "gameplay-detectors");
            var copy = panel.Q<Label>(className: "gameplay-detector-copy");
            foreach (var item in plan.passives.Where(p => p.buff == 13 || p.buff == 14))
            {
                await Nano(item.nano);
                string label = item.buff == 14 ? "E.G.G." : frontend.LiveWorld.Replicas.Actors.First(a => a.Hostile && a.Entity.NpcType == 59).Definition.Name;
                await Wait(() => !panel.ClassListContains("ui-suppressed") && copy.text.Contains(label),
                    "detector " + item.buff + " displays a live " + label + " interest");
                int effect = GameplayUiCatalog.Load().BuffEffect(item.buff, true);
                await Effect(frontend.LiveWorld.Player, effect, "detector-" + item.buff + "-pulse");
                await CaptureHud("detector-" + item.buff + "-active");
                await Wait(() => !frontend.LiveWorld.Player.GetComponentsInChildren<NativeCombatEffect>(true).Any(e => e.name == "Native combat ES" + effect),
                    "one-shot detector effect ES" + effect + " expires while the detector still lists its target", 10);
                await frontend.Session.ActivateNanoAsync(-1);
                await Wait(() => !frontend.LiveWorld.Buffs.Has(item.buff) && panel.resolvedStyle.display == DisplayStyle.None &&
                    !frontend.LiveWorld.Player.GetComponentsInChildren<NativeCombatEffect>(true).Any(e => e.name == "Native combat ES" + effect),
                    "detector " + item.buff + " hides its list and clears its flag and effect ES" + effect + " after withdrawal");
                await CaptureHud("detector-" + item.buff + "-withdrawn");
            }
            await Nano(16);
            var world = frontend.LiveWorld;
            var mobs = world.Replicas.Actors.Where(a => a.Hostile && a.Entity.NpcType == 59 && a.Entity.Hp > 0).ToArray();
            var skill = WorldSkillCatalog.Load().Find(46);
            var target = mobs.OrderByDescending(a => mobs.Count(b => Vector3.Distance(a.transform.position, b.transform.position) < skill.Area))
                .ThenBy(a => a.Entity.X).First();
            await frontend.LiveMoveTo(target.Entity.X - 650, target.Entity.Y, target.Entity.Z);
            var before = world.Replicas.Actors.Where(a => a.Hostile && a.Entity.Hp > 0).ToDictionary(a => a.Entity.Id, a => a.Entity.Hp);
            var center = target.GetComponent<Collider>().bounds.center;
            var muzzle = world.Player.position + Vector3.up;
            world.View.transform.SetPositionAndRotation(muzzle, Quaternion.LookRotation(center - muzzle));
            target = world.Target(skill.Range + 3);
            Check(target && target.Hostile && mobs.Any(m => m != target && Vector3.Distance(target.transform.position, m.transform.position) < skill.Area),
                "crosshair acquires a real hostile Nano target with a second mob within its authored area");
            int first = frames.Count, peerFirst = peerFrames.Count;
            frontend.LiveCastActiveNano();
            await Wait(() => frames.Skip(first).Any(f => f.PacketType == ProtocolNanoSkill.Success) &&
                peerFrames.Skip(peerFirst).Any(f => f.PacketType == ProtocolNanoSkill.Broadcast), "offensive Nano input reaches owner and observer");
            ProtocolNanoSkill.TryDecode(frames.Last(f => f.PacketType == ProtocolNanoSkill.Success), out var own);
            ProtocolNanoSkill.TryDecode(peerFrames.Last(f => f.PacketType == ProtocolNanoSkill.Broadcast), out var peer);
            transactions.Add(new { skill = own.SkillId, own.Stamina, focus = target.Entity.Id,
                hits = own.Effects.Select(e => new { e.Id, e.Kind, previous = before.TryGetValue(e.Id, out int hp) ? hp : -1, e.Hp }).ToArray(),
                observerHits = peer.Effects.Select(e => new { e.Id, e.Hp }).ToArray() });
            Check(own.SkillId == 46 && peer.SkillId == own.SkillId && own.Stamina < 150 && own.Effects.Count >= 2 && own.Effects.Count <= 3 &&
                own.Effects.Any(e => e.Id == target.Entity.Id) && own.Effects.All(e => e.Kind == WorldEntityKind.Npc && before.TryGetValue(e.Id, out var hp) &&
                    e.Hp < hp && peer.Effects.Any(p => p.Id == e.Id && p.Hp == e.Hp)),
                "area damage reduces HP for the focus and nearby mobs with one authoritative stamina cost and matching observer receipts");
            Capture("offensive-nano-area");
        }

        async Task LoadInspectionProfiles(string path)
        {
            var bundle = AssetBundle.LoadFromFile(path);
            Check(bundle != null, "opened the separate animation profile publication");
            try
            {
                foreach (string gender in new[] { "male", "female" })
                {
                    string assetPath = "assets/ffone/content/characters/humanoids/animation/profiles/" + gender + "-profile.asset";
                    var profile = bundle.LoadAsset<NativePlayerAnimationProfile>(assetPath);
                    Check(profile && profile.Controller && profile.ClipNames.Contains("woundupper"), gender + " publication contains woundupper");
                    var cached = await ContentAssets.GetAsync<NativePlayerAnimationProfile>(NativePlayerAnimator.ProfileAddress(gender), default);
                    cached.Controller = profile.Controller;
                    cached.ClipNames = profile.ClipNames;
                }
            }
            finally { bundle.Unload(false); }
        }

        async Task ExercisePlayerHealth()
        {
            var world = frontend.LiveWorld;
            var motion = world.Player.GetComponent<NativeAvatarPreview>().PlayerAnimation;
            var animator = motion.Animator;
            Check(motion.Supports("woundupper") && Mathf.Abs(motion.Duration("woundupper") - .466667f) < .001f,
                "actual equipped player rig contains the original 0.4667 second woundupper clip");
            Capture("player-before-damage");
            int initialHp = frontend.LiveState.Snapshot.Hp, start = frames.Count;
            var mob = world.Replicas.Actors.Where(a => a.Hostile && a.Entity.NpcType == 59 && a.Entity.Hp > 0)
                .OrderBy(a => Mathf.Abs(a.Entity.X - 632932)).First();
            await frontend.LiveMoveTo(mob.Entity.X - 80, mob.Entity.Y, mob.Entity.Z);
            await frontend.Session.AttackNpcsAsync(new[] { mob.Entity.Id });
            int hp = initialHp;
            string lower = null;
            float upperWeight = 0;
            int inspectedFrames = start;
            var incoming = new List<object>();
            var samples = new List<object>();
            var packetCounts = new Dictionary<uint, int>();
            float started = Time.realtimeSinceStartup, nextSample = 0;
            bool receivedHit = false, belowInitialHp = false;
            int ownerId = frontend.Session.World.PlayerId;
            try
            {
            await Wait(() =>
            {
                var state = animator.GetCurrentAnimatorStateInfo(1);
                var upperClips = animator.GetCurrentAnimatorClipInfo(1);
                upperWeight = upperClips.Where(c => c.clip && c.clip.name == "woundupper").Sum(c => c.weight);
                lower = animator.GetCurrentAnimatorClipInfo(0).Concat(animator.GetNextAnimatorClipInfo(0))
                    .Where(c => c.clip && c.weight > 0).OrderByDescending(c => c.weight).Select(c => c.clip.name).FirstOrDefault();
                bool woundState = state.IsName(NativePlayerAnimator.UpperLayer + ".woundupper");
                bool freshHit = false;
                float elapsed = Time.realtimeSinceStartup - started;
                for (; inspectedFrames < frames.Count; inspectedFrames++)
                {
                    var frame = frames[inspectedFrames];
                    packetCounts.TryGetValue(frame.PacketType, out int count);
                    packetCounts[frame.PacketType] = count + 1;
                    if (!ProtocolGameplay.TryCombat(frame, out var combat)) continue;
                    foreach (var hit in combat.Hits)
                    {
                        incoming.Add(new { elapsed, wire = frame.PacketType.ToString("x"), combat.AttackerId, combat.AttackerKind, hit.Id, hit.Kind, hit.Hp, hit.Damage });
                        if (combat.AttackerKind != WorldEntityKind.Npc || hit.Id != ownerId || hit.Kind != WorldEntityKind.Player || hit.Hp <= 0) continue;
                        freshHit = true; receivedHit |= hit.Damage > 0; belowInitialHp |= hit.Hp < initialHp;
                    }
                }
                if (freshHit || elapsed >= nextSample || woundState)
                {
                    nextSample = elapsed + .20f;
                    var next = animator.GetNextAnimatorStateInfo(1);
                    samples.Add(new { elapsed, hp = frontend.LiveState.Snapshot.Hp, receivedHit, belowInitialHp, freshHit, woundState, state.normalizedTime,
                        upper = state.shortNameHash, next = next.shortNameHash, weight = animator.GetLayerWeight(1), upperWeight, lower,
                        currentClips = upperClips.Where(c => c.clip).Select(c => new { c.clip.name, c.weight }).ToArray(),
                        nextClips = animator.GetNextAnimatorClipInfo(1).Where(c => c.clip).Select(c => new { c.clip.name, c.weight }).ToArray(),
                        distance = mob ? Vector3.Distance(mob.transform.position, world.Player.position) : -1,
                        targetHp = mob ? mob.Entity.Hp : -1, world.Ziplining, world.Riding, world.PersonalVehicleMounted });
                }
                if (!belowInitialHp || !woundState || state.normalizedTime < .12f || upperWeight < .01f || animator.GetLayerWeight(1) <= .5f || lower == null) return false;
                hp = frontend.LiveState.Snapshot.Hp;
                Capture("player-woundupper");
                return true;
            }, "source mob inflicts a nonlethal hit and the equipped player plays the weighted upper-body wound", 40);
            }
            finally
            {
                File.WriteAllText(Path.Combine(directory, "wound-observations.json"), JsonConvert.SerializeObject(new {
                    initialHp, ownerId, mobId = mob ? mob.Entity.Id : 0, states = new { wound = Animator.StringToHash("woundupper"), empty = Animator.StringToHash("Empty") },
                    packetCounts, hits = incoming, samples }, Formatting.Indented));
            }
            Check(lower != "woundupper", "a real locomotion clip still drives the lower body");
            await frontend.LiveMoveTo(629232, 187177, -5500);
            await Wait(() => animator.GetCurrentAnimatorStateInfo(1).IsName(NativePlayerAnimator.UpperLayer + ".Empty") &&
                animator.GetLayerWeight(1) < .01f, "authored wound finishes and releases the upper body", 8);
            Capture("player-after-damage");
            transactions.Add(new { initialHp, hpAfterHit = hp, upper = "woundupper", upperWeight, lower, duration = motion.Duration("woundupper") });
        }

        async Task ExerciseCombatAudio(int port)
        {
            var audio = FindFirstObjectByType<RetrobutionAudioController>();
            var tap = audio.UiEffects.gameObject.AddComponent<NanoAudioSourceTap>();
            var cues = new Dictionary<string, NanoAudioRecording>();
            var voices = new List<(string address, NanoAudioRecording recording)>();
            NativeAvatarPreview actor = null;
            NanoAudioSourceTap voiceTap = null;
            void Record(string address)
            {
                if (address is not ("audio/sfx/ui/action_failure01" or "audio/sfx/world_events/resurrectm_warp")) return;
                var recording = new NanoAudioRecording(AudioSettings.outputSampleRate);
                cues[address] = recording;
                tap.Recording = recording;
            }
            audio.UiCuePlayed += Record;
            try
            {
                Check(frontend.LiveState.Snapshot.Equipment[ProtocolVehicle.EquipmentSlot].IsEmpty, "mount audio fixture has no equipped vehicle");
                await frontend.LiveUi.ToggleVehicle();
                await Recorded("audio/sfx/ui/action_failure01");
                Check(!frontend.LiveState.Snapshot.VehicleMounted, "rejected mount leaves the player on foot");
                await Phoenix(port, avatar => { actor = avatar; actor.VoicePlayed += RecordVoice; });
                await Recorded("audio/sfx/world_events/resurrectm_warp");
                foreach (string family in new[] { "damage", "defeat" })
                {
                    var played = voices.FirstOrDefault(v => (family == "defeat" ? v.address.Contains("defeatlng") :
                        v.address.Contains("_hurt") || v.address.Contains("_critical")) && v.recording.Peak > .00001f);
                    Check(played.recording != null, "server " + family + " produced audible samples from the local avatar voice source");
                    string wav = Path.GetFileName(played.address) + ".wav";
                    int samples = played.recording.Write(Path.Combine(directory, wav));
                    Check(samples > 0, "captured " + wav);
                    transactions.Add(new { played.address, played.recording.Peak, samples, wav });
                }
            }
            finally
            {
                audio.UiCuePlayed -= Record; tap.Recording = null; Destroy(tap);
                if (actor) actor.VoicePlayed -= RecordVoice;
                if (voiceTap) { voiceTap.Recording = null; Destroy(voiceTap); }
            }

            void RecordVoice(string address, AudioSource source)
            {
                if (!voiceTap) voiceTap = source.gameObject.AddComponent<NanoAudioSourceTap>();
                var recording = new NanoAudioRecording(AudioSettings.outputSampleRate);
                voiceTap.Recording = recording;
                voices.Add((address, recording));
            }

            async Task Recorded(string address)
            {
                await Wait(() => cues.TryGetValue(address, out var r) && r.Peak > .00001f, address + " produces non-silent output on the UI effects source", 8);
                await Pause(.3f);
                var recording = cues[address];
                string filename = Path.GetFileName(address) + ".wav";
                Check(recording.Write(Path.Combine(directory, filename)) > 0, "captured " + filename);
                transactions.Add(new { address, recording.Peak, filename });
            }
        }

        async Task Passive(Case item)
        {
            await Nano(item.nano);
            await Wait(() => frontend.LiveWorld.Buffs.Has(item.buff) && frontend.LiveWorld.Buffs.Value(item.buff) == item.value, "native passive skill" + item.skill + " condition and exact source value");
            await Wait(() => BuffIcon(item.buff)?.image != null, "native passive skill" + item.skill + " resolves real XDT buff icon");
            // Authored instants can last just one second (ES429 maxTimer1,
            // longestLifeTime0). Observe their real activation before waiting
            // for grounded physics, jumping, teleporting or HUD pixel frames.
            int instant = GameplayUiCatalog.Load().BuffEffect(item.buff, true);
            if (instant > 0) await Effect(frontend.LiveWorld.Player, instant, "server-passive-instant-" + item.buff);
            if (item.buff == 1)
            {
                var world = frontend.LiveWorld; var before = world.Player.position; Vector3 movement = Vector3.zero;
                for (int i = 0; i < 6; i++) movement = world.AcceptBuffMotorStep(1, .1f);
                Check(Math.Abs(movement.magnitude - (6 + item.value / 100f)) < .0001f && Vector3.Distance(before, world.Player.position) > .2f,
                    "real run source increases production CharacterController movement by exact centiunit value");
                await frontend.LiveMoveTo(631632, 187177, -5500);
            }
            if (item.buff == 3)
            {
                var world = frontend.LiveWorld;
                await Wait(() => world.IsReady && world.Player.GetComponent<CharacterController>().isGrounded,
                    "native jump waits for actual world collision grounding before production launch", 15);
                var displacement = world.AcceptBuffJumpStep(.05f);
                float launch = (5.68f + item.value / 100f) * NativePlayerMotor.JumpMultiplier;
                Check(Math.Abs(world.JumpLaunchVelocity - launch) < .0001f && displacement.y > .05f && world.Jumping,
                    "real jump source launches actual CharacterController with exact source velocity");
                await frontend.LiveMoveTo(631632, 187177, -5500);
            }
            if (item.group) await Wait(() => peerBuffs.Has(item.buff), "native group passive reaches real recipient");
            else { await Pause(.15f); Check(!peerBuffs.Has(item.buff), "native self passive skill" + item.skill + " preserves group target boundary"); }
            int loop = GameplayUiCatalog.Load().BuffEffect(item.buff);
            if (loop > 0) await Effect(frontend.LiveWorld.Player, loop, "server-passive-loop-" + item.buff);
            Capture("buff-" + item.buff + "-icon-and-effect");
            await CheckVisibleIcon(item.buff, "buff-" + item.buff + "-hud");
            await frontend.Session.ActivateNanoAsync(-1);
            await Wait(() => !frontend.LiveWorld.Buffs.Has(item.buff) && frontend.Root.Q<VisualElement>("buff-" + item.buff) == null && !peerBuffs.Has(item.buff), "native passive source removal clears server flag and HUD icon");
            transactions.Add(new { kind = "passive", item.nano, item.skill, item.buff, item.value, item.group });
        }
        async Task GroupLifecycle()
        {
            await Nano(3);
            await Wait(() => frontend.LiveWorld.Buffs.Has(1) && peerBuffs.Has(1), "native group run aura reaches both actual members");
            observerHadAura = true;
            await observer.LeaveGroupAsync();
            await Wait(() => !peerBuffs.Has(1) && frontend.LiveWorld.Buffs.Has(1), "native leave removes only received aura and preserves source holder");
            // A second owned source survives removal of the first source.
            await observer.ActivateNanoAsync(2);
            await Wait(() => peerBuffs.Has(1), "second holder activates its own source-owned run aura");
            await frontend.Session.ActivateNanoAsync(-1);
            await Wait(() => !frontend.LiveWorld.Buffs.Has(1) && peerBuffs.Has(1), "removing first run source preserves second holder run");
            await observer.ActivateNanoAsync(-1);
            await Wait(() => !peerBuffs.Has(1), "last real run source removal clears the recipient");
        }
        async Task Active(Case item)
        {
            await Nano(item.nano);
            NetworkEntityView target = frontend.LiveWorld.Replicas.Actors.Where(a => a.Entity.Kind == WorldEntityKind.Npc && a.Entity.NpcType == 59 && a.Entity.Hp > 0).OrderBy(a => a.Entity.X).First();
            await frontend.LiveMoveTo(target.Entity.X - 650, target.Entity.Y, target.Entity.Z);
            int start = frames.Count, peerStart = peerFrames.Count;
            await frontend.Session.UseNanoSkillAsync(new[] { target.Entity.Id }, target.Entity.Id);
            await Wait(() => frames.Skip(start).Any(f => f.PacketType == ProtocolNanoSkill.Success) && peerFrames.Skip(peerStart).Any(f => f.PacketType == ProtocolNanoSkill.Broadcast), "native active skill" + item.skill + " owner and observer receipt");
            ProtocolNanoSkill.TryDecode(frames.Last(f => f.PacketType == ProtocolNanoSkill.Success), out var result);
            Check(result.SkillId == item.skill && result.Stamina < 150, "native active skill" + item.skill + " authoritative identity and stamina cost");
            if (item.buff > 0)
            {
                int bit = 1 << (item.buff - 1);
                await Wait(() => target && (target.Entity.ConditionFlags & bit) != 0, "native target receives server condition" + item.buff);
                int effect = GameplayUiCatalog.Load().BuffEffect(item.buff);
                if (effect == 0) effect = GameplayUiCatalog.Load().BuffEffect(item.buff, true);
                if (effect > 0) await Effect(target.transform, effect, "server-hostile-" + item.buff);
                await Wait(() => target && (target.Entity.ConditionFlags & bit) == 0, "native target condition" + item.buff + " expires through actual server timeout", 20);
                await Pause(.1f);
                Check(!(target.GetComponent<NativeBuffPresentation>()?.ActiveLoops.ContainsKey(item.buff) ?? false), "native target loop" + item.buff + " releases its source on timeout");
            }
            else Check(result.Effects.Count == 2 && result.Effects[0].Id == frontend.Session.World.PlayerId && result.Effects[1].Id == target.Entity.Id && result.Effects[1].Hp.HasValue,
                "native Leech renders distinct caster Heal16 and hostile Damage20 post-states");
            transactions.Add(new { kind = "active", item.nano, item.skill, item.buff, target = target.Entity.Id });
        }
        async Task UseItem(Item item)
        {
            int start = frames.Count, peerStart = peerFrames.Count;
            int remaining = frontend.LiveState.Snapshot.Inventory[item.slot].Option;
            if (item.supported) await frontend.LiveUi.LiveUseBuffItem(item.slot, 0);
            else await frontend.Session.UseItemAsync(item.slot, 0);
            await Wait(() => frames.Skip(start).Any(f => f.PacketType == (item.supported ? ProtocolItemUse.Success : ProtocolItemUse.Fail)), "native item" + item.item + " supported/rejected authoritative response");
            if (item.supported)
            {
                ProtocolItemUse.TryDecode(frames.Last(f => f.PacketType == ProtocolItemUse.Success), out var result);
                Check(result.SkillId == item.skill && result.Slot == item.slot && result.Remaining.Option == remaining - 1 && result.Effects.Count == 1,
                    "native item" + item.item + " uses exact linked skill, typed effect and one-item consumption");
                Check(frontend.LiveState.Snapshot.Inventory[item.slot].Option == remaining - 1, "native inventory applies real item" + item.item + " post-state");
                Check(!frontend.LiveUi.LiveBuffItemPending, "native inventory item" + item.item + " action releases its pending state on real ACK");
                await Wait(() => peerFrames.Skip(peerStart).Any(f => f.PacketType == ProtocolItemUse.Broadcast), "native item" + item.item + " observer receives complete result");
                start = frames.Count; await frontend.LiveUi.LiveUseBuffItem(item.slot, 0);
                await Wait(() => frames.Skip(start).Any(f => f.PacketType == ProtocolItemUse.Fail), "native item" + item.item + " cooldown replay rejected");
                Check(frontend.LiveState.Snapshot.Inventory[item.slot].Option == remaining - 1, "native cooldown failure preserves item" + item.item + " remaining stack");
                await Pause(1.1f);
            }
            else Check(frontend.LiveState.Snapshot.Inventory[item.slot].Option == remaining, "native unsupported item" + item.item + " rejection preserves inventory");
            transactions.Add(new { kind = "item", item.item, item.skill, item.supported });
        }
        async Task Phoenix(int port, Action<NativeAvatarPreview> observeAvatar = null)
        {
            // Item healing legitimately leaves Buffa at its level36 maximum.
            // The existing separate level1/HP500 fixture exercises actual death
            // without changing live HP, source mob stats, physics or timeouts.
            failures.AddRange(frontend.LiveWorld.Replicas.ContentFailures);
            await frontend.LiveExit();
            await Wait(() => frontend.LiveWorld == null && !frontend.Session.IsInWorld,
                "completed buff/item frontend exits through acknowledged save before Phoenix isolation");
            frontend.LiveResetInstance(); await frontend.LiveBuffLogin(port, "unitybuffd");
            await Wait(() => frontend.LiveWorld && frontend.LiveWorld.IsReady,
                "existing isolated Phoenix frontend completes real world and collider admission");
            frontend.LiveWorld.View.targetTexture = image;
            frontend.LiveWorld.Replicas.ContentError += failures.Add;
            observeAvatar?.Invoke(frontend.LiveWorld.Player.GetComponent<NativeAvatarPreview>());
            Check(Protocol0104.Read16(frontend.Session.World.RawLoadData, 0) == 99 && frontend.LiveState.Snapshot.Level == 1,
                "Phoenix uses the existing normal privilege99 level1 character without live stat changes");
            Check(frontend.Session.World.Hp == 500 && frontend.LiveState.Snapshot.Nanos.Single(n => n.Id == 8).Stamina == 150 &&
                frontend.LiveState.Snapshot.Nanos.Single(n => n.Id == 3).Stamina == 150,
                "Phoenix entry preserves seeded HP500 and original owned Nano8/3 stamina150");
            Check(!frontend.LiveWorld.Buffs.Has(15) && !frontend.LiveWorld.Buffs.Has(20),
                "fresh Phoenix frontend inherits neither the previous item reward nor Invulnerable sources");
            await Wait(() => frontend.LiveWorld.Replicas.Actors.Any(a => a.Entity.Kind == WorldEntityKind.Player && a.Entity.Id == peerEntry.PlayerId && a.Avatar && a.Avatar.IsReady) &&
                peerWorld.TryGet(WorldEntityKind.Player, frontend.Session.World.PlayerId, out _),
                "continuing observer and fresh Phoenix frontend establish actual mutual player interest");
            int start = frames.Count; await frontend.Session.RevivePhoenixAsync(true); await Pause(.2f);
            Check(!frames.Skip(start).Any(f => f.PacketType == ProtocolGameplay.RegenSuccess), "native frontend cannot request unearned group Phoenix revival");
            await Nano(8);
            await Wait(() => frontend.LiveWorld.Buffs.Has(5), "native frontend activates source Phoenix Nano8");
            int preservedStamina = frontend.LiveState.Snapshot.Nanos.Single(n => n.Id == 3).Stamina;
            int peerDeathStart = peerFrames.Count;
            await frontend.LiveMoveTo(635032, 187177, -5500);
            await Wait(() => frontend.LiveState.Snapshot.Hp == 0 && frontend.LiveRevivePrompt != null,
                "actual source mob combat kills native frontend and opens its Phoenix revival dialog", 150);
            Check(frontend.LiveState.Snapshot.HasBuff(5), "native death retains the valid self Phoenix condition for its real dialog");
            var confirm = frontend.LiveRevivePrompt.Q<Button>("revive-phoenix");
            Check(confirm != null && confirm.enabledInHierarchy, "native Phoenix dialog exposes enabled source revival action");
            await CaptureHud("phoenix-self-dialog");
            await Wait(() => peerFrames.Skip(peerDeathStart).Any(f => ReportsDeath(f, frontend.Session.World.PlayerId)) &&
                peerWorld.TryGet(WorldEntityKind.Player, frontend.Session.World.PlayerId, out var body) && body.Hp == 0,
                "continuing observer receives the actual Phoenix frontend death post-state", 15);
            var deathReceipt = peerFrames.Skip(peerDeathStart).First(f => ReportsDeath(f, frontend.Session.World.PlayerId));
            observerDeathReceipts.Add(new { playerId = frontend.Session.World.PlayerId, packetType = deathReceipt.PacketType,
                payloadHex = BitConverter.ToString(deathReceipt.Payload).Replace("-", "") });
            peerWorld.TryGet(WorldEntityKind.Player, frontend.Session.World.PlayerId, out var deathPosition);
            int deathX = deathPosition.X, deathY = deathPosition.Y, deathZ = deathPosition.Z;
            start = frames.Count;
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = confirm; confirm.SendEvent(submit); }
            await Wait(() => frames.Skip(start).Any(f => f.PacketType == ProtocolGameplay.RegenSuccess) && frontend.LiveState.Snapshot.Hp > 0 && frontend.LiveRevivePrompt == null,
                "real native Phoenix dialog sends regen3 and HUD applies living server post-state");
            var result = frames.Last(f => f.PacketType == ProtocolGameplay.RegenSuccess).Payload;
            Check(ProtocolGameplay.I(result, 8) == deathX && ProtocolGameplay.I(result, 12) == deathY && ProtocolGameplay.I(result, 16) == deathZ,
                "native Phoenix restores actual frontend at exact death location");
            Check(Protocol0104.Read16(result, 26) == 0 && Protocol0104.Read16(result, 38) >= preservedStamina,
                "native Phoenix drains active Nano8 while preserving other carried Nano stamina");
            await frontend.Session.ActivateNanoAsync(-1);
            await Wait(() => frontend.LiveState.Snapshot.Hp == 0 && frontend.LiveRevivePrompt != null,
                "native frontend suffers a second real source combat death for group Phoenix", 150);
            int peerStart = peerFrames.Count;
            await frontend.Session.InviteGroupAsync(peerEntry.PlayerId);
            await Wait(() => peerFrames.Skip(peerStart).Any(f => f.PacketType == ProtocolSocial.Invite), "native dead Phoenix target creates real group invitation");
            await observer.AnswerGroupAsync(frontend.Session.World.PlayerId, true);
            peerStart = peerFrames.Count; await observer.EquipNanoAsync(7, 0);
            await Wait(() => peerFrames.Skip(peerStart).Any(f => f.PacketType == ProtocolGameplay.NanoEquipSuccess), "native observer equips source group Phoenix Nano7 at station");
            peerStart = peerFrames.Count; await observer.ActivateNanoAsync(0);
            await Wait(() => peerFrames.Skip(peerStart).Any(f => f.PacketType == ProtocolGameplay.NanoActiveSuccess), "native observer activates source group Phoenix Nano7");
            start = frames.Count; await observer.UseNanoSkillAsync(Array.Empty<int>());
            await Wait(() => frames.Skip(start).Any(f => ProtocolNanoSkill.TryDecode(f, out var skill) && skill.SkillId == 20 && skill.Effects.Any(e => e.Id == frontend.Session.World.PlayerId && e.Resurrected)),
                "real group Phoenix skill20 reaches native frontend as Resurrect authorization");
            await Wait(() => frames.Skip(start).Any(f => f.PacketType == ProtocolGameplay.RegenSuccess) && frontend.LiveState.Snapshot.Hp > 0 && frontend.LiveRevivePrompt == null,
                "native Resurrect receiver automatically requests regen4 and restores actual HUD/avatar");
            start = frames.Count; await frontend.Session.RevivePhoenixAsync(true); await Pause(.2f);
            Check(!frames.Skip(start).Any(f => f.PacketType == ProtocolGameplay.RegenSuccess), "native group Phoenix one-shot authorization rejects replay");
            await frontend.LiveMoveTo(632032, 187177, -5500);
            Capture("phoenix-group-restored");
        }
        async Task RenderClosures()
        {
            // Independently prove the full accepted BuffData effect set, including
            // effects not naturally produced by this positive player-skill fixture.
            // These renders are not counted as server-applied buff transactions.
            await frontend.LiveMoveTo(630032, 187177, -5500);
            var stage = new GameObject("Original buff closure inspection");
            var cameraObject = new GameObject("Buff closure camera"); var view = cameraObject.AddComponent<Camera>();
            view.enabled = false; view.cullingMask = 1 << 30; view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(.16f, .16f, .16f, 1); view.fieldOfView = 40; view.aspect = (float)image.width / image.height;
            view.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            var lampObject = new GameObject("Buff closure inspection light"); var lamp = lampObject.AddComponent<Light>();
            lamp.type = LightType.Directional; lamp.intensity = 1; lamp.cullingMask = 1 << 30; lamp.transform.rotation = Quaternion.Euler(35, -30, 0);
            try
            {
                foreach (int id in plan.effects)
                {
                    var rotation = Quaternion.Euler(20, 35, 0);
                    view.transform.SetPositionAndRotation(rotation * Vector3.back * 5, rotation);
                    var root = NativeCombatEffect.Spawn(stage.transform, id, 1, false, view, 5);
                    try
                    {
                        foreach (var node in root.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 30;
                        await Wait(() => Ready(root), "closure-only ES" + id + " emits original geometry with supported materials", 15);
                        await Pause(.15f);
                        var renderers = root.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
                        var bounds = renderers[0].bounds; foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                        float radius = Mathf.Max(.01f, bounds.extents.magnitude);
                        float distance = radius / Mathf.Sin(view.fieldOfView * .5f * Mathf.Deg2Rad) * 1.2f;
                        view.nearClipPlane = Mathf.Max(.001f, distance * .001f); view.farClipPlane = Mathf.Max(10, distance * 4);
                        view.transform.SetPositionAndRotation(bounds.center - rotation * Vector3.forward * distance, rotation);
                        // Billboard vertices are rebuilt by the production effect's
                        // regular LateUpdate using this fixed inspection camera.
                        await Frames(3);
                        CheckVisibleEffect(root, id, "closure-es" + id, view);
                        rendered.Add(id);
                    }
                    finally { if (root) { root.SetActive(false); Destroy(root); } }
                }
                Check(closureVisuals.Count == plan.effects.Length, "all15 original buff closures independently contribute measured URP pixels");
            }
            finally { Destroy(stage); Destroy(cameraObject); Destroy(lampObject); }
        }
        async Task Effect(Transform owner, int id, string name)
        {
            GameObject root = null;
            await Wait(() => (root = owner.GetComponentsInChildren<NativeCombatEffect>(true).FirstOrDefault(e => e.name == "Native combat ES" + id)?.gameObject) && Ready(root),
                "real server condition creates original ES" + id + " rendered geometry", 15);
            rendered.Add(id); Capture(name + "-es" + id);
        }
        static bool Ready(GameObject root)
        {
            if (!root) return false;
            var renderers = root.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            return renderers.Length > 0 && renderers.Any(r => r.sharedMaterials.Length > 0) &&
                renderers.SelectMany(r => r.sharedMaterials).All(m => m && m.shader && m.shader.isSupported && m.shader.name != "Hidden/InternalErrorShader") &&
                renderers.Any(r =>
                {
                    var mesh = RenderedMesh(r);
                    return mesh && mesh.vertexCount > 0 &&
                        (r is not ParticleSystemRenderer || r.GetComponent<ParticleSystem>().particleCount > 0);
                });
        }
        static Mesh RenderedMesh(Renderer renderer) => renderer switch
        {
            SkinnedMeshRenderer skin => skin.sharedMesh,
            ParticleSystemRenderer particles => particles.mesh,
            _ => renderer.GetComponent<MeshFilter>()?.sharedMesh
        };
        void ApplyPeer(ProtocolFrame frame)
        {
            peerFrames.Add(frame); peerWorld?.Apply(frame);
            // WorldEntitySession owns interests and typed effects; production
            // NetworkWorldEntities.Receive separately applies basic combat HP.
            if (peerWorld != null && ProtocolGameplay.TryCombat(frame, out var combat))
                foreach (var hit in combat.Hits) peerWorld.SetHealth(hit.Kind, hit.Id, hit.Hp);
            if (peerBuffs != null && ProtocolBuff.TryUpdate(frame, out var buff)) peerBuffs.Apply(buff, Time.unscaledTimeAsDouble);
            if (peerBuffs != null && ProtocolBuff.TryEffects(frame, out var effects)) foreach (var effect in effects)
                if (effect.Kind == WorldEntityKind.Player && effect.Id == peerEntry.PlayerId && effect.Condition.HasValue) peerBuffs.SetCondition(effect.Condition.Value);
        }
        static bool ReportsDeath(ProtocolFrame frame, int playerId)
        {
            if (ProtocolGameplay.TryCombat(frame, out var combat))
                return combat.AttackerKind == WorldEntityKind.Npc && combat.Hits.Any(h => h.Kind == WorldEntityKind.Player && h.Id == playerId && h.Hp == 0);
            if (ProtocolNpcSkill.TryDecode(frame, out var npc))
                return npc.Any(e => e.Kind == WorldEntityKind.Player && e.Id == playerId && e.Hp == 0 && !e.Resurrected);
            return ProtocolBuff.TryEffects(frame, out var tick) && tick.Any(e => e.Kind == WorldEntityKind.Player && e.Id == playerId && e.Hp == 0 && !e.Resurrected);
        }
        void Observe(NetworkEvent e) { if (e.IsShard && e.Frame != null) frames.Add(e.Frame); }
        void Update()
        {
            if (observer != null) while (observer.TryDequeueEvent(out var e)) if (e.IsShard && e.Frame != null) ApplyPeer(e.Frame);
        }
        void LateUpdate()
        {
            if (!image) return;
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!camera.enabled || !camera.targetTexture) continue;
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = camera.targetTexture };
                if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
            }
        }
        async Task Wait(Func<bool> predicate, string message, float seconds = 120)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!predicate()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException(message); await Task.Yield(); }
            Check(true, message);
        }
        static async Task Pause(float seconds) { float end = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < end) await Task.Yield(); }
        static async Task Frames(int count) { for (int i = 0; i < count; i++) await Task.Yield(); }
        void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); checks.Add(message); Debug.Log("BUFF CHECK: " + message); }
        static Texture2D Read(RenderTexture source)
        {
            var result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false); var previous = RenderTexture.active;
            try { RenderTexture.active = source; result.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); result.Apply(); return result; }
            finally { RenderTexture.active = previous; }
        }
        async Task CaptureHud(string name)
        {
            await Frames(8); var pixels = Read(uiImage);
            try { File.WriteAllBytes(Path.Combine(directory, name + ".png"), pixels.EncodeToPNG()); }
            finally { Destroy(pixels); }
        }
        async Task CheckVisibleIcon(int buff, string name)
        {
            await Frames(8);
            var icon = BuffIcon(buff);
            Check(icon != null && icon.visible && icon.image && icon.worldBound.width > 0 && icon.worldBound.height > 0,
                "native buff" + buff + " icon participates in the actual HUD panel layout");
            Rect bounds = icon.worldBound;
            Check(bounds.xMin >= 0 && bounds.yMin >= 0 && bounds.xMax <= uiImage.width && bounds.yMax <= uiImage.height,
                "native buff" + buff + " icon lies inside the real player HUD target");
            Check(!frontend.Root.Query<Button>().ToList().Any(button => button.visible && button.resolvedStyle.display != DisplayStyle.None &&
                button.worldBound.width > 0 && button.worldBound.height > 0 && button.worldBound.Overlaps(bounds)),
                "native buff" + buff + " icon remains fully clear of real HUD action buttons");
            var shown = Read(uiImage); Texture2D hidden = null; var visibility = icon.style.visibility;
            try
            {
                icon.style.visibility = Visibility.Hidden; await Frames(4); hidden = Read(uiImage);
                var a = shown.GetPixels32(); var b = hidden.GetPixels32(); int changed = 0;
                int left = Mathf.FloorToInt(bounds.xMin), right = Mathf.CeilToInt(bounds.xMax);
                int bottom = uiImage.height - Mathf.CeilToInt(bounds.yMax), top = uiImage.height - Mathf.FloorToInt(bounds.yMin);
                for (int y = bottom; y < top; y++) for (int x = left; x < right; x++)
                {
                    int i = y * uiImage.width + x;
                    if (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) + Math.Abs(a[i].a - b[i].a) > 15) changed++;
                }
                Check(changed >= 8, "native buff" + buff + " original icon contributes actual HUD pixels (" + changed + ")");
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), shown.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(directory, name + "-icon-hidden.png"), hidden.EncodeToPNG());
                iconVisuals.Add(new { buff, originalAddress = GameplayUiCatalog.Load().BuffIcon(buff), visiblePixels = changed,
                    x = bounds.x, y = bounds.y, width = bounds.width, height = bounds.height, screenshot = name + ".png" });
            }
            finally { icon.style.visibility = visibility; Destroy(shown); if (hidden) Destroy(hidden); }
            await Frames(4);
        }
        void Capture(string name, Camera inspectionView = null)
        {
            var camera = inspectionView ? inspectionView : frontend.LiveWorld.View;
            if (!inspectionView)
            {
                camera.transform.position = frontend.LiveWorld.Player.position + new Vector3(0, 2.5f, -5);
                camera.transform.LookAt(frontend.LiveWorld.Player.position + Vector3.up * .8f);
            }
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = image };
            if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("Buff inspection requires the real player render pipeline.");
            RenderPipeline.SubmitRenderRequest(camera, request);
            var pixels = new Texture2D(image.width, image.height, TextureFormat.RGBA32, false); var previous = RenderTexture.active;
            try { RenderTexture.active = image; pixels.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); pixels.Apply(); File.WriteAllBytes(Path.Combine(directory, name + ".png"), pixels.EncodeToPNG()); }
            finally { RenderTexture.active = previous; Destroy(pixels); }
        }
        void CheckVisibleEffect(GameObject effect, int effectId, string name, Camera inspectionView)
        {
            var renderers = effect.GetComponentsInChildren<Renderer>(true);
            var enabled = renderers.Select(r => r.enabled).ToArray();
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = false;
            Capture(name + "-baseline", inspectionView);
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = enabled[i];
            Capture(name, inspectionView);
            var before = new Texture2D(2, 2); var after = new Texture2D(2, 2);
            try
            {
                before.LoadImage(File.ReadAllBytes(Path.Combine(directory, name + "-baseline.png")));
                after.LoadImage(File.ReadAllBytes(Path.Combine(directory, name + ".png")));
                var a = before.GetPixels32(); var b = after.GetPixels32(); int changed = 0;
                for (int i = 0; i < a.Length; i++)
                    if (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) > 15) changed++;
                Check(changed >= 8, name + " contributes visible pixels in actual URP player camera (" + changed + ")");
                closureVisuals.Add(new { effectId, changedPixels = changed, totalPixels = a.Length,
                    shaders = renderers.SelectMany(r => r.sharedMaterials).Select(m => m.shader.name).Distinct().ToArray(),
                    geometry = renderers.Where(r => RenderedMesh(r)).Select(r => new { renderer = r.GetType().Name, vertices = RenderedMesh(r).vertexCount }).ToArray(),
                    actualScale = 1, screenshot = name + ".png", kind = "isolated-original-buff-effect-closure" });
            }
            finally { Destroy(before); Destroy(after); }
        }
        void Write(bool passed, string error)
        {
            if (frontend?.LiveWorld) failures.AddRange(frontend.LiveWorld.Replicas.ContentFailures);
            if (directory != null) File.WriteAllText(Path.Combine(directory, "buffs-result.json"), JsonConvert.SerializeObject(new { passed = passed && failures.Count == 0, checks, transactions, iconVisuals, closureVisuals, observerDeathReceipts,
                renderedClosureEffects = rendered.OrderBy(i => i).ToArray(), closureRenderingIsIndependentOfServerCoverage = true, observerHadAura,
                unresolvedSkills = plan?.unresolvedSkills, unresolvedConsumers = plan?.unresolvedConsumers, unsupportedConsumers = plan?.unsupportedConsumers,
                contentComplete = failures.Count == 0, contentErrors = failures.Distinct().ToArray(), error }, Formatting.Indented));
        }
        void OnDestroy()
        {
            if (frontend) frontend.NetworkEventReceived -= Observe; observer?.Dispose();
            if (image) { image.Release(); Destroy(image); }
            if (uiImage) { uiImage.Release(); Destroy(uiImage); }
            if (uiPanel) Destroy(uiPanel);
        }
    }
}
#endif
