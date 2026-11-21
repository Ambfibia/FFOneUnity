using FFOne.Content;
using FFOne.Tutorial;
using FFOne.UI;
using UnityEngine;

namespace FFOne.World
{
    public sealed partial class NetworkWorldController
    {
        GameObject computerEffect;
        TutorialEffectAssets computerAssets;
        AudioSource computerLoop;
        int computerEffectId;
        float computerEventAt;

        void UpdateComputer()
        {
            int id = 0;
            if (InventoryOpen && Player && !playerDead && !Riding && !Ziplining && !Jumping)
                id = !PersonalVehicleMounted ? 425 : avatar.LocomotionClip("inven") == "board_inven" ? 833 : 834;
            if (id != computerEffectId)
            {
                ClearComputer();
                computerEffectId = id;
                if (id == 0) return;
                computerEventAt = Time.time + (id == 425 ? .25f : .3f);
                computerLoop = Player.gameObject.AddComponent<AudioSource>();
                computerLoop.playOnAwake = false;
                computerLoop.loop = true;
                computerLoop.spatialBlend = 0;
                computerLoop.clip = ContentAssets.Load<AudioClip>("audio/sfx/ui/invenlooping");
                computerLoop.volume = .7f * FrontendController.AudioGain(Preferences, "effects");
                computerLoop.Play();
            }
            if (id == 0) return;
            computerLoop.volume = .7f * FrontendController.AudioGain(Preferences, "effects");
            if (computerEffect || Time.time < computerEventAt) return;

            // Both donor effect wrappers reference the same m_inven subtree, published under ES740.
            string model = id == 425 ? "map/shared/effects/models/es740/m_inven" : null;
            computerAssets = TutorialEffectAssets.Load(id, model, worldShader: true, defaultModel: true);
            computerEffect = new GameObject("NanoCom ES" + id);
            computerEffect.transform.SetParent(Player, false);
            computerEffect.AddComponent<TutorialEffectPlayback>().Initialize(computerAssets, 1, View, true);
        }

        void ClearComputer()
        {
            if (computerLoop) { computerLoop.Stop(); Destroy(computerLoop); computerLoop = null; }
            if (computerEffect) { TutorialEffectAssets.Release(computerEffect); computerEffect = null; }
            computerAssets?.Dispose();
            computerAssets = null;
            computerEffectId = 0;
        }
    }
}
