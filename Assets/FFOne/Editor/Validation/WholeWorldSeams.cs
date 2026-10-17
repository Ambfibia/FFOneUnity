using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FFOne.Editor
{
    // Holds only boundary samples; at most one complete cell scene is resident.
    public static class WholeWorldSeams
    {
        const string PlanPath="Tools/ContentMigration/whole-world-seam-plan.json";
        const string AppliedPath="Tools/ContentMigration/whole-world-seam-applied.json";
        sealed class Sample { public string Id;public int Index;public float X,Y,Z; }
        public static void Inspect() => Scan(false);
        public static void Plan() => Scan(true);
        static MeshFilter Surface(Scene scene)
        {
            return scene.GetRootGameObjects().Single().GetComponentsInChildren<FFOne.Runtime.World.WorldTerrainParameters>(true)
                .Select(p=>p.GetComponent<MeshFilter>()).Where(m=>m && m.GetComponent<MeshCollider>()).Single();
        }
        static void Scan(bool writePlan)
        {
            if(!Application.isBatchMode)throw new InvalidOperationException("Use batch mode for whole-world seam acceptance");
            EditorSceneManager.OpenScene(WorldDistrictAcceptance.DemoPath);
            var receipt=JArray.Parse(File.ReadAllText(WholeWorldMigration.Receipt)).ToDictionary(r=>(string)r["id"]);
            var groups=new Dictionary<Vector2,List<Sample>>();var hashes=new Dictionary<string,string>();
            var tiles=WholeWorldMigration.Catalog.Tiles;
            foreach(var tile in tiles)
            {
                string path=EnvironmentWorldWorkspace.ScenePath(tile.Id);hashes.Add(tile.Id,EnvironmentWorldWorkspace.Hash(path));
                var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                try
                {
                    var filter=Surface(scene);var collider=filter.GetComponent<MeshCollider>();
                    if(filter.sharedMesh!=collider.sharedMesh || !collider.enabled)throw new InvalidDataException("Terrain collision mismatch "+tile.Id);
                    var vertices=filter.sharedMesh.vertices;
                    for(int i=0;i<vertices.Length;i++)
                    {
                        var p=filter.transform.TransformPoint(vertices[i]);
                        if(Mathf.Min(Mathf.Abs(p.x-tile.Minimum.x),Mathf.Abs(p.x-tile.Maximum.x),Mathf.Abs(p.z-tile.Minimum.z),Mathf.Abs(p.z-tile.Maximum.z))>.005f)continue;
                        var key=new Vector2(Mathf.Round(p.x*1000),Mathf.Round(p.z*1000));
                        if(!groups.TryGetValue(key,out var group))groups.Add(key,group=new List<Sample>());
                        group.Add(new Sample{Id=tile.Id,Index=i,X=p.x,Y=p.y,Z=p.z});
                    }
                }
                finally{EditorSceneManager.CloseScene(scene,true);}
                EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();
                Debug.Log("WHOLE WORLD SEAM SCAN "+tile.Id);
            }
            var pairs=new Dictionary<string,int>();var gaps=new List<object>();var fixes=new Dictionary<string,List<object>>();
            int shared=0;float maximum=0;var conflicts=new List<string>();var retained=new List<object>();
            foreach(var group in groups.Values.Where(g=>g.Select(p=>p.Id).Distinct().Count()>1))
            {
                shared++;float gap=group.Max(p=>p.Y)-group.Min(p=>p.Y);maximum=Mathf.Max(maximum,gap);
                var ids=group.Select(p=>p.Id).Distinct().OrderBy(id=>id).ToArray();
                for(int a=0;a<ids.Length;a++)for(int b=a+1;b<ids.Length;b++)
                {string key=ids[a]+"/"+ids[b];pairs.TryGetValue(key,out int count);pairs[key]=count+1;}
                if(gap<=.001f)continue;
                gaps.Add(new{x=group[0].X,z=group[0].Z,gap,ids});
                var fixedSamples=group.Where(p=>(string)receipt[p.Id]["kind"]!="created").ToArray();
                if(fixedSamples.Select(p=>p.Id).Distinct().Count()>1 && fixedSamples.Max(p=>p.Y)-fixedSamples.Min(p=>p.Y)>.001f)
                {conflicts.Add("Existing authored edges disagree at "+group[0].X+","+group[0].Z);continue;}
                float target=fixedSamples.Length>0 ? fixedSamples[0].Y : group.Average(p=>p.Y);
                if(gap>.5f){retained.Add(new{x=group[0].X,z=group[0].Z,gap,ids,reason="Existing source height discontinuity; relief preserved"});continue;}
                foreach(var sample in group.Where(p=>(string)receipt[p.Id]["kind"]=="created"))
                {
                    if(Mathf.Abs(sample.Y-target)<.00001f)continue;
                    if(!fixes.TryGetValue(sample.Id,out var changes))fixes.Add(sample.Id,changes=new List<object>());
                    changes.Add(new {index=sample.Index,x=sample.X,y=target,z=sample.Z});
                }
            }
            var edges=new List<object>();
            foreach(var a in tiles)foreach(var b in tiles.Where(b=>(b.X==a.X+1 && b.Z==a.Z)||(b.Z==a.Z+1 && b.X==a.X)))
            {
                string key=string.CompareOrdinal(a.Id,b.Id)<0 ? a.Id+"/"+b.Id : b.Id+"/"+a.Id;
                pairs.TryGetValue(key,out int count);edges.Add(new{a=a.Id,b=b.Id,sharedCoordinates=count});
                if(count<2)conflicts.Add("Missing shared edge samples "+key);
            }
            var report=new{cells=tiles.Length,sharedEdgeCoordinates=shared,maximumGap=maximum,adjacentEdges=edges,discontinuities=gaps,retainedSourceDiscontinuities=retained,conflicts};
            WholeWorldMigration.Write("Tools/ContentMigration/whole-world-seam-audit.json",report);
            if(writePlan)
            {
                if(File.Exists(PlanPath))throw new IOException("Seam plan already exists; audit is read-only. Never replace an accepted plan automatically.");
                if(conflicts.Count>0)throw new InvalidDataException("Seam review needed: "+conflicts.Count+" conflicts (see audit)");
                foreach(var id in fixes.Keys)
                    if(hashes[id]!=(string)receipt[id]["acceptedSha256"])throw new InvalidDataException("Author edits detected; refusing seam update "+id);
                WholeWorldMigration.Write(PlanPath,new{sceneHashes=hashes,fixes,retainedSourceDiscontinuities=retained,maximumGapBefore=maximum,sharedEdgeCoordinates=shared,protectedExisting=receipt.Values.Where(r=>(string)r["kind"]!="created").Select(r=>(string)r["id"]).ToArray()});
            }
            Debug.Log("WHOLE WORLD SEAMS: "+shared+" coordinates; max="+maximum+" conflicts="+conflicts.Count);
        }
        public static void Apply()
        {
            if(!Application.isBatchMode)throw new InvalidOperationException("Use batch mode");
            EditorSceneManager.OpenScene(WorldDistrictAcceptance.DemoPath);
            var plan=JObject.Parse(File.ReadAllText(PlanPath));
            var applied=File.Exists(AppliedPath) ? JObject.Parse(File.ReadAllText(AppliedPath)) : new JObject();
            foreach(var property in ((JObject)plan["fixes"]).Properties())
            {
                string id=property.Name,path=EnvironmentWorldWorkspace.ScenePath(id);
                if(applied[id]!=null)continue; // Accepted or subsequently authored surfaces are never regenerated.
                if(EnvironmentWorldWorkspace.Hash(path)!=(string)plan["sceneHashes"][id])throw new InvalidDataException("Scene changed since seam review: "+id);
                string meshPath=EnvironmentWorldWorkspace.Folder(id)+"/Terrain/WorldSurface.asset";
                if(File.Exists(meshPath))throw new IOException("Surface exists without completion receipt; inspect before recovery: "+id);
                var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                try
                {
                    var filter=Surface(scene);var mesh=UnityEngine.Object.Instantiate(filter.sharedMesh);var vertices=mesh.vertices;
                    foreach(var change in property.Value)
                        vertices[(int)change["index"]]=filter.transform.InverseTransformPoint(new Vector3((float)change["x"],(float)change["y"],(float)change["z"]));
                    mesh.name=id+" Authored Surface";mesh.vertices=vertices;mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,meshPath);
                    filter.sharedMesh=mesh;var collider=filter.GetComponent<MeshCollider>();collider.sharedMesh=mesh;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(filter);PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
                    EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
                    applied[id]=JObject.FromObject(new{sceneSha256=EnvironmentWorldWorkspace.Hash(path),verticesAdjusted=property.Value.Count(),meshGuid=AssetDatabase.AssetPathToGUID(meshPath)});
                    WholeWorldMigration.Write(AppliedPath,applied);
                }
                finally{EditorSceneManager.CloseScene(scene,true);}
                EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();Debug.Log("WHOLE WORLD STITCHED "+id);
            }
            AssetDatabase.SaveAssets();Scan(false);
            var audit=JObject.Parse(File.ReadAllText("Tools/ContentMigration/whole-world-seam-audit.json"));
            if(audit["conflicts"].Any())throw new InvalidDataException("Unresolved world seam conflicts");
            foreach(var gap in audit["discontinuities"])
            {
                var original=plan["retainedSourceDiscontinuities"].FirstOrDefault(p=>(float)p["x"]==(float)gap["x"] && (float)p["z"]==(float)gap["z"] && JToken.DeepEquals(p["ids"],gap["ids"]));
                if(original==null || Mathf.Abs((float)original["gap"]-(float)gap["gap"])>.001f)throw new InvalidDataException("New or unresolved small seam gap: "+gap);
            }
            audit["smallGapRepairPassed"]=true;audit["largeSourceReliefPreserved"]=true;
            WholeWorldMigration.Write("Tools/ContentMigration/whole-world-seam-audit.json",audit);
        }
    }
}
