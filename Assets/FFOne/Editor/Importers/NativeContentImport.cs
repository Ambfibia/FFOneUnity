using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    public static class NativeContentImport
    {
        public const string Destination = "Assets/Resources";
        public static string DefaultSource => Path.GetFullPath(Path.Combine(Application.dataPath, "../../FFOneClient/assets/game"));
        static readonly string[] Folders = {
            "ui/en/launcher/login", "ui/en/server-selection", "ui/en/character/selection",
            "ui/ru/character/selection", "ui/en/character/creation", "fonts"
        };
        static readonly string[] Files = {
            "localization/en.json", "localization/ru.json", "localization/catalog.json",
            "data/character_creation/appearance.json", "data/character_creation/avatar_items.json",
            "data/character_creation/customization.json", "data/character_creation/runtime_textures.json",
            "data/character_creation/name_wheel.json",
            "characters/player/shared/player_rig_contract.json",
            "ui/en/option/radio-empty.png", "ui/en/option/radio-checked.png",
            "ui/en/user-equip/scroll-track.png", "ui/en/user-equip/scroll-thumb.png",
            "ui/en/gameplay/player/DiskBack.png", "ui/en/gameplay/player/DiskFront.png",
            "audio/music/00_main_theme.ogg"
        };
        public static string ResolveSafe(string root, string relative)
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var result = Path.GetFullPath(Path.Combine(root, relative));
            if (!result.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Asset reference escapes its root: " + relative);
            return result;
        }
        public static List<string> Plan(string source)
        {
            if (!File.Exists(ContentPaths.FilePath(ResolveSafe(source, "data/character_creation/appearance.json"))))
                throw new DirectoryNotFoundException("Choose FFOneClient/assets/game.");
            var files = new HashSet<string>(Files, StringComparer.Ordinal);
            foreach (var folder in Folders)
                foreach (var path in ContentPaths.GetFiles(ResolveSafe(source, folder), "*", SearchOption.AllDirectories))
                    if (!path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                        files.Add(Path.GetRelativePath(source, path).Replace('\\', '/'));
            var rig = Read(source, "characters/player/shared/player_rig_contract.json");
            var avatar = Read(source, "data/character_creation/avatar_items.json");
            var textures = Read(source, "data/character_creation/runtime_textures.json");
            var customization = Read(source, "data/character_creation/customization.json");
            foreach (var texture in customization["textures"] ?? new JArray()) files.Add((string)texture["image"]!["path"]);
            var appearance = Read(source, "data/character_creation/appearance.json");
            foreach (var choice in appearance["choices"]!)
                foreach (var icon in (choice["icon"] as JObject)?["candidates"] ?? new JArray()) files.Add((string)icon["path"]);
            var neededNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var gender in rig["genders"]!)
            {
                files.Add((string)gender["skeletonGlb"]);
                string genderName = (string)gender["gender"];
                foreach (var part in gender["creatorParts"]!) files.Add((string)part["glb"]);
                foreach (var choice in gender["creatorChoices"]!)
                {
                    var category = (string)choice["appearanceCategory"];
                    var itemCategory = category == "hair" ? "head" : category;
                    var item = avatar["items"]!.FirstOrDefault(x => (string)x["category"] == itemCategory && (int)x["itemNumber"] == (int)choice["itemNumber"]);
                    if (item == null) throw new InvalidDataException("Creator item is absent: " + itemCategory + "/" + choice["itemNumber"]);
                    var visual = item[genderName];
                    foreach (var kind in new[] { "primaryTexture", "secondaryTexture" })
                    {
                        var texture = visual?[kind];
                        if (texture == null || texture.Type == JTokenType.Null) continue;
                        var name = (string)texture["trueName"];
                        if (!string.IsNullOrEmpty(name))
                        {
                            neededNames.Add(name);
                            if (category == "face") foreach (var suffix in "abcdefghij") neededNames.Add(name + "_" + suffix);
                            if (category == "hair") neededNames.Add(name + "_a");
                        }
                        foreach (var candidate in texture["candidates"] ?? new JArray()) files.Add((string)candidate["path"]);
                    }
                }
                neededNames.Add(genderName == "male" ? "m_skin" : "f_skin");
            }
            foreach (var texture in textures["textures"]!)
                if (neededNames.Contains((string)texture["trueName"])) files.Add((string)texture["nativeAsset"]!["path"]);
            foreach (var glb in files.Where(p => p.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                var json = NativeGlbDocument.Read(ResolveSafe(source, glb)).Json;
                foreach (var uri in json.Descendants().OfType<JProperty>().Where(p => p.Name == "uri").Select(p => (string)p.Value).Distinct())
                {
                    if (string.IsNullOrEmpty(uri) || uri.StartsWith("data:", StringComparison.Ordinal)) continue;
                    var full = ResolveSafe(source, Path.Combine(Path.GetDirectoryName(glb)!, uri));
                    files.Add(Path.GetRelativePath(source, full).Replace('\\', '/'));
                }
            }
            return files.OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
        public static string Preflight(string source, IReadOnlyList<string> files)
        {
            int fresh = 0, equal = 0, authored = 0;
            foreach (var relative in files)
            {
                var from = ResolveSafe(source, relative);
                if (!File.Exists(ContentPaths.FilePath(from))) throw new FileNotFoundException("Missing native dependency", relative);
                var to = ResolveSafe(Destination, relative);
                if (!File.Exists(ContentPaths.FilePath(to))) { fresh++; continue; }
                if (!Hash(from).SequenceEqual(Hash(to)))
                {
                    if (EnvironmentGlbBasis.IsAuthoringOverride(relative, to)) { authored++; continue; }
                    throw new IOException("Local edit/import conflict; no files changed: " + relative);
                }
                equal++;
            }
            return $"{files.Count} dependencies: {fresh} new, {equal} identical, {authored} authored GLBs preserved. Source is read-only.";
        }
        public static void ImportFirstSlice() => Import(DefaultSource);
        public static void Import(string source)
        {
            var files = Plan(source);
            var result = Preflight(source, files); // Validate the entire write set before creating any file.
            var created = new List<string>();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var relative in files)
                {
                    var to = ResolveSafe(Destination, relative);
                    if (File.Exists(ContentPaths.FilePath(to))) continue;
                    Directory.CreateDirectory(ContentPaths.FilePath(Path.GetDirectoryName(to)!));
                    File.Copy(ContentPaths.FilePath(ResolveSafe(source, relative)), ContentPaths.FilePath(to), false);
                    created.Add(to);
                }
            }
            catch
            {
                foreach (var path in created) File.Delete(ContentPaths.FilePath(path));
                throw;
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh();
            Debug.Log("FFOne native content: " + result);
        }
        static JObject Read(string root, string path) => JObject.Parse(File.ReadAllText(ContentPaths.FilePath(ResolveSafe(root, path))));
        static byte[] Hash(string path) { using var sha = SHA256.Create(); using var stream = File.OpenRead(ContentPaths.FilePath(path)); return sha.ComputeHash(stream); }
    }

    public sealed class NativeContentImportWindow : EditorWindow
    {
        string source, status = "Import a bounded creator wardrobe, original selection art, fonts, and EN/RU text.";
        [MenuItem("FFOne/Content Import")]
        static void Open() => GetWindow<NativeContentImportWindow>("FFOne Content");
        void OnEnable() => source = EditorPrefs.GetString("FFOne.NativeSource", NativeContentImport.DefaultSource);
        void OnGUI()
        {
            EditorGUILayout.LabelField("Native → Unity first slice", EditorStyles.boldLabel);
            source = EditorGUILayout.TextField("Native asset root", source);
            if (GUILayout.Button("Choose assets/game folder"))
            {
                var selected = EditorUtility.OpenFolderPanel("Native asset root", source, "");
                if (!string.IsNullOrEmpty(selected)) source = selected;
            }
            EditorGUILayout.HelpBox(status, MessageType.Info);
            if (GUILayout.Button("Check dependencies and local edits")) Run(false);
            if (GUILayout.Button("Import missing assets")) Run(true);
            EditorGUILayout.LabelField("Different existing files are conflicts. Repeating an unchanged import is safe.", EditorStyles.wordWrappedLabel);
        }
        void Run(bool import)
        {
            try
            {
                EditorPrefs.SetString("FFOne.NativeSource", source);
                if (import) NativeContentImport.Import(source);
                status = NativeContentImport.Preflight(source, NativeContentImport.Plan(source));
            }
            catch (Exception exception) { status = exception.Message; Debug.LogException(exception); }
        }
    }

    public sealed class NativeTextureImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            string assetPath = ContentPaths.Legacy(this.assetPath);
            if (!assetPath.StartsWith(NativeContentImport.Destination + "/", StringComparison.Ordinal)) return;
            if (assetPath.StartsWith("Assets/Resources/objects/", StringComparison.Ordinal) ||
                assetPath.StartsWith("Assets/Resources/map/", StringComparison.Ordinal)) return;
            var texture = (TextureImporter)assetImporter;
            if (!texture.importSettingsMissing) return;
            texture.textureShape = TextureImporterShape.Texture2D;
            bool ui = assetPath.Contains("/ui/");
            texture.textureType = ui ? TextureImporterType.Sprite : TextureImporterType.Default;
            if (ui)
            {
                texture.spriteImportMode = SpriteImportMode.Single;
                texture.spritePixelsPerUnit = 100;
                texture.spriteBorder = FFOne.UI.FFSpriteBorders.For(assetPath);
                var settings = new TextureImporterSettings();
                texture.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                texture.SetTextureSettings(settings);
            }
            texture.filterMode = FilterMode.Bilinear;
            texture.textureCompression = TextureImporterCompression.Uncompressed;
            texture.alphaIsTransparency = ui;
            texture.mipmapEnabled = !ui && !assetPath.Contains("toonramp");
            texture.sRGBTexture = !assetPath.Contains("toonramp");
            texture.wrapMode = ui || assetPath.Contains("toonramp") ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            texture.maxTextureSize = 4096;
        }
    }
}
