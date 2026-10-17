using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FFOne.World
{
    // Water spans authored tile boundaries; Rust admits the surrounding 3x3
    // water surfaces even when their full terrain/behaviour tile is absent.
    public sealed class WorldNeighborWater : MonoBehaviour
    {
        sealed class Resident
        {
            public GameObject Root;
            public bool Ready, Retiring;
            public int Visuals;
            public readonly List<Material> Owned = new();
            public readonly List<FFOne.Content.NativeModelAssets.Request> Leases = new();
        }
        public WorldStreamer Stream;
        public string Error { get; private set; }
        readonly Dictionary<string, Resident> resident = new();
        Vector2Int previousCell = new(int.MinValue, int.MinValue);
        int previousVersion = -1;
        public bool Ready => resident.Values.All(r => r.Ready);
        public int VisualCount => resident.Values.Sum(r => r.Visuals);

        public void Tick()
        {
            var cell = new Vector2Int(Mathf.FloorToInt(Stream.Anchor.x / 512), Mathf.FloorToInt(Stream.Anchor.z / 512));
            if (cell == previousCell && previousVersion == Stream.PresentedVersion) return;
            previousCell = cell; previousVersion = Stream.PresentedVersion;
            var current = Stream.Catalog.Tiles.OrderBy(t => t.DistanceSquared(Stream.Anchor)).ThenBy(t => t.Z).ThenBy(t => t.X).First();
            // Prefer the containing cell when multiple overflow footprints have zero distance.
            current = Stream.Catalog.Tiles.FirstOrDefault(t => t.X == cell.x && t.Z == cell.y) ?? current;
            var desired = Stream.Catalog.NeighborWater.Where(t => Stream.MapNumber == 0 && Math.Abs(t.X-current.X) <= 1 && Math.Abs(t.Z-current.Z) <= 1 && !Stream.TilePresented(t.Id)).ToDictionary(t => t.Id);
            foreach (var key in resident.Keys.ToArray())
                if (!desired.ContainsKey(key))
                {
                    var item = resident[key]; item.Retiring = true; item.Root.SetActive(false);
                    foreach (var material in item.Owned) Destroy(material);
                    Destroy(item.Root); foreach(var lease in item.Leases)lease.Dispose(); resident.Remove(key);
                }
            foreach (var entry in desired)
                if (!resident.ContainsKey(entry.Key))
                {
                    var item = new Resident { Root = new GameObject(entry.Key + " neighbor water") };
                    item.Root.transform.SetParent(transform, false); resident.Add(entry.Key, item);
                    StartCoroutine(Admit(entry.Value, item));
                }
        }
        IEnumerator Admit(WorldWaterTile tile, Resident item)
        {
            foreach (var placement in tile.Visuals)
            {
                var request = FFOne.Content.NativeModelAssets.LoadAsync(placement.ModelResource);
                item.Leases.Add(request);
                yield return request;
                if (item.Retiring) yield break;
                if (!request.asset) { Error = "Missing neighbor water model: " + placement.ModelResource; yield break; }
                var instance = Instantiate((GameObject)request.asset, item.Root.transform, false);
                WorldMaterialState.Prepare(instance);
                WorldModelBasis.ApplySource(instance);
                instance.name = placement.Name;
                instance.transform.SetLocalPositionAndRotation(placement.Position, placement.Rotation);
                instance.transform.localScale = placement.Scale;
                if (!Stream.Optimized)
                    foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                        foreach (var material in renderer.materials) { material.enableInstancing = false; item.Owned.Add(material); }
                item.Visuals++;
            }
            item.Ready = true;
        }
        void OnDestroy()
        {
            StopAllCoroutines();
            foreach(var item in resident.Values)foreach(var lease in item.Leases)lease.Dispose();
            foreach (var item in resident.Values) foreach (var material in item.Owned) if (material) Destroy(material);
        }
    }
}
