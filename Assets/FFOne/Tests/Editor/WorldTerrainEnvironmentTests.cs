using System.Collections;
using System.Reflection;
using FFOne.Editor;
using FFOne.Runtime.World;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FFOne.Tests
{
    public sealed class WorldTerrainEnvironmentTests
    {
        static readonly BindingFlags StaticFields = BindingFlags.NonPublic | BindingFlags.Static;
        static readonly FieldInfo Entries = typeof(WorldTerrainEnvironment).GetField("Entries", StaticFields);
        static readonly FieldInfo AppliedFrame = typeof(WorldTerrainEnvironment).GetField("appliedFrame", StaticFields);
        static readonly FieldInfo FrameFog = typeof(WorldTerrainEnvironment).GetField("frameFog", StaticFields);
        static readonly FieldInfo FogCamera = typeof(WorldTerrainEnvironment).GetField("fogCamera", StaticFields);
        static readonly MethodInfo Tick = typeof(WorldTerrainEnvironment).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
        readonly Hashtable previousEntries = new();
        Transform previousTarget;
        Camera previousView;
        bool previousTutorial, previousFog;
        FogMode previousFogMode;
        float previousDensity;
        Color previousFogColor;
        Vector4 previousShaderFog, previousShaderLight;
        int previousFrame;
        object previousFrameFog, previousFogCamera;
        GameObject owner, cameraOwner, targetOwner;
        WorldTerrainEnvironment environment;
        Camera camera;

        [SetUp]
        public void SetUp()
        {
            var entries = (IDictionary)Entries.GetValue(null);
            foreach (DictionaryEntry entry in entries) previousEntries.Add(entry.Key, entry.Value);
            entries.Clear();
            previousFrame = (int)AppliedFrame.GetValue(null);
            previousFrameFog = FrameFog.GetValue(null); previousFogCamera = FogCamera.GetValue(null);
            previousTarget = WorldTerrainEnvironment.SamplingTarget;
            previousView = WorldTerrainEnvironment.ViewOverride;
            previousTutorial = WorldTerrainEnvironment.TutorialScope;
            previousFog = RenderSettings.fog; previousFogMode = RenderSettings.fogMode;
            previousDensity = RenderSettings.fogDensity; previousFogColor = RenderSettings.fogColor;
            previousShaderFog = Shader.GetGlobalVector("_FFWorldFog");
            previousShaderLight = Shader.GetGlobalVector("_FFWorldAmbienceLight");
            owner = new GameObject("World ambience test");
            environment = owner.AddComponent<WorldTerrainEnvironment>(); environment.enabled = false;
            cameraOwner = new GameObject("World ambience camera"); camera = cameraOwner.AddComponent<Camera>(); camera.enabled = false;
            camera.farClipPlane = 1800; camera.transform.position = new Vector3(768, 0, 768);
            targetOwner = new GameObject("Distant streaming anchor"); targetOwner.transform.position = new Vector3(1792, 0, 1792);
            WorldTerrainEnvironment.ViewOverride = camera;
            WorldTerrainEnvironment.SamplingTarget = targetOwner.transform;
            WorldTerrainEnvironment.TutorialScope = false;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(owner); Object.DestroyImmediate(cameraOwner); Object.DestroyImmediate(targetOwner);
            var entries = (IDictionary)Entries.GetValue(null); entries.Clear();
            foreach (DictionaryEntry entry in previousEntries) entries.Add(entry.Key, entry.Value);
            previousEntries.Clear(); AppliedFrame.SetValue(null, previousFrame);
            FrameFog.SetValue(null, previousFrameFog); FogCamera.SetValue(null, previousFogCamera);
            WorldTerrainEnvironment.SamplingTarget = previousTarget; WorldTerrainEnvironment.ViewOverride = previousView;
            WorldTerrainEnvironment.TutorialScope = previousTutorial;
            RenderSettings.fog = previousFog; RenderSettings.fogMode = previousFogMode;
            RenderSettings.fogDensity = previousDensity; RenderSettings.fogColor = previousFogColor;
            Shader.SetGlobalVector("_FFWorldFog", previousShaderFog); Shader.SetGlobalVector("_FFWorldAmbienceLight", previousShaderLight);
        }

        void Apply() { AppliedFrame.SetValue(null, -1); Tick.Invoke(environment, null); }

        [Test]
        public void CinematicCameraSamplesItsDongInsteadOfTheStreamingAnchor()
        {
            var cameraFog = new Color(.2f, .4f, .6f, 1);
            WorldTerrainEnvironment.Register(new Vector2Int(1, 1), .8f, cameraFog, Color.cyan, Color.green);
            WorldTerrainEnvironment.Register(new Vector2Int(3, 3), .3f, Color.red, Color.red, Color.red);
            Apply();
            Assert.That(RenderSettings.fogColor, Is.EqualTo(cameraFog));
            Assert.That(camera.backgroundColor, Is.EqualTo(Color.cyan));
            Assert.That(Shader.GetGlobalVector("_FFWorldAmbienceLight"), Is.EqualTo(new Vector4(.4f, 1, .4f, 1)));
            Assert.That(Shader.GetGlobalVector("_FFWorldFog").w, Is.EqualTo(.004f).Within(.000001));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        public void UnfoggedDongDisablesBothUnityAndNativeShaderFog(float depth)
        {
            WorldTerrainEnvironment.Register(new Vector2Int(1, 1), depth, Color.green, Color.white, Color.white);
            Apply();
            Assert.That(RenderSettings.fog, Is.False);
            Assert.That(RenderSettings.fogDensity, Is.Zero);
            Assert.That(Shader.GetGlobalVector("_FFWorldFog").w, Is.Zero);
        }

        [Test]
        public void TutorialSkyTintDoesNotBrightenWorldFog()
        {
            var rawFog = new Color(.1f, .3f, .5f, 1);
            var material = new Material(Shader.Find("Skybox/6 Sided"));
            try
            {
                cameraOwner.AddComponent<Skybox>().material = material;
                WorldTerrainEnvironment.TutorialScope = true;
                WorldTerrainEnvironment.Register(new Vector2Int(1, 1), .5f, rawFog, Color.gray, Color.white);
                Apply();
                Assert.That(RenderSettings.fogColor, Is.EqualTo(rawFog));
                Assert.That(Shader.GetGlobalVector("_FFWorldFog"), Is.EqualTo(new Vector4(.1f, .3f, .5f, .0025f)));
                var tint = material.GetColor("_Tint");
                Assert.That(tint.r, Is.EqualTo(.55f).Within(.000001f));
                Assert.That(tint.g, Is.EqualTo(.65f).Within(.000001f));
                Assert.That(tint.b, Is.EqualTo(.75f).Within(.000001f));
                Assert.That(tint.a, Is.EqualTo(.85f).Within(.000001f));
            }
            finally { Object.DestroyImmediate(material); }
        }

        [Test]
        public void PortraitCameraDisablesWorldFogAndRestoresThePreviousUniform()
        {
            WorldTerrainEnvironment.Register(new Vector2Int(1, 1), .6f, Color.green, Color.gray, Color.white);
            Apply();
            var portrait = targetOwner.AddComponent<Camera>(); portrait.enabled = false;
            var previous = new Vector4(.1f, .2f, .3f, .4f);
            Shader.SetGlobalVector("_FFWorldFog", previous);
            RenderCamera("BeginCamera", portrait);
            Assert.That(Shader.GetGlobalVector("_FFWorldFog").w, Is.Zero);
            RenderCamera("EndCamera", portrait);
            Assert.That(Shader.GetGlobalVector("_FFWorldFog"), Is.EqualTo(previous));
            // Offscreen portraits can render before the next LateUpdate, while
            // the global uniform still contains the previous frame's world fog.
            typeof(WorldTerrainEnvironment).GetField("appliedFrame", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, Time.frameCount - 1);
            RenderCamera("BeginCamera", portrait);
            Assert.That(Shader.GetGlobalVector("_FFWorldFog").w, Is.Zero);
            RenderCamera("EndCamera", portrait);
            Assert.That(Shader.GetGlobalVector("_FFWorldFog"), Is.EqualTo(previous));
            RenderCamera("BeginCamera", camera);
            Assert.That(Shader.GetGlobalVector("_FFWorldFog").w, Is.EqualTo(.003f).Within(.000001));
            RenderCamera("EndCamera", camera);
            Assert.That(Shader.GetGlobalVector("_FFWorldFog"), Is.EqualTo(previous));
        }

        [TestCase("SkinnedToonShading_blendSrcalphaInvsrcalpha", 1)]
        [TestCase("SkinnedToonShading_blendSrcalphaInvsrcalpha_cullOff", 1)]
        [TestCase("ToonShading_blendSrcalphaInvsrcalpha", 1)]
        [TestCase("SkinnedToonShading", 0)]
        public void ToonMaterialImportRetainsTheExactSourceFogSelection(string sourceShader, int mode)
        {
            var material = new Material(Shader.Find("FFOne/NativeToon"));
            try
            {
                var definition = new JObject { ["extras"] = new JObject { ["ffone"] = new JObject { ["declaredShaderName"] = sourceShader } } };
                NativeWorldMaterialImport.Apply(null, new JObject(), definition, material);
                Assert.That(material.shader.name, Is.EqualTo("FFOne/NativeToon"));
                Assert.That(material.GetFloat("_FixedFogMode"), Is.EqualTo(mode));
            }
            finally { Object.DestroyImmediate(material); }
        }

        static void RenderCamera(string method, Camera view) => typeof(WorldTerrainEnvironment)
            .GetMethod(method, StaticFields).Invoke(null, new object[] { default(ScriptableRenderContext), view });
    }
}
