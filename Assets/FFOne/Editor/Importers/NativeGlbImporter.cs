using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace FFOne.Editor
{
    [ScriptedImporter(4, "glb")]
    public sealed class NativeGlbImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            var document = NativeGlbDocument.Read(context.assetPath);
            var json = document.Json;
            var root = new GameObject(Path.GetFileNameWithoutExtension(context.assetPath));
            try
            {
                var nodes = ((JArray)json["nodes"]).Select((n, i) => new GameObject((string)n["name"] ?? "node" + i).transform).ToArray();
                for (int i = 0; i < nodes.Length; i++)
                {
                    nodes[i].SetParent(root.transform, false);
                    var n = json["nodes"]![i]!;
                    if (n["matrix"] != null) throw new InvalidDataException("Matrix nodes need explicit decomposition support: " + nodes[i].name);
                    nodes[i].localPosition = Position(n["translation"], Vector3.zero);
                    nodes[i].localRotation = Rotation(n["rotation"]);
                    nodes[i].localScale = Vector(n["scale"], Vector3.one);
                }
                for (int i = 0; i < nodes.Length; i++)
                    foreach (var child in json["nodes"]![i]!["children"] ?? new JArray()) nodes[(int)child].SetParent(nodes[i], false);
                var materials = Materials(context, json);
                for (int i = 0; i < nodes.Length; i++)
                {
                    var n = json["nodes"]![i]!;
                    if (n["mesh"] == null) continue;
                    var gltfMesh = json["meshes"]![(int)n["mesh"]!]!;
                    int primitiveIndex = 0;
                    foreach (var primitive in gltfMesh["primitives"]!)
                    {
                        if (((int?)primitive["mode"] ?? 4) != 4 || primitive["targets"] != null)
                            throw new InvalidDataException("Only triangle primitives without morph targets are certified by this importer.");
                        var mesh = Mesh(document, primitive);
                        mesh.name = (string)gltfMesh["name"] ?? "mesh";
                        var target = new GameObject(mesh.name + "_" + primitiveIndex);
                        target.transform.SetParent(nodes[i], false);
                        Renderer renderer;
                        if (n["skin"] != null)
                        {
                            var skin = json["skins"]![(int)n["skin"]!]!;
                            var inverse = document.Values((int)skin["inverseBindMatrices"]!);
                            mesh.bindposes = inverse.Select(Matrix).ToArray();
                            var skinned = target.AddComponent<SkinnedMeshRenderer>();
                            skinned.sharedMesh = mesh;
                            skinned.bones = skin["joints"]!.Select(x => nodes[(int)x]).ToArray();
                            skinned.rootBone = skin["skeleton"] != null ? nodes[(int)skin["skeleton"]!] : skinned.bones[0];
                            skinned.updateWhenOffscreen = true;
                            renderer = skinned;
                        }
                        else
                        {
                            target.AddComponent<MeshFilter>().sharedMesh = mesh;
                            renderer = target.AddComponent<MeshRenderer>();
                        }
                        if (primitive["material"] != null)
                        {
                            renderer.sharedMaterial = materials[(int)primitive["material"]!];
                            // ColorMaterial AmbientAndDiffuse has a white primary color when
                            // the original mesh omits its color stream (including ES739).
                            if (renderer.sharedMaterial.HasProperty("_VertexColorAD") && renderer.sharedMaterial.GetFloat("_VertexColorAD") > .5f && primitive["attributes"]?["COLOR_0"] == null)
                                mesh.colors = Enumerable.Repeat(Color.white, mesh.vertexCount).ToArray();
                        }
                        context.AddObjectToAsset($"mesh_{i}_{primitiveIndex++}", mesh);
                    }
                }
                AddAnimations(document, root, nodes, context);
                if (EnvironmentGlbBasis.IsNormalized(json)) root.AddComponent<FFOne.World.WorldModelBasis>();
                context.AddObjectToAsset("root", root);
                context.SetMainObject(root);
            }
            catch { DestroyImmediate(root); throw; }
        }
        static UnityEngine.Mesh Mesh(NativeGlbDocument document, JToken primitive)
        {
            var attributes = primitive["attributes"]!;
            var positions = document.Values((int)attributes["POSITION"]!);
            var mesh = new UnityEngine.Mesh { indexFormat = positions.Length > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = positions.Select(v => new Vector3(-v[0], v[1], v[2])).ToArray();
            if (attributes["NORMAL"] != null) mesh.normals = document.Values((int)attributes["NORMAL"]!).Select(v => new Vector3(-v[0], v[1], v[2])).ToArray();
            if (attributes["TEXCOORD_0"] != null) mesh.uv = document.Values((int)attributes["TEXCOORD_0"]!).Select(v => new Vector2(v[0], 1-v[1])).ToArray();
            if (attributes["COLOR_0"] != null) mesh.colors = VertexColors(document, (int)attributes["COLOR_0"]!, positions.Length);
            if (attributes["TANGENT"] != null) mesh.tangents = document.Values((int)attributes["TANGENT"]!).Select(v => new Vector4(-v[0], v[1], v[2], -v[3])).ToArray();
            var indices = primitive["indices"] != null ? document.Values((int)primitive["indices"]!).Select(v => checked((int)v[0])).ToArray() : Enumerable.Range(0, positions.Length).ToArray();
            if (indices.Length % 3 != 0) throw new InvalidDataException("Invalid triangle index count");
            for (int i = 0; i < indices.Length; i += 3) (indices[i+1], indices[i+2]) = (indices[i+2], indices[i+1]);
            mesh.triangles = indices;
            if (attributes["JOINTS_0"] != null)
            {
                if (attributes["JOINTS_1"] != null) throw new InvalidDataException("More than four skin weights are not supported.");
                var joints = document.Values((int)attributes["JOINTS_0"]!);
                var weights = document.Values((int)attributes["WEIGHTS_0"]!);
                mesh.boneWeights = joints.Select((j, i) => new BoneWeight {
                    boneIndex0 = (int)j[0], boneIndex1 = (int)j[1], boneIndex2 = (int)j[2], boneIndex3 = (int)j[3],
                    weight0 = weights[i][0], weight1 = weights[i][1], weight2 = weights[i][2], weight3 = weights[i][3]
                }).ToArray();
            }
            if (attributes["NORMAL"] == null) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
        public static Color[] VertexColors(NativeGlbDocument document, int accessor, int vertexCount)
        {
            var definition = document.Json["accessors"]![accessor]!;
            string type = (string)definition["type"];
            int component = (int)definition["componentType"]!;
            bool normalized = (bool?)definition["normalized"] ?? false;
            if ((type != "VEC3" && type != "VEC4") || (int)definition["count"]! != vertexCount ||
                (component != 5126 && !(normalized && (component == 5121 || component == 5123))))
                throw new InvalidDataException("COLOR_0 requires matching RGB/RGBA float or normalized unsigned byte/short values.");
            // Values preserves encoded color data and handles byteStride/offsets and integer
            // normalization. Colors are not positions: no handedness or gamma conversion.
            return document.Values(accessor).Select(value => new Color(value[0], value[1], value[2], type == "VEC4" ? value[3] : 1)).ToArray();
        }
        static Material[] Materials(AssetImportContext context, JObject json)
        {
            var definitions = json["materials"] as JArray ?? new JArray();
            var shader = Shader.Find("FFOne/NativeToon");
            if (definitions.Count > 0 && shader == null) throw new InvalidDataException("FFOne/NativeToon URP shader is missing.");
            return definitions.Select((definition, index) => {
                var material = new Material(shader) { name = (string)definition["name"] ?? "material" + index };
                var native = definition["extras"]?["ffone"];
                string shaderName = (string)native?["declaredShaderName"] ?? "";
                material.SetFloat("_VertexColorAD", shaderName.StartsWith("normal_", StringComparison.Ordinal) &&
                    shaderName.EndsWith("_vertexColorAD", StringComparison.Ordinal) ? 1 : 0);
                bool toon = shaderName.Contains("ToonShading", StringComparison.Ordinal);
                material.SetFloat("_Toon", toon ? 1 : 0);
                material.SetFloat("_Comic", toon || (string)native?["nativeSurfaceStyle"] == "cel" ? 1 : 0);
                material.SetFloat("_InkRim", (bool?)native?["nativeInkRim"] == true ? 1 : 0);
                material.SetFloat("_RimIntensity", shaderName.Contains("_Rim", StringComparison.Ordinal) ? 2 : 0);
                var pass = native?["passes"]?.FirstOrDefault();
                material.SetFloat("_Cull", (string)pass?["cull"] == "off" || (bool?)definition["doubleSided"] == true ? 0 : 2);
                material.SetFloat("_ZWrite", (bool?)pass?["zWrite"] == false ? 0 : 1);
                bool blend = (bool?)pass?["blend"]?["enabled"] ?? ((string)definition["alphaMode"] == "BLEND");
                material.SetFloat("_SrcBlend", blend ? (int)BlendMode.SrcAlpha : (int)BlendMode.One);
                material.SetFloat("_DstBlend", blend ? (int)BlendMode.OneMinusSrcAlpha : (int)BlendMode.Zero);
                material.renderQueue = (int?)native?["renderQueue"] ?? (blend ? 3000 : 2000);
                foreach (var color in native?["colors"] ?? new JArray())
                {
                    string name = (string)color["name"];
                    if (material.HasProperty(name)) { var c = color["value"]!; material.SetColor(name, new Color((float)c[0], (float)c[1], (float)c[2], (float)c[3])); }
                }
                foreach (var number in native?["floats"] ?? new JArray())
                    if (material.HasProperty((string)number["name"])) material.SetFloat((string)number["name"], (float)number["value"]!);
                material.SetFloat("_Outline", (bool?)native?["nativeOutline"] == true || native?["passes"]?.Any(p => (string)p["outline"]?["mode"] == "worldSpace") == true ? material.GetFloat("_Outline") : 0);
                foreach (var binding in native?["textureBindings"] ?? new JArray())
                {
                    string uri = (string)binding["uri"], slot = (string)binding["slot"];
                    if (string.IsNullOrEmpty(uri) || !material.HasProperty(slot)) continue;
                    material.SetTexture(slot, WorldModelTextureAssets.Resolve(context, json, uri, binding, slot));
                    if (binding["scale"] is JArray scale) material.SetTextureScale(slot, new Vector2((float)scale[0], (float)scale[1]));
                    if (binding["offset"] is JArray offset) material.SetTextureOffset(slot, new Vector2((float)offset[0], (float)offset[1]));
                }
                NativeWorldMaterialImport.Apply(context, json, definition, material);
                material.enableInstancing = true;
                context.AddObjectToAsset("material_" + index, material);
                return material;
            }).ToArray();
        }
        static void AddAnimations(NativeGlbDocument document, GameObject root, Transform[] nodes, AssetImportContext context)
        {
            var animations = document.Json["animations"] as JArray;
            if (animations == null || animations.Count == 0) return;
            var player = root.AddComponent<Animation>();
            foreach (var definition in animations)
            {
                string name = (string)definition["name"] ?? "animation";
                // Tutorial actors and effects share this importer. Keep every authored
                // clip, including the player poses and nif-default effect animation.
                var clip = new AnimationClip { name = name, legacy = true, frameRate = 30,
                    wrapMode = name == "stand1" || name == "nif-default" ? WrapMode.Loop : WrapMode.ClampForever };
                foreach (var channel in definition["channels"]!)
                {
                    var sampler = definition["samplers"]![(int)channel["sampler"]!]!;
                    string interpolation = (string)sampler["interpolation"] ?? "LINEAR";
                    if (interpolation != "LINEAR" && interpolation != "STEP" && interpolation != "CUBICSPLINE") throw new InvalidDataException("Animation interpolation unsupported: " + interpolation);
                    var times = document.Values((int)sampler["input"]!);
                    var values = document.Values((int)sampler["output"]!);
                    string property = (string)channel["target"]!["path"];
                    string target = AnimationUtility.CalculateTransformPath(nodes[(int)channel["target"]!["node"]!], root.transform);
                    int dimensions = property == "rotation" ? 4 : 3;
                    string prefix = property switch { "translation" => "localPosition", "rotation" => "localRotation", "scale" => "localScale", _ => throw new InvalidDataException("Unsupported animation target") };
                    for (int component = 0; component < dimensions; component++)
                    {
                        var keys = new Keyframe[times.Length];
                        for (int key = 0; key < keys.Length; key++)
                        {
                            float sign = property == "translation" && component == 0 || property == "rotation" && (component == 1 || component == 2) ? -1 : 1;
                            keys[key] = interpolation == "CUBICSPLINE"
                                ? new Keyframe(times[key][0], values[key * 3 + 1][component] * sign,
                                    values[key * 3][component] * sign, values[key * 3 + 2][component] * sign)
                                : new Keyframe(times[key][0], values[key][component] * sign);
                        }
                        var curve = new AnimationCurve(keys);
                        for (int key = 0; key < keys.Length && interpolation != "CUBICSPLINE"; key++)
                        {
                            var mode = interpolation == "STEP" ? AnimationUtility.TangentMode.Constant : AnimationUtility.TangentMode.Linear;
                            AnimationUtility.SetKeyLeftTangentMode(curve, key, mode);
                            AnimationUtility.SetKeyRightTangentMode(curve, key, mode);
                        }
                        clip.SetCurve(target, typeof(Transform), prefix + "." + "xyzw"[component], curve);
                    }
                }
                clip.EnsureQuaternionContinuity();
                player.AddClip(clip, name);
                if (name == "stand1") { player.clip = clip; player.playAutomatically = true; }
                context.AddObjectToAsset("animation_" + name, clip);
            }
        }
        static Vector3 Vector(JToken value, Vector3 fallback) => value == null ? fallback : new Vector3((float)value[0], (float)value[1], (float)value[2]);
        static Vector3 Position(JToken value, Vector3 fallback) { var result = Vector(value, fallback); result.x = -result.x; return result; }
        static Quaternion Rotation(JToken value) => value == null ? Quaternion.identity : new Quaternion((float)value[0], -(float)value[1], -(float)value[2], (float)value[3]);
        static Matrix4x4 Matrix(float[] value)
        {
            var result = new Matrix4x4();
            for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++) result[row, column] = value[column * 4 + row] * (row == 0 ? -1 : 1) * (column == 0 ? -1 : 1);
            return result;
        }
    }
}
