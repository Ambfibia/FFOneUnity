#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FFOne.Content
{
    /// <summary>Opt-in ownership and cancellation checks against the shipped bundle providers.</summary>
    public sealed class ContentRequestSmoke : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            if (Environment.GetCommandLineArgs().Contains("--content-request-smoke"))
                new GameObject("Content request validation").AddComponent<ContentRequestSmoke>();
        }

        async void Start()
        {
            try
            {
                using var canceled = new CancellationTokenSource();
                canceled.Cancel();
                await ExpectCanceled(ContentAssets.InitializeAsync(canceled.Token));
                await Task.WhenAll(ContentAssets.InitializeAsync(), ContentAssets.InitializeAsync());
                Require(ContentAssets.LoadAll<Font>("fonts").Length == 3, "Typed catalog enumeration lost the accepted fonts.");
                Require(!await ContentAssets.GetAsync<Texture2D>("__missing_content_probe__"),
                    "An absent catalog address did not return null.");
                using (var absent = ContentAssets.AcquireAsync<Texture2D>("__missing_content_probe__"))
                {
                    await absent.WaitAsync();
                    Require(!absent.asset, "An absent acquired address resolved an asset.");
                }
                foreach (string cue in new[] { "run1_1", "run1_2", "skill0", "melee2_1", "melee2_2", "melee2_3" })
                    Require(await ContentAssets.GetAsync<AudioClip>("audio/sfx/creatures/spawn_cs/" + cue), "Recovered source sound: " + cue);
                foreach (string expression in new[] { "talk", "talksad" })
                    Require(await ContentAssets.GetAsync<Texture2D>("characters/npcs/npc_numbuhtwo_future/textures/npc_numbuhtwo_face_" + expression),
                        "Recovered source face: " + expression);
                Require(await ContentAssets.GetAsync<Texture2D>("map/shared/effects/models/es768/EP_DL_Scamper_tail.textures/bubble_tie_02"),
                    "Recovered Scamper texture.");
                Require(await ContentAssets.GetAsync<GameObject>("NativeEditable/map/shared/effects/models/es768/EP_DL_Scamper_tail/model"),
                    "Recovered Scamper prefab.");
                Require(await ContentAssets.GetAsync<Shader>("Tutorial/DexterShipHologram"), "Tutorial hologram shader.");

                using (var lease = ContentAssets.AcquireAsync<Texture2D>("ui/en/user-equip/calculator-back"))
                {
                    using var abandoned = new CancellationTokenSource();
                    var survivor = lease.WaitAsync();
                    var leaving = lease.WaitAsync(abandoned.Token);
                    abandoned.Cancel();
                    if (!leaving.IsCompletedSuccessfully) await ExpectCanceled(leaving);
                    await survivor;
                    Require(lease.asset, "Canceling one waiter discarded another waiter's asset.");
                }
                var retained = await ContentAssets.GetAsync<Texture2D>("ui/en/user-equip/calculator-back");
                using (var lease = ContentAssets.AcquireAsync<Texture2D>("ui/en/user-equip/calculator-back"))
                {
                    await lease.WaitAsync();
                    Require(lease.asset == retained, "Concurrent leases resolved different texture objects.");
                }
                Require(retained, "Disposing an acquired reference released the retained consumer.");

                var ending = ContentAssets.AcquireAsync<AudioClip>("audio/music/06_tech_center");
                bool pending = !ending.isDone;
                var ended = ending.WaitAsync();
                ending.Dispose();
                if (pending)
                {
                    try { await ended; throw new InvalidOperationException("Disposal did not wake the pending waiter."); }
                    catch (ObjectDisposedException) { }
                }
                ending.Dispose();

                var first = NativeData.ObjectAsync("data/character_creation/customization");
                var second = NativeData.ObjectAsync("data/character_creation/customization");
                await ExpectCanceled(NativeData.ObjectAsync("data/character_creation/customization", canceled.Token));
                Require(JToken.DeepEquals(await first, await second), "Concurrent document leases changed the parsed data.");
                Require(JToken.DeepEquals(await first, await NativeData.ObjectAsync("data/character_creation/customization")),
                    "Releasing document leases prevented a subsequent load.");

                Debug.Log("CONTENT REQUEST SMOKE PASSED: catalog lookup, typed enumeration, recovered content, shared initialization, independent cancellation, acquired/retained ownership, disposal, document reload.");
                Application.Quit(0);
            }
            catch (Exception error) { Debug.LogException(error); Application.Quit(1); }
        }
        static async Task ExpectCanceled(Task task)
        {
            try { await task; throw new InvalidOperationException("Expected cancellation."); }
            catch (OperationCanceledException) { }
        }
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
#endif
