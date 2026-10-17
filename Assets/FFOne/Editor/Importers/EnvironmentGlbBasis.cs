using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FFOne.Editor
{
    public static class EnvironmentGlbBasis
    {
        public const string Marker = "ffoneAuthoringBasis";
        public static bool IsNormalized(JObject document) => (string)document["extras"]?[Marker] == "sourceZUpToYUp";
        // Unity-owned geometry is an intentional override of the read-only extraction, like local translations.
        public static bool IsAuthoringOverride(string relative, string destination) => relative.StartsWith("objects/", StringComparison.Ordinal) &&
            relative.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) && IsNormalized(NativeGlbDocument.Read(destination).Json);
        public static Vector3 Rotate(Vector3 v) => new(v.x, v.z, -v.y);
        public static void Validate(JObject document)
        {
            if ((document["animations"] as JArray)?.Count > 0 || (document["skins"] as JArray)?.Count > 0)
                throw new InvalidDataException("Skeletal/animated models require their own basis conversion.");
            foreach (var node in document["nodes"])
                if (new[] { "matrix", "translation", "rotation", "scale", "children", "skin" }.Any(k => node[k] != null))
                    throw new InvalidDataException("Only flat source meshes may be baked by this conversion.");
            foreach (var primitive in document["meshes"].SelectMany(m => m["primitives"]))
                if (primitive["targets"] != null) throw new InvalidDataException("Morph targets require their own basis conversion.");
        }
        public static bool Bake(string path)
        {
            var chunks = new List<(uint Kind, byte[] Bytes)>();
            using (var reader = new BinaryReader(File.OpenRead(ContentPaths.FilePath(path))))
            {
                if (reader.ReadUInt32() != 0x46546C67 || reader.ReadUInt32() != 2 || reader.ReadUInt32() != reader.BaseStream.Length)
                    throw new InvalidDataException("Invalid GLB");
                while (reader.BaseStream.Position < reader.BaseStream.Length)
                { int length = checked((int)reader.ReadUInt32()); uint kind = reader.ReadUInt32(); var data = reader.ReadBytes(length); if (data.Length != length) throw new EndOfStreamException(); chunks.Add((kind, data)); }
            }
            int jsonIndex = chunks.FindIndex(c => c.Kind == 0x4E4F534A), binIndex = chunks.FindIndex(c => c.Kind == 0x004E4942);
            var json = JObject.Parse(Encoding.UTF8.GetString(chunks[jsonIndex].Bytes));
            if (IsNormalized(json)) return false;
            Validate(json);
            byte[] buffer = chunks[binIndex].Bytes;
            var seen = new HashSet<int>();
            foreach (var primitive in json["meshes"].SelectMany(m => m["primitives"]))
                foreach (string semantic in new[] { "POSITION", "NORMAL", "TANGENT" })
                {
                    var token = primitive["attributes"]?[semantic]; if (token == null || !seen.Add((int)token)) continue;
                    var a = json["accessors"][(int)token];
                    if ((int)a["componentType"] != 5126 || a["sparse"] != null || a["normalized"]?.Value<bool>() == true ||
                        (string)a["type"] != (semantic == "TANGENT" ? "VEC4" : "VEC3")) throw new InvalidDataException("Unsupported basis accessor");
                    var view = json["bufferViews"][(int)a["bufferView"]];
                    if (((int?)view["buffer"] ?? 0) != 0) throw new InvalidDataException("External buffer");
                    int start = ((int?)view["byteOffset"] ?? 0) + ((int?)a["byteOffset"] ?? 0), stride = (int?)view["byteStride"] ?? (semantic == "TANGENT" ? 16 : 12);
                    var min = Vector3.positiveInfinity; var max = Vector3.negativeInfinity;
                    for (int i = 0; i < (int)a["count"]; i++)
                    {
                        int p = checked(start + i * stride);
                        if (p < 0 || p + 12 > buffer.Length) throw new InvalidDataException("Accessor out of bounds");
                        var v = Rotate(new Vector3(BitConverter.ToSingle(buffer,p), BitConverter.ToSingle(buffer,p+4), BitConverter.ToSingle(buffer,p+8)));
                        Buffer.BlockCopy(BitConverter.GetBytes(v.y),0,buffer,p+4,4); Buffer.BlockCopy(BitConverter.GetBytes(v.z),0,buffer,p+8,4);
                        min=Vector3.Min(min,v); max=Vector3.Max(max,v);
                    }
                    if (a["min"] != null) { var values=(JArray)a["min"]; values[0]=min.x; values[1]=min.y; values[2]=min.z; }
                    if (a["max"] != null) { var values=(JArray)a["max"]; values[0]=max.x; values[1]=max.y; values[2]=max.z; }
                }
            var extras = json["extras"] as JObject ?? new JObject(); json["extras"] = extras; extras[Marker] = "sourceZUpToYUp";
            var bytes = Encoding.UTF8.GetBytes(json.ToString(Formatting.None)); int padded = (bytes.Length+3)&~3;
            var encoded = Enumerable.Repeat((byte)32,padded).ToArray(); Buffer.BlockCopy(bytes,0,encoded,0,bytes.Length); chunks[jsonIndex]=(0x4E4F534A,encoded);
            // All validation and conversion finish in memory before replacing this source file.
            using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
            writer.Write(0x46546C67u); writer.Write(2u); writer.Write(checked((uint)(12+chunks.Sum(c=>8+c.Bytes.Length))));
            foreach(var chunk in chunks) { writer.Write(chunk.Bytes.Length); writer.Write(chunk.Kind); writer.Write(chunk.Bytes); }
            File.WriteAllBytes(ContentPaths.FilePath(path),output.ToArray()); return true;
        }
    }
}
