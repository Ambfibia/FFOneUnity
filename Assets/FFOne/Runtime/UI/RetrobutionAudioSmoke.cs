#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    // Opt-in standalone validation with the real audio engine and shipped clips.
    // No account, character mutations or network requests.
    public sealed class RetrobutionAudioSmoke : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            if (Environment.GetCommandLineArgs().Contains("--retrobution-audio-smoke"))
                new GameObject("Retrobution audio validation").AddComponent<RetrobutionAudioSmoke>();
        }
        async void Start()
        {
            try { await Run(); Debug.Log("RETROBUTION AUDIO SMOKE PASSED"); Application.Quit(0); }
            catch (Exception error) { Debug.LogException(error); Application.Quit(1); }
        }
        async Task Run()
        {
            FrontendController frontend = null;
            await Wait(() => { frontend = FindFirstObjectByType<FrontendController>(); return frontend && frontend.Root?.Q<Button>("create-character") != null; });
            var audio = frontend.GetComponent<RetrobutionAudioController>();
            Require(audio && audio.Catalog != null, "Audio catalog was not prepared before frontend presentation.");
            audio.SetGain(.5f, .5f, true);
            await Wait(() => audio.Bgm.CurrentAddress == RetrobutionAudioController.MainTheme && audio.Bgm.Source.isPlaying);
            Require(!audio.Bgm.Source.loop && audio.Ambient.Source.loop, "Music/ambient loop policy differs from MusicController.Awake.");
            audio.PlayCreator();
            await Wait(() => audio.Creator.isPlaying && audio.Bgm.Source.clip == null);
            int creatorStarts = audio.CreatorStarts;
            int samples = audio.Creator.timeSamples;
            await Seconds(.2f); audio.PlayCreator(); await Seconds(.2f);
            Require(audio.CreatorStarts == creatorStarts && audio.Creator.timeSamples > samples, "Entering the creator restarted its ship-scene loop.");
            audio.SetMode(RetrobutionAudioController.Mode.Loading);
            await Wait(() => audio.Bgm.CurrentAddress == RetrobutionAudioController.MainTheme && audio.Bgm.Source.isPlaying);
            Require(!audio.Creator.isPlaying, "Creator loop leaked into world loading.");
            var zone = audio.Catalog.Music.First(z => z.Address == "audio/music/01_future" && !z.Instance);
            var point = zone.Points.Aggregate(Vector2.zero, (a, p) => a + p) / zone.Points.Length;
            audio.SetWorldPosition(new Vector3(point.x, 0, point.y), false);
            audio.SetMode(RetrobutionAudioController.Mode.World);
            await Wait(() => audio.Bgm.CurrentAddress == zone.Address && audio.Bgm.Source.isPlaying);
            float quiet = audio.Bgm.Source.volume;
            await Seconds(1.1f);
            Require(audio.Bgm.Source.volume > quiet, "Replacement music did not fade in.");
            int zoneStarts = audio.Bgm.Starts;
            int zoneSamples = audio.Bgm.Source.timeSamples;
            var adjacent = audio.Catalog.Music.First(z => z.Address == zone.Address && z.Index != zone.Index && !z.Instance);
            var adjacentPoint = adjacent.Points.Aggregate(Vector2.zero, (a, p) => a + p) / adjacent.Points.Length;
            audio.SetWorldPosition(new Vector3(adjacentPoint.x, 0, adjacentPoint.y), false, relocated: true);
            audio.SetMode(RetrobutionAudioController.Mode.World);
            await Seconds(.6f);
            Require(audio.Bgm.Source.isPlaying && audio.Bgm.Starts == zoneStarts && audio.Bgm.Source.timeSamples > zoneSamples,
                "Travelling between zones that share music restarted their track.");
            audio.Bgm.Source.time = audio.Bgm.Source.clip.length - .05f;
            await Wait(() => !audio.Bgm.Source.isPlaying);
            audio.SetWorldPosition(new Vector3(point.x, 0, point.y), false, relocated: true);
            await Seconds(1.2f);
            Require(audio.Bgm.Starts == zoneStarts, "Crossing an adjacent polygon bypassed the shared song's replay delay.");
            audio.SetWorldPosition(new Vector3(point.x, 0, point.y), false);
            audio.SetMode(RetrobutionAudioController.Mode.Loading);
            await Wait(() => audio.Bgm.CurrentAddress == RetrobutionAudioController.MainTheme && audio.Bgm.Source.isPlaying);
            audio.SetMode(RetrobutionAudioController.Mode.World);
            await Wait(() => audio.Bgm.CurrentAddress == zone.Address && audio.Bgm.Source.isPlaying);
            Require(audio.Bgm.Source.clip.name != "00 Main Theme", "The loading theme survived same-region reentry during its repeat delay.");
            var lair = audio.Catalog.Music.First(z => z.Address == "audio/music/buttercuplair1");
            point = lair.Points.Aggregate(Vector2.zero, (a, p) => a + p) / lair.Points.Length;
            audio.SetWorldPosition(new Vector3(point.x, 0, point.y), true);
            await Wait(() => audio.Bgm.CurrentAddress == lair.Address && audio.Bgm.Source.isPlaying);
            var dark = audio.Catalog.Ambient.First(z => z.Instance && z.Address == "audio/ambient/ambient_fusion_lairs");
            point = dark.Points.Aggregate(Vector2.zero, (a, p) => a + p) / dark.Points.Length;
            audio.SetWorldPosition(new Vector3(point.x, 0, point.y), true, relocated: true);
            await Wait(() => audio.Bgm.Source.clip == null && audio.Ambient.CurrentAddress == dark.Address &&
                audio.Ambient.Source.isPlaying);
            audio.SetWorldPosition(new Vector3(-100000, 0, -100000), true, relocated: true);
            await Wait(() => audio.Ambient.Source.clip == null);
            audio.SetMode(RetrobutionAudioController.Mode.Tutorial);
            await Wait(() => audio.Bgm.Source.clip == null && audio.Ambient.Source.clip == null);
            audio.SetMode(RetrobutionAudioController.Mode.Lobby);
            await Wait(() => audio.Bgm.CurrentAddress == RetrobutionAudioController.MainTheme && audio.Bgm.Source.isPlaying);
            int starts = audio.Bgm.Starts;
            audio.SetGain(.5f, .5f, false); await Seconds(.1f);
            Require(audio.Bgm.Source.volume == 0, "Music toggle did not mute BGM.");
            audio.SetGain(.5f, .5f, true); await Seconds(.1f);
            Require(audio.Bgm.Starts == starts && audio.Bgm.Source.isPlaying, "Music toggle restarted the current track.");
            audio.Bgm.Select(zone.Address); audio.Bgm.Select(RetrobutionAudioController.MainTheme);
            await Seconds(.7f);
            Require(audio.Bgm.Starts == starts && audio.Bgm.CurrentAddress == RetrobutionAudioController.MainTheme,
                "A cancelled asynchronous replacement restarted or resurrected the wrong track.");

            await frontend.ShowLoadingAsync(FrontendLoadingStage.CharacterCreation);
            frontend.SetLoadingProgress(.8f, .7f);
            await frontend.ShowLoadingAsync(FrontendLoadingStage.CharacterSelection);
            Require(frontend.Root.Q("loading-current-fill").style.width.value.value == 15, "The current-resource bar did not reset for the new group.");
            Require(frontend.Root.Q("loading-overall-fill").style.width.value.value > 15, "Advancing a resource group reset the overall session progress.");
            frontend.SetLoadingResourceProgressVisible(false);
            Require(frontend.LoadingPhase == FrontendLoadingPhase.Scene, "Resource completion did not retain the scene-loading background.");
            frontend.HideLoading();
            Require(frontend.LoadingPhase == FrontendLoadingPhase.Hidden, "Loading overlay did not finish.");
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static async Task Wait(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while (!condition()) { if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Retrobution audio validation timed out."); await Task.Yield(); }
        }
        static async Task Seconds(float seconds)
        { float until = Time.unscaledTime + seconds; while (Time.unscaledTime < until) await Task.Yield(); }
    }
}
#endif
