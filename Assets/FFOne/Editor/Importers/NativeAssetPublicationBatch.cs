using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Tutorial;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    public static class NativeAssetPublicationBatch
    {
        public static string[] ResolveModels(IEnumerable<string> filesOrFolders)
        {
            if (filesOrFolders == null) throw new ArgumentNullException(nameof(filesOrFolders));
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in filesOrFolders)
            {
                string path = raw?.Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || path.Split('/').Any(p => p == ".." || p == "."))
                    throw new InvalidDataException("Choose imported GLB assets or an Assets subfolder.");
                if (AssetDatabase.IsValidFolder(ContentPaths.Resolve(path)))
                {
                    foreach (string guid in AssetDatabase.FindAssets("t:GameObject", ContentPaths.Resolve(new[] { path })))
                    {
                        string asset = ContentPaths.Legacy(AssetDatabase.GUIDToAssetPath(guid));
                        if (asset.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) paths.Add(asset);
                    }
                }
                else paths.Add(path);
                if (paths.Count > 512) throw new InvalidDataException("Selection exceeds 512 models. Choose smaller content folders.");
            }
            return paths.OrderBy(p => p, StringComparer.Ordinal).ToArray();
        }

        public static GameObject[] PublishFilesOrFolders(IEnumerable<string> filesOrFolders, string destination = NativeAssetPublisher.DefaultDestination)
            => NativeAssetPublisher.PublishBatch(ResolveModels(filesOrFolders), destination);

        [MenuItem("Assets/FFOne/Preflight Selected Native Models")]
        public static void PreflightSelection()
        {
            var plans = NativeAssetPublisher.PreflightBatch(ResolveModels(Selection.assetGUIDs.Select(AssetDatabase.GUIDToAssetPath)));
            Debug.Log($"FFOne publication preflight passed for {plans.Length} models; " +
                $"{plans.Sum(p => p.MeshCount)} meshes, {plans.Sum(p => p.MaterialCount)} materials, {plans.Sum(p => p.AnimationCount)} clips. No files written.");
        }

        [MenuItem("Assets/FFOne/Publish Selected Native Models")]
        public static void PublishSelection()
            => PublishFilesOrFolders(Selection.assetGUIDs.Select(AssetDatabase.GUIDToAssetPath));

        /// <summary>Contract-bounded set; excludes the open world's model inventory.</summary>
        public static string[] PlayerAndTutorialSources()
        {
            const string root = "Assets/Resources/";
            var rig = JObject.Parse(File.ReadAllText(ContentPaths.FilePath(root + "characters/player/shared/player_rig_contract.json")));
            var sources = new HashSet<string>(StringComparer.Ordinal);
            foreach (var gender in rig["genders"] ?? throw new InvalidDataException("Missing player rig genders."))
            {
                sources.Add(root + (string)gender["skeletonGlb"]);
                foreach (var part in gender["creatorParts"] ?? new JArray()) sources.Add(root + (string)part["glb"]);
            }
            var tutorial = AssetDatabase.LoadAssetAtPath<TutorialContent>(ContentPaths.Resolve(root + "Tutorial/Content.asset"));
            if (!tutorial) throw new InvalidDataException("Import tutorial content before publishing its actor models.");
            foreach (var actor in tutorial.Actors) sources.Add(ContentPaths.Legacy(AssetDatabase.GetAssetPath(actor.Model)));
            sources.Add(ContentPaths.Legacy(AssetDatabase.GetAssetPath(tutorial.ButtercupNano)));
            foreach (string effect in TutorialCinematicEffects.RequiredNativePaths().Where(p => p.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)))
                sources.Add(root + effect);
            sources.Add(root + "characters/player/items/weapon/rifle_electricgun/models/rifle_electricgun/model.glb");
            return sources.OrderBy(p => p, StringComparer.Ordinal).ToArray();
        }

        // Suitable for Unity -batchmode -executeMethod, as well as the editor menu.
        [MenuItem("FFOne/Content/Publish Player And Tutorial Models")]
        public static void PublishPlayerAndTutorial()
        {
            var plans = NativeAssetPublisher.PreflightBatch(PlayerAndTutorialSources());
            Debug.Log($"FFOne player/tutorial publication preflight: {plans.Length} models accepted before writing.");
            NativeAssetPublisher.PublishBatch(plans);
        }

        public static void InspectFirstModelReferences()
        {
            string path = PlayerAndTutorialSources().First();
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(path));
            foreach (var value in AssetDatabase.LoadAllAssetsAtPath(ContentPaths.Resolve(path)).Concat(root.GetComponentsInChildren<Component>(true)))
            {
                using var serialized = new SerializedObject(value);
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var reference = property.objectReferenceValue;
                    if (!reference || ContentPaths.Legacy(AssetDatabase.GetAssetPath(reference)) != path || reference is GameObject || reference is Component || reference is Mesh || reference is Material || reference is AnimationClip) continue;
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(reference, out string guid, out long id);
                    Debug.Log($"Native reference inspection: source={path}, owner={value.GetType().Name}/{value.name}, property={property.propertyPath}, target={reference.GetType().FullName}/{reference.name}, guid={guid}, localId={id}");
                }
            }
        }
    }
}
