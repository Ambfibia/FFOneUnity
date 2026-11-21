using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Content;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FFOne.UI
{
    // Primary main.unity3d SHA256 01b544976b2d54355507cf30fe6dfada2b476b92b209a3d47c1499669ed9b4ef.
    // first pass MusicController: Awake 0600024e, FadeInNewAudio.MoveNext
    // 060004ce (IL0309-0385 / 0693-070f), replay end time IL045b-047d.
    // SoundUtil.PlayLoopSound 060002a2 IL0052-0085 retains the playing UI loop.
    // MusicController and SoundUtil have separate lifetimes in the primary client.
    // A UI mode change must not restart SoundUtil's already playing creator loop.
    public sealed class RetrobutionAudioController : MonoBehaviour
    {
        public const string MainTheme = "audio/music/00_main_theme";
        public const string CreatorTheme = "audio/music/charactercreation_loop";
        public enum Mode { Lobby, Loading, Creation, Tutorial, World }
        public Mode CurrentMode { get; private set; }
        public RetrobutionAudioChannel Bgm { get; private set; }
        public RetrobutionAudioChannel Ambient { get; private set; }
        public AudioSource Creator { get; private set; }
        public AudioSource UiEffects { get; private set; }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal event Action<string> UiCuePlayed;
#endif
        AudioSource serviceVoice;
        int voiceRequest;
        public int CreatorStarts { get; private set; }
        public RetrobutionAudioCatalog Catalog { get; private set; }
        CancellationTokenSource lifetime;
        float nextScan, musicGain = .5f, ambientGain = .5f;
        bool musicEnabled = true, creatorWanted, modeSelected;
        Vector2 position;
        bool instance;
        int worldMap;
        const string TrainingTheme = "audio/music/01_knd_training";
        const string LairAmbience = "audio/ambient/ambient_fusion_lairs";

        void Awake()
        {
            lifetime = new CancellationTokenSource();
            Bgm = new RetrobutionAudioChannel(NewSource("Retrobution BGM"), false, lifetime.Token);
            Ambient = new RetrobutionAudioChannel(NewSource("Retrobution ambient"), true, lifetime.Token);
            UiEffects = NewSource("Retrobution UI effects");
            serviceVoice = NewSource("Retrobution service voice");
            Creator = NewSource("Retrobution UI loop"); Creator.loop = true;
        }

        AudioSource NewSource(string label)
        {
            var root = new GameObject(label); root.transform.SetParent(transform, false);
            var source = root.AddComponent<AudioSource>(); source.playOnAwake = false;
            source.spatialBlend = 0; return source;
        }

        public async Task InitializeAsync(CancellationToken cancellation)
        {
            var catalog = NativeData.ObjectAsync("data/audio/retrobution-world", cancellation);
            var creator = ContentAssets.GetAsync<AudioClip>(CreatorTheme, cancellation);
            await Task.WhenAll(catalog, creator);
            Catalog = new RetrobutionAudioCatalog(catalog.Result);
            Creator.clip = creator.Result;
            if (!Creator.clip) throw new InvalidOperationException("Missing character creation soundtrack.");
            nextScan = 0;
        }

        public void SetGain(float music, float ambient, bool enabled)
        {
            musicGain = music; ambientGain = ambient; musicEnabled = enabled;
            Creator.volume = enabled ? music : 0;
        }

        public void SetEffectsGain(float gain) => UiEffects.volume = .7f * gain;

        // SoundUtil.ButtonSound chooses a fresh sample at each accepted control action.
        public void PlayUi(string cue)
        {
            if (!UiEffects || UiEffects.volume <= 0 || AudioListener.volume <= 0) return;
            if (cue == "button") cue = "mouse_click0" + UnityEngine.Random.Range(1, 6);
            string folder = cue switch {
                "taros" => "environment",
                "nano_creation" => "nano",
                "transportation_warp" or "resurrectm_warp" or "dexbot_warp" or "fusionmatter" => "world_events",
                _ => "ui"
            };
            string address = "audio/sfx/" + folder + "/" + cue;
            var clip = ContentAssets.Load<AudioClip>(address);
            if (!clip) return;
            UiEffects.PlayOneShot(clip);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UiCuePlayed?.Invoke(address);
#endif
        }

        Transform serviceVoiceSpeaker;

        internal async Task PlayServiceVoice(string owner, string cue, string locale, float gain, CancellationToken cancellation, Transform speaker = null)
        {
            int request = ++voiceRequest;
            serviceVoice.Stop();
            serviceVoiceSpeaker = speaker;
            serviceVoice.spatialBlend = speaker ? 1 : 0;
            if (speaker)
            {
                serviceVoice.transform.position = speaker.position + Vector3.up;
                serviceVoice.minDistance = 3;
                serviceVoice.maxDistance = 30;
                serviceVoice.dopplerLevel = 0;
            }
            if (string.IsNullOrWhiteSpace(owner) || gain <= 0 || AudioListener.volume <= 0 || CurrentMode != Mode.World) return;
            string folder = owner.Trim().ToLowerInvariant();
            // Echo Echo's XDT owner and the retained desktop voice folder have different names.
            if (folder == "echoecho") folder = "echo";
            if (cue is "QGreeting" or "Farewell" or "GoodLuck" or "NiceJob")
            {
                int first = folder.IndexOf('_');
                int role = first < 0 ? -1 : folder.IndexOf('_', first + 1);
                if (role >= 0) folder = folder.Substring(0, role);
            }
            string stem = folder.StartsWith("m_",StringComparison.Ordinal) || folder.StartsWith("f_",StringComparison.Ordinal)
                ? folder.Substring(2) : folder;
            int count = cue == "Greeting" ? 2 : cue is "QGreeting" or "ClickBarb" ? 1 : 3;
            int firstTake = UnityEngine.Random.Range(0, count);
            string Address(string language)
            {
                string prefix = "audio/voice/" + language + "/" + folder + "/";
                if (cue.StartsWith("FirstUse", StringComparison.OrdinalIgnoreCase))
                {
                    string exact = prefix + cue.ToLowerInvariant();
                    return ContentAssets.Known(exact) ? exact : null;
                }
                for (int i = 0; i < count; i++)
                {
                    string take = cue.ToLowerInvariant();
                    if (cue != "QGreeting") take += "0" + (1 + (firstTake + i) % count);
                    string prefixed = prefix + stem + "_" + take;
                    if (ContentAssets.Known(prefixed)) return prefixed;
                    if (ContentAssets.Known(prefix + take)) return prefix + take;
                }
                return null;
            }
            string address = Address(locale) ?? (locale == "en" ? null : Address("en"));
            if (address == null) return;
            try
            {
                var clip = await ContentAssets.GetAsync<AudioClip>(address,cancellation);
                if (!clip || request != voiceRequest || !serviceVoice || CurrentMode != Mode.World) return;
                serviceVoice.Stop();
                serviceVoice.volume = gain;
                serviceVoice.clip = clip;
                serviceVoice.Play();
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (this && request==voiceRequest) Debug.LogWarning("NPC voice "+owner+" / "+cue+": "+error.Message); }
        }

        void LateUpdate()
        {
            if (serviceVoiceSpeaker && serviceVoice.isPlaying) serviceVoice.transform.position = serviceVoiceSpeaker.position + Vector3.up;
        }

        public void SetMode(Mode mode)
        {
            if (modeSelected && CurrentMode == mode) return;
            modeSelected = true;
            CurrentMode = mode; nextScan = 0; Ambient.ResetZone();
            if (mode != Mode.World) { ++voiceRequest; serviceVoice.Stop(); }
            if (mode != Mode.Creation) StopCreator();
            if (mode == Mode.Creation || mode == Mode.Tutorial)
            { Bgm.Select(null); Ambient.Select(null); }
            else if (mode == Mode.World)
            {
                if (Catalog != null) RefreshWorldAudio(Time.unscaledTime);
            }
            else if (mode != Mode.World)
            { Bgm.Select(MainTheme); Ambient.Select(null); }
        }

        public void PlayCreator()
        {
            SetMode(Mode.Creation);
            if (creatorWanted) return;
            creatorWanted = true;
            Creator.volume = musicEnabled ? musicGain : 0;
            Creator.Play(); CreatorStarts++;
            Debug.Log("RETROBUTION AUDIO: UI loop started " + CreatorTheme);
        }

        public void StopCreator()
        {
            creatorWanted = false;
            if (Creator) Creator.Stop();
        }

        public void SetWorldPosition(Vector3 value, bool inInstance, bool relocated = false, int mapNumber = 0)
        {
            // Published polygons retain Unity X/Z. The native Rust X reflection
            // belongs to Bevy coordinates and must not be applied in Unity.
            position = new Vector2(value.x, value.z);
            if (instance != inInstance || worldMap != mapNumber || relocated) { instance = inInstance; worldMap = mapNumber; nextScan = 0; Ambient.ResetZone(); }
        }

        static string MusicAddress(RetrobutionAudioCatalog.Zone zone, int map)
            => zone != null ? zone.Address : map == 5 ? TrainingTheme : null;

        static string AmbientAddress(RetrobutionAudioCatalog.Zone zone, int map)
            => zone != null ? zone.Address : map > 0 && FFOne.World.WorldTravelCatalog.Load().LairMaps.Contains(map) ? LairAmbience : null;

        public async Task PreloadWorldAsync(Vector3 value, bool inInstance, CancellationToken cancellation, int mapNumber = 0)
        {
            var point = new Vector2(value.x, value.z);
            var addresses = new[] { MusicAddress(Catalog.Find(Catalog.Music, point, inInstance), mapNumber),
                AmbientAddress(Catalog.Find(Catalog.Ambient, point, inInstance), mapNumber) };
            await Task.WhenAll(addresses.Where(address => address != null).Distinct()
                .Select(address => ContentAssets.GetAsync<AudioClip>(address, cancellation)));
        }

        void Update()
        {
            if (modeSelected && Catalog != null && Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 1;
                if (CurrentMode == Mode.World) RefreshWorldAudio(Time.unscaledTime);
                else if (CurrentMode == Mode.Lobby || CurrentMode == Mode.Loading) Bgm.Select(MainTheme);
            }
            Bgm.Tick(Time.unscaledDeltaTime, musicEnabled ? musicGain : 0);
            // CnGuiCharSelection.ResetMusicOff mutes ambience immediately.
            Ambient.Tick(Time.unscaledDeltaTime, CurrentMode == Mode.World ? ambientGain : 0);
        }

        void RefreshWorldAudio(float now)
        {
            var music = Catalog.Find(Catalog.Music, position, instance);
            // A quiet zone or a gap in the catalog must release the previous track.
            Bgm.SelectWorldMusic(MusicAddress(music, worldMap), now);
            var ambient = Catalog.Find(Catalog.Ambient, position, instance);
            if (ambient == null && AmbientAddress(null, worldMap) is string fallback)
                Ambient.Select(fallback);
            else if (ambient != null && ambient.Address != Bgm.TargetAddress)
                Ambient.SelectZone(Catalog.Ambient, position, instance, now);
            else
            {
                Ambient.Select(null);
                Ambient.ResetZone();
            }
        }

        void OnDestroy() { lifetime?.Cancel(); lifetime?.Dispose(); }
    }

    public sealed class RetrobutionAudioCatalog
    {
        public sealed class Zone
        {
            public int Index;
            public bool Instance;
            public float Delay;
            public string Address;
            public Vector2[] Points;
            public bool Contains(Vector2 point)
            {
                bool inside = false;
                for (int i = 0, j = Points.Length - 1; i < Points.Length; j = i++)
                {
                    var a = Points[i]; var b = Points[j];
                    if ((a.y > point.y) != (b.y > point.y) && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                        inside = !inside;
                }
                return inside;
            }
        }
        public Zone[] Music { get; }
        public Zone[] Ambient { get; }
        public RetrobutionAudioCatalog(JObject document)
        {
            if ((string)document["schema"] != "ffone.retrobution-world-audio.v1") throw new InvalidOperationException("Unsupported world audio catalog.");
            Music = Read(document["musicZones"]); Ambient = Read(document["ambientZones"]);
        }
        static Zone[] Read(JToken rows) => rows.Select(row => new Zone {
            Index = (int)row["index"], Instance = (bool)row["instance"], Delay = (float)row["delaySeconds"],
            Address = row["logicalKey"].Type == JTokenType.Null ? null : "audio/" + (string)row["logicalKey"],
            Points = row["points"].Select(p => new Vector2((float)p[0], (float)p[1])).ToArray()
        }).ToArray();
        public Zone Find(Zone[] zones, Vector2 point, bool instance) => zones.FirstOrDefault(z => z.Instance == instance && z.Contains(point));
    }

        // Latest request wins while Addressables is pending. Fade the old source only
    // once the new clip is ready; stale loads can never replace a newer mode.
    public sealed class RetrobutionAudioChannel
    {
        public const float FadeOutSeconds = .5f, FadeInSeconds = 1;
        const float WorldMusicRepeatDelay = 180;
        public AudioSource Source { get; }
        public string TargetAddress { get; private set; }
        public string CurrentAddress { get; private set; }
        public int Starts { get; private set; }
        readonly bool looping;
        readonly CancellationToken cancellation;
        readonly System.Collections.Generic.Dictionary<string, float> lastEnded = new();
        int version;
        bool forceZoneSelection = true;
        AudioClip pending;
        bool ready, fadingOut, loading, fadeIn;
        bool wasPlaying;
        float replayAfter;
        float envelope = 1, fadeFrom = 1, elapsed;

        public RetrobutionAudioChannel(AudioSource source, bool loop, CancellationToken token)
        { Source = source; looping = loop; Source.loop = loop; cancellation = token; }
        public void ResetZone() => forceZoneSelection = true;

        internal bool SelectWorldMusic(string address, float now)
        {
            ObserveMusicEnd(now);
            if (address == TargetAddress && now < replayAfter) return false;
            return Select(address);
        }

        void ObserveMusicEnd(float now)
        {
            if (looping) return;
            bool playing = Source.isPlaying;
            if (wasPlaying && !playing && CurrentAddress != null && CurrentAddress == TargetAddress)
                replayAfter = now + WorldMusicRepeatDelay;
            wasPlaying = playing;
        }

        public void SelectZone(RetrobutionAudioCatalog.Zone[] zones, Vector2 point, bool instance, float now)
        {
            var zone = zones.FirstOrDefault(z => z.Instance == instance && z.Contains(point));
            if (zone == null) return;
            bool force = forceZoneSelection; forceZoneSelection = false;
            if (force || !lastEnded.TryGetValue(zone.Address ?? "", out float last) || last + zone.Delay < now)
            {
                Select(zone.Address);
            }
        }
        public bool Select(string address)
        {
            if (TargetAddress == address && (loading || ready || fadingOut || CurrentAddress == address && (address == null || Source.isPlaying))) return false;
            if (address != null && address == CurrentAddress && Source.isPlaying)
            {
                // A rapid return cancels the pending replacement and retains the
                // playing clip's sample position, rather than restarting it.
                version++; TargetAddress = address; pending = null;
                ready = fadingOut = loading = false;
                fadeIn = true; elapsed = envelope * FadeInSeconds;
                return false;
            }
            TargetAddress = address; pending = null; ready = false; fadingOut = false;
            replayAfter = 0;
            int request = ++version;
            loading = address != null;
            if (address == null) Queue(null);
            else _ = LoadAsync(address, request);
            return true;
        }
        async Task LoadAsync(string address, int request)
        {
            try
            {
                var clip = await ContentAssets.GetAsync<AudioClip>(address, cancellation);
                if (request != version || cancellation.IsCancellationRequested || !Source) return;
                if (!clip) Debug.LogWarning("Missing Retrobution audio: " + address);
                Queue(clip);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (request == version) { Debug.LogException(error); Queue(null); } }
        }
        void Queue(AudioClip clip)
        {
            pending = clip; ready = true; loading = false;
            if (fadingOut) return;
            fadingOut = Source.isPlaying; fadeIn = fadingOut; fadeFrom = envelope; elapsed = 0;
        }
        public void Tick(float delta, float gain) => Tick(delta, gain, Time.unscaledTime);

        internal void Tick(float delta, float gain, float now)
        {
            if (!Source) return;
            ObserveMusicEnd(now);
            if (ready)
            {
                if (fadingOut)
                {
                    elapsed += delta; envelope = fadeFrom * (1 - Mathf.Clamp01(elapsed / FadeOutSeconds));
                    if (elapsed < FadeOutSeconds) { Source.volume = envelope * gain; return; }
                }
                Source.Stop(); wasPlaying = false; Source.clip = pending; Source.loop = looping;
                CurrentAddress = TargetAddress; ready = fadingOut = false; elapsed = 0;
                envelope = pending && fadeIn ? 0 : 1;
                if (pending)
                {
                    Source.Play(); Starts++;
                    wasPlaying = Source.isPlaying;
                    if (looping) lastEnded[CurrentAddress] = now + pending.length;
                    Debug.Log("RETROBUTION AUDIO: " + CurrentAddress);
                }
            }
            else if (!loading && Source.clip && fadeIn) { elapsed += delta; envelope = Mathf.Clamp01(elapsed / FadeInSeconds); }
            Source.volume = envelope * gain;
        }
    }
}
