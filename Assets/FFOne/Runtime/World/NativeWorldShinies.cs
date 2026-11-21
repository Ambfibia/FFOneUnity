using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Network;
using UnityEngine;

namespace FFOne.World
{
    // An interest ID survives asset loading. The server alone consumes the egg and grants its reward.
    internal sealed class NativeWorldShinies : MonoBehaviour
    {
        sealed class Definition
        {
            internal string Resource;
            internal bool TreasureOnly;
            internal int PickupEffect;
        }
        sealed class Egg
        {
            internal ShinyInterest Interest;
            internal Definition Definition;
            internal GameObject Root;
            internal bool Touched;
        }
        static Dictionary<int, Definition> definitions;
        readonly Dictionary<int, Egg> eggs = new();
        NetworkWorldController world;
        Egg pending;
        bool treasureSight;

        internal void Initialize(NetworkWorldController owner) => world = owner;

        static Dictionary<int, Definition> Read(NativeDataDocument d)
        {
            int Child(int p, string key) => p < 0 ? -1 : d.Children(p).Where(i => d.Nodes[i].Key == key).DefaultIfEmpty(-1).First();
            string Field(int p, string key) { int i = Child(p, key); return i < 0 ? null : d.Nodes[i].Value; }
            int Number(int p, string key) => int.TryParse(Field(p, key), out int n) ? n : 0;
            int table = Child(0, "m_pShinyTable");
            var meshes = d.Children(Child(table, "m_pShinyMeshData")).ToArray();
            var result = new Dictionary<int, Definition>();
            int index = 0;
            foreach (var row in d.Children(Child(table, "m_pShinyData")))
            {
                int id = index++, mesh = Number(row, "m_iMesh");
                if (mesh <= 0 || mesh >= meshes.Length) continue;
                string model = Field(meshes[mesh], "m_pstrMMeshModelString")?.Trim();
                if (string.IsNullOrEmpty(model) || model == "null") continue;
                int effect = Number(row, "m_iGetEffect");
                result[id] = new Definition {
                    Resource = $"characters/shinies/{model}/{model}",
                    TreasureOnly = Field(meshes[mesh], "ffoneTreasureOnly") == "true",
                    PickupEffect = effect > 0 ? effect : 397
                };
            }
            // Studio-authored egg variants can share a model and choose independent visibility.
            var tables = Child(Child(0, "_ffone"), "tables");
            if (tables < 0) return result;
            var original = new Dictionary<int, Definition>(result);
            foreach (int extra in d.Children(tables))
            {
                int authored = Child(Child(Child(extra, "value"), "ffone_egg_tips"), "EggTypes");
                if (authored < 0) continue;
                foreach (int row in d.Children(authored))
                {
                    if (!original.TryGetValue(Number(row, "ModelType"), out var source)) continue;
                    result[Number(row, "Id")] = new Definition { Resource = source.Resource,
                        TreasureOnly = Field(row, "Visible") == "false" || source.TreasureOnly, PickupEffect = source.PickupEffect };
                }
            }
            return result;
        }

        internal void Apply(ShinyInterest value)
        {
            eggs.TryGetValue(value.Id, out var previous);
            if (value.Removed)
            {
                if (previous != null) { eggs.Remove(value.Id); if (previous.Root) Destroy(previous.Root); }
                return;
            }
            var at = WorldCoordinates.FromServer(value.X, value.Y, value.Z);
            if (previous != null && previous.Interest.Type == value.Type)
            {
                previous.Interest = value;
                if (previous.Root) previous.Root.transform.position = at;
                return;
            }
            if (previous?.Root) Destroy(previous.Root);
            definitions ??= ContentAssets.Read<NativeDataDocument, Dictionary<int, Definition>>(NativeData.Resource("data/tables/xdt"), Read);
            if (!definitions.TryGetValue(value.Type, out var definition)) { eggs.Remove(value.Id); return; }
            var egg = new Egg { Interest = value, Definition = definition };
            eggs[value.Id] = egg;
            _ = LoadModel(egg, destroyCancellationToken);
        }

        async Task LoadModel(Egg egg, CancellationToken cancellation)
        {
            try
            {
                var prefab = await NativeModelAssets.PreloadAsync(egg.Definition.Resource, cancellation);
                if (cancellation.IsCancellationRequested || !world || !eggs.TryGetValue(egg.Interest.Id, out var current) || current != egg) return;
                if (!prefab) { world.Replicas?.ContentFailure("Missing published E.G.G. model " + egg.Definition.Resource); return; }
                egg.Root = Instantiate(prefab, transform);
                egg.Root.name = "E.G.G. " + egg.Interest.Id;
                egg.Root.transform.SetPositionAndRotation(WorldCoordinates.FromServer(egg.Interest.X, egg.Interest.Y, egg.Interest.Z), Quaternion.identity);
                var animation = egg.Root.GetComponentInChildren<Animation>(true);
                if (animation && animation["stand1"] != null) { animation["stand1"].wrapMode = WrapMode.Loop; animation.Play("stand1"); }
                Show(egg);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (world) world.Replicas?.ContentFailure("E.G.G. model: " + error.Message); }
        }

        void Show(Egg egg)
        {
            bool visible = egg.Interest.Map == world.MapNumber && (!egg.Definition.TreasureOnly || treasureSight);
            if (egg.Root && egg.Root.activeSelf != visible) egg.Root.SetActive(visible);
        }

        internal void Tick(bool alive, bool searching, Func<int, Task> send)
        {
            treasureSight = searching;
            if (!world || !world.Player) return;
            foreach (var egg in eggs.Values)
            {
                if (!egg.Root) continue;
                Show(egg);
                bool contact = egg.Interest.Map == world.MapNumber && (egg.Root.transform.position - world.Player.position).sqrMagnitude <= 1f;
                if (!contact) egg.Touched = false;
                if (!contact || egg.Touched || pending != null || !alive || !world.IsReady || !world.MovementEnabled || world.Riding || world.Ziplining) continue;
                egg.Touched = true;
                pending = egg;
                _ = Send(egg, send);
            }
        }

        async Task Send(Egg egg, Func<int, Task> send)
        {
            try { await send(egg.Interest.Id); }
            catch (Exception error)
            {
                if (pending == egg) pending = null;
                if (error is not OperationCanceledException && this) Debug.LogWarning("E.G.G. pickup: " + error.Message);
            }
        }

        internal void Acknowledged(bool success, Action<string> play, Action onPickedUp = null)
        {
            var egg = pending; pending = null;
            if (!success || egg == null) return;
            onPickedUp?.Invoke();
            play("egg_pickup");
            var at = WorldCoordinates.FromServer(egg.Interest.X, egg.Interest.Y, egg.Interest.Z);
            var definition = WorldCombatEffects.Find(egg.Definition.PickupEffect);
            if (definition)
            {
                NativeCombatEffect.Spawn(transform, definition, world.View, at);
                return;
            }
            if (ContentAssets.Known(NativeEffectDefinition.Resource(egg.Definition.PickupEffect)))
            {
                NativeCombatEffect.Spawn(transform, egg.Definition.PickupEffect, 1, false, world.View, -1, at);
                return;
            }
            if (egg.Definition.PickupEffect != 397) return;
            var prefab = NativeModelAssets.Load("effects/shinies/pickup/shineni_ai");
            if (!prefab) return;
            var pickup = Instantiate(prefab, at, Quaternion.identity, transform);
            var animation = pickup.GetComponentInChildren<Animation>(true);
            if (animation && animation.clip)
            {
                animation[animation.clip.name].wrapMode = WrapMode.Once;
                animation.Play();
                Destroy(pickup, animation.clip.length);
            }
            else Destroy(pickup, 1);
        }

        internal void Clear()
        {
            foreach (var egg in eggs.Values) if (egg.Root) Destroy(egg.Root);
            eggs.Clear(); pending = null;
        }
    }
}
