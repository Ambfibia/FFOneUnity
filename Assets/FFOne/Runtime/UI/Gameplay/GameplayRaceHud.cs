using FFOne.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        VisualElement raceHud;
        Label raceCount, raceClock, raceLimitLabel;
        Image racePodImage;
        float lastPickup;

        void BuildRaceHud()
        {
            raceHud = FFView.InstantiateClass("ui/views/Retrobution/GameplayHud", "ff-race-hud");
            racePodImage = FFView.RequireClass<Image>(raceHud, "ff-race-hud-pod");
            raceCount = FFView.RequireClass<Label>(raceHud, "ff-race-hud-count");
            raceClock = FFView.RequireClass<Label>(raceHud, "ff-race-hud-clock");
            raceLimitLabel = FFView.RequireClass<Label>(raceHud, "ff-race-hud-limit");
            minimap.View.Add(raceHud);
            lastPickup = float.NegativeInfinity;
        }

        void UpdateRaceHud(int ticks)
        {
            string Copy(string key) => LocalizationCatalog.Lookup("ui.race.hud." + key, language);
            raceCount.text = Copy("pods").Replace("{pods}", racePods.ToString("000"));
            raceClock.text = Copy("time").Replace("{time}", $"{ticks/6000:00}:{ticks/100%60:00}:{ticks%100:00}");
            raceLimitLabel.text = Copy("limit").Replace("{time}", $"{raceLimit/60:00}:{raceLimit%60:00}");
            float age = Mathf.Clamp01(Time.unscaledTime - lastPickup);
            float displacement = Mathf.Sin(age * Mathf.PI * 4) * (1 - age) * 6;
            racePodImage.style.translate = new Translate(0, displacement);
        }
    }

}
