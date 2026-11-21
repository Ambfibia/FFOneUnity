#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Network;
using FFOne.World;
using UnityEngine;

namespace FFOne.UI
{
    internal sealed partial class LiveNanoCompanionSmoke
    {
        sealed class ExpectedPresentation
        {
            public string Phase, VoiceCharacter, VoiceCue, Sfx;
            public int Effect;
            public GameObject Object;
            public bool Pixels;
            public float NextRender;
        }
        sealed class PlayedCue
        {
            public string Phase, Address, Clip;
            public AudioSource Source;
            public int InitialSample, MaxSample;
            public float SourcePeak, ListenerPeak;
            public NanoAudioRecording Recording;
        }

        ExpectedPresentation presentation;
        readonly List<PlayedCue> audioCues = new();
        readonly List<object> effectCaptures = new(), skillReceipts = new();
        readonly float[] waveform = new float[2048];
        int skillTarget;

        void BeginPresentationChecks()
        {
            Check(Companion, "inactive companion owner ready for actual presentation events");
            Companion.AudioStarted += ObserveAudio;
            Companion.EffectSpawned += ObserveEffect;
            frontend.NetworkEventReceived += ObserveSkill;
        }

        void ArmPresentation(string phase, int nano)
        {
            string actor = nano switch { 1 => "nano_buttercup", 6 => "nano_wilt", 9 => "nano_edd",
                _ => throw new InvalidDataException("Unexpected fixture Nano " + nano) };
            presentation = new ExpectedPresentation { Phase = phase, VoiceCharacter = actor,
                VoiceCue = phase == "skill" ? "nanpwrmf" : phase == "withdraw" ? "nandismiss" : "nansummon",
                Sfx = phase == "withdraw" ? null : phase == "skill" ? "audio/sfx/nano_skills/stun/root/stun_root_generic" :
                    "audio/sfx/nano/nano_ability_06",
                Effect = phase == "skill" ? 60 : phase == "withdraw" ? 10 : nano == 9 ? 529 : 528 };
        }

        async Task UseButtercupSkill()
        {
            NetworkEntityView target = null;
            await Until(() => (target = World.Replicas.Actors.Where(a => a && a.Hostile && a.Entity.Hp > 0 &&
                a.Entity.NpcType == 59 && !a.Entity.Hidden && Vector3.Distance(World.Player.position, a.transform.position) < 10)
                .OrderBy(a => (a.transform.position - World.Player.position).sqrMagnitude).FirstOrDefault()), "legal nearby type-59 target");
            skillTarget = target.Entity.Id;
            await frontend.Session.UseNanoSkillAsync(new[] { skillTarget }, skillTarget);
        }

        void ObserveSkill(NetworkEvent evt)
        {
            if (presentation?.Phase != "skill" || evt?.Frame == null || !evt.IsShard ||
                evt.Frame.PacketType != (owner ? ProtocolNanoSkill.Success : ProtocolNanoSkill.Broadcast) ||
                !ProtocolNanoSkill.TryDecode(evt.Frame, out var update) || update.NanoId != 1 || update.SkillId != 1 ||
                (!owner && update.CasterId != ownerId)) return;
            if (!update.Effects.Any(e => e.Kind == WorldEntityKind.Npc && (!owner || e.Id == skillTarget))) return;
            skillReceipts.Add(new { packet = evt.Frame.PacketType.ToString("x8"), update.NanoId, update.SkillId,
                update.CasterId, targets = update.Effects.Select(e => e.Id).ToArray() });
        }

        void ObserveEffect(int id, GameObject root)
        {
            if (presentation != null && presentation.Effect == id)
            {
                presentation.Object = root;
                Debug.Log("NANO COMPANION " + role + " " + presentation.Phase + " live ES" + id);
            }
        }

        void ObserveAudio(string address, AudioSource source)
        {
            if (presentation == null || !source || !source.clip) return;
            var recording = new NanoAudioRecording(AudioSettings.outputSampleRate);
            var cue = new PlayedCue { Phase = presentation.Phase, Address = address, Clip = source.clip.name,
                Source = source, InitialSample = source.timeSamples, Recording = recording };
            audioCues.Add(cue);
            Debug.Log("NANO COMPANION " + role + " " + presentation.Phase + " live audio " + address);
            // Each production Nano cue owns its own source and object. The filter only copies mixer data.
            if (source.GetComponents<AudioSource>().Length == 1)
                source.gameObject.AddComponent<NanoAudioSourceTap>().Recording = recording;
        }

        void LateUpdate()
        {
            if (presentation == null || !subject || !frontend || !World) return;
            foreach (var cue in audioCues)
            {
                if (!cue.Source || !cue.Source.isPlaying || cue.Source.mute) continue;
                cue.MaxSample = Math.Max(cue.MaxSample, cue.Source.timeSamples);
                cue.Source.GetOutputData(waveform, 0);
                cue.SourcePeak = Mathf.Max(cue.SourcePeak, Peak(waveform));
                AudioListener.GetOutputData(waveform, 0);
                cue.ListenerPeak = Mathf.Max(cue.ListenerPeak, Peak(waveform));
            }
            var step = presentation;
            if (step.Pixels || !step.Object || Time.unscaledTime < step.NextRender) return;
            step.NextRender = Time.unscaledTime + .06f;
            step.Pixels = Capture(step.Phase + "-ES" + step.Effect, step.Effect, step.Object, true);
        }

        static float Peak(float[] samples)
        {
            float maximum = 0;
            foreach (float sample in samples) maximum = Math.Max(maximum, Math.Abs(sample));
            return maximum;
        }

        bool VoiceMatches(PlayedCue cue, ExpectedPresentation phase)
        {
            string[] parts = cue.Address.Split('/');
            if (parts.Length != 5 || parts[0] != "audio" || parts[1] != "voice" || parts[3] != phase.VoiceCharacter ||
                !parts[4].StartsWith(phase.VoiceCue, StringComparison.OrdinalIgnoreCase)) return false;
            string requested = frontend.VoiceLanguage;
            if (parts[2] == requested) return true;
            if (phase.VoiceCharacter == "nano_wilt") return false;
            if (requested != "ru" || parts[2] != "en") return false;
            var localized = cue.Address.Replace("audio/voice/en/", "audio/voice/ru/");
            return !ContentAssets.Known(localized);
        }

        static bool Audible(PlayedCue cue) => cue.ListenerPeak > .00001f && cue.MaxSample > cue.InitialSample &&
            (cue.SourcePeak > .00001f || cue.Recording.Peak > .00001f);

        async Task VerifyPresentation()
        {
            var phase = presentation;
            await Until(() => phase.Pixels, phase.Phase + " ES" + phase.Effect + " contributes isolated visible pixels");
            await Until(() => audioCues.Any(c => c.Phase == phase.Phase && VoiceMatches(c, phase) && Audible(c)),
                phase.Phase + " audible localized " + phase.VoiceCharacter + "/" + phase.VoiceCue);
            if (phase.Sfx != null)
                await Until(() => audioCues.Any(c => c.Phase == phase.Phase && c.Address == phase.Sfx && Audible(c)),
                    phase.Phase + " audible " + phase.Sfx);
            if (phase.Phase == "skill")
                await Until(() => skillReceipts.Count > 0, "authoritative Buttercup skill confirmed on target");
            presentation = null;
        }

        IEnumerable<object> AudioResults()
        {
            for (int i = 0; i < audioCues.Count; i++)
            {
                var cue = audioCues[i];
                string name = cue.Phase + "-" + i + "-" + Path.GetFileName(cue.Address) + ".wav";
                int samples = cue.Recording.Write(Path.Combine(directory, name));
                yield return new { cue.Phase, cue.Address, cue.Clip, cue.InitialSample, cue.MaxSample, cue.SourcePeak,
                    cue.ListenerPeak, dspPeak = cue.Recording.Peak, samples, wav = samples > 0 ? name : null };
            }
        }
    }

    // One short PCM excerpt per isolated production AudioSource. No samples are modified or played back.
    internal sealed class NanoAudioRecording
    {
        readonly int rate;
        readonly short[] pcm;
        int used, channels;
        public volatile float Peak;
        public NanoAudioRecording(int rate)
        {
            this.rate = rate;
            pcm = new short[Math.Max(8000, rate) * 4];
        }
        public void Append(float[] data, int channelCount)
        {
            lock (pcm)
            {
                channels = channelCount;
                int count = Math.Min(data.Length, pcm.Length - used);
                for (int i = 0; i < count; i++)
                {
                    float sample = data[i];
                    Peak = Math.Max(Peak, Math.Abs(sample));
                    pcm[used++] = (short)(Math.Max(-1f, Math.Min(1f, sample)) * short.MaxValue);
                }
            }
        }
        public int Write(string path)
        {
            lock (pcm)
            {
                if (used == 0 || channels == 0 || Peak < .00001f) return 0;
                using var w = new BinaryWriter(File.Create(path));
                w.Write(new[] { 'R','I','F','F' }); w.Write(36 + used * 2); w.Write(new[] { 'W','A','V','E','f','m','t',' ' });
                w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(rate); w.Write(rate * channels * 2);
                w.Write((short)(channels * 2)); w.Write((short)16); w.Write(new[] { 'd','a','t','a' }); w.Write(used * 2);
                for (int i = 0; i < used; i++) w.Write(pcm[i]);
                return used;
            }
        }
    }
    internal sealed class NanoAudioSourceTap : MonoBehaviour
    {
        internal volatile NanoAudioRecording Recording;
        void OnAudioFilterRead(float[] data, int channels) => Recording?.Append(data, channels);
    }
}
#endif
