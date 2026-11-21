using System;
using System.Collections.Generic;
using System.Linq;
using FFOne.Content;
using UnityEngine;

namespace FFOne.World
{
    internal static class WorldBehaviourEffectAssets
    {
        sealed class Shared {internal NativeParticleSettings Value;internal int References;}
        static readonly Dictionary<string,Shared> plans=new();
        internal static int PlanCount=>plans.Count;
        internal static int MaterialCount=>plans.Values.Select(p=>p.Value.WorldMaterial).Distinct().Count();
        internal static int TextureCount=>plans.Values.Select(p=>p.Value.WorldMaterial.mainTexture).Distinct().Count();
        internal static NativeParticleSettings Acquire(NativeParticleSettings definition)
        {
            if(definition==null||!definition.WorldMaterial)throw new InvalidOperationException("Publish native world particle definition");
            string id=definition.Id;
            if(plans.TryGetValue(id,out var shared)){shared.References++;return shared.Value;}
            plans.Add(id,new Shared{Value=definition,References=1});return definition;
        }
        internal static void Release(NativeParticleSettings plan)
        {if(--plans[plan.Id].References==0)plans.Remove(plan.Id);}
    }
}
