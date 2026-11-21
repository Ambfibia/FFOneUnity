#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Network;
using FFOne.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
        internal void LiveUseRecallNanoSkill() => UseActiveNanoSkill();
    }

    public sealed partial class LiveGameplayPlayerSmoke
    {
        async Task RecallPoint()
        {
            var world = frontend.LiveWorld;
            Check(world.MapNumber == 75, "Recall Point acceptance runs inside the real instance 75");
            var returnTo = WorldCoordinates.ToServer(world.Player.position);
            int failures = world.Replicas.ContentFailures.Count;
            // Original tabledata/npcs.json placement 569. Its spawn key is not
            // assumed to equal the server's network entity ID.
            var sourcePoint = WorldCoordinates.FromServer(130715, 223861, -16066);
            var forward = Vector3.ProjectOnPlane(world.View.transform.forward, Vector3.up).normalized;
            var observer = WorldCoordinates.ToServer(sourcePoint - forward * 3);
            RecallPointPresentation presentation = null;
            try
            {
                await frontend.LiveMoveTo(observer.x, observer.y, observer.z);
                NetworkEntityView actor = null;
                await Wait(() => world.IsReady && (actor = world.Replicas.Actors.FirstOrDefault(a =>
                    a.Entity.Kind == WorldEntityKind.Npc && a.Entity.NpcType == 1386 &&
                    Math.Abs(a.Entity.X - 130715) <= 2 && Math.Abs(a.Entity.Y - 223861) <= 2 && Math.Abs(a.Entity.Z + 16066) <= 2)),
                    "original Recall Point type 1386 at instance-75 placement 569 enters real server interest");
                await Wait(() => actor && (presentation = actor.GetComponent<RecallPointPresentation>()) && presentation.EffectRoot,
                    "native Recall Point model route loads and creates the original marker presentation");
                Check(actor.Definition.Model == WorldNpcCatalog.RecallPointModel && world.Replicas.ContentFailures.Count == failures,
                    "Recall Point resolves its exact published prefab without a missing-content fallback");
                var body = actor.GetComponentInChildren<NativeNpcAnimator>(true);
                Check(body && body.Animator.runtimeAnimatorController, "loaded RXcom uses its authored Animator Controller");
                // The RXcom source body is inactive. Its separate effect supplies the visible marker.
                Check(!body.gameObject.activeInHierarchy && body.GetClip("stand1", true) && body.GetClip("nif-default", true),
                    "the inactive RXcom body retains both authored native clip bindings");
                Check(body.GetComponentsInChildren<Transform>(true).Length == 4 && !body.GetComponentsInChildren<Renderer>(true).Any(),
                    "loaded RXcom preserves its four source nodes with no fabricated body Renderer");
                var box = body.transform.Find("Box01/Box01 NonAccum/Box").GetComponent<MeshFilter>().sharedMesh;
                Check(box && box.vertexCount == 24 && box.triangles.Length == 36,
                    "native RXcom retains the original unrendered Box geometry");
                Check(presentation.EffectId == 446 &&
                    Mathf.Abs(presentation.EffectRoot.localPosition.y - 1.08f) < .00001f &&
                    Quaternion.Angle(presentation.EffectRoot.localRotation, Quaternion.Euler(90, 0, 0)) < .001f,
                    "unselected Recall Point uses authored ES446 at its proven root attachment");
                var particleMesh = new Mesh();
                try
                {
                    await Wait(() => RecallParticlesReady(presentation, world.View, particleMesh),
                        "authored ES446 emits both native particle surfaces in the player");
                }
                finally { Destroy(particleMesh); }
                var viewport = world.View.WorldToViewportPoint(presentation.EffectRoot.position);
                Check(viewport.z > 0 && viewport.x > 0 && viewport.x < 1 && viewport.y > 0 && viewport.y < 1,
                    "the real Recall Point marker is inside the unchanged gameplay camera view");
                await Frames(12); Capture(worldTexture, "recall-point-446");

                Check(frontend.LiveUi.OpenNpcTravel(actor.Entity, actor.Definition), "native Recall Point interaction opens its registration action");
                received.Remove(ProtocolRecall.Success);
                Submit(frontend.LiveUi.Windows.View.Q<Button>("recall-register"));
                var selectedMesh = new Mesh();
                try
                {
                    await Wait(() => received.Contains(ProtocolRecall.Success) && !frontend.LiveUi.TravelBusy &&
                        world.Replicas.SelectedRecallPointId == actor.Entity.Id && presentation.EffectId == 811 &&
                        RecallParticlesReady(presentation, world.View, selectedMesh),
                        "RustyFusion accepts the real Recall registration and its decoded NPC identity selects authored ES811");
                }
                finally { Destroy(selectedMesh); }
                Check(!frontend.LiveUi.Windows.View.Q<Button>("recall-register").enabledInHierarchy,
                    "acknowledged Recall registration disables the native Register action");
                await Frames(12); Capture(worldTexture, "recall-point-811-registered"); Capture(uiTexture, "recall-registration");
                var selectedEffect = presentation.EffectRoot;
                received.Remove(ProtocolRecall.Success);
                await frontend.Session.RegisterRecallPointAsync(actor.Entity.Id);
                await Wait(() => received.Contains(ProtocolRecall.Success), "repeat registration is acknowledged by RustyFusion");
                Check(presentation.EffectRoot == selectedEffect, "idempotent live Recall ACK preserves the selected particle owner");
                received.Remove(ProtocolRecall.Fail);
                await frontend.Session.RegisterRecallPointAsync(int.MaxValue);
                await Wait(() => received.Contains(ProtocolRecall.Fail), "RustyFusion rejects an unknown Recall NPC with its real failure reply");
                Check(world.Replicas.SelectedRecallPointId == actor.Entity.Id && presentation.EffectRoot == selectedEffect && presentation.EffectId == 811,
                    "rejected live registration preserves the accepted Recall ID and ES811 marker");
                frontend.LiveUi.Windows.Close();
                var recallNano = frontend.LiveState.Snapshot.Nanos.Single(n => n.Id == 9);
                Check(frontend.LiveState.Snapshot.NanoSlots[1] == 9 && recallNano.SkillId == 25,
                    "native player owns original Nano 9 with its accepted Recall skill 25 in carry slot 2");
                int previousActive = frontend.LiveState.Snapshot.ActiveNanoSlot;
                await frontend.Session.ActivateNanoAsync(1);
                await Wait(() => frontend.LiveState.Snapshot.ActiveNanoSlot == 1, "native HUD accepts the real Recall Nano activation");
                await frontend.LiveMoveTo(actor.Entity.X + 400, actor.Entity.Y, actor.Entity.Z);
                received.Remove(0x31000031);
                frontend.LiveUseRecallNanoSkill();
                await Wait(() => received.Contains(0x31000031) &&
                    Vector3.Distance(world.Player.position, WorldCoordinates.FromServer(actor.Entity.X, actor.Entity.Y, actor.Entity.Z)) < .2f,
                    "native Recall action resolves self without hostile aim and RustyFusion returns to the registered NPC coordinates");
                Check(world.MapNumber == 75 && world.Replicas.SelectedRecallPointId == actor.Entity.Id && presentation.EffectId == 811,
                    "successful Recall power stays in the same instance and preserves its selected marker");
                await frontend.Session.ActivateNanoAsync(previousActive);
                await Wait(() => frontend.LiveState.Snapshot.ActiveNanoSlot == previousActive,
                    "native Recall power restores the previous active Nano slot");
                Check(world.Replicas.ContentFailures.Count == failures, "Recall Point native playback introduces no missing-content errors");
            }
            finally
            {
                await frontend.LiveMoveTo(returnTo.x, returnTo.y, returnTo.z);
                await Wait(() => world.MapNumber == 75 && world.IsReady, "Recall Point acceptance returns to the original instance-75 position");
            }
        }

        static bool RecallParticlesReady(RecallPointPresentation presentation, Camera camera, Mesh baked)
        {
            if (!presentation || !presentation.EffectRoot || !camera) return false;
            var systems = presentation.EffectRoot.GetComponentsInChildren<ParticleSystem>();
            if (systems.Length != 2) return false;
            foreach (var system in systems)
            {
                if (system.particleCount == 0 || !system.TryGetComponent<ParticleSystemRenderer>(out var renderer) ||
                    !renderer.enabled || !renderer.sharedMaterial || !renderer.sharedMaterial.shader.isSupported)
                    return false;
                baked.Clear();
                renderer.BakeMesh(baked, camera, ParticleSystemBakeMeshOptions.Default);
                if (baked.vertexCount == 0) return false;
            }
            return true;
        }
    }
}
#endif
