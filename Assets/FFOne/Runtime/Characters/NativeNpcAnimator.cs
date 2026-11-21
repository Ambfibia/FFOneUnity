using System;
using System.Collections.Generic;
using System.Linq;
using FFOne.Tutorial;
using FFOne.UI;
using FFOne.World;
using UnityEngine;

namespace FFOne.Content
{
    [DisallowMultipleComponent, RequireComponent(typeof(Animator))]
    public sealed class NativeNpcAnimator : MonoBehaviour
    {
        public const string Layer = "Motion";
        public const string AttackLayer = "Melee";
        public const string WoundLayer = "Wound";
        public const string DeltaActionsParameter = "SourceDeltaActions";
        public const string LoopSuffix = " (Loop)";
        public const string SpeedParameter = "MotionSpeed";
        static readonly int SpeedId = UnityEngine.Animator.StringToHash(SpeedParameter);
        static readonly int EmptyStateId = UnityEngine.Animator.StringToHash("Empty");

        [SerializeField] Animator animator;
        public TutorialAnimationSound[] AnimationSounds = Array.Empty<TutorialAnimationSound>();
        public TutorialFaceTexture[] Faces = Array.Empty<TutorialFaceTexture>();
        Dictionary<string, AnimationClip> clips;
        internal TutorialAnimationEffectReceiver TutorialEvents { get; set; }
        internal bool WorldEvents { get; set; }
        int state, currentLayer, loopingActionLayer, activeAttackLayer, activeWoundLayer;
        bool sourceDeltaActions;
        bool reachedEnd;
        AudioSource audioSource;
        readonly Dictionary<(Renderer Renderer, int Slot), Texture> replacedFaces = new();

        public Animator Animator => animator ? animator : animator = GetComponent<Animator>();
        public string CurrentClip { get; private set; }
        public AnimationClip CurrentMotion { get; private set; }
        public float MotionTime
        {
            get
            {
                var info = Animator.GetCurrentAnimatorStateInfo(currentLayer);
                if (info.fullPathHash != state && Animator.IsInTransition(currentLayer)) info = Animator.GetNextAnimatorStateInfo(currentLayer);
                if (info.fullPathHash == state && CurrentMotion) return info.normalizedTime * CurrentMotion.length;
                return currentLayer > 0 && CurrentMotion ? CurrentMotion.length : 0;
            }
        }
        internal bool ReachedEnd => reachedEnd || CurrentMotion && MotionTime >= CurrentMotion.length;

        internal static string SelectStand(int roll, string previous)
        {
            if (roll < 0 || roll >= 100) throw new ArgumentOutOfRangeException(nameof(roll));
            string selected = roll < 40 ? "stand1" : roll < 70 ? "stand2" : roll < 90 ? "stand3" : "stand4";
            return selected == previous ? "stand1" : selected;
        }

        internal bool PlayWorldIdle(int roll, string model, bool restart)
        {
            bool Loop(string clip) => Play(clip, true, .3f, restart);
            string stand = SelectStand(roll, CurrentClip);
            if (Loop(stand) || stand != "stand1" && Loop("stand1") || Loop("nif-default")) return true;
            // These source props and the tutorial Numbuh One have specifically named
            // resting poses. A generated controller's first state can instead be melee1.
            switch (model)
            {
                case "characters/npcs/t_numbuhone/t_numbuhone.glb": return Loop("npc_numbuhone");
                case "characters/npcs/npc_scamper/npc_scamper.glb": return Loop("close");
                case "characters/npcs/objectnpc1/ObjectNPC1.glb": return Loop("npc_location");
                case "characters/npcs/npc_stanley/npc_stanley.glb": return Play("Stand01", false, .3f, restart);
                case "characters/npcs/npc_wisdomtooth/npc_wisdomtooth.glb": return Play("stand01", false, .3f, restart);
            }
            // Mole and Private Kokas start their stand inventory at 2. Lord Fuse's
            // separately animated arms only expose ready as a resting pose.
            return Loop("stand2") || Loop("stand3") || Loop("stand4") || Loop("ready");
        }

        public AnimationClip GetClip(string name, bool loop)
        {
            if (string.IsNullOrEmpty(name) || !Animator.runtimeAnimatorController) return null;
            if (clips == null)
            {
                clips = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
                foreach (var clip in Animator.runtimeAnimatorController.animationClips)
                    if (clip) clips.TryAdd(clip.name, clip);
                sourceDeltaActions = Animator.parameters.Any(p => p.name == DeltaActionsParameter && p.defaultBool);
            }
            return clips.TryGetValue(name + (loop ? LoopSuffix : ""), out var result) ? result : null;
        }

        public static string ActionLayer(string name) => name is "wound" ? WoundLayer
            : name is "melee1" or "melee2" or "melee3" ? AttackLayer : null;

        internal bool ActionPlaying(string layerName)
        {
            int layer = Animator.GetLayerIndex(layerName);
            if (layer < 1 || !Animator.isInitialized || Animator.GetLayerWeight(layer) == 0) return false;
            var info = Animator.GetCurrentAnimatorStateInfo(layer);
            return !info.IsName(layerName + ".Empty") && info.normalizedTime < 1;
        }

        internal bool PlayWorldAction(string name)
        {
            string lane = ActionLayer(name);
            int layer = lane == null ? -1 : Animator.GetLayerIndex(lane);
            if (layer > 0 && Animator.isInitialized && Animator.GetLayerWeight(layer) > 0)
            {
                var info = Animator.GetCurrentAnimatorStateInfo(layer);
                if (info.IsName(lane + "." + name) && info.normalizedTime < 1)
                {
                    state = info.fullPathHash;
                    currentLayer = layer;
                    CurrentClip = name;
                    CurrentMotion = GetClip(name, false);
                    reachedEnd = false;
                    return true;
                }
            }
            return Play(name, false, restart: true);
        }

        public bool Play(string name, bool loop, float fade = .15f, bool restart = false, float speed = 1)
        {
            var clip = GetClip(name, loop);
            if (!clip) return false;
            if (!Animator.isInitialized && Animator.isActiveAndEnabled) Animator.Update(0);
            // Scripted actors normally request an absolute pose. Only the three source
            // rigs authored as deltas require their additive base even in scripted loops.
            var action = sourceDeltaActions || !loop && !TutorialEvents ? ActionLayer(name) : null;
            int layer = action == null ? 0 : Mathf.Max(0, Animator.GetLayerIndex(action));
            int next = UnityEngine.Animator.StringToHash((layer == 0 ? Layer : action) + "." + clip.name);
            if (!Animator.HasState(layer, next)) return false;
            if (layer == 0) Animator.SetFloat(SpeedId, speed);
            if (layer == 0 && name is "death" or "die" or "sleep" or "stun") ClearWorldActions();
            bool same = state == next && (layer == 0 || ActionPlaying(action));
            if (same && !restart) return true;
            if (loopingActionLayer > 0 && loopingActionLayer != layer) ClearAction(loopingActionLayer);
            loopingActionLayer = loop ? layer : 0;
            if (layer > 0)
            {
                Animator.SetLayerWeight(layer, 1);
                if (action == AttackLayer) activeAttackLayer = layer;
                else if (action == WoundLayer) activeWoundLayer = layer;
            }
            RestoreFace();
            bool immediate = layer > 0 || state == 0 || fade <= 0 || same;
            state = next;
            currentLayer = layer;
            CurrentClip = name;
            CurrentMotion = clip;
            reachedEnd = false;
            if (immediate)
            {
                Animator.Play(next, layer, 0);
                Animator.Update(0);
            }
            else Animator.CrossFadeInFixedTime(next, fade, layer, 0);
            return true;
        }

        internal void ClearWorldActions()
        {
            if (!Animator.isInitialized) return;
            ClearAction(AttackLayer);
            ClearAction(WoundLayer);
        }

        void ClearAction(string name)
        {
            int layer = Animator.GetLayerIndex(name);
            if (layer > 0) ClearAction(layer);
        }

        void ClearAction(int layer)
        {
            Animator.Play(Animator.GetLayerName(layer) + ".Empty", layer, 0);
            Animator.SetLayerWeight(layer, 0);
            if (activeAttackLayer == layer) activeAttackLayer = 0;
            if (activeWoundLayer == layer) activeWoundLayer = 0;
        }

        void LateUpdate()
        {
            if ((activeAttackLayer == 0 && activeWoundLayer == 0) || !Animator.isInitialized) return;
            // An Empty additive state can keep the previous motion's final offsets in
            // its pose stream. Drop its weight before the next frame is rendered.
            bool released = ReleaseEmptyLayer(ref activeAttackLayer);
            released |= ReleaseEmptyLayer(ref activeWoundLayer);
            if (released) Animator.Update(0);
        }

        bool ReleaseEmptyLayer(ref int layer)
        {
            if (layer == 0 || Animator.GetCurrentAnimatorStateInfo(layer).shortNameHash != EmptyStateId) return false;
            Animator.SetLayerWeight(layer, 0);
            layer = 0;
            return true;
        }

        bool Owns(AnimationEvent cue)
        {
            if (!cue.isFiredByAnimator) return false;
            int hash = cue.animatorStateInfo.fullPathHash;
            if (hash == state) return true;
            for (int i = 0; i < Animator.layerCount; i++)
            {
                var active = Animator.IsInTransition(i) ? Animator.GetNextAnimatorStateInfo(i) : Animator.GetCurrentAnimatorStateInfo(i);
                if (active.fullPathHash == hash) return true;
            }
            return false;
        }

        public void FFOneAnimationEnd(AnimationEvent cue)
        {
            if (cue.isFiredByAnimator && cue.animatorStateInfo.fullPathHash == state) reachedEnd = true;
        }

        // RXCOM retains this spelling from its source clip.
        public void end(AnimationEvent cue) => FFOneAnimationEnd(cue);

        public void FFOneAttachedEffect(AnimationEvent cue)
        {
            if (!Owns(cue)) return;
            string key = cue.stringParameter;
            if (TutorialEvents) { TutorialEvents.ApplyAttachedEffect(key); return; }
            if (!WorldEvents) return;
            int separator = key.IndexOf(':');
            if (separator < 1 || !int.TryParse(key.Substring(0, separator), out int id)) return;
            string node = key.Substring(separator + 1);
            var bone = GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == node);
            if (bone) SpawnEffect(id, bone, Quaternion.Euler(90, 0, 0));
        }

        public void FFOneParticleEffect(AnimationEvent cue)
        {
            if (!Owns(cue)) return;
            if (TutorialEvents) TutorialEvents.ApplyParticleEffect(cue.stringParameter);
            else if (WorldEvents && int.TryParse(cue.stringParameter, out int id)) SpawnEffect(id, transform, Quaternion.identity);
        }

        async void SpawnEffect(int id, Transform anchor, Quaternion rotation)
        {
            try
            {
                var definition = await ContentAssets.GetAsync<NativeEffectDefinition>(
                    NativeEffectDefinition.Resource(id), destroyCancellationToken);
                if (!this || !anchor || !isActiveAndEnabled || !definition) return;
                var world = GetComponentInParent<NetworkWorldController>();
                var instance = NativeCombatEffect.Spawn(anchor, id, 1, false, world ? world.View : Camera.main, 0);
                instance.transform.localRotation = rotation;
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { Debug.LogException(error, this); }
        }

        public void FFOneAnimationSound(AnimationEvent cue)
        {
            if (!Owns(cue)) return;
            if (TutorialEvents) { TutorialEvents.ApplyAnimationSound(cue.stringParameter); return; }
            if (!WorldEvents) return;
            var sound = Array.Find(AnimationSounds, row => row.Cue == cue.stringParameter);
            var variants = sound?.Variants;
            if (variants == null || variants.Length == 0) return;
            int variant = UnityEngine.Random.Range(0, variants.Length);
            var world = GetComponentInParent<NetworkWorldController>();
            var russian = sound.RussianVariants;
            var clip = world && (string)world.Preferences["voice_locale"] == "ru" &&
                russian != null && variant < russian.Length && russian[variant] ? russian[variant] : variants[variant];
            if (!clip) return;
            if (!audioSource)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.spatialBlend = 1;
                audioSource.maxDistance = 40;
                audioSource.rolloffMode = AudioRolloffMode.Linear;
            }
            if (world) audioSource.volume = FrontendController.AudioGain(world.Preferences, "effects");
            audioSource.PlayOneShot(clip);
        }

        public void FFOneFaceOn(AnimationEvent cue)
        {
            if (!Owns(cue)) return;
            var faces = TutorialEvents ? TutorialEvents.Definition.Faces : Faces;
            var texture = Array.Find(faces, face => face.Name == cue.stringParameter)?.Texture;
            if (!texture) return;
            var block = new MaterialPropertyBlock();
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (!material || !material.name.Contains("face") || !material.name.Contains("sub")) continue;
                    renderer.GetPropertyBlock(block, i);
                    replacedFaces.TryAdd((renderer, i), block.GetTexture("_MainTex") ?? material.mainTexture);
                    block.SetTexture("_MainTex", texture);
                    renderer.SetPropertyBlock(block, i);
                }
            }
        }

        public void FFOneFaceOff(AnimationEvent cue)
        {
            if (Owns(cue)) RestoreFace();
        }

        void RestoreFace()
        {
            if (replacedFaces.Count == 0) return;
            var block = new MaterialPropertyBlock();
            foreach (var (slot, texture) in replacedFaces)
            {
                if (!slot.Renderer || !texture) continue;
                slot.Renderer.GetPropertyBlock(block, slot.Slot);
                block.SetTexture("_MainTex", texture);
                slot.Renderer.SetPropertyBlock(block, slot.Slot);
            }
            replacedFaces.Clear();
        }
    }
}
