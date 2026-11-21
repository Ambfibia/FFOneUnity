using System;
using System.Globalization;
using System.Numerics;
using FFOne.Content;
using Newtonsoft.Json.Linq;

namespace FFOne.Tutorial
{
    // Existing gameplay command adapters consume tokens. Read their serialized
    // nodes directly, without a Nodes -> JSON text -> parser round trip or montage.
    internal static class TutorialActionData
    {
        internal static JToken Read(NativeDataDocument document)
        {
            if (!document || document.Nodes.Length == 0) throw new InvalidOperationException("Missing Timeline action data.");
            JToken Node(int index)
            {
                var node = document.Nodes[index];
                switch (node.Kind)
                {
                    case NativeDataDocument.ValueKind.Object:
                        var obj = new JObject();
                        foreach (int child in document.Children(index)) obj.Add(document.Nodes[child].Key, Node(child));
                        return obj;
                    case NativeDataDocument.ValueKind.Array:
                        var array = new JArray();
                        foreach (int child in document.Children(index)) array.Add(Node(child));
                        return array;
                    case NativeDataDocument.ValueKind.String: return new JValue(node.Value);
                    case NativeDataDocument.ValueKind.Boolean: return new JValue(bool.Parse(node.Value));
                    case NativeDataDocument.ValueKind.Null: return JValue.CreateNull();
                    default:
                        string value = node.Value;
                        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return new JValue(integer);
                        if (value.IndexOfAny(new[] { '.', 'e', 'E' }) < 0) return new JValue(BigInteger.Parse(value, CultureInfo.InvariantCulture));
                        return new JValue(double.Parse(value, CultureInfo.InvariantCulture));
                }
            }
            return Node(0);
        }
    }
}
