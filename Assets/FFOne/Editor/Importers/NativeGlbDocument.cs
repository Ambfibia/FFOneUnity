using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace FFOne.Editor
{
    // A deliberately bounded reader for the accepted native glTF 2.0 files.
    // No global converter, external processes, or legacy bundle dependency.
    public sealed class NativeGlbDocument
    {
        public JObject Json { get; private set; }
        byte[] buffer;
        public static NativeGlbDocument Read(string path)
        {
            using var stream = File.OpenRead(ContentPaths.FilePath(path));
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != 0x46546C67 || reader.ReadUInt32() != 2 || reader.ReadUInt32() != stream.Length)
                throw new InvalidDataException("Expected complete glTF 2 binary: " + path);
            var result = new NativeGlbDocument();
            while (stream.Position < stream.Length)
            {
                int length = checked((int)reader.ReadUInt32());
                uint kind = reader.ReadUInt32();
                if (length > stream.Length - stream.Position) throw new InvalidDataException("Truncated GLB chunk");
                var bytes = reader.ReadBytes(length);
                if (kind == 0x4E4F534A) result.Json = JObject.Parse(Encoding.UTF8.GetString(bytes).TrimEnd('\0', ' '));
                else if (kind == 0x004E4942) result.buffer = bytes;
            }
            if (result.Json == null || result.buffer == null) throw new InvalidDataException("Missing GLB JSON or binary chunk");
            if (result.Json["extensionsRequired"] is JArray required && required.Count > 0)
                throw new InvalidDataException("Unsupported required glTF extension: " + required);
            return result;
        }
        public float[][] Values(int accessor)
        {
            var a = Json["accessors"]![accessor]!;
            if (a["sparse"] != null) throw new InvalidDataException("Sparse accessors require explicit importer support.");
            var v = Json["bufferViews"]![(int)a["bufferView"]!]!;
            if ((int?)v["buffer"] != 0) throw new InvalidDataException("External GLB buffers are not supported.");
            int width = (string)a["type"] switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => throw new InvalidDataException("Unsupported accessor type") };
            int component = (int)a["componentType"]!;
            int bytes = component switch { 5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4, _ => throw new InvalidDataException("Unsupported accessor component") };
            int stride = (int?)v["byteStride"] ?? width * bytes;
            int offset = ((int?)v["byteOffset"] ?? 0) + ((int?)a["byteOffset"] ?? 0);
            int count = (int)a["count"]!;
            bool normalize = (bool?)a["normalized"] ?? false;
            var result = new float[count][];
            for (int i = 0; i < count; i++)
            {
                result[i] = new float[width];
                for (int j = 0; j < width; j++)
                {
                    int p = checked(offset + i * stride + j * bytes);
                    if (p < 0 || p + bytes > buffer.Length) throw new InvalidDataException("Accessor exceeds GLB binary buffer.");
                    float value = component switch {
                        5120 => (sbyte)buffer[p], 5121 => buffer[p], 5122 => BitConverter.ToInt16(buffer, p),
                        5123 => BitConverter.ToUInt16(buffer, p), 5125 => BitConverter.ToUInt32(buffer, p),
                        5126 => BitConverter.ToSingle(buffer, p), _ => 0
                    };
                    if (normalize) value = component switch {
                        5120 => Math.Max(-1, value / 127), 5121 => value / 255,
                        5122 => Math.Max(-1, value / 32767), 5123 => value / 65535, _ => value
                    };
                    result[i][j] = value;
                }
            }
            return result;
        }
    }
}
