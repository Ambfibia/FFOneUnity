using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FFOne.Content;
using FFOne.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FFOne.Editor
{
    // Only the editor decodes retained closure payloads and shader source. Player
    // definitions contain typed values and ordinary Unity asset references.
    public static class NativeEffectPublication
    {
        public const string Root="Assets/FFOne/Content/VFX/Published";
        static readonly Dictionary<string,UnityEngine.Object> created=new();
        static readonly Dictionary<string,NativeEffectDefinition> effects=new();
        static Dictionary<string,string> models;
        static string Hash(string value)
        {using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
        static string Fingerprint(JToken value)=>Hash(value.ToString(Formatting.None));
        static string PathFor(string folder,string name,string extension)=>Root+"/"+folder+"/"+name+extension;
        public static void PrepareDirectories()
        {
            bool changed=false;
            foreach(string folder in new[]{"Effects","Projectiles","Particles","Textures","Materials","Cells","Vehicles"})
                if(!Directory.Exists(Root+"/"+folder)){Directory.CreateDirectory(Root+"/"+folder);changed=true;}
            if(changed)AssetDatabase.Refresh();
        }
        static T Existing<T>(string path) where T:UnityEngine.Object
        {
            if(created.TryGetValue(path,out var cached)&&cached)return cached as T;
            var asset=AssetDatabase.LoadAssetAtPath<T>(path);if(asset)created[path]=asset;return asset;
        }
        static T Save<T>(T value,string path) where T:UnityEngine.Object
        {AssetDatabase.CreateAsset(value,path);created[path]=value;return value;}
        static Vector3 Vector(JToken t)=>t==null?Vector3.zero:t is JArray?new Vector3((float)t[0],(float)t[1],(float)t[2]):new Vector3((float)t["x"],(float)t["y"],(float)t["z"]);
        static AnimationCurve Curve(JToken d)
        {
            var keys=d?["m_Curve"] as JArray??new JArray();
            return new AnimationCurve(keys.Select(k=>new Keyframe((float)k["time"],(float)k["value"],(float)k["inSlope"],(float)k["outSlope"])).ToArray()){
                preWrapMode=WrapMode.ClampForever,postWrapMode=WrapMode.ClampForever};
        }
        static Color Packed(JToken p){uint v=(uint)p;return new Color((v&255)/255f,((v>>8)&255)/255f,((v>>16)&255)/255f,(v>>24)/255f);}
        static JToken Component(JArray objects,long actor,string type)=>objects.SingleOrDefault(o=>(string)o["objectType"]==type&&(long?)o["value"]?["m_GameObject"]?["pathId"]==actor)?["value"];
        static JToken Object(JArray objects,long path,string type)=>objects.Single(o=>(string)o["objectType"]==type&&(long)o["pathId"]==path);
        static NativeEffectKey[] Keys(JToken keys)=>(keys??new JArray()).Select(k=>new NativeEffectKey{Time=(float)k["time"],Translation=Vector(k["translate"]),Emit=(int)k["genType"]!=0}).ToArray();
        public static NativeParticleSettings CompileParticle(JArray objects,JToken settings,JToken keys,string id,bool strictWorld)
        {
            long actor=(long)settings["m_GameObject"]["pathId"];
            var renderer=Component(objects,actor,"ParticleRenderer")??throw new InvalidDataException("Missing particle renderer "+id);
            var animator=Component(objects,actor,"ParticleAnimator")??throw new InvalidDataException("Missing particle animator "+id);
            int generation=(int)settings["m_iGenType"],mode=(int)renderer["m_StretchParticles"];
            if(generation<0||generation>11||mode!=0&&mode!=4&&mode!=5)throw new InvalidDataException("Unsupported particle "+id);
            if(strictWorld&&((float?)animator["damping"]!=1||(float?)animator["sizeGrow"]!=0||(bool?)animator["stopSimulation"]==true
                ||Vector(animator["rndForce"])!=Vector3.zero||Vector(animator["localRotationAxis"])!=Vector3.zero||Vector(animator["worldRotationAxis"])!=Vector3.zero
                ||(settings["textureKeys"] as JArray)?.Count>0))throw new InvalidDataException("Unsupported world animator "+id);
            var result=new NativeParticleSettings {Id=id,Name=(string)settings["particleName"],Generation=generation,RenderMode=mode,
                Lifetime=(float)settings["lifeTime"],InitialSize=(float)settings["initialSize"],RandomPosition=(float)settings["randomPosition"],
                RandomAngle=(float)settings["randomAngle"],RandomVelocity=(float)settings["randomVelocity"],Rate=(float)settings["generationsPerSecond"],Count=(float)settings["numberPerGeneration"],
                Plane=Vector(settings["plane"]),InitialVelocity=Vector(settings["initVelocity"]),Force=Vector(animator["force"]),InitialEmit=(int?)settings["emit"]!=0,
                RendererEnabled=(bool?)renderer["m_Enabled"]??true,Width=Curve(renderer["m_WidthCurve"]),Height=Curve(renderer["m_HeightCurve"]),Rotation=Curve(renderer["m_RotationCurve"]),
                XTiles=Math.Max(1,(int)renderer["UV Animation"]["x Tile"]),YTiles=Math.Max(1,(int)renderer["UV Animation"]["y Tile"]),Cycles=(float)renderer["UV Animation"]["cycles"],
                AnimateColor=(bool)animator["Does Animate Color?"],Colors=Enumerable.Range(0,5).Select(i=>Packed(animator["colorAnimation["+i+"]"]["rgba"])).ToArray(),Keys=Keys(keys)};
            if(result.Lifetime<=0||result.Rate<0||result.Count<0)throw new InvalidDataException("Invalid particle timing "+id);
            result.WorldMaterial=ParticleMaterial(objects,renderer,true,strictWorld,false,1);
            result.TutorialMaterial=strictWorld?null:ParticleMaterial(objects,renderer,false,false,false,1);
            return result;
        }
        static Texture2D Texture(JToken source,bool world)
        {
            var d=source["value"];var recipe=new JObject { ["value"]=d.DeepClone(),["worldSampler"]=world };
            // Names and source object IDs do not prevent sharing identical native data.
            ((JObject)recipe["value"]).Remove("m_Name");
            string path=PathFor("Textures",Fingerprint(recipe),".asset");var existing=Existing<Texture2D>(path);if(existing)return existing;
            int format=(int)d["m_TextureFormat"];
            var texture=new Texture2D((int)d["m_Width"],(int)d["m_Height"],format switch {10=>TextureFormat.DXT1,12=>TextureFormat.DXT5,2=>TextureFormat.ARGB4444,3=>TextureFormat.RGB24,4=>TextureFormat.RGBA32,_=>throw new InvalidDataException("Unsupported FX texture format "+format)},(bool)d["m_MipMap"],false){
                name=(string)d["m_Name"],filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Repeat};
            if(world&&d["m_TextureSettings"] is JObject sampler)
            {
                int filter=(int?)sampler["m_FilterMode"]??1,wrap=(int?)sampler["m_WrapMode"]??0;
                if(filter>=0&&filter<=2)texture.filterMode=(FilterMode)filter;
                if(wrap>=0&&wrap<=3)texture.wrapMode=(TextureWrapMode)wrap;
                texture.anisoLevel=Mathf.Clamp((int?)sampler["m_Aniso"]??1,0,16);
            }
            try{texture.LoadRawTextureData(Convert.FromBase64String((string)d["image data"]["base64"]));texture.Apply(false,true);return Save(texture,path);}
            catch{UnityEngine.Object.DestroyImmediate(texture);throw;}
        }
        static BlendMode Blend(string n)=>n.ToLowerInvariant() switch {"zero"=>BlendMode.Zero,"one"=>BlendMode.One,"srccolor"=>BlendMode.SrcColor,"srcalpha"=>BlendMode.SrcAlpha,
            "dstcolor"=>BlendMode.DstColor,"dstalpha"=>BlendMode.DstAlpha,"oneminussrcalpha"=>BlendMode.OneMinusSrcAlpha,"oneminussrccolor"=>BlendMode.OneMinusSrcColor,
            "oneminusdstcolor"=>BlendMode.OneMinusDstColor,"oneminusdstalpha"=>BlendMode.OneMinusDstAlpha,_=>throw new InvalidDataException("Unsupported FX blend "+n)};
        internal static Material ParticleMaterial(JArray objects,JToken renderer,bool world,bool worldSampler,bool trail,float gain)
        {
            var source=Object(objects,(long)renderer["m_Materials"][0]["pathId"],"Material")["value"];
            string script=(string)Object(objects,(long)source["m_Shader"]["pathId"],"Shader")["value"]["m_Script"];
            var blend=Regex.Match(script,world?@"Blend\s+(\w+)\s+(\w+)":@"Blend\s+(One|SrcColor|SrcAlpha)\s+One\b",RegexOptions.IgnoreCase);
            if(!blend.Success){if(!world)return null;throw new InvalidDataException("Missing authored FX blend");}
            var props=source["m_SavedProperties"];var env=props["m_TexEnvs"].Single(e=>(string)e[0]["name"]=="_MainTex")[1];
            var texture=Texture(Object(objects,(long)env["m_Texture"]["pathId"],"Texture2D"),worldSampler);
            var recipe=new JObject { ["source"]=source.DeepClone(),["script"]=script,["texture"]=AssetDatabase.GetAssetPath(texture),["world"]=world,["worldSampler"]=worldSampler,["trail"]=trail,["gain"]=gain };
            string path=PathFor("Materials",Fingerprint(recipe),".mat");var existing=Existing<Material>(path);if(existing)return existing;
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(world?"Assets/FFOne/Worlds/Overworld/Settings/Particle.shader":"Assets/FFOne/Worlds/Tutorial/Content/Particle.shader");
            if(!shader)throw new InvalidDataException("Missing published FX shader");
            var result=new Material(shader){name=(string)source["m_Name"],mainTexture=texture};
            var tint=props["m_Colors"].FirstOrDefault(c=>(string)c[0]["name"]=="_TintColor")?[1];
            result.SetColor("_TintColor",tint==null?Color.white:new Color((float)tint["r"],(float)tint["g"],(float)tint["b"],(float)tint["a"]));
            result.SetFloat("_SrcBlend",(float)Blend(blend.Groups[1].Value));result.SetFloat("_LegacyAccumulationGain",gain);
            if(world){result.SetFloat("_DstBlend",(float)Blend(blend.Groups[2].Value));result.SetFloat("_ZWrite",Regex.IsMatch(script,@"ZWrite\s+Off",RegexOptions.IgnoreCase)?0:1);
                result.SetFloat("_Cull",Regex.IsMatch(script,@"Cull\s+Off",RegexOptions.IgnoreCase)?0:worldSampler&&Regex.IsMatch(script,@"Cull\s+Front",RegexOptions.IgnoreCase)?1:2);}
            if(worldSampler)
            {
                var queue=Regex.Match(script,@"""Queue""\s*=\s*""(Transparent|Geometry)([+-]\d+)?",RegexOptions.IgnoreCase);
                result.renderQueue=queue.Success?(queue.Groups[1].Value.Equals("Geometry",StringComparison.OrdinalIgnoreCase)?2000:3000)+(queue.Groups[2].Success?int.Parse(queue.Groups[2].Value):0):3000;
                result.mainTextureOffset=new Vector2((float?)env["m_Offset"]?["x"]??0,(float?)env["m_Offset"]?["y"]??0);
                result.mainTextureScale=new Vector2((float?)env["m_Scale"]?["x"]??1,(float?)env["m_Scale"]?["y"]??1);
            }
            else{var queue=Regex.Match(script,"Transparent\\+(\\d+)");result.renderQueue=3000+(queue.Success?int.Parse(queue.Groups[1].Value):0);}
            if(trail)result.EnableKeyword("FFONE_NATIVE_WOUND_TRAIL");
            return Save(result,path);
        }
        static string ModelPath(int id,bool projectile)
        {
            models??=ContentAssets.Load<NativeModelCatalog>("NativeEditable/Index").Bindings.GroupBy(b=>b.OriginalResource).ToDictionary(g=>g.Key,g=>g.First().EditableResource,StringComparer.Ordinal);
            string prefix=(projectile?"map/shared/projectiles/effects/models/es":"map/shared/effects/models/es")+id+"/";
            string found=models.Keys.SingleOrDefault(k=>k.StartsWith(prefix,StringComparison.Ordinal));
            if(found==null&&!projectile){var statuses=NativeData.Object("effects/skills/catalog");found=(string)statuses["effects"]?.SingleOrDefault(e=>(int)e["id"]==id)?["mesh"];}
            return found;
        }
        static NativeEffectMeshMaterial[] MeshMaterials(JArray objects,GameObject model)
        {
            if(!model)return Array.Empty<NativeEffectMeshMaterial>();var result=new List<NativeEffectMeshMaterial>();
            foreach(var original in model.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m).Distinct())
            {
                var d=objects.FirstOrDefault(o=>(string)o["objectType"]=="Material"&&(string)o["name"]==original.name)?["value"];if(d==null)continue;
                string script=(string)Object(objects,(long)d["m_Shader"]["pathId"],"Shader")["value"]["m_Script"];
                string hash=Hash(AssetDatabase.GetAssetPath(original)+"|"+Fingerprint(d)+"|"+script);
                string path=PathFor("Materials","mesh-"+hash,".mat");var material=Existing<Material>(path);
                if(!material)
                {
                    material=new Material(original){name=original.name};var blend=Regex.Match(script,@"Blend\s+(\w+)\s+(\w+)");
                    if(blend.Success){material.SetFloat("_SrcBlend",(float)Blend(blend.Groups[1].Value));material.SetFloat("_DstBlend",(float)Blend(blend.Groups[2].Value));}
                    ApplyAdditiveMeshState(material, script, d);
                    material.SetFloat("_ZWrite",Regex.IsMatch(script,@"ZWrite\s+Off",RegexOptions.IgnoreCase)?0:1);material.SetFloat("_Cull",Regex.IsMatch(script,@"Cull\s+Off",RegexOptions.IgnoreCase)?0:2);
                    foreach(var t in d["m_SavedProperties"]?["m_TexEnvs"]??new JArray())if((string)t[0]["name"]=="_MainTex"){
                        var v=t[1];material.SetVector("_MainTexPivot",new Vector4((float)v["m_Pivot"]["x"],(float)v["m_Pivot"]["y"],0,0));material.SetFloat("_MainTexRotation",(float)v["m_Rotation"]);}
                    Save(material,path);
                }
                result.Add(new NativeEffectMeshMaterial{SourceName=original.name,Material=material});
            }
            return result.ToArray();
        }
        static void ApplyAdditiveMeshState(Material material, string script, JToken source)
        {
            string name = Regex.Match(script, "^\\s*Shader\\s+\"([^\"]+)\"").Groups[1].Value;
            if (!NativeWorldMaterialImport.IsActorAdditiveMesh(name)) return;

            // A republished effect may clone an older editable model material. These
            // fixed-function passes write RGB only, leaving the target alpha untouched.
            material.SetFloat("_ColorMask", (int)(ColorWriteMask.Red | ColorWriteMask.Green | ColorWriteMask.Blue));
            material.SetFloat("_FixedFogMode", NativeWorldMaterialImport.FixedFogMode(name, true));
            if (name is "normal_blendOneOne_zwriteOff_cullOff" or "normal_blendSrcalphaOneTest_cullOff")
                material.SetFloat("_AmbientEnabled", 1);
            if (name == "normal_blendSrcalphaOneTest_cullOff")
            {
                var saved = source["m_SavedProperties"]?["m_Floats"]?.FirstOrDefault(p => (string)p[0]["name"] == "_Cutoff");
                material.SetFloat("_Cutoff", (float?)saved?[1] ?? 0);
            }
        }
        public static NativeEffectDefinition PublishEffect(int id,bool projectile)
        {
            string key=NativeEffectDefinition.Resource(id,projectile);if(effects.TryGetValue(key,out var prepared)&&prepared)return prepared;
            string identity=(projectile?"map/shared/projectiles/effects/es":"map/shared/effects/es")+id+".closure";
            return PublishEffect(id, projectile, identity, NativeData.Object(identity), null, false);
        }

        internal static NativeEffectDefinition PublishNanoEffect(int id, JObject closure, GameObject model)
            => PublishEffect(id, false, "map/shared/effects/es" + id + ".closure", closure, model, true);

        static NativeEffectDefinition PublishEffect(int id,bool projectile,string identity,JObject document,GameObject model,bool nano)
        {
            string key=NativeEffectDefinition.Resource(id,projectile);
            string hash="v2:"+Fingerprint(document);
            string path=PathFor(projectile?"Projectiles":"Effects","ES"+id,".asset");var definition=Existing<NativeEffectDefinition>(path);
            if(definition&&definition.SourceHash==hash&&(!nano||definition.Model==model)){effects[key]=definition;return definition;}
            if(!definition)definition=Save(ScriptableObject.CreateInstance<NativeEffectDefinition>(),path);
            var objects=(JArray)document["objects"];var controller=objects.Where(o=>(string)o["objectType"]=="MonoBehaviour").Select(o=>o["value"]).FirstOrDefault(v=>v["particles"]!=null&&v["maxTimer"]!=null);
            definition.name="ES"+id;definition.Identity=identity;definition.Id=id;definition.ModelPath=nano?null:ModelPath(id,projectile);
            definition.Model=nano?model:definition.ModelPath==null?null:NativeModelAssets.Load(definition.ModelPath);
            definition.MaximumTimer=(float?)controller?["maxTimer"]??-1;definition.LongestLifetime=(float?)controller?["longestLifeTime"]??0;definition.DisableUpdate=((int?)controller?["disableUpdate"]??1)!=0;
            var particles=new List<NativeParticleSettings>();
            foreach(var source in objects.Where(o=>(string)o["objectType"]=="MonoBehaviour"&&o["value"]?["numberPerGeneration"]!=null))
            {
                var settings=source["value"];long actor=(long)settings["m_GameObject"]["pathId"];
                if((int)settings["m_iGenType"]==12)
                {
                    var trail=objects.Select(o=>o["value"]).Single(v=>v["trailLength"]!=null&&(long?)v["m_GameObject"]?["pathId"]==actor);
                    definition.TrailLength=Mathf.CeilToInt((float)settings["numberPerGeneration"]);definition.TrailHeight=(float)settings["initialSize"];definition.TrailStep=(float)trail["fTimes"];
                    definition.WorldTrailMaterial=ParticleMaterial(objects,Component(objects,actor,"MeshRenderer"),true,false,true,1);
                    definition.TutorialTrailMaterial=ParticleMaterial(objects,Component(objects,actor,"MeshRenderer"),false,false,true,1);continue;
                }
                if(controller==null)throw new InvalidDataException("Missing FX controller ES"+id);
                var keys=controller["particles"].Single(p=>(long)p["particlePrefab"]["pathId"]==actor)["scriptKeys"];
                var plan=CompileParticle(objects,settings,keys,identity+"#"+actor,false);
                if(id==668){plan.WorldMaterial=ParticleMaterial(objects,Component(objects,actor,"ParticleRenderer"),true,false,false,2);plan.TutorialMaterial=ParticleMaterial(objects,Component(objects,actor,"ParticleRenderer"),false,false,false,2);}
                particles.Add(plan);
            }
            definition.Emitters=particles.ToArray();
            if(id==594&&!projectile)
            {
                definition.ElementParticles=objects.Where(o=>(string)o["objectType"]=="MonoBehaviour"&&o["value"]?["numberPerGeneration"]!=null).Select(o=>{
                    var settings=o["value"];long actor=(long)settings["m_GameObject"]["pathId"];
                    var gameObject=Object(objects,actor,"GameObject");
                    string identity="world-ep:"+(string)gameObject["asset"]+"#"+actor;
                    var closure=new JObject{["id"]=identity,["root"]=new JObject{["pathId"]=actor},["objects"]=objects.DeepClone()};
                    var published=PublishParticle(closure);published.Settings.Keys=Keys(controller["particles"].Single(p=>(long)p["particlePrefab"]["pathId"]==actor)["scriptKeys"]);EditorUtility.SetDirty(published);return published;
                }).ToArray();
            }definition.FloatCurves=objects.Where(o=>(string)o["objectType"]=="AnimationClip").SelectMany(o=>o["value"]?["m_FloatCurves"] as JArray??new JArray())
                .Select(c=>new NativeEffectFloatCurve{Path=(string)c["path"],Attribute=(string)c["attribute"],Curve=Curve(c["curve"])}).ToArray();
            definition.MeshMaterials=MeshMaterials(objects,definition.Model);definition.SourceHash=hash;EditorUtility.SetDirty(definition);effects[key]=definition;return definition;
        }
        public static NativeWorldParticleDefinition PublishParticle(JToken closure)
        {
            string hash=Fingerprint(closure),path=PathFor("Particles",hash,".asset");var asset=Existing<NativeWorldParticleDefinition>(path);if(asset)return asset;
            var objects=(JArray)closure["objects"];long actor=(long)closure["root"]["pathId"];
            var settings=objects.Where(o=>(string)o["objectType"]=="MonoBehaviour"&&o["value"]?["numberPerGeneration"]!=null).Select(o=>o["value"]).Single(s=>(long)s["m_GameObject"]["pathId"]==actor);
            asset=ScriptableObject.CreateInstance<NativeWorldParticleDefinition>();asset.name=(string)settings["particleName"];asset.SourceHash=hash;
            asset.Settings=CompileParticle(objects,settings,null,(string)closure["id"],true);return Save(asset,path);
        }
        public static void PrepareCell(WorldCellDefinition cell)
        {
            var document=NativeData.Object("map/tiles/"+cell.TileId+"/behaviour");string hash="v2:"+Fingerprint(document);
            string path=PathFor("Cells",cell.TileId,".asset");var asset=Existing<NativeWorldEffectLibrary>(path);
            if(asset&&asset.SourceHash==hash){if(cell.Effects!=asset){cell.Effects=asset;EditorUtility.SetDirty(cell);}return;}
            if(!asset)asset=Save(ScriptableObject.CreateInstance<NativeWorldEffectLibrary>(),path);
            asset.name=cell.TileId+" effects";asset.SourceHash=hash;asset.Particles=(document["effectPrefabClosures"]??new JArray()).Select(PublishParticle).ToArray();
            asset.Elements=(document["effectEmitters"]??new JArray()).SelectMany(e=>e["particleElements"]??new JArray()).Select(e=>(int)e["particleIndex"]).Where(i=>i!=0).Distinct().Select(i=>PublishEffect(i,false)).ToArray();
            document.Remove("effectPrefabClosures");asset.Behaviour=document.ToString(Formatting.None);EditorUtility.SetDirty(asset);cell.Effects=asset;EditorUtility.SetDirty(cell);
        }
        static void Vehicles()
        {
            var catalog=NativeData.Object("effects/vehicles/catalog");
            foreach(var plan in catalog["trails"])
            {
                string style=(string)plan["name"],path=PathFor("Vehicles",style,".asset");var definition=Existing<NativeEffectDefinition>(path);
                string hash="v2:"+Fingerprint(plan);if(definition&&definition.SourceHash==hash)continue;
                if(!definition)definition=Save(ScriptableObject.CreateInstance<NativeEffectDefinition>(),path);
                definition.name="Vehicle "+style;definition.Identity=NativeEffectDefinition.VehicleResource(style);definition.SourceHash=hash;definition.Id=style=="standard"?15:style=="board"?835:836;
                if(style=="standard"){var original=PublishEffect(15,true);definition.Model=original.Model;definition.ModelPath=original.ModelPath;definition.FloatCurves=original.FloatCurves;definition.MeshMaterials=original.MeshMaterials;definition.Emitters=original.Emitters;}
                definition.TrailLength=(int)plan["length"];definition.TrailHeight=(float)plan["height"];definition.TrailStep=(float)plan["interpolation_step"];
                string materialPath=PathFor("Materials","vehicle-"+hash.Replace(":","-"),".mat");var material=Existing<Material>(materialPath);
                if(!material)
                {
                    material=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FFOne/Worlds/Tutorial/Content/Particle.shader")){name="Vehicle "+style+" exhaust",renderQueue=3000};
                    string texture=(string)plan["texture"][0];material.mainTexture=ContentAssets.Load<Texture2D>(texture.Substring(0,texture.LastIndexOf('.')));
                    if(!material.mainTexture)throw new FileNotFoundException("Missing vehicle trail texture "+texture);
                    var tint=plan["material_tint"];material.SetColor("_TintColor",new Color((float)tint[0],(float)tint[1],(float)tint[2],(float)tint[3]));material.SetFloat("_SrcBlend",1);material.EnableKeyword("FFONE_NATIVE_WOUND_TRAIL");Save(material,materialPath);
                }
                definition.TutorialTrailMaterial=material;EditorUtility.SetDirty(definition);
            }
        }
        static void PruneUnusedResources()
        {
            var roots=AssetDatabase.FindAssets("t:NativeEffectDefinition t:NativeWorldEffectLibrary",new[]{Root})
                .Select(AssetDatabase.GUIDToAssetPath).ToArray();
            var referenced=new HashSet<string>(AssetDatabase.GetDependencies(roots,true),StringComparer.Ordinal);
            int removed=0;
            foreach(string guid in AssetDatabase.FindAssets("t:Material t:Texture2D",new[]{Root}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);
                if(!path.StartsWith(Root+"/",StringComparison.Ordinal)||referenced.Contains(path))continue;
                if(AssetDatabase.DeleteAsset(path)){created.Remove(path);removed++;}
            }
            Debug.Log("NATIVE VFX REMOVED UNUSED RESOURCES: "+removed);
        }
        [MenuItem("FFOne/VFX/Publish Native Effect Assets")]
        public static void RebuildAll()
        {
            PrepareDirectories();effects.Clear();models=null;
            var sources=AssetDatabase.FindAssets("t:NativeDataDocument",new[]{"Assets/FFOne/Data/Tables/map/shared"})
                .Select(g=>AssetDatabase.LoadAssetAtPath<NativeDataDocument>(AssetDatabase.GUIDToAssetPath(g)))
                .Select(d=>d.Identity).Where(i=>Regex.IsMatch(i,@"^map/shared/(projectiles/)?effects/es\d+\.closure\.json$")).ToArray();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach(string identity in sources)
                    PublishEffect(int.Parse(Path.GetFileName(identity).Split('.')[0].Substring(2)),identity.Contains("/projectiles/"));
                Vehicles();
                foreach(string guid in AssetDatabase.FindAssets("t:WorldCellDefinition",new[]{"Assets/FFOne/Worlds"}))
                    PrepareCell(AssetDatabase.LoadAssetAtPath<WorldCellDefinition>(AssetDatabase.GUIDToAssetPath(guid)));
                foreach(var asset in created.Values.Where(a=>a))AssetDatabase.SaveAssetIfDirty(asset);
            }
            finally{AssetDatabase.StopAssetEditing();}
            AssetDatabase.SaveAssets();PruneUnusedResources();AssetDatabase.SaveAssets();Debug.Log("NATIVE VFX PUBLISHED: "+effects.Count+" source effects; "+created.Count+" shared native assets.");
        }
    }
}
