using FFOne.UI;
using Newtonsoft.Json.Linq;

namespace FFOne.Tutorial
{
    public sealed partial class TutorialController
    {
        bool showComputressHints = true;
        bool tutorialBalloons = true;
        bool tutorialAnimatedNanos = true, tutorialNpcMessagesInChat = true;

        public void ApplyPreferences(JObject options, bool musicEnabled = true)
        {
            Russian = (string)options["text_locale"] == "ru";
            showComputressHints = (bool?)options["display"]?["game_hint"] ?? true;
            tutorialBalloons = (bool?)options["display"]?["balloon"] ?? true;
            tutorialAnimatedNanos = (bool?)options["display"]?["animated_nanos"] ?? true;
            tutorialNpcMessagesInChat = (bool?)options["display"]?["npc_messages_in_chat"] ?? true;
            tutorialNanoHud?.SetAnimated(tutorialAnimatedNanos);
            VoiceRussian = (string)options["voice_locale"] == "ru";
            mouseSensitivity = (float?)options["input"]?["sensitivity"] / 5 ?? 1;
            padSensitivity = (float?)options["input"]?["pad_sensitivity"] ?? 5;
            invertPadY = (bool?)options["input"]?["pad_invert_y"] ?? false;
            invertMouseY = (bool?)options["input"]?["invert_y"] ?? false;
            if (effects) effects.UseRussianVoice = VoiceRussian;
            if (voice) voice.volume = FrontendController.AudioGain(options, "voice");
            if (sound) sound.volume = FrontendController.AudioGain(options, "effects");
            if (ambient) ambient.volume = FrontendController.AudioGain(options, "ambient");
            if (music) music.volume = musicEnabled ? FrontendController.AudioGain(options, "music") : 0;
            foreach (var loop in loops) if (loop) loop.volume = FrontendController.AudioGain(options, "effects");
            LayoutTutorialHud();
        }
    }
}
