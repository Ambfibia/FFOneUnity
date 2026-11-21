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
    /// <summary>Updates only the source fog flag on the actual editable character materials.</summary>
    public static class PublishedToonFogRepair
    {
        sealed class Change { internal Material Material; internal int Mode; }
        public static string LastSummary { get; private set; }

        [MenuItem("FFOne/Repair Tutorial And Avatar Material Fog")]
        public static void RepairMenu() => Debug.Log(RepairTutorialAndAvatarFog() + " materials updated. " + LastSummary);

        public static int RepairTutorialAndAvatarFog()
        {
            var changes = Plan(out int models);
            int changed = 0;
            // All source/publication identities and conflicts are checked before writing.
            foreach (var change in changes)
            {
                if (change.Material.GetFloat("_FixedFogMode") == change.Mode) continue;
                change.Material.SetFloat("_FixedFogMode", change.Mode);
                EditorUtility.SetDirty(change.Material); AssetDatabase.SaveAssetIfDirty(change.Material); changed++;
            }
            LastSummary = $"Verified {changes.Count} published toon materials across {models} tutorial and shared-avatar prefabs; changed only _FixedFogMode on {changed}.";
            return changed;
        }

        public static int ValidateTutorialAndAvatarFog()
        {
            var changes = Plan(out int models);
            var wrong = changes.Where(c => c.Material.GetFloat("_FixedFogMode") != c.Mode).ToArray();
            if (wrong.Length != 0) throw new InvalidDataException("Published toon fog requires repair: " + string.Join(", ", wrong.Select(c => AssetDatabase.GetAssetPath(c.Material))));
            LastSummary = $"Verified {changes.Count} published toon materials across {models} tutorial and shared-avatar prefabs.";
            return changes.Count;
        }

        static List<Change> Plan(out int modelCount)
        {
            var index = AssetDatabase.LoadAssetAtPath<NativeModelCatalog>(ContentPaths.Resolve(NativeModelPublicationIndex.Path));
            if (!index) throw new InvalidDataException("Native model publication index is missing.");
            var byOriginal = index.Bindings.ToDictionary(b => b.OriginalResource, StringComparer.Ordinal);
            var byEditable = index.Bindings.ToDictionary(b => b.EditableResource, StringComparer.Ordinal);
            var models = new Dictionary<string, string>(StringComparer.Ordinal);
            void AddSource(string resource)
            {
                if (string.IsNullOrEmpty(resource)) return;
                if (resource.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) resource = resource.Substring(0, resource.Length - 4);
                if (!byOriginal.TryGetValue(resource, out var binding)) throw new InvalidDataException("Shared avatar model has no publication binding: " + resource);
                string path = ContentPaths.ResourcePath(binding.EditableResource);
                if (string.IsNullOrEmpty(path)) throw new InvalidDataException("Published character prefab is absent: " + binding.EditableResource);
                models[resource] = path;
            }
            void AddTutorial(GameObject model)
            {
                if (!model) return;
                string path = AssetDatabase.GetAssetPath(model), resource = ContentPaths.Resource(path);
                if (path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) { AddSource(resource); return; }
                if (!byEditable.TryGetValue(resource, out var binding)) throw new InvalidDataException("Tutorial actor has no exact publication binding: " + path);
                AddSource(binding.OriginalResource);
            }
            string tutorialPath = ContentPaths.Resolve("Assets/Resources/Tutorial/Content.asset");
            var tutorial = AssetDatabase.LoadAssetAtPath<TutorialContent>(tutorialPath);
            if (!tutorial) throw new InvalidDataException("Tutorial content is missing: " + tutorialPath);
            foreach (var actor in tutorial.Actors) AddTutorial(actor.Model);
            AddTutorial(tutorial.ButtercupNano);
            var rig = JObject.Parse(File.ReadAllText(ContentPaths.FilePath("Assets/Resources/characters/player/shared/player_rig_contract.json")));
            foreach (var gender in rig["genders"])
            {
                AddSource((string)gender["skeletonGlb"]);
                foreach (var choice in gender["creatorChoices"]) AddSource((string)choice["glb"]);
            }

            var planned = new Dictionary<string, Change>(StringComparer.Ordinal);
            var selected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var model in models)
            {
                var dependencies = AssetDatabase.GetDependencies(model.Value, true).Where(p => p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                    .Select(p => AssetDatabase.LoadAssetAtPath<Material>(p)).Where(m => m && m.shader && m.shader.name == "FFOne/NativeToon").ToArray();
                if (dependencies.Length == 0) continue;
                var dependencyGuids = dependencies.ToDictionary(m => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m)), StringComparer.Ordinal);
                selected.UnionWith(dependencyGuids.Keys);
                string manifestPath = NativeAssetPublisher.ManifestRoot + "/" + model.Key + "/publication.json";
                var manifest = JsonConvert.DeserializeObject<NativeAssetPublisher.Manifest>(File.ReadAllText(ContentPaths.FilePath(manifestPath)));
                if (manifest == null || manifest.Version != 1 || manifest.Assets == null || manifest.PrefabGuid != AssetDatabase.AssetPathToGUID(model.Value))
                    throw new InvalidDataException("Character publication identity differs: " + manifestPath);
                string sourcePath = AssetDatabase.GUIDToAssetPath(manifest.SourceGuid);
                if (string.IsNullOrEmpty(sourcePath) || ContentPaths.Resolve(manifest.SourcePath) != sourcePath)
                    throw new InvalidDataException("Character source GUID/path differs: " + manifestPath);
                var json = NativeGlbDocument.Read(sourcePath).Json;
                var definitions = (json["materials"] as JArray ?? new JArray()).Select((definition, i) => new {
                    Name = (string)definition["name"] ?? "material" + i, Definition = definition }).ToArray();
                var sourceMaterials = AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<Material>().ToDictionary(LocalId);
                foreach (var entry in manifest.Assets.Where(e => e.Type == typeof(Material).FullName && dependencyGuids.ContainsKey(e.Guid)))
                {
                    if (!sourceMaterials.TryGetValue(entry.SourceLocalId, out var original) || original.name != entry.Name)
                        throw new InvalidDataException("Published material source identity differs: " + entry.Path);
                    var matches = definitions.Where(d => d.Name == original.name).ToArray();
                    if (matches.Length != 1) throw new InvalidDataException("Source material metadata is missing or ambiguous: " + sourcePath + " " + original.name);
                    var definition = matches[0].Definition;
                    string shader = (string)definition["extras"]?["ffone"]?["declaredShaderName"] ?? (string)definition["extras"]?["shaderName"];
                    if (string.IsNullOrEmpty(shader)) throw new InvalidDataException("Source material has no declared shader: " + sourcePath + " " + original.name);
                    int mode = NativeWorldMaterialImport.FixedFogMode(definition);
                    if (planned.TryGetValue(entry.Guid, out var previous) && previous.Mode != mode)
                        throw new InvalidDataException("Shared published material has conflicting source fog modes: " + entry.Path);
                    planned[entry.Guid] = new Change { Material = dependencyGuids[entry.Guid], Mode = mode };
                }
            }
            var unknown = selected.Where(g => !planned.ContainsKey(g)).ToArray();
            if (unknown.Length != 0) throw new InvalidDataException("Selected toon materials have no exact publication evidence: " + string.Join(", ", unknown.Select(AssetDatabase.GUIDToAssetPath)));
            modelCount = models.Count;
            return planned.Values.ToList();
        }

        static long LocalId(UnityEngine.Object value)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string _, out long id)) throw new InvalidDataException("Source material has no Unity identity: " + value.name);
            return id;
        }
    }
}
