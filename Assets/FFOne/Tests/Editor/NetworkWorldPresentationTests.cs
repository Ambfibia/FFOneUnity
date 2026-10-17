using ContentPaths = FFOne.Content.ContentPaths;
using System.Collections;
using System.IO;
using FFOne.Creation;
using FFOne.Network;
using FFOne.Tutorial;
using FFOne.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FFOne.Tests
{
    public sealed class NetworkWorldPresentationTests
    {
        GameObject owner, previousCamera, light;
        RenderTexture target;

        [UnityTest]
        public IEnumerator ImportedOverworldLoadsAtServerPositionAndRestoresPresentation()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            yield return new EnterPlayMode();
            var catalog = new CreationCatalog();
            int Item(string category) => (int)catalog.Choices(category, 1)[0]["value"];
            var character = CreationCatalog.Preview(new CharacterCreation {
                Style = new CharacterStyle { Gender = 1, FaceStyle = Item("face"), HairStyle = Item("hair"), EyeColor = 1 },
                UpperBodyId = Item("shirt"), LowerBodyId = Item("pants"), FootId = Item("shoes")
            });
            var entry = new WorldEntry();
            void Set(string name, int value) => typeof(WorldEntry).GetProperty(name).SetValue(entry, value);
            // Current RustyFusion's authoritative post-tutorial snapshot, captured by the isolated live smoke.
            Set("MapNumber", 0); Set("X", 632032); Set("Y", 187177); Set("Z", -5500); Set("Angle", -45);
            light = new GameObject("World acceptance light", typeof(Light));
            light.GetComponent<Light>().type = LightType.Directional;
            light.transform.rotation = Quaternion.LookRotation(new Vector3(.89370085f, -.30901706f, -.32528046f));
            previousCamera = new GameObject("Previous presentation", typeof(Camera), typeof(AudioListener));
            var previous = previousCamera.GetComponent<Camera>();
            previous.enabled = true;
            owner = new GameObject("World acceptance");
            var world = owner.AddComponent<NetworkWorldController>();
            world.Begin(character, entry);
            Assert.That(previous.enabled, Is.False);
            Assert.That(world.Player.position, Is.EqualTo(WorldCoordinates.FromServer(entry.X, entry.Y, entry.Z)));
            Assert.That(world.Player.eulerAngles.y, Is.EqualTo(135).Within(.001f));
            Assert.That(world.MovementEnabled, Is.False, "Loading must not emit gameplay movement before server acknowledgement.");
            float deadline = Time.realtimeSinceStartup + 120;
            while (!world.IsReady)
            {
                Assert.That(world.LastError, Is.Null.Or.Empty);
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Native tile admission timed out.");
                yield return null;
            }
            Assert.That(world.Stream.ResidentTiles, Is.InRange(1, WorldStreamer.MaximumRoots));
            Assert.That(world.Stream.ColliderCount, Is.GreaterThan(0));
            TutorialPlayerEquipment.Equip(world.Player);
            Assert.That(world.Player.GetComponent<TutorialPlayerEquipment>(), Is.Not.Null, "Mecanim rig must expose the exact temporary weapon socket.");
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                target = new RenderTexture(1264, 681, 24); target.Create(); world.View.targetTexture = target;
                yield return null; yield return null;
                var previousTarget = RenderTexture.active;
                var picture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                try
                {
                    RenderTexture.active = target; picture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); picture.Apply();
                    Directory.CreateDirectory(ContentPaths.FilePath("Logs/MigrationPreview")); File.WriteAllBytes(ContentPaths.FilePath("Logs/MigrationPreview/network-world.png"), picture.EncodeToPNG());
                }
                finally { RenderTexture.active = previousTarget; Object.Destroy(picture); }
                world.View.targetTexture = null;
            }
            world.SetServerPosition(-100, -100, 0);
            Assert.That(world.LastError, Does.Contain("no imported world"));
            Assert.That(world.MovementEnabled, Is.False);
            world.Stop();
            Assert.That(previous.enabled, Is.True);
            yield return null;
            Assert.That(owner.GetComponentsInChildren<WorldStreamer>(), Is.Empty);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (owner) Object.Destroy(owner);
            if (previousCamera) Object.Destroy(previousCamera);
            if (light) Object.Destroy(light);
            if (target) { target.Release(); Object.Destroy(target); }
            yield return null;
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
    }
}
