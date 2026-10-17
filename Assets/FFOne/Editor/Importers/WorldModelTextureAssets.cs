using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace FFOne.Editor
{
    // Texture2D sampler state is resource-wide. Separate resources preserve bindings which
    // share one published PNG but have different samplers, color spaces or exact mip chains.
    public static class WorldModelTextureAssets
    {
        public const string Output = "Assets/Generated/WorldModelTextures";
        const string Revision = "world-model-exact-texture-v1";
        public static int CreatedResources { get; private set; }
        public static long CreatedRgbaBytes { get; private set; }
        public static int PreparedResources => prepared.Count;
        static readonly HashSet<string> prepared = new HashSet<string>(StringComparer.Ordinal);
        static readonly Dictionary<string, PngInfo> sourceInfo = new Dictionary<string, PngInfo>(StringComparer.Ordinal);
        sealed class PngInfo { internal long Length, Stamp; internal string Sha; internal int Width, Height; }
        sealed class Level { internal string Path, PngSha, RgbaSha, SourceSha; internal int Width, Height; }
        sealed class Plan
        {
            internal Level[] Levels; internal bool Linear; internal FilterMode Filter;
            internal TextureWrapMode U, V; internal int Aniso; internal float Bias;
            internal string Key; internal long Bytes => Levels.Sum(l => (long)l.Width*l.Height*4);
        }
        public static bool IsWorld(string assetPath, JObject document)
        {
            string path = ContentPaths.Legacy(assetPath).Replace('\\','/');
            if (path.StartsWith(NativeContentImport.Destination + "/map/", StringComparison.Ordinal)) return true;
            return path.StartsWith(NativeContentImport.Destination + "/objects/", StringComparison.Ordinal)
                && (document["materials"] ?? new JArray()).Any(m => m["extras"]?["ffone"] == null && !string.IsNullOrEmpty((string)m["extras"]?["shaderName"]));
        }
        public static void Prepare(string assetPath, JObject document)
        {
            if (!IsWorld(assetPath, document)) return;
            EnsureFolder(Output);
            foreach (var material in document["materials"] ?? new JArray())
            {
                foreach (var binding in material["extras"]?["ffone"]?["textureBindings"] ?? new JArray())
                {
                    string uri = (string)binding["uri"];
                    if (!string.IsNullOrEmpty(uri)) Prepare(Build(assetPath, document, uri, binding, (string)binding["slot"] ?? "_MainTex"));
                }
                foreach (var slot in material["extras"]?["exactTextureSlots"] ?? new JArray())
                {
                    string identity = (string)slot["textureId"];
                    if (identity == null) continue;
                    var texture = document["textures"]?.SingleOrDefault(t => (string)t["name"] == identity)
                        ?? throw new InvalidDataException("Static texture identity absent: " + identity);
                    string uri = (string)document["images"][(int)texture["source"]]["uri"];
                    Prepare(Build(assetPath, document, uri, texture, (string)slot["name"] ?? "_MainTex"));
                }
            }
        }
        public static string ExpectedKey(string assetPath, JObject document, string uri, JToken binding = null, string slot = "_MainTex")
            => Build(assetPath, document, uri, binding, slot).Key;
        public static Texture2D Resolve(AssetImportContext context, JObject document, string uri, JToken binding = null, string slot = "_MainTex")
        {
            if (!IsWorld(context.assetPath, document))
            {
                string original = SafePath(context.assetPath, uri); context.DependsOnSourceAsset(ContentPaths.Resolve(original));
                return AssetDatabase.LoadAssetAtPath<Texture2D>(ContentPaths.Resolve(original)) ?? throw new InvalidDataException("Missing texture: " + original);
            }
            var plan = Build(context.assetPath, document, uri, binding, slot);
            foreach (var level in plan.Levels) context.DependsOnSourceAsset(ContentPaths.Resolve(level.Path));
            string path = Output + "/" + plan.Key + ".asset"; context.DependsOnSourceAsset(ContentPaths.Resolve(path));
            return AssetDatabase.LoadAssetAtPath<Texture2D>(ContentPaths.Resolve(path))
                ?? throw new InvalidDataException("World texture was not prepared before GLB import: " + path + ". Run WorldModelTextureAssets.Prepare before ForceReimport.");
        }
        static void Prepare(Plan plan)
        {
            string path = Output + "/" + plan.Key + ".asset";
            if (prepared.Contains(path)) return;
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(ContentPaths.Resolve(path));
            if (existing)
            {
                if (existing.width != plan.Levels[0].Width || existing.height != plan.Levels[0].Height || existing.mipmapCount != plan.Levels.Length
                    || existing.filterMode != plan.Filter || existing.wrapModeU != plan.U || existing.wrapModeV != plan.V
                    || existing.anisoLevel != plan.Aniso || existing.mipMapBias != plan.Bias)
                    throw new InvalidDataException("Prepared world texture metadata differs from its content key: " + path);
                prepared.Add(path); return;
            }
            var texture = new Texture2D(plan.Levels[0].Width, plan.Levels[0].Height, TextureFormat.RGBA32, plan.Levels.Length, plan.Linear)
            { name = "Native world texture " + plan.Key, filterMode = plan.Filter, wrapModeU = plan.U, wrapModeV = plan.V, anisoLevel = plan.Aniso, mipMapBias = plan.Bias };
            try
            {
                for (int i = 0; i < plan.Levels.Length; i++) texture.SetPixels32(Pixels(plan.Levels[i]), i);
                texture.Apply(false, true); // Never synthesize levels or retain a second readable CPU copy.
                AssetDatabase.CreateAsset(texture, ContentPaths.Resolve(path));
                prepared.Add(path); CreatedResources++; CreatedRgbaBytes += plan.Bytes;
            }
            catch { UnityEngine.Object.DestroyImmediate(texture); throw; }
        }
        static Plan Build(string assetPath, JObject document, string uri, JToken binding, string slot)
        {
            if (string.IsNullOrEmpty(uri)) throw new InvalidDataException("World texture has no editable URI");
            var texture = Texture(document, uri, binding);
            var image = texture == null ? null : document["images"]?[(int)texture["source"]];
            var publishedLevels = binding?["mipLevels"] as JArray ?? texture?["extras"]?["ffone"]?["mipLevels"] as JArray
                ?? image?["extras"]?["ffone"]?["mipLevels"] as JArray;
            var levels = new List<Level>();
            if (publishedLevels == null || publishedLevels.Count == 0) levels.Add(ReadLevel(assetPath, uri, null));
            else
            {
                for (int i = 0; i < publishedLevels.Count; i++)
                {
                    var published = publishedLevels[i];
                    if ((int?)published["level"] != i) throw new InvalidDataException("Exact world mip levels are not contiguous: " + uri);
                    levels.Add(ReadLevel(assetPath, (string)published["uri"], published));
                }
                if (levels[0].Path != SafePath(assetPath, uri)) throw new InvalidDataException("World binding base URI differs from mip level zero: " + uri);
            }
            for (int i = 1; i < levels.Count; i++)
                if (levels[i].Width != Math.Max(1, levels[0].Width >> i) || levels[i].Height != Math.Max(1, levels[0].Height >> i))
                    throw new InvalidDataException("Authored world mip dimensions do not match the chain: " + levels[i].Path);
            string color = (string)binding?["colorSpace"];
            bool linear = color == null ? slot == "_BumpMap" || slot == "_NormalTex" : color == "linear";
            if (color != null && color != "linear" && color != "srgb") throw new InvalidDataException("Unsupported world texture color space: " + color);
            int samplerIndex = binding?["sampler"]?.Type == JTokenType.Integer ? (int)binding["sampler"]
                : binding?["sampler"]?["index"] != null ? (int)binding["sampler"]["index"] : (int?)texture?["sampler"] ?? -1;
            var sampler = samplerIndex >= 0 ? document["samplers"]?[samplerIndex] : null;
            var descriptor = binding?["sampler"]?.Type == JTokenType.Object ? binding["sampler"]["descriptor"] : null;
            descriptor = descriptor ?? sampler?["extras"]?["ffone"];
            int min = (int?)sampler?["minFilter"] ?? 9987, mag = (int?)sampler?["magFilter"] ?? 9729;
            int filter = (int?)descriptor?["legacyFilterMode"] ?? (min == 9987 ? 2 : mag == 9728 ? 0 : 1);
            if (filter < 0 || filter > 2) throw new InvalidDataException("Unsupported world filter mode: " + filter);
            var plan = new Plan { Levels = levels.ToArray(), Linear = linear, Filter = (FilterMode)filter,
                U = Wrap((string)descriptor?["wrapS"], (int?)sampler?["wrapS"] ?? 10497),
                V = Wrap((string)descriptor?["wrapT"], (int?)sampler?["wrapT"] ?? 10497),
                Aniso = (int?)descriptor?["anisotropyLevel"] ?? 1, Bias = (float?)descriptor?["mipMapBias"] ?? 0 };
            if (plan.Aniso < 0 || plan.Aniso > 16 || float.IsNaN(plan.Bias) || float.IsInfinity(plan.Bias)) throw new InvalidDataException("Invalid world anisotropy/mip bias");
            var key = new StringBuilder(Revision).Append('|').Append(linear ? "linear" : "srgb").Append('|').Append(filter)
                .Append('|').Append((int)plan.U).Append('|').Append((int)plan.V).Append('|').Append(plan.Aniso)
                .Append('|').Append(plan.Bias.ToString("R", CultureInfo.InvariantCulture));
            foreach (var level in plan.Levels) key.Append('|').Append(level.Width).Append('x').Append(level.Height).Append(':')
                .Append(level.PngSha).Append(':').Append(level.RgbaSha ?? "png-defined-rgba").Append(':').Append(level.SourceSha ?? "published-png");
            plan.Key = Hash(Encoding.UTF8.GetBytes(key.ToString())); return plan;
        }
        static JToken Texture(JObject document, string uri, JToken binding)
        {
            if (binding?["source"] != null) return binding; // Static exactTextureSlots selected a glTF texture object.
            if (binding?["texture"]?.Type == JTokenType.Integer) return document["textures"]?[(int)binding["texture"]];
            if (binding?["textureIndex"]?.Type == JTokenType.Integer) return document["textures"]?[(int)binding["textureIndex"]];
            var matches = (document["textures"] ?? new JArray()).Where(t => (string)document["images"]?[(int)t["source"]]?["uri"] == uri).ToArray();
            if (matches.Length > 1 && matches.Select(t => (int?)t["sampler"] ?? -1).Distinct().Count() > 1)
                throw new InvalidDataException("Texture URI has several samplers; an exact texture index is required: " + uri);
            return matches.FirstOrDefault();
        }
        static Level ReadLevel(string assetPath, string uri, JToken published)
        {
            string path = SafePath(assetPath, uri); var info = Info(path);
            if (published != null && ((int)published["width"] != info.Width || (int)published["height"] != info.Height
                || (string)published["pngSha256"] != info.Sha || (long?)published["pngByteLength"] != info.Length))
                throw new InvalidDataException("Authored world mip PNG failed dimensions/length/SHA256 validation: " + path);
            return new Level { Path = path, Width = info.Width, Height = info.Height, PngSha = info.Sha,
                RgbaSha = (string)published?["decodedRgba8Sha256"], SourceSha = (string)published?["sourceByteSha256"] };
        }
        static PngInfo Info(string path)
        {
            var file = new FileInfo(ContentPaths.FilePath(path));
            if (!file.Exists) throw new InvalidDataException("Missing world texture PNG: " + path);
            if (sourceInfo.TryGetValue(path, out var info) && info.Length == file.Length && info.Stamp == file.LastWriteTimeUtc.Ticks) return info;
            byte[] bytes = File.ReadAllBytes(ContentPaths.FilePath(path));
            if (bytes.Length < 24 || bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71) throw new InvalidDataException("World texture is not a PNG: " + path);
            int Big(int offset) => bytes[offset]<<24 | bytes[offset+1]<<16 | bytes[offset+2]<<8 | bytes[offset+3];
            info = new PngInfo { Length = file.Length, Stamp = file.LastWriteTimeUtc.Ticks, Sha = Hash(bytes), Width = Big(16), Height = Big(20) };
            sourceInfo[path] = info; return info;
        }
        static Color32[] Pixels(Level level)
        {
            var decoded = new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            try
            {
                if (!decoded.LoadImage(File.ReadAllBytes(ContentPaths.FilePath(level.Path))) || decoded.width != level.Width || decoded.height != level.Height)
                    throw new InvalidDataException("Cannot decode authored world mip PNG: " + level.Path);
                var pixels = decoded.GetPixels32();
                if (level.RgbaSha != null)
                {
                    var rgba = new byte[pixels.Length*4];
                    for (int i = 0; i < pixels.Length; i++) { rgba[i*4] = pixels[i].r; rgba[i*4+1] = pixels[i].g; rgba[i*4+2] = pixels[i].b; rgba[i*4+3] = pixels[i].a; }
                    if (Hash(rgba) != level.RgbaSha) throw new InvalidDataException("Decoded world mip RGBA SHA256 differs from the published source: " + level.Path);
                }
                return pixels; // LoadImage already gives Unity bottom-to-top rows; retain the importer's canonical UV (1-v).
            }
            finally { UnityEngine.Object.DestroyImmediate(decoded); }
        }
        static string SafePath(string assetPath, string uri)
        {
            if (string.IsNullOrEmpty(uri) || uri.StartsWith("data:",StringComparison.Ordinal) || Path.IsPathRooted(uri)) throw new InvalidDataException("World texture URI is not a relative PNG: " + uri);
            string full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ContentPaths.Legacy(assetPath)),uri));
            string root = Path.GetFullPath(NativeContentImport.Destination) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("World texture escaped imported root: " + uri);
            return Path.GetRelativePath(Directory.GetCurrentDirectory(),full).Replace('\\','/');
        }
        static TextureWrapMode Wrap(string value,int gltf) => value switch {
            "repeat" => TextureWrapMode.Repeat, "clampToEdge" => TextureWrapMode.Clamp, "mirroredRepeat" => TextureWrapMode.Mirror,
            null => gltf == 10497 ? TextureWrapMode.Repeat : gltf == 33071 ? TextureWrapMode.Clamp : gltf == 33648 ? TextureWrapMode.Mirror
                : throw new InvalidDataException("Unsupported glTF wrap: " + gltf),
            _ => throw new InvalidDataException("Unsupported native wrap: " + value)
        };
        static string Hash(byte[] bytes) { using var hash = SHA256.Create(); return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(); }
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(ContentPaths.Resolve(path))) return; string parent = Path.GetDirectoryName(path).Replace('\\','/'); EnsureFolder(parent);
            ContentPaths.CreateFolder(parent,Path.GetFileName(path));
        }
    }
}
