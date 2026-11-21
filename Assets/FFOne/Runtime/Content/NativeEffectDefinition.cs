using System;
using UnityEngine;

namespace FFOne.Content
{
    [Serializable] public struct NativeEffectKey
    {
        public float Time;
        public Vector3 Translation;
        public bool Emit;
    }
    [Serializable] public sealed class NativeParticleSettings
    {
        public string Id, Name;
        public int Generation, RenderMode, XTiles=1, YTiles=1;
        public float Lifetime, InitialSize, RandomPosition, RandomAngle, RandomVelocity, Rate, Count, Cycles;
        public Vector3 Plane, InitialVelocity, Force;
        public bool InitialEmit, AnimateColor, RendererEnabled=true;
        public AnimationCurve Width=new(), Height=new(), Rotation=new();
        public Color[] Colors=Array.Empty<Color>();
        public NativeEffectKey[] Keys=Array.Empty<NativeEffectKey>();
        public Material WorldMaterial, TutorialMaterial;
        public Material Material => WorldMaterial;
        public bool Persistent => Lifetime>=300 && Rate<=1f/300 && Count>0 && Count<=1;
    }
    [Serializable] public sealed class NativeEffectFloatCurve
    {
        public string Path, Attribute;
        public AnimationCurve Curve;
    }
    [Serializable] public sealed class NativeEffectMeshMaterial
    {
        public string SourceName;
        public Material Material;
    }
    [CreateAssetMenu(menuName="FFOne/VFX/Effect Definition")]
    public sealed class NativeEffectDefinition : ScriptableObject
    {
        public string Identity, SourceHash, ModelPath;
        public int Id;
        public GameObject Model;
        public bool DisableUpdate=true;
        public float MaximumTimer=-1, LongestLifetime;
        public NativeWorldParticleDefinition[] ElementParticles=Array.Empty<NativeWorldParticleDefinition>();
        public NativeParticleSettings[] Emitters=Array.Empty<NativeParticleSettings>();
        public NativeEffectFloatCurve[] FloatCurves=Array.Empty<NativeEffectFloatCurve>();
        public NativeEffectMeshMaterial[] MeshMaterials=Array.Empty<NativeEffectMeshMaterial>();
        public Material WorldTrailMaterial, TutorialTrailMaterial;
        public int TrailLength;
        public float TrailHeight, TrailStep;
        public static string Resource(int id, bool projectile=false) => "vfx/"+(projectile||id==15||id==391?"projectiles":"effects")+"/es"+id;
        public static string VehicleResource(string style) => "vfx/vehicles/"+style;
    }
}
