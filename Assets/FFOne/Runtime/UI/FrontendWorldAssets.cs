using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using FFOne.Content;
using FFOne.Network;
using FFOne.Tutorial;
using FFOne.World;
using UnityEngine;
using UnityEngine.UIElements;
using CharacterInfo = FFOne.Network.CharacterInfo;

namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
        async Task PrepareWorldAssetsAsync(CharacterInfo character, CancellationToken cancellation)
        {
            var requests = new List<Task> {
                NativeAvatarPreview.PreloadAsync(NativeAvatarPreview.Snapshot(character), cancellation),
                ContentAssets.GetAsync<WorldCatalog>("World/Catalog", cancellation),
                ContentAssets.GetAsync<Texture2D>("ui/ru/gameplay/player/character_info", cancellation),
                GameplayRewardCounters.PreloadAsync(language, cancellation),
                ContentAssets.GetAsync<NativeDataDocument>(NativeData.Resource("data/tables/xdt"), cancellation),
                ContentAssets.GetAsync<StyleSheet>("ui/styles/Gameplay", cancellation)
            };
            foreach (string view in new[] { "GameplayHud", "GameplayNanoHud", "Chat", "NanoCom", "MissionJournal", "Inventory", "WorldMap", "QuitMenu" })
                requests.Add(ContentAssets.GetAsync<VisualTreeAsset>("ui/views/Retrobution/" + view, cancellation));
            foreach (string art in new[] { "fusionmeter/regular", "fusionmeter/regular_right", "fusionmatter_us", "camera", "map_icon_00", "line_minimap" })
                requests.Add(ContentAssets.GetAsync<Texture2D>(GameplayMinimap.Art + art, cancellation));
            if (character.Style.TutorialComplete)
            {
                foreach (string art in new[] { "new-mail", "zoom-in", "zoom-out" })
                    requests.Add(ContentAssets.GetAsync<Texture2D>(GameplayMinimap.Art + art, cancellation));
                requests.Add(GameplayMinimap.PreloadTilesAsync(WorldCoordinates.FromServer(character.X, character.Y, character.Z), cancellation));
            }
            else
                requests.Add(GameplayMinimap.PreloadTilesAsync(TutorialController.StartPosition, cancellation));
            foreach (string cue in new[] { "open_screen", "close_screen", "mission_accepted", "abandon_mission", "email_arrived" })
                requests.Add(ContentAssets.GetAsync<AudioClip>("audio/sfx/ui/" + cue, cancellation));
            requests.Add(ContentAssets.GetAsync<AudioClip>("audio/sfx/environment/taros", cancellation));
            requests.Add(ContentAssets.GetAsync<AudioClip>("audio/sfx/world_events/fusionmatter", cancellation));
            if (!character.Style.TutorialComplete) requests.Add(TutorialController.PreloadResourcesAsync(character, cancellation));
            int completed = 0, total = requests.Count;
            await Task.WhenAll(requests.Select(async request => {
                await request; SetLoadingProgress(.9f * ++completed / total, 1);
            }));
            // Build table adapters before starting asynchronous additive scene activation.
            // Their synchronous consumers now read the already resident document.
            GameplayUiCatalog.Load();
            await NativeEffectLoading.PrepareWorldAsync(cancellation);
            await Task.Yield(); cancellation.ThrowIfCancellationRequested();
            WorldTravelCatalog.Load();
            // The destination is authoritative only after world entry. Reserve
            // the final part for its exact music/ambient clips before area load.
            SetLoadingProgress(.95f, 1);
        }
    }
}
