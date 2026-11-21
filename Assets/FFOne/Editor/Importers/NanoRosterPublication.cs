using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using FFOne.Content;
using UnityEditor;
namespace FFOne.Editor
{
    public static class NanoRosterPublication
    {
        public static void PublishForBuild()
        {
            Publish();
            PublishLocalizedNames();
            PublishLandings();
        }

        static void PublishLocalizedNames()
        {
            foreach (var language in new[] { "en", "ru" })
            {
                string root = "Assets/FFOne/Data/Localization/";
                var target = AssetDatabase.LoadAssetAtPath<NativeDataDocument>(root + "Documents/" + language + ".json.asset");
                var published = (JObject)target.ToToken();
                var entries = (JObject)published["entries"];
                var source = (JObject)JObject.Parse(File.ReadAllText(root + "Source/" + language + ".json"))["entries"];
                bool IsNano(JProperty entry) => entry.Name.StartsWith("content.nano.", StringComparison.Ordinal) ||
                    (language == "ru" && entry.Name == "content.tabledata.nano.nano_string.65.str_name");
                var names = source.Properties().Where(IsNano).ToArray();
                if (names.Length != 68 * 4 + (language == "ru" ? 1 : 0)) throw new InvalidDataException("Incomplete localized Nano roster: " + language);
                if (entries.Properties().Count(IsNano) == names.Length && names.All(p => JToken.DeepEquals(entries[p.Name], p.Value)))
                    continue;
                foreach (var stale in entries.Properties().Where(p => IsNano(p) && source[p.Name] == null).ToArray())
                    stale.Remove();
                foreach (var name in names) entries[name.Name] = name.Value.DeepClone();
                target.Import(published.ToString(Formatting.None));
                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssetIfDirty(target);
            }
        }

        // Every search starts at a server-owned position on this exact map. Nearby maps can
        // share a Unity scene, so never sample the unqualified scene bounds as a destination.
        public static void PublishLandings() => PublishLandings("../RustyFusion/tabledata/unstable_landings.json");

        public static void VerifyLandings()
        {
            var args = Environment.GetCommandLineArgs();
            int flag = Array.IndexOf(args, "-ffoneLandingOutput");
            if (flag < 0 || flag + 1 == args.Length) throw new ArgumentException("Missing -ffoneLandingOutput <path>.");
            string output = Path.GetFullPath(args[flag + 1]);
            string live = Path.GetFullPath("../RustyFusion/tabledata/unstable_landings.json");
            if (string.Equals(output, live, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Verification requires its own output file.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            PublishLandings(output);
        }

        static void PublishLandings(string outputPath)
        {
            const string tables = "../RustyFusion/tabledata/";
            const string supplementalFile = "Assets/FFOne/Editor/Importers/UnstableNanoSupplementalLandings.json";
            var anchors = new Dictionary<(int map, int x, int y, int z), Vector3Int>();
            var portalPositions = new HashSet<(int map, Vector3Int point)>();
            void Add(int map, int x, int y, int z)
            {
                if (x <= 0 || y <= 0) return;
                int spacing = map == 0 ? 12000 : 1200;
                anchors.TryAdd((map, x / spacing, y / spacing, z / 1000), new Vector3Int(x,y,z));
            }
            var xdt = JObject.Parse(File.ReadAllText(tables + "xdt.json"));
            foreach (var r in xdt["m_pInstanceTable"]["m_pWarpData"])
                if ((int)r["m_iWarpNumber"] > 0)
                {
                    Add((int)r["m_iToMapNum"], (int)r["m_iToX"], (int)r["m_iToY"], (int)r["m_iToZ"]);
                    portalPositions.Add(((int)r["m_iToMapNum"], new Vector3Int((int)r["m_iToX"], (int)r["m_iToY"], (int)r["m_iToZ"])));
                }
            var enterableMaps = portalPositions.Where(p => p.point != Vector3Int.zero).Select(p => p.map).ToHashSet();
            foreach (var r in xdt["m_pXComTable"]["m_pXComData"])
                if ((int)r["m_iXcomNumber"] > 0)
                {
                    Add((int)r["m_iZone"], (int)r["m_iXpos"], (int)r["m_iYpos"], (int)r["m_iZpos"]);
                    portalPositions.Add(((int)r["m_iZone"], new Vector3Int((int)r["m_iXpos"], (int)r["m_iYpos"], (int)r["m_iZpos"])));
                }
            var mapCells = xdt["m_pInstanceTable"]["m_pInstanceData"].ToDictionary(
                r => (int)r["m_iInstanceNameID"], r => ((int)r["m_iZoneX"], (int)r["m_iZoneY"]));
            var supplemental = JArray.Parse(File.ReadAllText(supplementalFile));
            Vector3Int Coordinates(JToken row) => new((int)row["x"], (int)row["y"], (int)row["z"]);
            foreach (string filename in new[] {"NPCs", "mobs"})
            {
                var source = JObject.Parse(File.ReadAllText(tables + filename + ".json"));
                foreach (var section in source.Properties())
                    if (section.Value is JObject placements)
                        foreach (var placement in placements.Properties())
                        {
                            var r = placement.Value;
                            if (r["iX"] == null || r["iY"] == null || r["iZ"] == null) continue;
                            Add((int?)r["iMapNum"] ?? 0, Mathf.RoundToInt((float)r["iX"]),
                                Mathf.RoundToInt((float)r["iY"]), Mathf.RoundToInt((float)r["iZ"]));
                        }
            }
            var sparseMaps = anchors.GroupBy(pair=>pair.Key.map).Where(group=>group.Count()<4)
                .Select(group=>group.Key).ToHashSet();
            var output = new SortedDictionary<int, SortedDictionary<(int,int,int), Vector3Int>>();
            var tiles = anchors.GroupBy(r => (x:r.Value.x/51200, z:r.Value.y/51200)).OrderBy(r=>r.Key).ToArray();
            Debug.Log($"Unstable Nano landing check begins across {tiles.Length} cells.");
            int loaded = 0;
            foreach (var tile in tiles)
            {
                string name = $"map_{tile.Key.x:00}_{tile.Key.z:00}";
                string cell = $"Assets/FFOne/Worlds/Overworld/Cells/{name}";
                string path = $"{cell}/{name}.unity";
                if (!File.Exists(path)) continue;
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    Physics.SyncTransforms();
                    var physics = scene.GetPhysicsScene();
                    string attributesPath = cell + "/Source/terrain/gameplay/attributes.bin";
                    byte[] attributes = File.Exists(attributesPath) ? File.ReadAllBytes(attributesPath) : null;
                    var overlaps = new Collider[1];
                    var overhead = new RaycastHit[64];
                    var overheadHeights = new[] { 3f, 12f, 32f, 96f };
                    var liquids = scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<Renderer>())
                        .Where(r=>r.name.StartsWith("ffWater", StringComparison.OrdinalIgnoreCase) ||
                                  r.name.StartsWith("ffPoison", StringComparison.OrdinalIgnoreCase))
                        .Select(r=>r.bounds).ToArray();
                    bool HasWaterAbove(Vector3 p)
                    {
                        // A shore may have dry terrain attributes even on the bottom of its lake.
                        // A solid ceiling between the floor and the water shelters an indoor room.
                        foreach (float height in overheadHeights)
                        {
                            int count = physics.Raycast(p + Vector3.up * height, Vector3.down, overhead, height + .1f,
                                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                            float nearest = float.MaxValue;
                            Collider cover = null;
                            for (int i = 0; i < count; i++)
                            {
                                var hit = overhead[i];
                                float above = hit.point.y - p.y;
                                if (hit.collider.gameObject.scene != scene || above <= .1f || above >= nearest) continue;
                                nearest = above;
                                cover = hit.collider;
                            }
                            if (!cover) continue;
                            if (cover.name.StartsWith("ffWater", StringComparison.OrdinalIgnoreCase) ||
                                cover.name.StartsWith("ffPoison", StringComparison.OrdinalIgnoreCase)) return true;
                            break;
                        }
                        return false;
                    }
                    bool Dry(Vector3 p, Collider floor)
                    {
                        if (floor.name.StartsWith("ffWater",StringComparison.OrdinalIgnoreCase) ||
                            floor.name.StartsWith("ffPoison",StringComparison.OrdinalIgnoreCase)) return false;
                        foreach (var b in liquids)
                            if (p.x>=b.min.x && p.x<=b.max.x && p.z>=b.min.z && p.z<=b.max.z &&
                                p.y<=b.max.y+.15f && p.y>=b.min.y-12) return false;
                        bool terrain = floor is MeshCollider mesh && mesh.sharedMesh &&
                            (mesh.sharedMesh.name=="WorldSurface" || mesh.sharedMesh.name=="Native collision WorldSurface");
                        if (!terrain) return true;
                        int x=Mathf.FloorToInt((p.x-tile.Key.x*512)/4), z=Mathf.FloorToInt((p.z-tile.Key.z*512)/4);
                        if (attributes==null || attributes.Length!=129*129 || x<0 || z<0 || x>=129 || z>=129) return false;
                        byte flags=attributes[z*129+x];
                        return flags>=64 || (flags&8)==0;
                    }
                    bool Floor(Vector3 p, float above, float down, out Vector3 floor)
                    {
                        floor=default;
                        if (!physics.Raycast(p+Vector3.up*above,Vector3.down,out var hit,down,
                            Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore) || hit.collider.gameObject.scene!=scene ||
                            hit.normal.y<.72f || !Dry(hit.point,hit.collider)) return false;
                        floor=hit.point;
                        return physics.OverlapCapsule(floor+Vector3.up*.46f,floor+Vector3.up*1.70f,.34f,overlaps,
                            Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)==0;
                    }
                    foreach (var r in tile)
                    {
                        int map=r.Key.map;
                        if (!output.TryGetValue(map,out var cells)) output.Add(map,cells=new());
                        var origin=new Vector3(r.Value.x,r.Value.z,r.Value.y)*.01f;
                        bool sparse=sparseMaps.Contains(map);
                        if (!Floor(origin,1.5f,5,out var start) &&
                            !(sparse && Floor(origin,2f,12f,out start))) continue;
                        float interval=map==0?52f:sparse?1.5f:4f;
                        void Accept(Vector3 point)
                        {
                            var bucket=(Mathf.FloorToInt(point.x/interval),Mathf.FloorToInt(point.z/interval),Mathf.FloorToInt(point.y/4));
                            if (!cells.ContainsKey(bucket) && !HasWaterAbove(point))
                                cells.Add(bucket,new Vector3Int(Mathf.RoundToInt(point.x*100),Mathf.RoundToInt(point.z*100),Mathf.RoundToInt(point.y*100)+5));
                        }
                        if (sparse) Accept(start);
                        // Offset from the NPC/mob's standing position. Each accepted sample has a
                        // continuous, slope-checked capsule corridor back to the map-owned anchor.
                        for(int spoke=0;spoke<8;spoke++)
                        {
                            var direction=Quaternion.Euler(0,spoke*45,0)*Vector3.forward;
                            var last=start;
                            for(int step=1;step<=8;step++)
                            {
                                if (!Floor(last+direction*1.25f,1.3f,2.6f,out var next) || Mathf.Abs(next.y-last.y)>.65f ||
                                    physics.CapsuleCast(last+Vector3.up*.46f,last+Vector3.up*1.70f,.30f,direction,out _,
                                    1.25f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)) break;
                                last=next;
                                if (step==3 || step==7 || (sparse && (step==2 || step==4))) Accept(next);
                            }
                        }
                    }
                    foreach (var row in supplemental)
                    {
                        int map = (int)row["map"];
                        var anchor = Coordinates(row["anchor"]);
                        if (!portalPositions.Contains((map, anchor))) throw new InvalidDataException($"Unknown entrance for map {map}.");
                        if (!mapCells.TryGetValue(map, out var mapCell) || mapCell != (tile.Key.x, tile.Key.z)) continue;
                        string requiredWater = (string)row["waterSurface"];
                        if (requiredWater != null && (map is not (142 or 143) || requiredWater != "ffWater [BuildPlayer-Map_07_05#77 collision]"))
                            throw new InvalidDataException($"Unknown water landing for map {map}.");
                        if (!output.TryGetValue(map, out var cells)) output.Add(map, cells = new());
                        foreach (var record in row["points"])
                        {
                            var position = Coordinates(record);
                            var p = new Vector3(position.x, position.z, position.y) * .01f;
                            if (position.x / 51200 != mapCell.Item1 || position.y / 51200 != mapCell.Item2) continue;
                            if (!physics.Raycast(p + Vector3.up, Vector3.down, out var hit, 2, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                                || hit.collider.gameObject.scene != scene || hit.normal.y < .72f || Mathf.Abs(hit.point.y + .05f - p.y) > .12f) continue;
                            if (requiredWater != null)
                            {
                                if (hit.collider.name != requiredWater || hit.normal.y < .95f || attributes == null) continue;
                                int x = Mathf.FloorToInt((p.x - tile.Key.x * 512) / 4), z = Mathf.FloorToInt((p.z - tile.Key.z * 512) / 4);
                                int flag = attributes[z * 129 + x];
                                if (flag < 64 && (flag & 8) != 0) continue;
                            }
                            else if (!Dry(hit.point, hit.collider) || HasWaterAbove(hit.point)) continue;
                            if (physics.OverlapCapsule(hit.point + Vector3.up * .46f, hit.point + Vector3.up * 1.7f,
                                .34f, overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) == 0)
                                cells.TryAdd((position.x, position.y, position.z), position);
                        }
                    }
                }
                finally { EditorSceneManager.CloseScene(scene,true); }
                if (++loaded%8==0)
                {
                    Debug.Log($"Unstable Nano landing check: {loaded}/{tiles.Length} cells, {output.Values.Sum(points=>points.Count)} accepted points.");
                    EditorUtility.UnloadUnusedAssetsImmediate();
                }
            }
            var validated = output.ToDictionary(pair => pair.Key, pair => pair.Value.Values.Distinct().ToArray());
            foreach (int map in enterableMaps)
                if (!validated.TryGetValue(map, out var points) || points.Length < 2 ||
                    points.Any(p => !points.Any(other => ((Vector3)(other - p)).sqrMagnitude >= 22500)))
                    throw new InvalidDataException($"Map {map} has no repeatable collision-checked Nano destination.");
            var dto=validated.Where(pair=>pair.Value.Length>0).ToDictionary(pair=>pair.Key,pair=>pair.Value.Select(p=>new{x=p.x,y=p.y,z=p.z}).ToArray());
            string destination = Path.GetFullPath(outputPath);
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonConvert.SerializeObject(dto, Formatting.Indented));
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            Debug.Log($"Unstable Nano sampled {loaded} authored cells, {dto.Count} maps and {dto.Values.Sum(v=>v.Length)} collision-checked landings.");
        }
        public static void Publish()
        {
            const string source = "Assets/FFOne/Data/Gameplay/Source/tables/xdt.json";
            const string target = "Assets/FFOne/Data/Tables/data/tables/xdt.json.asset";
            var document = AssetDatabase.LoadAssetAtPath<NativeDataDocument>(target);
            if (!document) throw new FileNotFoundException("Missing published XDT", target);
            document.Import(XdtModelRoutePublication.PreserveDirectRoutes(File.ReadAllText(source), document.ToJson()));
            EditorUtility.SetDirty(document);
            AssetDatabase.SaveAssetIfDirty(document);
        }
    }
}
