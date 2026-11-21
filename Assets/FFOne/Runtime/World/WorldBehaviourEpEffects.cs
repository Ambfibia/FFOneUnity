using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Tutorial;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FFOne.World
{
    internal sealed class WorldBehaviourEpEffects : IDisposable
    {
        readonly WorldBehaviour owner;
        readonly Dictionary<int, TutorialEffectAssets> meshAssets = new Dictionary<int, TutorialEffectAssets>();
        
        readonly WorldBehaviourParticles particles;
        readonly Camera view;
        internal WorldBehaviourEpEffects(WorldBehaviour parent, WorldBehaviourParticles playback, Camera camera)
        { owner = parent; particles = playback; view = camera; }
        internal void Bind(JToken record, Transform parent)
        {
            foreach (var element in record["particleElements"] ?? new JArray())
            {
                owner.ElementEffectRecords++;
                int id = (int)element["particleIndex"]; if (id == 0) continue;
                try
                {
                    var instance = new GameObject("EP effect " + id); instance.transform.SetParent(parent, false);
                    instance.transform.localPosition = WorldBehaviour.Vector(element["position"]);
                    var q = element["rotation"];
                    instance.transform.localRotation = new Quaternion((float)q["x"], (float)q["y"], (float)q["z"], (float)q["w"]).normalized;
                    float scale = (float)element["scale"]; instance.transform.localScale = Vector3.one * scale;
                    if (id == 594)
                    {
                        var definition=owner.NativeEffects.Elements.Single(e=>e.Id==id);
                        particles.BindElement(definition, instance.transform, (string)record["node"]);
                    }
                    else
                    {
                        string model = id switch { 461 => "launcher_symbol", 464 => "jumppad_S_power", 465 => "jumppad_M_power",
                            466 => "jumppad_B_power", 533 => "run_symbol", 534 => "roket_symbol", _ => null };
                        if (model == null) throw new InvalidDataException("Unsupported world EP effect " + id);
                        if (!meshAssets.TryGetValue(id, out var assets))
                        {
                            var definition=owner.NativeEffects.Elements.Single(e=>e.Id==id);
                            assets = TutorialEffectAssets.FromDefinition(definition,defaultModel:true);
                            if(!assets.Model)throw new InvalidDataException("Missing published EP model ES"+id);
                            meshAssets.Add(id, assets);
                        }
                        instance.AddComponent<TutorialEffectPlayback>().Initialize(assets, Mathf.Abs(instance.transform.lossyScale.x), view);
                        foreach (var target in instance.GetComponentsInChildren<Transform>(true))
                        {
                            if (target.name.Contains("BillboardCamera", StringComparison.Ordinal)) owner.AddElementBillboard(target, 0);
                            else if (target.name.Contains("BillboardUp", StringComparison.Ordinal)) owner.AddElementBillboard(target, 1);
                        }
                    }
                    owner.ElementEffectBindings++;
                }
                catch (Exception error) { owner.Problem("EP effect " + id + ": " + error.Message, true); }
            }
        }
        public void Dispose()
        { foreach (var assets in meshAssets.Values) assets.Dispose(); meshAssets.Clear(); }
    }
}
