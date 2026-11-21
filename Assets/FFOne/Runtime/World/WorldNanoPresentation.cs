using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.UI;
using Newtonsoft.Json;
using UnityEngine;
using Random = UnityEngine.Random;

namespace FFOne.World
{
    // The editable companion prefabs carry the animation curves. Their original
    // timed gameplay callbacks and the accepted localized audio routes live here.
    internal sealed class NanoPresentationCatalog
    {
        internal sealed class Cue
        {
            public float time;
            public string function, payload, voiceOwner;
            public string[] voices, sfx;
        }
        internal sealed class ModelData
        {
            public string voiceOwner;
            public string[] dismiss;
            public Dictionary<string, Cue[]> clips;
        }

        public Dictionary<string, ModelData> models;
        static NanoPresentationCatalog cached;

        internal static ModelData Find(string modelPath)
        {
            if (cached == null)
            {
                var asset = Resources.Load<TextAsset>("WorldNanoPresentation");
                if (!asset) throw new InvalidDataException("Missing WorldNanoPresentation catalog.");
                cached = JsonConvert.DeserializeObject<NanoPresentationCatalog>(asset.text);
            }
            string key = Path.GetFileNameWithoutExtension(modelPath).ToLowerInvariant();
            return cached.models.TryGetValue(key, out var model) ? model : null;
        }
    }

    internal sealed partial class WorldNanoCompanion
    {
        internal event Action<string, AudioSource> AudioStarted;
        internal event Action<int, GameObject> EffectSpawned;
        NetworkWorldController world;
        NanoPresentationCatalog.ModelData presentation;
        NanoPresentationCatalog.Cue[] events;
        string activeClip, activeAssetClip, pendingSkill;
        int cursor, sceneGeneration;
        bool clipEnded;
        float clipStarted;
        bool PresentationEnded => (events == null || cursor >= events.Length) &&
            (clipEnded || !animationPlayer.IsPlaying(activeAssetClip));
        readonly List<GameObject> effects = new();

        internal void ShowSkill(int nanoId, int skillId)
        {
            if (nanoId <= 0 || nanoId != NanoId || withdrawing) return;
            var tunes = GameplayUiCatalog.Load().Tunes(nanoId);
            for (int i = 0; i < tunes.Count && i < 3; i++)
            {
                if (tunes[i].Skill != skillId) continue;
                pendingSkill = "skill" + (i + 1);
                if (Model && !calling) PlayPendingSkill();
                return;
            }
        }

        bool PlayPendingSkill()
        {
            string next = pendingSkill;
            pendingSkill = null;
            return next != null && Play(next);
        }

        void StartEvents(string clip, string assetClip)
        {
            activeClip = clip;
            activeAssetClip = assetClip;
            clipStarted = Time.time;
            cursor = 0; clipEnded = false; events = null;
            presentation?.clips?.TryGetValue(assetClip, out events);
        }

        void AdvanceEvents()
        {
            if (events == null || cursor >= events.Length) return;
            var state = animationPlayer[activeAssetClip];
            float time = animationPlayer.IsPlaying(activeAssetClip) ? state.time : Mathf.Max(state.length, Time.time - clipStarted);
            while (cursor < events.Length && events[cursor].time <= time)
            {
                var cue = events[cursor++];
                if (cue.function == "end") clipEnded = true;
                else if (cue.function == "sound")
                {
                    if (cue.voices?.Length > 0) QueueVoice(cue.voiceOwner ?? presentation.voiceOwner, cue.voices, false);
                    if (cue.sfx?.Length > 0) QueueSound(cue.sfx[Random.Range(0, cue.sfx.Length)], false, false);
                }
                else if (cue.function is "particle" or "tag_p")
                {
                    string[] fields = cue.payload.Split(new[] { ':' }, 2);
                    if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id <= 0) continue;
                    Transform node = fields.Length == 2 ? Model.GetComponentsInChildren<Transform>(true)
                        .FirstOrDefault(t => t.name == fields[1]) : null;
                    _ = SpawnEffectAsync(id, node ? node : Model.transform, Vector3.zero,
                        node ? Quaternion.Euler(90, 0, 0) : Quaternion.identity, true);
                }
            }
        }

        void SpawnDetachedEffect(int id, Vector3 at, Quaternion facing) =>
            _ = SpawnEffectAsync(id, transform.parent, at, facing, false);

        async Task SpawnEffectAsync(int id, Transform anchor, Vector3 at, Quaternion facing, bool attached)
        {
            int generation = sceneGeneration;
            try
            {
                string address = NativeEffectDefinition.Resource(id);
                var definition = ContentAssets.Known(address)
                    ? await ContentAssets.GetAsync<NativeEffectDefinition>(address, destroyCancellationToken)
                    : WorldNanoEffectLibrary.Find(id);
                if (!this || generation != sceneGeneration || !anchor || !anchor.gameObject.activeInHierarchy) return;
                if (!definition) { replicas.ContentFailure($"Nano effect ES{id} has no published definition."); return; }
                var instance = NativeCombatEffect.Spawn(anchor, definition, view, attached ? (Vector3?)null : at);
                if (attached) instance.transform.localRotation = facing;
                else instance.transform.rotation = facing;
                effects.RemoveAll(item => !item);
                effects.Add(instance);
                EffectSpawned?.Invoke(id, instance);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (this && generation == sceneGeneration) replicas.ContentFailure($"Nano effect ES{id}: {error.Message}");
            }
        }

        sealed class Sound
        {
            public string Address;
            public bool Voice, Farewell;
            public ContentAssets.Request Request;
            public AudioSource Source;
        }

        readonly List<Sound> sounds = new();
        static int playingSources;
        // Unity 2.x VOLoad/SFXLoad set rolloffFactor to 0.1. Unity 6 exposes a
        // per-source distance curve. Sample the corresponding inverse-distance law.
        static AnimationCurve attenuation;
        static AnimationCurve NanoAttenuation()
        {
            if (attenuation != null) return attenuation;
            var keys = new Keyframe[41];
            for (int distance = 0; distance <= 40; distance++)
            {
                float volume = 1f / (1 + .1f * Mathf.Max(0, distance - 1));
                keys[distance] = new Keyframe(distance / 40f, volume);
            }
            return attenuation = new AnimationCurve(keys);
        }

        void QueueVoice(string owner, string[] takes, bool farewell)
        {
            if (string.IsNullOrEmpty(owner) || takes == null || takes.Length == 0 || !world) return;
            string locale = (string)world.Preferences["voice_locale"] ?? "en";
            int start = Random.Range(0, takes.Length);
            string Choose(string language)
            {
                for (int i = 0; i < takes.Length; i++)
                {
                    string address = $"audio/voice/{language}/{owner}/{takes[(i + start) % takes.Length]}";
                    if (ContentAssets.Known(address)) return address;
                }
                return null;
            }
            string chosen = Choose(locale);
            if (chosen == null && locale != "en") chosen = Choose("en");
            if (chosen != null) QueueSound(chosen, true, farewell);
        }

        void QueueSound(string address, bool voice, bool farewell)
        {
            if (string.IsNullOrEmpty(address) || !world || !ContentAssets.Known(address)) return;
            Transform listener = world.Player;
            bool playerPresent = listener;
            if (!listener && Camera.main) listener = Camera.main.transform;
            if (!listener) return;
            float range = playingSources < 10 ? (playerPresent ? 18 : 40) : (playerPresent ? 16 : 30);
            Vector3 origin = farewell ? transform.position : position;
            if (playingSources >= 20 || (origin - listener.position).sqrMagnitude >= range * range ||
                Gain(voice) <= 0 || AudioListener.volume <= 0) return;
            sounds.Add(new Sound { Address = address, Voice = voice, Farewell = farewell,
                Request = ContentAssets.AcquireAsync<AudioClip>(address) });
        }

        float Gain(bool voice) => FrontendController.AudioGain(world.Preferences, voice ? "voice" : "effects");

        void UpdateSounds()
        {
            if (sounds.Count == 0) return;
            bool voiceBusy = false;
            for (int i = 0; i < sounds.Count;)
            {
                var cue = sounds[i];
                if (!ReferenceEquals(cue.Source, null))
                {
                    if (!cue.Source || !cue.Source.isPlaying) { ReleaseSound(i); continue; }
                    cue.Source.volume = Gain(cue.Voice);
                }
                else if ((!cue.Voice || !voiceBusy) && cue.Request.isDone)
                {
                    if (!(cue.Request.asset is AudioClip clip) || Gain(cue.Voice) <= 0)
                    { ReleaseSound(i); continue; }
                    var root = new GameObject("Nano audio " + clip.name);
                    root.transform.SetParent(transform.parent, false);
                    root.transform.position = cue.Farewell ? transform.position : position;
                    cue.Source = root.AddComponent<AudioSource>();
                    cue.Source.playOnAwake = false; cue.Source.spatialBlend = 1;
                    cue.Source.maxDistance = 40; cue.Source.rolloffMode = AudioRolloffMode.Custom;
                    cue.Source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, NanoAttenuation());
                    cue.Source.volume = Gain(cue.Voice); cue.Source.clip = clip;
                    cue.Source.Play(); playingSources++;
                    AudioStarted?.Invoke(cue.Address, cue.Source);
                }
                voiceBusy |= cue.Voice;
                i++;
            }
        }

        void ReleaseSound(int i)
        {
            var sound = sounds[i];
            if (sound.Source)
            {
                sound.Source.Stop();
                Destroy(sound.Source.gameObject);
            }
            if (!ReferenceEquals(sound.Source, null)) playingSources--;
            sound.Request.Dispose();
            sounds.RemoveAt(i);
        }

        void CancelPendingModelSounds()
        {
            for (int i = sounds.Count - 1; i >= 0; i--)
                if (!sounds[i].Source && !sounds[i].Farewell) ReleaseSound(i);
        }

        void ClearPresentation()
        {
            sceneGeneration++;
            foreach (var fx in effects)
                if (fx) { fx.SetActive(false); Destroy(fx); }
            effects.Clear();
            for (int i = sounds.Count - 1; i >= 0; i--) ReleaseSound(i);
        }
    }
}
