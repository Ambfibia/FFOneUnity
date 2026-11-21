using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Content;
using FFOne.Tutorial;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    /// <summary>Restores packed-mask programs on existing editable tutorial materials.</summary>
    public static class PublishedFusionMatterRepair
    {
        [MenuItem("FFOne/Tutorial/Repair Fusion Matter Materials")]
        public static void RepairMenu() => Debug.Log($"Tutorial Fusion Matter: {Repair()} materials repaired.");

        public static int Repair()
        {
            var tutorial = ContentAssets.Load<TutorialContent>("Tutorial/Content");
            var catalog = ContentAssets.Load<NativeModelCatalog>(NativeModelAssets.EditableRoot + "/Index");
            var changes = new Dictionary<Material, JToken>();
            foreach (var prefab in tutorial.Actors.Select(a => a.Model).Distinct())
            {
                string resource = ContentPaths.Resource(AssetDatabase.GetAssetPath(prefab));
                var binding = catalog.Bindings.Single(b => b.EditableResource == resource);
                string manifestPath = ContentPaths.Resolve(NativeAssetPublisher.ManifestRoot + "/" + binding.OriginalResource + "/publication.json");
                var manifest = JsonConvert.DeserializeObject<NativeAssetPublisher.Manifest>(File.ReadAllText(manifestPath));
                string source = AssetDatabase.GUIDToAssetPath(manifest.SourceGuid);
                if (string.IsNullOrEmpty(source)) throw new InvalidDataException("Missing material source: " + manifestPath);
                var definitions = NativeGlbDocument.Read(source).Json["materials"];
                foreach (var definition in definitions)
                {
                    var native = definition["extras"]?["ffone"];
                    if (!((string)native?["declaredShaderName"] ?? "").Contains("FusionMatterLightDir", StringComparison.Ordinal)) continue;
                    var entry = manifest.Assets.Single(e => e.Type == typeof(Material).FullName && e.Name == (string)definition["name"]);
                    var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(entry.Guid));
                    if (!material || material.shader.name != "FFOne/NativeToon" || !material.HasProperty("_FusionMatter"))
                        throw new InvalidDataException("Unexpected published Fusion Matter material: " + entry.Path);
                    changes[material] = native;
                }
            }
            // Complete the exact source/publication preflight before changing anything.
            int changed = 0;
            foreach (var change in changes)
            {
                var material = change.Key;
                bool dirty = material.GetFloat("_FusionMatter") != 1;
                material.SetFloat("_FusionMatter", 1);
                foreach (var color in change.Value["colors"])
                {
                    string name = (string)color["name"];
                    if (name is not ("_FusionTint" or "_BubblesTint" or "_ElectricityTint")) continue;
                    var values = color["value"];
                    var value = new Color((float)values[0], (float)values[1], (float)values[2], (float)values[3]);
                    dirty |= material.GetColor(name) != value;
                    material.SetColor(name, value);
                }
                if (!dirty) continue;
                EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material); changed++;
            }
            return changed;
        }
    }
}
