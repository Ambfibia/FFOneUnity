using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FFOne.Runtime.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FFOne.Editor
{
    // A presentation of the accepted native grid, not Unity Terrain's adaptive LOD.
    // Immutable derived assets are content addressed; existing GUIDs survive reimport.
    public static class WorldTerrainImport
    {
        public static string SharedFolder = "Assets/Generated/WorldTerrainShared";
        public static int TilesBuilt { get; private set; }
        public static long TerrainVertices { get; private set; }
        public static long GrassInstances { get; private set; }
        public static int GrassLayers { get; private set; }
        public static readonly List<string> Errors = new();
        const int GrassChunkSamples = 128, GrassChunkInstances = 8192;
        const string Revision = "native-grid-v1";

        public static void ResetCoverage()
        {
            TilesBuilt = GrassLayers = 0;
            TerrainVertices = GrassInstances = 0;
            Errors.Clear();
        }

        public static GameObject Build(string source, JObject scene, string outputFolder)
        {
            var instance = scene["nativeTerrain"];
            if (instance == null || instance.Type == JTokenType.Null) return null;
            string path = (string)instance["path"];
            GameObject root = null;
            try
            {
                EnsureFolder(outputFolder);
                EnsureFolder(SharedFolder);
                string folder = Path.GetDirectoryName(path)!.Replace('\\', '/');
                var definition = Read(source, path);
                int width = (int)definition["dimensions"]!["width"]!;
                int height = (int)definition["dimensions"]!["height"]!;
                if (width != height) throw new InvalidDataException("Non-square native heightmap: " + path);
                var samples = TutorialHeightmap.Read(File.ReadAllBytes(ContentPaths.FilePath(NativeContentImport.ResolveSafe(source,
                    Relative(folder, (string)definition["heightmap"]!["path"])))), width);
                var mesh = TerrainMesh(definition, samples, outputFolder);
                root = new GameObject((string)instance["trueName"] ?? "Accepted native terrain");
                ApplyComposedTransform(root.transform, instance);
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = root.AddComponent<MeshRenderer>();
                var material = TerrainMaterial(source, definition, instance, folder, outputFolder,
                    out var scales, out var sizes, out var tints, out var weights);
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ((int?)instance["terrainRenderContract"]?["serializedFields"]?["m_CastShadows"] ?? 1) != 0
                    ? ShadowCastingMode.On : ShadowCastingMode.Off;
                renderer.receiveShadows = false; // Authored lightmap contains the accepted terrain illumination.
                root.AddComponent<MeshCollider>().sharedMesh = mesh;
                var parameters = root.AddComponent<WorldTerrainParameters>();
                parameters.Configure(renderer, scales, sizes, tints, weights);
                string tileId = folder.Split('/').Reverse().Skip(1).First();
                // Once adopted, preserve painted TerrainData on subsequent source imports.
                if (!File.Exists(ContentPaths.FilePath(TerrainVegetationImport.DataPath(tileId))) ||
                    !TerrainVegetationImport.Attach(tileId, root, definition, instance, NativeContentImport.Destination, folder))
                    BuildGrass(source, definition, instance, folder, outputFolder, samples, root.transform);
                AddEnvironment(source, definition, folder, root);
                TerrainVertices += mesh.vertexCount;
                TilesBuilt++;
                return root;
            }
            catch (Exception error)
            {
                if (root) Object.DestroyImmediate(root);
                Errors.Add(path + ": " + error.Message);
                throw new InvalidDataException("Native terrain import failed for " + path + ": " + error.Message, error);
            }
        }

        static Mesh TerrainMesh(JObject definition, float[,] samples, string outputFolder)
        {
            int width = samples.GetLength(1), height = samples.GetLength(0);
            float sx = (float)definition["scale"]!["sampleSpacingX"], sz = (float)definition["scale"]!["sampleSpacingZ"];
            float sy = (float)definition["scale"]!["heightScale"];
            var positions = new Vector3[width * height];
            var uv = new Vector2[positions.Length];
            for (int row = 0; row < height; row++) for (int column = 0; column < width; column++)
            {
                int at = row * width + column;
                positions[at] = new Vector3(column * sx, samples[row, column] * sy, row * sz);
                uv[at] = new Vector2(column / (float)(width - 1), 1f - row / (float)(height - 1));
            }
            foreach (var shift in definition["heightmap"]?["vertexShifts"] ?? new JArray())
            {
                int flags = (int)shift["flags"], row = (int)shift["row"], column = (int)shift["column"];
                int dx = ((flags & 2) != 0 ? 1 : 0) - ((flags & 1) != 0 ? 1 : 0);
                int dz = ((flags & 8) != 0 ? 1 : 0) - ((flags & 4) != 0 ? 1 : 0);
                int at = row * width + column;
                positions[at] += new Vector3(dx * sx, 0, dz * sz);
                uv[at] += new Vector2(dx / (2f * (width - 1)), -dz / (2f * (height - 1)));
            }
            var indices = new int[(width - 1) * (height - 1) * 6];
            int index = 0;
            for (int row = 0; row < height - 1; row++) for (int column = 0; column < width - 1; column++)
            {
                int a = row * width + column, b = a + 1, c = a + width, d = c + 1;
                // Reflect native -X into Unity +X and reverse every original triangle.
                indices[index++] = a; indices[index++] = c; indices[index++] = b;
                indices[index++] = b; indices[index++] = c; indices[index++] = d;
            }
            var mesh = new Mesh { name = (string)definition["trueName"], indexFormat = IndexFormat.UInt32 };
            mesh.vertices = positions;
            mesh.uv = uv;
            mesh.triangles = indices;
            var normals = new Vector3[positions.Length];
            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                Vector3 face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                if (face.sqrMagnitude <= 1.1920929e-7f) continue;
                normals[a] += face; normals[b] += face; normals[c] += face;
            }
            for (int i = 0; i < normals.Length; i++) normals[i] = normals[i].normalized;
            mesh.normals = normals;
            var surfaceToLight = new Vector3(-.89370085f, .30901706f, .32528046f);
            mesh.colors = normals.Select(n => {
                float channel = Mathf.Clamp01(.2f + Mathf.Max(Vector3.Dot(n, surfaceToLight), 0));
                return new Color(channel, channel, channel, 1);
            }).ToArray();
            mesh.RecalculateBounds();
            return SaveMesh(mesh, outputFolder);
        }

        static Material TerrainMaterial(string source, JObject definition, JToken instance, string folder, string outputFolder,
            out Vector4[] scales, out Vector4[] sizes, out Vector4[] tints, out Vector4[] weights)
        {
            var layers = definition["splat"]!["layers"]!.ToArray();
            var maps = definition["splat"]!["weightMaps"]!.ToArray();
            if (layers.Length > 17 || maps.Length > 5) throw new InvalidDataException("Native terrain shader capacity exceeded.");
            var layerArray = TextureArray(source, layers.Select(l => l["albedo"]).ToArray(), folder, "Layers");
            var weightArray = TextureArray(source, maps, folder, "Weights");
            var lightmap = definition["lightmap"];
            Texture2D light = lightmap != null && (string)lightmap["status"] == "exported"
                ? ExactTexture(source, lightmap, folder, "Lightmap") : null;
            scales = new Vector4[17]; sizes = new Vector4[17]; tints = Enumerable.Repeat(Vector4.one, 17).ToArray();
            weights = new Vector4[5];
            float ex = (float)definition["scale"]!["extentX"], ez = (float)definition["scale"]!["extentZ"];
            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i]; var image = layer["albedo"];
                scales[i] = new Vector4(ex / (float)layer["tileSize"]!["x"], ez / (float)layer["tileSize"]!["y"], (float)layer["mode"], 0);
                sizes[i] = new Vector4((float)image["width"], (float)image["height"], Mips(image).Length - 1, (float?)image["sampler"]?["mipBias"] ?? 0);
                if (layer["editorTint"] is JArray tint) tints[i] = new Vector4((float)tint[0], (float)tint[1], (float)tint[2], 1);
                if ((int)layer["weight"]!["mapIndex"] != i / 4 || (int)layer["weight"]!["channelIndex"] != i % 4)
                    throw new InvalidDataException("Terrain layer quartet routing differs from native shader contract.");
            }
            for (int i = 0; i < maps.Length; i++) weights[i] = new Vector4(
                (float?)maps[i]["sampler"]?["filterMode"]?["serializedValue"] ?? 1,
                Mips(maps[i]).Length - 1, (float?)maps[i]["sampler"]?["mipBias"] ?? 0, (float)maps[i]["width"]);
            var fields = instance["terrainRenderContract"]?["serializedFields"];
            var metadata = new Vector4(layers.Length, (float?)fields?["m_RenderMode"] ?? 1, (float?)fields?["m_SplatMapDistance"] ?? 150, 0);
            string identity = JsonConvert.SerializeObject(new { Revision, layers = ContentPaths.Legacy(AssetDatabase.GetAssetPath(layerArray)),
                maps = ContentPaths.Legacy(AssetDatabase.GetAssetPath(weightArray)), light = light ? ContentPaths.Legacy(AssetDatabase.GetAssetPath(light)) : "white",
                scales = Vectors(scales), sizes = Vectors(sizes), tints = Vectors(tints), weights = Vectors(weights), metadata = Vectors(new[] { metadata }) });
            string path = SharedFolder + "/TerrainMaterial_" + Digest(identity) + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(ContentPaths.Resolve(path));
            if (material) return material;
            material = new Material(Shader.Find("FFOne/WorldTerrain") ?? throw new InvalidDataException("Missing FFOne/WorldTerrain shader."));
            material.name = "Accepted native terrain splat";
            material.SetTexture("_Layers", layerArray); material.SetTexture("_Weights", weightArray);
            material.SetTexture("_Lightmap", light ? light : Texture2D.whiteTexture);
            material.SetVector("_Metadata", metadata);
            material.SetFloat("_AnisotropicTaps", 4);
            AssetDatabase.CreateAsset(material, ContentPaths.Resolve(path));
            return material;
        }

        static Texture2DArray TextureArray(string source, JToken[] images, string folder, string role)
        {
            if (images.Length == 0) throw new InvalidDataException("Native terrain has no " + role + ".");
            string identity = TextureIdentity(source, images, folder, role);
            string path = SharedFolder + "/" + role + "_" + Digest(identity) + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2DArray>(ContentPaths.Resolve(path));
            if (existing) return existing;
            int width = images.Max(i => (int)i["width"]), height = images.Max(i => (int)i["height"]);
            int mipCount = images.Max(i => Mips(i).Length);
            var result = new Texture2DArray(width, height, images.Length, TextureFormat.RGBA32, mipCount, true)
                { name = "Accepted " + role, filterMode = FilterMode.Point, wrapMode = role == "Weights" ? TextureWrapMode.Clamp : TextureWrapMode.Repeat };
            for (int slice = 0; slice < images.Length; slice++)
            {
                var mips = Mips(images[slice]);
                for (int level = 0; level < mipCount; level++)
                {
                    int sourceLevel = Math.Min(level, mips.Length - 1);
                    var mip = mips[sourceLevel];
                    int w = Math.Max(width >> level, 1), h = Math.Max(height >> level, 1);
                    int sw = (int)mip["width"], sh = (int)mip["height"];
                    var pixels = Pixels(source, Relative(folder, (string)mip["path"]), sw, sh);
                    var padded = new Color32[w * h];
                    for (int y = 0; y < Math.Min(h, sh); y++)
                        Array.Copy(pixels, y * sw, padded, y * w, Math.Min(w, sw));
                    result.SetPixels32(padded, slice, level);
                }
            }
            result.Apply(false, true);
            AssetDatabase.CreateAsset(result, ContentPaths.Resolve(path));
            return result;
        }

        static Texture2D ExactTexture(string source, JToken image, string folder, string role)
        {
            string path = SharedFolder + "/" + role + "_" + Digest(TextureIdentity(source, new[] { image }, folder, role)) + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(ContentPaths.Resolve(path));
            if (existing) return existing;
            var mips = Mips(image);
            // Native lightmaps deliberately interpolate in gamma space before
            // the legacy compositor converts its final framebuffer to linear.
            bool srgb = role == "GrassTexture" && (string)image["sampler"]?["usageColorSpace"] != "linear";
            var texture = new Texture2D((int)image["width"], (int)image["height"], TextureFormat.RGBA32, mips.Length, !srgb)
                { name = "Accepted " + role, filterMode = Filter(image), wrapMode = Wrap(image), anisoLevel = (int?)image["sampler"]?["anisoLevel"] ?? 1,
                    mipMapBias = (float?)image["sampler"]?["mipBias"] ?? 0 };
            for (int level = 0; level < mips.Length; level++)
                texture.SetPixels32(Pixels(source, Relative(folder, (string)mips[level]["path"]), (int)mips[level]["width"], (int)mips[level]["height"]), level);
            texture.Apply(false, true);
            AssetDatabase.CreateAsset(texture, ContentPaths.Resolve(path));
            return texture;
        }

        // Unity's image decoder uses bottom-up rows; native textureLoad uses PNG row order.
        static Color32[] Pixels(string source, string path, int width, int height)
        {
            if (!AssetDatabase.LoadAssetAtPath<Texture2D>(ContentPaths.Resolve(NativeContentImport.Destination + "/" + path)))
                throw new InvalidDataException("Terrain image is absent from editable imported content: " + path);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(ContentPaths.FilePath(NativeContentImport.ResolveSafe(source, path)))) || texture.width != width || texture.height != height)
                    throw new InvalidDataException("Terrain image dimensions differ from descriptor: " + path);
                var decoded = texture.GetPixels32();
                var pixels = new Color32[decoded.Length];
                for (int row = 0; row < height; row++) Array.Copy(decoded, (height - row - 1) * width, pixels, row * width, width);
                return pixels;
            }
            finally { Object.DestroyImmediate(texture); }
        }

        static void BuildGrass(string source, JObject definition, JToken instance, string folder, string outputFolder, float[,] samples, Transform parent)
        {
            var detail = definition["detailAndTrees"];
            if (detail == null || (string)detail["status"] != "serializedAssetClosureExported") return;
            if (((int?)detail["trees"]?["instanceCount"] ?? 0) != 0)
                throw new InvalidDataException("Nonempty terrain tree prototypes require model admission.");
            var layers = new List<GrassLayer>();
            foreach (var prototype in detail["prototypes"] ?? new JArray())
            {
                var density = prototype["density"];
                long count = (long)density["densityTotal"];
                if (count == 0) continue;
                int width = (int)density["width"], height = (int)density["height"];
                var pixels = Pixels(source, Relative(folder, (string)density["path"]), width, height);
                var values = pixels.Select(p => p.r).ToArray();
                if (values.Sum(v => (long)v) != count) throw new InvalidDataException("Grass density total differs from accepted prototype.");
                var serialized = prototype["serialized"];
                if (((int?)serialized["usePrototypeMesh"] ?? 0) != 0) throw new InvalidDataException("Grass mesh prototype unsupported.");
                layers.Add(new GrassLayer { Prototype = prototype, Density = values, Width = width, Height = height });
                GrassInstances += count; GrassLayers++;
            }
            float distance = (float?)instance["terrainRenderContract"]?["serializedFields"]?["m_DetailObjectDistance"] ?? 80;
            foreach (var group in layers.GroupBy(l => (string)l.Prototype["prototypeTexture"]!["path"]))
            {
                var first = group.First();
                string docPath = Relative(folder, (string)first.Prototype["prototypeTexture"]!["documentPath"]);
                var textureDefinition = Read(source, docPath);
                var texture = ExactTexture(source, textureDefinition, folder, "GrassTexture");
                string materialPath = SharedFolder + "/GrassMaterial_" + Digest(ContentPaths.Legacy(AssetDatabase.GetAssetPath(texture))) + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(ContentPaths.Resolve(materialPath));
                if (!material)
                {
                    material = new Material(Shader.Find("FFOne/WorldTerrainGrass") ?? throw new InvalidDataException("Missing terrain grass shader."));
                    material.SetTexture("_MainTex", texture);
                    AssetDatabase.CreateAsset(material, ContentPaths.Resolve(materialPath));
                }
                foreach (var layer in group) if (layer.Width != first.Width || layer.Height != first.Height)
                    throw new InvalidDataException("Shared grass prototypes have different density dimensions.");
                for (int row = 0; row < first.Height; row += GrassChunkSamples)
                    for (int column = 0; column < first.Width; column += GrassChunkSamples)
                        GrassChunk(definition, samples, group, row, column, distance, material, parent, outputFolder);
            }
        }

        sealed class GrassLayer
        {
            public JToken Prototype;
            public byte[] Density;
            public int Width, Height;
        }

        static void GrassChunk(JObject definition, float[,] heights, IEnumerable<GrassLayer> layers, int rowStart, int columnStart,
            float distance, Material material, Transform parent, string outputFolder)
        {
            var positions = new List<Vector3>(); var uv = new List<Vector2>(); var colors = new List<Color>();
            var normals = new List<Vector3>(); var indices = new List<int>();
            float ex = (float)definition["scale"]!["extentX"], ez = (float)definition["scale"]!["extentZ"];
            float sy = (float)definition["scale"]!["heightScale"];
            void Finish()
            {
                if (positions.Count == 0) return;
                var mesh = new Mesh { name = "Accepted grass density chunk", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(positions); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetColors(colors); mesh.SetTriangles(indices, 0);
                mesh.RecalculateBounds(); mesh = SaveMesh(mesh, outputFolder);
                var go = new GameObject("Grass " + rowStart + ":" + columnStart);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                go.AddComponent<WorldTerrainGrassVisibility>().Distance = distance;
                positions.Clear(); normals.Clear(); uv.Clear(); colors.Clear(); indices.Clear();
            }
            foreach (var layer in layers)
            {
                var s = layer.Prototype["serialized"];
                var healthy = ColorValue(s["healthyColor"]); var dry = ColorValue(s["dryColor"]);
                int rowEnd = Math.Min(rowStart + GrassChunkSamples, layer.Height), columnEnd = Math.Min(columnStart + GrassChunkSamples, layer.Width);
                for (int row = rowStart; row < rowEnd; row++) for (int column = columnStart; column < columnEnd; column++)
                {
                    int sample = row * layer.Width + column;
                    for (uint ordinal = 0; ordinal < layer.Density[sample]; ordinal++)
                    {
                        if (positions.Count / 8 == GrassChunkInstances) Finish();
                        uint seed = GrassHash((uint)sample, ordinal);
                        float x = (column + (seed & 0xffff) / 65536f) / layer.Width * ex;
                        float z = (row + ((seed >> 16) & 0xffff) / 65536f) / layer.Height * ez;
                        float t = GrassHash(seed, 0x9e3779b9) / (float)uint.MaxValue;
                        float width = (float)s["minWidth"] + ((float)s["maxWidth"] - (float)s["minWidth"]) * t;
                        float height = (float)s["minHeight"] + ((float)s["maxHeight"] - (float)s["minHeight"]) * (1 - t);
                        var center = new Vector3(x, TriangleHeight(heights, x / ex, z / ez) * sy, z);
                        var color = Color.LerpUnclamped(healthy, dry, t);
                        foreach (var direction in new[] { Vector3.left, Vector3.forward })
                        {
                            int at = positions.Count; var side = direction * (width * .5f);
                            positions.Add(center - side); positions.Add(center + side);
                            positions.Add(center + side + Vector3.up * height); positions.Add(center - side + Vector3.up * height);
                            Vector3 normal = -Vector3.Cross(direction, Vector3.up).normalized;
                            normals.AddRange(new[] { normal, normal, normal, normal }); colors.AddRange(new[] { color, color, color, color });
                            uv.AddRange(new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) });
                            indices.AddRange(new[] { at, at + 2, at + 1, at, at + 3, at + 2 });
                        }
                    }
                }
            }
            Finish();
        }

        static float TriangleHeight(float[,] samples, float u, float v)
        {
            int width = samples.GetLength(1), height = samples.GetLength(0);
            float x = Mathf.Clamp(u * (width - 1), 0, width - 1), z = Mathf.Clamp(v * (height - 1), 0, height - 1);
            int column = Math.Min((int)x, width - 2), row = Math.Min((int)z, height - 2);
            float a = x - column, b = z - row;
            float h00 = samples[row, column], h10 = samples[row, column + 1], h01 = samples[row + 1, column], h11 = samples[row + 1, column + 1];
            return a + b <= 1 ? h00 + (h10 - h00) * a + (h01 - h00) * b : h11 + (h01 - h11) * (1 - a) + (h10 - h11) * (1 - b);
        }

        static uint GrassHash(uint x, uint salt)
        {
            unchecked { x ^= salt + 0x9e3779b9 + (x << 6) + (x >> 2); x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; return x ^ (x >> 16); }
        }

        static void AddEnvironment(string source, JObject definition, string folder, GameObject root)
        {
            var reference = definition["environment"];
            if (reference == null) return;
            var ambience = Read(source, Relative(folder, (string)reference["path"]))["ambience"];
            var environment = root.AddComponent<WorldTerrainEnvironment>();
            environment.Grid = new Vector2Int((int)ambience["gridCoordinates"]![0], (int)ambience["gridCoordinates"]![1]);
            environment.FogDepth = (float)ambience["fogDepth"];
            environment.FogColor = ArrayColor(ambience["fogColor"]);
            environment.SkyColor = ArrayColor(ambience["skyColor"]);
            environment.LightColor = ArrayColor(ambience["lightColor"]);
        }

        static Mesh SaveMesh(Mesh mesh, string outputFolder)
        {
            // Compare all actual fields after a digest lookup, preserving full mesh equality.
            using var data = new MemoryStream();
            using (var writer = new BinaryWriter(data, Encoding.UTF8, true))
            {
                writer.Write(Revision);
                foreach (var field in new[] { mesh.vertices, mesh.normals })
                {
                    writer.Write(field.Length);
                    foreach (var v in field) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
                }
                var uv = mesh.uv; writer.Write(uv.Length); foreach (var v in uv) { writer.Write(v.x); writer.Write(v.y); }
                var colors = mesh.colors; writer.Write(colors.Length);
                foreach (var c in colors) { writer.Write(c.r); writer.Write(c.g); writer.Write(c.b); writer.Write(c.a); }
                var indices = mesh.triangles; writer.Write(indices.Length); foreach (int i in indices) writer.Write(i);
            }
            string path = SharedFolder + "/Mesh_" + Digest(data.ToArray()) + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(ContentPaths.Resolve(path));
            if (existing)
            {
                if (!existing.vertices.SequenceEqual(mesh.vertices) || !existing.normals.SequenceEqual(mesh.normals) ||
                    !existing.uv.SequenceEqual(mesh.uv) || !existing.colors.SequenceEqual(mesh.colors) || !existing.triangles.SequenceEqual(mesh.triangles))
                    throw new InvalidDataException("Native terrain mesh digest collision.");
                Object.DestroyImmediate(mesh); return existing;
            }
            AssetDatabase.CreateAsset(mesh, ContentPaths.Resolve(path)); return mesh;
        }

        static string TextureIdentity(string source, IEnumerable<JToken> images, string folder, string role)
        {
            // Actual source-byte SHA256 plus all mip dimensions/state form the equality key;
            // source provenance/path never duplicates a byte-identical native texture.
            return JsonConvert.SerializeObject(new { Revision, role, encoding = role == "GrassTexture" ? "hardware-srgb" : "native-gamma-rgba8",
                images = images.Select(i => new {
                width = (int)i["width"], height = (int)i["height"], colorSpace = (string)i["colorSpace"], sampler = i["sampler"],
                mips = Mips(i).Select(m => new { level = (int?)m["level"] ?? 0, width = (int)m["width"], height = (int)m["height"],
                    bytes = Digest(File.ReadAllBytes(ContentPaths.FilePath(NativeContentImport.ResolveSafe(source, Relative(folder, (string)m["path"]))))) }) }) });
        }

        static JToken[] Mips(JToken image) => image["mips"] is JArray values && values.Count > 0 ? values.ToArray() : new[] { image };
        static FilterMode Filter(JToken image) => (FilterMode)((int?)image["sampler"]?["filterMode"]?["serializedValue"] ?? 1);
        static TextureWrapMode Wrap(JToken image) => ((int?)image["sampler"]?["wrapMode"]?["serializedValue"] ?? 1) == 0 ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        static Color ColorValue(JToken c) => new((float)c["r"], (float)c["g"], (float)c["b"], (float)c["a"]);
        static Color ArrayColor(JToken c) => new((float)c[0], (float)c[1], (float)c[2], (float)c[3]);
        static JObject Read(string source, string path) => JObject.Parse(File.ReadAllText(ContentPaths.FilePath(NativeContentImport.ResolveSafe(source, path))));
        static string Relative(string folder, string path) => path.StartsWith("map/", StringComparison.Ordinal) || path.StartsWith("world/", StringComparison.Ordinal)
            ? path : (folder + "/" + path).Replace('\\', '/');
        static string Digest(string value) => Digest(Encoding.UTF8.GetBytes(value));
        static IEnumerable<float[]> Vectors(IEnumerable<Vector4> vectors) => vectors.Select(v => new[] { v.x, v.y, v.z, v.w });
        static string Digest(byte[] bytes) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }

        static void ApplyComposedTransform(Transform transform, JToken instance)
        {
            Matrix4x4 result = Matrix(instance["transform"]);
            foreach (var node in instance["rootChain"]?["nodes"] ?? new JArray()) result = Matrix(node["nativeLocalTransform"]) * result;
            transform.localPosition = result.GetColumn(3);
            transform.localRotation = result.rotation;
            transform.localScale = result.lossyScale;
        }
        static Matrix4x4 Matrix(JToken value)
        {
            var p = value["translation"]; var q = value["rotation"]; var s = value["scale"];
            return Matrix4x4.TRS(new Vector3(-(float)p[0], (float)p[1], (float)p[2]),
                new Quaternion((float)q[0], -(float)q[1], -(float)q[2], (float)q[3]), new Vector3((float)s[0], (float)s[1], (float)s[2]));
        }
        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(ContentPaths.Resolve(folder))) return;
            string parent = Path.GetDirectoryName(folder)!.Replace('\\', '/'); EnsureFolder(parent);
            ContentPaths.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
