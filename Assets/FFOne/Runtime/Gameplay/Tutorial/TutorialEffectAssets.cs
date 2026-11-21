using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Content;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace FFOne.Tutorial
{
    // A cheap playback view of published assets. No closure parsing, texture decode
    // or immutable material allocation occurs when an effect instance is spawned.
    internal sealed class TutorialEffectAssets : IDisposable
    {
        internal NativeEffectDefinition Definition;
        internal bool WorldShader;
        internal int Id => Definition.Id;
        internal GameObject Model;
        internal NativeParticleSettings[] Emitters => Definition.Emitters;
        internal NativeEffectFloatCurve[] FloatCurves => Definition.FloatCurves;
        internal Material TrailMaterial => WorldShader?Definition.WorldTrailMaterial:Definition.TutorialTrailMaterial;
        internal int TrailLength => Definition.TrailLength;
        internal float TrailHeight => Definition.TrailHeight;
        internal float TrailStep => Definition.TrailStep;
        internal float MaximumTimer => Definition.MaximumTimer;
        internal float LongestLifetime => Definition.LongestLifetime;
        internal bool DisableUpdate => Definition.DisableUpdate;
        internal static async Task PreloadAsync(int id,CancellationToken cancellation)
        {
            var definition=await ContentAssets.GetAsync<NativeEffectDefinition>(NativeEffectDefinition.Resource(id),cancellation);
            if(!definition)throw new InvalidDataException("Publish native VFX definition ES"+id);
        }
        internal static TutorialEffectAssets Load(int id,string modelPath,bool projectile=false,bool worldShader=false,bool defaultModel=false)
        {
            var definition=ContentAssets.Load<NativeEffectDefinition>(NativeEffectDefinition.Resource(id,projectile));
            if(!definition)throw new InvalidDataException("Publish native VFX definition ES"+id);
            return FromDefinition(definition,modelPath,worldShader,defaultModel);
        }
        internal static TutorialEffectAssets FromDefinition(NativeEffectDefinition definition,string modelPath=null,bool worldShader=false,bool defaultModel=false)
        {
            if(!definition)throw new InvalidDataException("Missing published VFX definition");
            var model=modelPath==null?(defaultModel?definition.Model:null):modelPath==definition.ModelPath?definition.Model:NativeModelAssets.Load(modelPath);
            if(modelPath!=null&&!model)throw new InvalidDataException("Missing accepted effect model "+modelPath);
            var result=new TutorialEffectAssets{Definition=definition,WorldShader=worldShader,Model=model};
            if(definition.TrailLength>0&&!result.TrailMaterial)throw new InvalidDataException("Unsupported published trail material ES"+definition.Id);
            if(definition.Emitters.Any(p=>!(worldShader?p.WorldMaterial:p.TutorialMaterial)))throw new InvalidDataException("Unsupported published particle material ES"+definition.Id);
            return result;
        }
        internal Material ParticleMaterial(NativeParticleSettings plan)=>WorldShader?plan.WorldMaterial:plan.TutorialMaterial;
        internal Material MeshMaterial(Material source)
        {
            if(!source)return source;
            return Definition.MeshMaterials.FirstOrDefault(m=>m.SourceName==source.name.Replace(" (Instance)",""))?.Material??source;
        }
        // Retained for expressions in the tutorial script adapter, not effect data.
        internal static Vector3 Vector(JToken value)=>value==null?Vector3.zero:value is JArray?new Vector3((float)value[0],(float)value[1],(float)value[2]):new Vector3((float)value["x"],(float)value["y"],(float)value["z"]);
        internal static void Release(Object item){if(!item)return;if(Application.isPlaying)Object.Destroy(item);else Object.DestroyImmediate(item);}
        public void Dispose() { }
    }
}
