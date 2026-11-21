using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Content;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    public static class WorldNanoEffectPublication
    {
        const string Sources = "Assets/FFOne/Editor/ImportMetadata/NanoEffects";
        const string Models = "Assets/FFOne/Content/VFX/NanoModels";
        const string ResourceFolder = "Assets/FFOne/Content/VFX/Resources";
        const string LibraryPath = ResourceFolder + "/WorldNanoEffectLibrary.asset";

        [MenuItem("FFOne/VFX/Publish World Nano Effects")]
        public static void Publish()
        {
            NativeEffectPublication.PrepareDirectories();
            if (!AssetDatabase.IsValidFolder(ResourceFolder))
                AssetDatabase.CreateFolder("Assets/FFOne/Content/VFX", "Resources");
            var definitions = new SortedDictionary<int, NativeEffectDefinition>();
            foreach (string file in Directory.GetFiles(Sources, "es*.closure.json").OrderBy(path => path, StringComparer.Ordinal))
            {
                var closure = JObject.Parse(File.ReadAllText(file));
                int id = (int)closure["effectId"];
                if (id >= 843 && id <= 846) continue;
                if ((string)closure["schema"] != "ffone.tutorial-effect-closure.v1" ||
                    Path.GetFileName(file) != $"es{id}.closure.json")
                    throw new InvalidDataException($"Unexpected Nano effect source {file}");
                var objects = (JArray)closure["objects"];
                GameObject model = null;
                if (objects.Any(row => (string)row["objectType"] == "Mesh"))
                {
                    string path = Directory.GetFiles(Models + "/ES" + id, "*.glb").Single().Replace('\\', '/');
                    model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (!model || model.GetComponentsInChildren<Renderer>(true).Length == 0)
                        throw new InvalidDataException($"Could not load the authored mesh for ES{id} at {path}");
                }
                var definition = NativeEffectPublication.PublishNanoEffect(id, closure, model);
                if (!definition.Model && definition.Emitters.Length == 0 && definition.TrailLength == 0)
                    throw new InvalidDataException($"ES{id} has no published presentation");
                definitions.Add(id, definition);
            }
            foreach (var raw in WorldNanoRawEffectPublication.Publish()) definitions.Add(raw.Id, raw);
            if (definitions.Count != 90)
                throw new InvalidDataException($"Expected 90 recovered Nano effect definitions, found {definitions.Count}");
            var index = AssetDatabase.LoadAssetAtPath<WorldNanoEffectLibrary>(LibraryPath);
            if (!index)
            {
                index = ScriptableObject.CreateInstance<WorldNanoEffectLibrary>();
                AssetDatabase.CreateAsset(index, LibraryPath);
            }
            index.Effects = definitions.Values.ToArray();
            EditorUtility.SetDirty(index);
            foreach (var definition in definitions.Values) AssetDatabase.SaveAssetIfDirty(definition);
            AssetDatabase.SaveAssetIfDirty(index);
            Debug.Log($"Published {index.Effects.Length} authored world Nano animation effects.");
        }

        public static void BuildValidationPlayer()
        {
            Publish();
            NativeUiValidationBuild.BuildWithCurrentContent();
        }
    }
}
