using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FFOne.Editor
{
    internal static class XdtModelRoutePublication
    {
        // Direct prefab routes are authored in Unity and can be absent from a server XDT.
        internal static string PreserveDirectRoutes(string source, string accepted)
        {
            var previous = ReadRoutes(accepted, out _);
            var incoming = ReadRoutes(source, out int close);
            var matches = incoming.ToLookup(r => (string)r["logicalName"], StringComparer.Ordinal);
            var missing = new Dictionary<string, JToken>(StringComparer.Ordinal);
            foreach (var row in previous)
            {
                string address = (string)row["glb"];
                if (address == null || !address.StartsWith("ffone/", StringComparison.Ordinal) ||
                    !Guid.TryParseExact(address.Substring(6), "N", out _)) continue;
                string name = (string)row["logicalName"];
                if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("A direct XDT model route has no logical name: " + address);
                var existing = matches[name];
                foreach (var candidate in existing)
                    CheckAddress(name, address, candidate);
                if (existing.Any()) continue;
                if (missing.TryGetValue(name, out var duplicate)) CheckAddress(name, address, duplicate);
                else missing.Add(name, row);
            }
            if (missing.Count == 0) return source;
            // Insert only the recovered rows. Unrelated tables retain their original JSON and numeric precision.
            string rows = string.Join(",", missing.Values.Select(r => r.ToString(Formatting.None)));
            return source.Insert(close, (incoming.Count == 0 ? "" : ",") + rows);
        }

        static void CheckAddress(string name, string accepted, JToken candidate)
        {
            string incoming = (string)candidate["glb"];
            if (!string.Equals(accepted, incoming, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Conflicting XDT model route '{name}': accepted '{accepted}', incoming '{incoming}'.");
        }

        static JArray ReadRoutes(string json, out int close)
        {
            using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
            string table = null, name = null;
            JArray routes = null;
            int line = 0, column = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.StartObject && reader.Depth == 3 &&
                    reader.Path.StartsWith("_ffone.tables[", StringComparison.Ordinal))
                {
                    table = reader.Path;
                    name = null;
                    routes = null;
                }
                else if (table != null && reader.Path == table + ".name" && reader.TokenType == JsonToken.String)
                    name = (string)reader.Value;
                else if (table != null && reader.Path == table + ".value.m_pCharacterModelData" && reader.TokenType == JsonToken.StartArray)
                {
                    routes = JArray.Load(reader);
                    line = reader.LineNumber;
                    column = reader.LinePosition;
                }
                else if (table != null && reader.TokenType == JsonToken.EndObject && reader.Path == table)
                {
                    if (name == "native_asset_routes" && routes != null)
                    {
                        int start = 0;
                        for (int i = 1; i < line; ++i)
                        {
                            while (json[start] != '\r' && json[start] != '\n') ++start;
                            if (json[start++] == '\r' && start < json.Length && json[start] == '\n') ++start;
                        }
                        close = start + column - 1;
                        if (json[close] != ']') throw new InvalidDataException("Invalid closing position of the XDT model routes.");
                        return routes;
                    }
                    table = null;
                }
                else if ((reader.TokenType == JsonToken.StartArray || reader.TokenType == JsonToken.StartObject) &&
                    reader.Depth != 0 && reader.Path != "_ffone" && reader.Path != "_ffone.tables" &&
                    (table == null || reader.Path != table + ".value"))
                    reader.Skip();
            }
            throw new InvalidDataException("The XDT has no native_asset_routes character model table.");
        }
    }
}
