using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FFOne.Content;

namespace FFOne.World
{
    public sealed class TravelLocation { public int Id, Npc, Type, X, Y, Z; public string Name; }
    public sealed class TravelTrip { public int Id, Npc, Type, From, To, Cost; }
    public sealed class NpcWarp
    {
        public int Id, Npc, Map, X, Y, Z, Level, Task, RequiredType, RequiredItem, ConsumedType, ConsumedItem, Cost;
        public bool Group;
        public string Name;
    }
    public sealed class CrocOdds { public int LooksCost, StatsCost; public float Base; public float[] Rarity; }
    /// <summary>Source XDT IDs and server map-square links. Templates can share geometry while retaining distinct map identities.</summary>
    public sealed class WorldTravelCatalog
    {
        public readonly List<TravelLocation> Locations = new();
        public readonly List<TravelTrip> Trips = new();
        public readonly List<NpcWarp> Warps = new();
        public readonly Dictionary<int, (int X, int Z)> Maps = new();
        public readonly Dictionary<int, CrocOdds> Croc = new();
        static WorldTravelCatalog cached;
        public static WorldTravelCatalog Load() => cached ??= ContentAssets.Read<NativeDataDocument, WorldTravelCatalog>(NativeData.Resource("data/tables/xdt"), Read);
        public static WorldTravelCatalog Read(NativeDataDocument d)
        {
            var c = new WorldTravelCatalog();
            IEnumerable<int> Rows(int n) => n < 0 ? Array.Empty<int>() : d.Children(n);
            int Child(int n, string key) => Rows(n).Where(i => d.Nodes[i].Key == key).DefaultIfEmpty(-1).First();
            string Text(int n, string key) { int i = Child(n, key); return i < 0 ? "" : d.Nodes[i].Value ?? ""; }
            int N(int n, string key) => int.TryParse(Text(n, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
            float F(int n, string key) => float.TryParse(Text(n, key), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0;
            int t = Child(0, "m_pTransportationTable");
            foreach (int type in new[] { 1, 2 })
            {
                var names = Rows(Child(t, type == 1 ? "m_pTransportationWarpString" : "m_pBroomstickString")).ToArray();
                foreach (int r in Rows(Child(t, type == 1 ? "m_pTransportationWarpLocation" : "m_pBroomstickLocation")))
                {
                    int id = N(r, "m_iLocationID"); if (id <= 0) continue;
                    c.Locations.Add(new TravelLocation { Id = id, Type = type, Npc = N(r, "m_iNPCID"), X = N(r, "m_iXpos"), Y = N(r, "m_iYpos"), Z = N(r, "m_iZpos"),
                        Name = id < names.Length ? Text(names[id], "m_pstrLocationName") : id.ToString() });
                }
            }
            foreach (int r in Rows(Child(t, "m_pTransportationData")))
                if (N(r, "m_iVehicleID") > 0 && N(r, "m_iMoveType") is 1 or 2)
                    c.Trips.Add(new TravelTrip { Id = N(r, "m_iVehicleID"), Npc = N(r, "m_iNPCID"), Type = N(r, "m_iMoveType"), From = N(r, "m_iStartLocation"), To = N(r, "m_iEndLocation"), Cost = N(r, "m_iCost") });
            t = Child(0, "m_pInstanceTable"); var warpNames = Rows(Child(t, "m_pWarpNameData")).ToArray();
            foreach (int r in Rows(Child(t, "m_pInstanceData"))) c.Maps[N(r, "m_iInstanceNameID")] = (N(r, "m_iZoneX"), N(r, "m_iZoneY"));
            foreach (int r in Rows(Child(t, "m_pWarpData")))
            {
                int id = N(r, "m_iWarpNumber"); if (id <= 0) continue;
                c.Warps.Add(new NpcWarp { Id = id, Npc = N(r, "m_iNpcNumber"), Map = N(r, "m_iToMapNum"), X = N(r, "m_iToX"), Y = N(r, "m_iToY"), Z = N(r, "m_iToZ"),
                    Level = N(r, "m_iLimit_Level"), Task = N(r, "m_iLimit_TaskID"), RequiredType = N(r, "m_iLimit_ItemType"), RequiredItem = N(r, "m_iLimit_ItemID"),
                    ConsumedType = N(r, "m_iLimit_UseItemType"), ConsumedItem = N(r, "m_iLimit_UseItemID"), Cost = N(r, "m_iCost"), Group = N(r, "m_iWarpGroupType") != 0,
                    Name = id < warpNames.Length ? Text(warpNames[id], "m_pstrNameString") : id.ToString() });
            }
            foreach (int r in Rows(Child(Child(0, "m_pCombiningTable"), "m_pCombiningData")))
                c.Croc[N(r, "m_iLevelGap")] = new CrocOdds { LooksCost = N(r, "m_iLookConstant"), StatsCost = N(r, "m_iStatConstant"), Base = F(r, "m_fLevelGapStandard") / 100,
                    Rarity = new[] { F(r, "m_fSameGrade"), F(r, "m_fOneGrade"), F(r, "m_fTwoGrade"), F(r, "m_fThreeGrade") }.Select(v => v / 100).ToArray() };
            return c;
        }
    }
}
