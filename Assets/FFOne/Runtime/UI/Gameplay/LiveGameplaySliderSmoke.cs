#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Network;
using FFOne.World;
using UnityEngine;

namespace FFOne.UI
{
    public sealed partial class LiveGameplayPlayerSmoke
    {
        async Task Sliders()
        {
            await frontend.LiveMoveTo(220450, 162484, -3502);
            await peer.SendMovementAsync(220450, 162584, -3502, 0, 0, 0, 0, 0, 0, true);
            NetworkEntityView bus = null;
            await Wait(() => frontend.LiveWorld.IsReady && (bus = frontend.LiveWorld.Replicas.Actors.FirstOrDefault(v =>
                v.Entity.Kind == WorldEntityKind.Transportation && v.GetComponentsInChildren<MeshCollider>().Any(c => c.enabled))),
                "Slider streams its accepted model and authored nonconvex mesh collision");
            Check(bus.GetComponentsInChildren<Renderer>().Any(r => r.enabled), "Slider model has visible accepted renderers");
            var collider = bus.GetComponentsInChildren<MeshCollider>().Single();
            Vector3 landing = Vector3.zero; bool found = false;
            var bounds = collider.bounds;
            for (int x = -1; x <= 1 && !found; x++) for (int z = -1; z <= 1 && !found; z++)
            {
                var ray = new Ray(new Vector3(bounds.center.x + bounds.extents.x * x * .5f, bounds.max.y + 5,
                    bounds.center.z + bounds.extents.z * z * .5f), Vector3.down);
                if (collider.Raycast(ray, out var hit, bounds.size.y + 10) && hit.normal.y > .5f) { landing = hit.point; found = true; }
            }
            Check(found, "Slider source collision has a real walkable surface");
            var position = WorldCoordinates.ToServer(landing + Vector3.up * .03f);
            await frontend.LiveMoveTo(position.x, position.y, position.z);
            await Wait(() => frontend.LiveWorld.SliderId == bus.Entity.Id, "CharacterController contact boards the real Slider surface");
            var start = bus.transform.position; var local = frontend.LiveWorld.SliderLocalPosition;
            await Wait(() => bus && (bus.transform.position - start).sqrMagnitude > 4, "server route moves the rendered Slider with a passenger");
            Check(frontend.LiveWorld.SliderId == bus.Entity.Id && (frontend.LiveWorld.SliderLocalPosition - local).magnitude < .5f,
                "moving platform carries the local character without relative drift");
            await Frames(8); Capture(worldTexture, "slider-riding");
            await frontend.LiveMoveTo(632032, 187177, -5500);
            await peer.SendMovementAsync(632132, 187177, -5500, 0, 0, 0, 0, 0, 0, true);
            await Wait(() => frontend.LiveWorld.SliderId == 0 && frontend.LiveWorld.IsReady, "leaving the circuit clears local Slider support and restores world movement");
        }
    }
}
#endif
