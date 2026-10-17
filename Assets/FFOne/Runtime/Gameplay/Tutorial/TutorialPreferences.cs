using FFOne.UI;
using Newtonsoft.Json.Linq;

namespace FFOne.Tutorial
{
    public sealed partial class TutorialController
    {
        public void ApplyPreferences(JObject options)
        {
            Russian = (string)options["text_locale"] == "ru";
            VoiceRussian = (string)options["voice_locale"] == "ru";
            if (effects) effects.UseRussianVoice = VoiceRussian;
            if (voice) voice.volume = FrontendController.AudioGain(options, "voice");
            if (sound) sound.volume = FrontendController.AudioGain(options, "effects");
            if (ambient) ambient.volume = FrontendController.AudioGain(options, "ambient");
            if (music) music.volume = FrontendController.AudioGain(options, "music");
            foreach (var loop in loops) if (loop) loop.volume = FrontendController.AudioGain(options, "effects");
        }
    }
}
