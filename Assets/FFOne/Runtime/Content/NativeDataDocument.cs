using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Serialization;

namespace FFOne.Content
{
    // Preorder tree: End is the last descendant, allowing O(1) skipping of large tables.
    // Numeric tokens remain strings deliberately: Unity floats would corrupt 64-bit IDs,
    // precise decimal values and explicit exponents. This is structured data, not a JSON blob.
    public sealed class NativeDataDocument : ScriptableObject
    {
        public enum ValueKind { Object, Array, String, Number, Boolean, Null }
        [Serializable] public struct Node
        {
            [SerializeField, FormerlySerializedAs("Key")] string rawKey;
            [SerializeField] string exactKey;
            public ValueKind Kind;
            [SerializeField, FormerlySerializedAs("Value")] string rawValue;
            [SerializeField] string exactValue;
            public int End;
            // Unity's text serializer folds whitespace at wrapped YAML lines. Keep
            // readable text plus lossless UTF-16 code units for whitespace-bearing
            // strings; the public/editor value always uses the lossless spelling.
            public string Key { get => Decode(rawKey, exactKey); set { rawKey = value; exactKey = Encode(value); } }
            public string Value { get => Decode(rawValue, exactValue); set { rawValue = value; exactValue = Encode(value); } }
            static string Encode(string value)
            {
                if (value == null) return null;
                bool needed = false;
                foreach (char c in value) if (char.IsWhiteSpace(c) || char.IsControl(c) || char.IsSurrogate(c)) { needed = true; break; }
                if (!needed) return null;
                var bytes = new byte[value.Length * 2];
                for (int i = 0; i < value.Length; i++) { bytes[i * 2] = (byte)value[i]; bytes[i * 2 + 1] = (byte)(value[i] >> 8); }
                return Convert.ToBase64String(bytes);
            }
            static string Decode(string fallback, string exact)
            {
                if (string.IsNullOrEmpty(exact)) return fallback;
                var bytes = Convert.FromBase64String(exact);
                if (bytes.Length % 2 != 0) throw new InvalidDataException("Invalid exact native text.");
                var chars = new char[bytes.Length / 2];
                for (int i = 0; i < chars.Length; i++) chars[i] = (char)(bytes[i * 2] | bytes[i * 2 + 1] << 8);
                return new string(chars);
            }
        }
        public string Identity;
        public int TextEncodingVersion;
        public Node[] Nodes = Array.Empty<Node>();
        public void Import(string json) { var parsed = new Parser(json).Parse(); Nodes = parsed; TextEncodingVersion = 1; }
        public string ToJson()
        {
            if (Nodes.Length == 0) throw new InvalidDataException("Empty native document: " + Identity);
            var output = new StringBuilder(); Write(0, output); return output.ToString();
        }
        public JToken ToToken() => JToken.Parse(ToJson(), new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        public IEnumerable<int> Children(int index)
        {
            for (int i = index + 1; i <= Nodes[index].End; i = Nodes[i].End + 1) yield return i;
        }
        void Write(int index, StringBuilder output)
        {
            var node = Nodes[index];
            if (node.End < index || node.End >= Nodes.Length) throw new InvalidDataException("Invalid tree span: " + Identity);
            if (node.Kind == ValueKind.String) { output.Append(JsonConvert.ToString(node.Value)); return; }
            if (node.Kind != ValueKind.Object && node.Kind != ValueKind.Array)
            { output.Append(node.Kind == ValueKind.Null ? "null" : node.Value); return; }
            bool obj = node.Kind == ValueKind.Object, first = true;
            output.Append(obj ? '{' : '[');
            foreach (int child in Children(index))
            {
                if (!first) output.Append(','); first = false;
                if (obj) output.Append(JsonConvert.ToString(Nodes[child].Key)).Append(':');
                Write(child, output);
            }
            output.Append(obj ? '}' : ']');
        }
        sealed class Parser
        {
            readonly string text;
            readonly List<Node> nodes = new();
            int offset;
            public Parser(string text) => this.text = text ?? throw new ArgumentNullException(nameof(text));
            public Node[] Parse()
            {
                Read(null, 0); Space();
                if (offset != text.Length) Fail();
                return nodes.ToArray();
            }
            void Space() { while (offset < text.Length && char.IsWhiteSpace(text[offset])) offset++; }
            void Fail() => throw new InvalidDataException("Invalid JSON at character " + offset);
            char Peek() { Space(); if (offset >= text.Length) Fail(); return text[offset]; }
            string String()
            {
                int start = offset++;
                while (offset < text.Length)
                {
                    char c = text[offset++];
                    if (c == '\\') { offset++; continue; }
                    if (c == '"') return JsonConvert.DeserializeObject<string>(text.Substring(start, offset - start));
                    if (c < 32) Fail();
                }
                Fail(); return null;
            }
            void Read(string key, int depth)
            {
                if (depth > 256) throw new InvalidDataException("Native JSON nesting exceeds 256.");
                char c = Peek(); int index = nodes.Count;
                var node = new Node { Key = key, End = index };
                nodes.Add(node);
                if (c == '{' || c == '[')
                {
                    bool obj = c == '{'; node.Kind = obj ? ValueKind.Object : ValueKind.Array;
                    char close = obj ? '}' : ']'; offset++;
                    var names = obj ? new HashSet<string>(StringComparer.Ordinal) : null;
                    if (Peek() != close)
                    {
                        while (true)
                        {
                            string childKey = null;
                            if (obj)
                            {
                                if (Peek() != '"') Fail(); childKey = String();
                                if (!names.Add(childKey)) throw new InvalidDataException("Duplicate JSON key: " + childKey);
                                if (Peek() != ':') Fail(); offset++;
                            }
                            Read(childKey, depth + 1);
                            if (Peek() == close) break;
                            if (Peek() != ',') Fail(); offset++;
                        }
                    }
                    offset++; node.End = nodes.Count - 1;
                }
                else if (c == '"') { node.Kind = ValueKind.String; node.Value = String(); }
                else
                {
                    int start = offset;
                    while (offset < text.Length && !char.IsWhiteSpace(text[offset]) && text[offset] != ',' && text[offset] != ']' && text[offset] != '}') offset++;
                    node.Value = text.Substring(start, offset - start);
                    if (node.Value == "null") { node.Kind = ValueKind.Null; node.Value = null; }
                    else if (node.Value == "true" || node.Value == "false") node.Kind = ValueKind.Boolean;
                    else
                    {
                        node.Kind = ValueKind.Number;
                        if (!System.Text.RegularExpressions.Regex.IsMatch(node.Value, @"\A-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?\z")) Fail();
                    }
                }
                nodes[index] = node;
            }
        }
    }

    public static class NativeData
    {
        public static string Resource(string identity) => "NativeData/" + identity.Replace('\\', '/').TrimEnd('/') + (identity.EndsWith(".json", StringComparison.Ordinal) ? "" : ".json");
        public static NativeDataDocument Load(string identity) => FFOne.Content.ContentAssets.Load<NativeDataDocument>(Resource(identity));
        public static string Read(string identity)
        {
            string json = ContentAssets.Read<NativeDataDocument,string>(Resource(identity), document => document ? document.ToJson() : null);
            if (json != null) return json;
            string path = identity.EndsWith(".json", StringComparison.Ordinal) ? identity.Substring(0, identity.Length - 5) : identity;
            var fallback = FFOne.Content.ContentAssets.Load<TextAsset>(path);
            return fallback ? fallback.text : throw new FileNotFoundException("Missing native data document", identity);
        }
        public static JObject Object(string identity) => JObject.Parse(Read(identity));
    }
}
