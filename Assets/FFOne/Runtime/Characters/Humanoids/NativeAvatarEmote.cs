using System;
using System.Collections;
using UnityEngine;

namespace FFOne.Content
{
    public sealed partial class NativeAvatarPreview
    {
        int emoteCode;
        internal bool HasActiveEmote => emoteCode != 0;
        float emoteStarted, emoteDuration;
        string emoteLocale, firstEmoteSound, secondEmoteSound;
        float emoteVolume;
        AudioSource avatarVoice, danceMusic;
        internal float DanceMusicGain { get; private set; } = 1;
        int danceMusicCode;
        float danceEchoDeadline;
        internal void SetDanceMusicGain(float gain)
        {
            DanceMusicGain = gain;
            if (danceMusic) danceMusic.volume = gain;
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal event Action<string, AudioSource> VoicePlayed;
#endif
        Action<int> continueEmote;

        // AvatarEmote's wire IDs and per-rig EndAnimation / PlaySound times.
        static (string clip, float male, float female, string first, string second) Emote(int code) =>
            code switch
            {
                1 => ("cry", 2.35000f, 2.28333f, "sad{3}", null),
                2 => ("angry", 1.01667f, 1.01667f, "angry{3}", null),
                3 => ("shocked", 1.48333f, 1.48333f, "scared{3}", null),
                4 => ("hello", 2.48333f, 2.48333f, "hello{3}", null),
                5 => ("thank", 1.35000f, 1.35000f, "thanks{3}", null),
                6 => ("dance1", 3.48333f, 3.88333f, "dance01", "sfx_dance1_{2}"),
                7 => ("kiss", 1.61667f, 1.48333f, "love{3}", null),
                8 => ("agree", 1.15000f, 1.15000f, "yes{3}", null),
                9 => ("laugh", 3.08333f, 2.41667f, "laugh{3}", null),
                10 => ("no", 1.41667f, 1.41667f, "no{3}", null),
                11 => ("flex", 1.48333f, 1.48333f, "flex{3}", null),
                12 => ("tease", 2.48333f, 2.48333f, "tease{3}", null),
                13 => ("ok", 1.41667f, 1.41667f, "yes{3}", null),
                14 => ("applaud", 2.01667f, 2.41667f, "applause", null),
                15 or 16 => ("cheer", 1.28333f, 0.95000f, "happy{3}", null),
                17 => ("dance2", 2.75000f, 3.41667f, "dance02", "sfx_dance2_{2}"),
                18 => ("dance3", 2.75000f, 2.68333f, "dance03", "sfx_dance3_{2}"),
                19 => ("dance4", 3.61667f, 3.75000f, "sfx_dance4_{2}", "dance04"),
                20 => ("dance5", 2.75000f, 4.01667f, "sfx_dance5_{2}", "dance05"),
                21 => ("goodbye", 2.48333f, 2.48333f, null, null),
                22 => ("beach1", 2.95000f, 2.95000f, null, null),
                23 => ("beach2", 2.95000f, 2.95000f, null, null),
                24 => ("beach3", 2.95000f, 2.95000f, null, null),
                31 => ("ffr_dance_02", 0.00000f, 0.00000f, null, null),
                32 => ("ffr_dance_04", 0.00000f, 0.00000f, null, null),
                33 => ("ffr_dance_07", 0.00000f, 0.00000f, null, null),
                34 => ("ffr_dance_08", 0.00000f, 0.00000f, null, null),
                35 => ("ffr_dance_10", 0.00000f, 0.00000f, null, null),
                36 => ("ffr_dance_13", 0.00000f, 0.00000f, null, null),
                37 => ("ffr_dance_15", 0.00000f, 0.00000f, null, null),
                38 => ("ffr_dance_18", 0.00000f, 0.00000f, null, null),
                39 => ("ffr_dance_19", 0.00000f, 0.00000f, null, null),
                40 => ("ffr_dance_20", 0.00000f, 0.00000f, null, null),
                41 => ("ffr_dance_bully", 0.00000f, 0.00000f, null, null),
                42 => ("ffr_dance_tellme", 0.00000f, 0.00000f, null, null),
                43 => ("ffr_emote_catpose", 0.00000f, 0.00000f, null, null),
                44 => ("ffr_emote_idolpose", 0.00000f, 0.00000f, null, null),
                _ => default
            };

        public void PlayEmote(int code, string locale, float volume, Action<int> continuation)
        {
            var cue = Emote(code);
            if (cue.clip == null || !playerAnimation || PersonalVehicleId != 0 || !playerAnimation.Play(cue.clip, true)) return;
            if (code is >= 31 and <= 42) StartDanceLoop(code);
            else StopDanceLoop();
            emoteCode = code; emoteStarted = Time.time;
            emoteDuration = actorGender == "female" ? cue.female : cue.male;
            if (emoteDuration <= 0) emoteDuration = playerAnimation.Duration(cue.clip);
            emoteLocale = locale == "ru" ? "ru" : "en"; emoteVolume = volume;
            firstEmoteSound = cue.first; secondEmoteSound = cue.second; continueEmote = continuation;
            SetWeaponVisible(false);
        }

        void TickEmote()
        {
            if (emoteCode == 0)
            {
                if (danceMusicCode != 0 && Time.unscaledTime >= danceEchoDeadline) StopDanceLoop();
                return;
            }
            float elapsed = Time.time - emoteStarted;
            if (elapsed >= .25f && firstEmoteSound != null)
            {
                PlayEmoteSound(firstEmoteSound); PlayEmoteSound(secondEmoteSound);
                firstEmoteSound = secondEmoteSound = null;
            }
            if (elapsed < emoteDuration) return;
            int next = emoteCode switch {
                6 or >= 17 and <= 20 => new[] { 6, 17, 18, 19, 20 }[UnityEngine.Random.Range(0, 5)],
                >= 22 and <= 24 or >= 31 and <= 42 => emoteCode,
                _ => 0
            };
            var send = continueEmote;
            bool repeatDance = next == emoteCode && danceMusicCode == next;
            if (repeatDance) danceEchoDeadline = Time.unscaledTime + 1;
            EndEmote(repeatDance);
            playerAnimation.Play("stand1");
            if (next != 0) send?.Invoke(next);
        }
        void PlayEmoteSound(string cue)
        {
            PlayEmoteSound(cue, emoteLocale, cue != null && cue.StartsWith("sfx_dance", StringComparison.Ordinal) ? DanceMusicGain : emoteVolume);
        }

        void PlayEmoteSound(string cue, string locale, float volume)
        {
            if (cue == null) return;
            int range = cue.EndsWith("{3}", StringComparison.Ordinal) ? 3 : cue.EndsWith("{2}", StringComparison.Ordinal) ? 2 : 0;
            if (range != 0) cue = cue.Substring(0, cue.Length - 3) + UnityEngine.Random.Range(1, range + 1).ToString("00");
            PlayAvatarVoice(cue, locale, volume);
        }

        internal void PlayNpcIdleSounds(string clip, string locale, float volume, bool nearby)
        {
            if (volume <= 0) return;
            for (int code = 1; code <= 24; code++)
            {
                var cue = Emote(code);
                if (cue.clip != clip) continue;
                void Play(string sound)
                {
                    if (sound == null || !nearby && !sound.StartsWith("sfx_dance", StringComparison.Ordinal)) return;
                    PlayEmoteSound(sound, locale, volume);
                }
                Play(cue.first);
                Play(cue.second);
                return;
            }
        }

        internal void PlayDamageVoice(bool dead, bool critical, string locale, float volume)
        {
            if (volume <= 0 || !IsReady) return;
            string cue = (critical ? "critical" : "hurt") + UnityEngine.Random.Range(1, 3).ToString("00");
            PlayAvatarVoice(cue, locale, volume);
            if (dead) StartCoroutine(DefeatVoice(locale, volume));
        }

        internal void PlayJumpVoice(bool airborne, string locale, float volume)
        {
            if (!IsReady || volume <= 0) return;
            string cue = airborne ? "jumpstart" + UnityEngine.Random.Range(1, 10).ToString("00")
                : "jumplnd" + UnityEngine.Random.Range(1, 4).ToString("00");
            PlayAvatarVoice(cue, locale, volume);
        }

        internal void PlayStunVoice(string locale, float volume)
        {
            if (volume > 0 && IsReady) PlayAvatarVoice("stun" + UnityEngine.Random.Range(1, 4).ToString("00"), locale, volume);
        }

        IEnumerator DefeatVoice(string locale, float volume)
        {
            // The accepted die clip dispatches the separate defeat voice at 0.25 s.
            yield return new WaitForSeconds(.25f);
            PlayAvatarVoice("defeatlng" + UnityEngine.Random.Range(1, 4).ToString("00"), locale, volume);
        }

        void PlayAvatarVoice(string cue, string locale, float volume)
        {
            if (volume <= 0) return;
            string route = (actorGender == "male" ? "/m_avatar/" : "/f_avatar/") + "avatar_" + cue;
            string address = "audio/voice/" + (locale == "ru" ? "ru" : "en") + route;
            var clip = ContentAssets.Load<AudioClip>(address);
            if (!clip && locale == "ru") { address = "audio/voice/en" + route; clip = ContentAssets.Load<AudioClip>(address); }
            if (!clip) return;
            if (!avatarVoice) { avatarVoice = gameObject.AddComponent<AudioSource>(); avatarVoice.playOnAwake = false; avatarVoice.spatialBlend = 1; }
            avatarVoice.PlayOneShot(clip, volume);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            VoicePlayed?.Invoke(address, avatarVoice);
#endif
        }
        void StartDanceLoop(int code)
        {
            if (danceMusic && danceMusicCode == code && danceMusic.isPlaying) return;
            StopDanceLoop();
            // Custom FFR animation clips contain no sound events. Use the published
            // instrumental dance takes for the corresponding avatar rig.
            string rig = actorGender == "male" ? "m_avatar" : "f_avatar";
            int tune = (code - 31) % 5 + 1;
            int take = (code - 31) / 5 % 2 + 1;
            var clip = ContentAssets.Load<AudioClip>($"audio/voice/en/{rig}/avatar_sfx_dance{tune}_{take:00}");
            if (!clip) return;
            if (!danceMusic)
            {
                danceMusic = gameObject.AddComponent<AudioSource>();
                danceMusic.playOnAwake = false; danceMusic.loop = true;
                danceMusic.spatialBlend = 1; danceMusic.dopplerLevel = 0;
                danceMusic.rolloffMode = AudioRolloffMode.Linear; danceMusic.maxDistance = 30;
            }
            danceMusicCode = code;
            danceMusic.volume = DanceMusicGain;
            danceMusic.clip = clip;
            danceMusic.Play();
        }

        void StopDanceLoop()
        {
            if (danceMusicCode == 0) return;
            danceMusicCode = 0;
            if (!danceMusic) return;
            danceMusic.Stop();
            danceMusic.clip = null;
        }

        public bool CancelEmote() => EndEmote(false);

        bool EndEmote(bool waitingForDanceEcho)
        {
            if (!waitingForDanceEcho) StopDanceLoop();
            if (emoteCode == 0) return false;
            emoteCode = 0; firstEmoteSound = secondEmoteSound = null; continueEmote = null;
            SetWeaponVisible(PersonalVehicleId == 0);
            return true;
        }
    }
}
