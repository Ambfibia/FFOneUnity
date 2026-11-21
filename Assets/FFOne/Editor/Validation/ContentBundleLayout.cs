using System;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace FFOne.Editor
{
    /// <summary>Local bundle boundaries follow consumption, independently of public addresses.</summary>
    public static class ContentBundleLayout
    {
        public static string SpecializedGroup(string path)
        {
            var parts = path.Replace('\\', '/').Split('/');
            if (path.StartsWith("Assets/FFOne/Content/UI/Artwork/", StringComparison.Ordinal) && parts.Length >= 8)
            {
                string screen = parts[6];
                if ((screen == "character" || screen == "gameplay" || screen == "launcher") && parts.Length >= 9)
                    screen += "-" + parts[7];
                return "UI-Artwork-" + parts[5] + "-" + screen;
            }
            if (path.StartsWith("Assets/FFOne/Content/UI/Icons/", StringComparison.Ordinal) && parts.Length >= 7)
                return "UI-Icons-" + parts[5];
            if (path.StartsWith("Assets/FFOne/Content/Audio/Voice/", StringComparison.Ordinal) && parts.Length >= 8)
                return "Audio-Voice-" + parts[5] + "-" + Bucket(parts[6]);
            if (path.StartsWith("Assets/FFOne/Content/Characters/Humanoids/Equipment/", StringComparison.Ordinal) && parts.Length >= 9)
                return "Characters-Humanoids-Equipment-" + parts[6] + "-" + Bucket(parts[7]);
            if (path.StartsWith("Assets/FFOne/Content/Characters/Humanoids/Base/", StringComparison.Ordinal) && parts.Length >= 8)
                return "Characters-Humanoids-Base-" + parts[6];
            if (path.StartsWith("Assets/FFOne/Content/Environment/SourceLibrary/", StringComparison.Ordinal) && parts.Length >= 8)
                return "Environment-SourceLibrary-" + parts[5] + "-" + Bucket(parts[6]);
            if (path.StartsWith("Assets/FFOne/Data/Tables/characters/player/", StringComparison.Ordinal) && parts.Length >= 8)
                return "Data-Player-" + parts[6];
            // Creator tables are much larger than other gameplay definitions. Load each
            // document independently rather than pinning all creator data for one lookup.
            if (path.StartsWith("Assets/FFOne/Data/Tables/data/character_creation/", StringComparison.Ordinal))
                return "Data-CharacterCreation";
            return null;
        }

        // Stable across machines and editor sessions; all parts of an item/speaker stay
        // together, with at most sixteen bundles per category instead of one per file.
        static string Bucket(string owner)
        {
            uint hash = 2166136261;
            unchecked { foreach (char value in owner) hash = (hash ^ value) * 16777619; }
            return (hash & 15).ToString("x");
        }

        public static void Configure(AddressableAssetSettings settings, AddressableAssetGroup group)
        {
            var schema = group.GetSchema<BundledAssetGroupSchema>();
            // A stopped editor can leave a newly created group saved before its
            // schema references. Rebuilding must repair that partial publication.
            if (!schema) schema = group.AddSchema<BundledAssetGroupSchema>();
            if (!group.GetSchema<ContentUpdateGroupSchema>()) group.AddSchema<ContentUpdateGroupSchema>();
            schema.UseDefaultSchemaSettings = false;
            schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
            schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
            schema.BundleMode = group.Name == "Terrain-Materials" || group.Name == "Terrain-Textures" ||
                group.Name == "Audio-Music" || group.Name == "Audio-Ambience" || group.Name == "Data-CharacterCreation"
                ? BundledAssetGroupSchema.BundlePackingMode.PackSeparately
                : BundledAssetGroupSchema.BundlePackingMode.PackTogether;
            schema.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;
            schema.AssetLoadMode = AssetLoadMode.RequestedAssetAndDependencies;
            schema.InternalIdNamingMode = BundledAssetGroupSchema.AssetNamingMode.GUID;
            // Runtime uses explicit semantic addresses (including ffone/<guid>), with
            // no AssetReference, label or folder loads. Keep only those catalog keys.
            schema.IncludeAddressInCatalog = true;
            schema.IncludeGUIDInCatalog = false;
            schema.IncludeLabelsInCatalog = false;
            schema.IncludeFolderKeysInCatalog = false;
        }
    }
}
