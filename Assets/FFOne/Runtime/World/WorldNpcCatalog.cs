using System;
using System.Collections.Generic;
using System.Globalization;
using FFOne.Content;

namespace FFOne.World
{
    public sealed class WorldNpcDefinition
    {
        public int Type, MaxHp, Team, Category, AiType, Effect, SightRange;
        public bool IsGroupNpc => Team == 1 && (Category == 6 || AiType > 0 && AiType < 11);
        public bool IsBank => Category == 50 || Category == 111;
        public bool HiddenMarker => Category >= 100 && HnpcAppearance < 0;
        public bool IsVendor;
        public float Scale, Height, Radius;
        public string Name, Model;
        public int HnpcAppearance;
    }

    /// <summary>Projects only NPC rows and the accepted native model routes; no giant JObject is retained.</summary>
    public sealed class WorldNpcCatalog
    {
        // Exact renderless primary RXcom prefab. The renderer-based native model
        // exporter omits this owner; recovery preserves its MeshFilter and clips.
        // Source identity and replay: docs/recall-point-rxcom.md.
        public const string RecallPointModel = "ffone/6949aa01f8c678a3f4f3390964a8aaf8";
        readonly Dictionary<int, WorldNpcDefinition> definitions = new();
        static WorldNpcCatalog cached;
        public static WorldNpcCatalog Load() => cached ??= ContentAssets.Read<NativeDataDocument, WorldNpcCatalog>(NativeData.Resource("data/tables/xdt"), Read);
        public WorldNpcDefinition Find(int type) => definitions.TryGetValue(type, out var value) ? value : null;
        public static WorldNpcCatalog Read(NativeDataDocument d)
        {
            var result = new WorldNpcCatalog(); var routes = new Dictionary<string, string>(StringComparer.Ordinal);
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal); var ambiguousAliases = new HashSet<string>(StringComparer.Ordinal);
            var vendors = new HashSet<int>();
            foreach (int row in Children(d, Child(d, Child(d, 0, "m_pVendorTable"), "m_pItemData"))) vendors.Add(Number(d, row, "m_iNpcNumber"));
            foreach (int table in Children(d, Child(d, Child(d, 0, "_ffone"), "tables")))
            {
                if (Text(d, table, "name") != "native_asset_routes") continue;
                int rows = Child(d, Child(d, table, "value"), "m_pCharacterModelData");
                foreach (int row in Children(d, rows))
                {
                    string name = Text(d, row, "logicalName"), path = Text(d, row, "glb");
                    if (name.Length == 0 || path.Length == 0) continue;
                    routes[name] = path;
                    foreach (int alias in Children(d, Child(d, row, "legacyAliases")))
                    {
                        string key = d.Nodes[alias].Value; if (string.IsNullOrEmpty(key)) continue;
                        if (aliases.TryGetValue(key, out var previous) && previous != path) ambiguousAliases.Add(key);
                        else aliases[key] = path;
                    }
                }
            }
            // Retain established canonical bindings; only explicit, unambiguous source aliases
            // may fill an absent key. Never guess a prefab from a localized NPC name.
            foreach (var alias in aliases)
                if (!routes.ContainsKey(alias.Key) && !ambiguousAliases.Contains(alias.Key)) routes.Add(alias.Key, alias.Value);
            if (!routes.ContainsKey("RXcom")) routes.Add("RXcom", RecallPointModel);
            int npc = Child(d, 0, "m_pNpcTable");
            var meshes = new List<int>(Children(d, Child(d, npc, "m_pNpcMeshData")));
            var strings = new List<int>(Children(d, Child(d, npc, "m_pNpcStringData")));
            foreach (int row in Children(d, Child(d, npc, "m_pNpcData")))
            {
                int type = Number(d, row, "m_iNpcNumber"); if (type <= 0) continue;
                int mesh = Number(d, row, "m_iMesh"), name = Number(d, row, "m_iNpcName");
                string logical = mesh >= 0 && mesh < meshes.Count ? Text(d, meshes[mesh], "m_pstrMMeshModelString") : "";
                routes.TryGetValue(logical, out var model);
                result.definitions[type] = new WorldNpcDefinition { Type = type, MaxHp = Number(d, row, "m_iHP"), Team = Number(d, row, "m_iTeam"),
                    AiType = Number(d, row, "m_iAiType"), Category = Number(d, row, "m_iNpcType"), SightRange = Number(d, row, "m_iSightRange"), Effect = Number(d, row, "m_iEffect"), IsVendor = vendors.Contains(type) || Number(d, row, "m_iNpcType") == 2 || Number(d, row, "m_iNpcType") == 25,
                    Name = name >= 0 && name < strings.Count ? Text(d, strings[name], "m_strName") : type.ToString(),
                    Model = model, HnpcAppearance = Number(d, row, "m_iHNpc") != 0 ? Number(d, row, "m_iHNpcNum") : -1,
                    Scale = Real(d, row, "m_fScale", 1), Height = Number(d, row, "m_iHeight") / 100f,
                    Radius = Number(d, row, "m_iRadius") / 100f };
            }
            return result;
        }
        static IEnumerable<int> Children(NativeDataDocument d, int node) => node < 0 ? Array.Empty<int>() : d.Children(node);
        static int Child(NativeDataDocument d, int parent, string key)
        { foreach (int i in Children(d, parent)) if (d.Nodes[i].Key == key) return i; return -1; }
        static string Text(NativeDataDocument d, int row, string key)
        { int i = Child(d, row, key); return i < 0 ? "" : d.Nodes[i].Value ?? ""; }
        static int Number(NativeDataDocument d, int row, string key) => int.TryParse(Text(d, row, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
        static float Real(NativeDataDocument d, int row, string key, float fallback) => float.TryParse(Text(d, row, key), NumberStyles.Float, CultureInfo.InvariantCulture, out float n) && float.IsFinite(n) ? n : fallback;
    }
}
