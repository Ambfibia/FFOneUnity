using System.IO;
using FFOne.Content;
using FFOne.Tutorial;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    /// <summary>Publishes the six exact Retrobution tutorial sky textures as an editable Unity material.</summary>
    public static class TutorialSkyboxImport
    {
        public const string Folder = "Assets/FFOne/Worlds/Tutorial/Content/Skybox";
        public const string MaterialPath = Folder + "/TutorialSkybox.mat";
        // Primary Tutorial.resourceFile SHA256 729e9aad557709d4a75937b7478b6784b82f202c000d13127d1074213991f916,
        // serialized asset CustomAssetBundle-b4f543c102ded400fbc6f1da25d9679a, material 1875 Skybox_FreeZone.
        // cnPlayerCamera.DefaultSkybox + LateUpdate select it inside TutorialZoneRect.
        // Texture path IDs: Front 101, Back 229, Left 311, Right 36, Up 61, Down 253.
        // Reproduce each PNG with FusionForge export-exact-texture <Tutorial.resourceFile> <pathId> -,
        // decoding payload.dataUrl directly into the matching final PNG below.
        static readonly string[] Faces = { "Front", "Back", "Left", "Right", "Up", "Down" };

        [MenuItem("FFOne/Rebuild Tutorial Skybox")]
        public static void Build()
        {
            var shader = Shader.Find("FFOne/TutorialSkybox");
            if (!shader) throw new InvalidDataException("The tutorial skybox shader is unavailable.");
            foreach (string face in Faces)
            {
                string path = Folder + "/" + face + ".png";
                if (!File.Exists(path)) throw new FileNotFoundException("Exact tutorial sky texture is missing.", path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default;
                importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear;
                importer.mipmapEnabled = false; importer.sRGBTexture = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, MaterialPath); }
            material.shader = shader; material.SetColor("_Tint", new Color(.5f, .5f, .5f, 1));
            foreach (string face in Faces)
            {
                string property = "_" + face + "Tex";
                material.SetTexture(property, AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/" + face + ".png"));
                material.SetTextureScale(property, face == "Up" || face == "Down" ? Vector2.one : new Vector2(1, 2));
            }
            EditorUtility.SetDirty(material);
            var content = ContentAssets.Load<TutorialContent>("Tutorial/Content");
            if (content) { content.World = null; content.Skybox = material; EditorUtility.SetDirty(content); }
            AssetDatabase.SaveAssets();
        }
    }
}
