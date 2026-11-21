using System;
using System.IO;
using System.Linq;
using FFOne.Content;
using FFOne.World;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    public static class WorldCellPublication
    {
        public static string[] Calculate(WorldCellDefinition definition)
        {
            string path = AssetDatabase.GUIDToAssetPath(definition.ImportedContentGuid);
            var accepted = AssetDatabase.LoadAssetAtPath<WorldTileContent>(path);
            if (!accepted || accepted.Id != definition.TileId)
                throw new InvalidDataException("Missing accepted source manifest for " + definition.TileId);
            string document = NativeData.Read("map/tiles/" + definition.TileId + "/behaviour");
            return WorldSourceBindings.FindUnpublishedModels(document, accepted).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        }

        public static void Prepare(WorldCellDefinition definition)
        {
            Apply(definition, Calculate(definition));
            WorldMotionPublication.Prepare(definition);
        }

        static void Apply(WorldCellDefinition definition, string[] ids)
        {
            if (definition.SourceBindingsVersion == 1 && definition.UnpublishedModelIds != null &&
                definition.UnpublishedModelIds.SequenceEqual(ids)) return;
            definition.UnpublishedModelIds = ids;
            definition.SourceBindingsVersion = 1;
            EditorUtility.SetDirty(definition);
        }

        [MenuItem("FFOne/World/Publish Cell Source Bindings")]
        public static void RebuildAll()
        {
            // Preflight every source before writing. Authored scenes are never opened or saved.
            var plans = AssetDatabase.FindAssets("t:WorldCellDefinition", new[] { "Assets/FFOne/Worlds" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<WorldCellDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
                .OrderBy(d => d.TileId, StringComparer.Ordinal)
                .Select(d => (Definition: d, Ids: Calculate(d))).ToArray();
            WorldMotionPublication.PrepareDirectory();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var plan in plans)
                {
                    Apply(plan.Definition, plan.Ids);
                    WorldMotionPublication.Prepare(plan.Definition);
                    AssetDatabase.SaveAssetIfDirty(plan.Definition);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            Debug.Log("CELL SOURCE BINDINGS PUBLISHED: " + plans.Length + " cells; " + plans.Sum(p => p.Ids.Length) + " missing IDs.");
        }
    }
}
