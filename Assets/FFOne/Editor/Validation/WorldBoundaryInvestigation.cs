using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FFOne.Editor
{
    // Read-only, pair-specific evidence. A third cell at a corner must not
    // contribute its height to the difference between the other two cells.
    public static class WorldBoundaryInvestigation
    {
        public const string Report="Tools/ContentMigration/world-boundary-surfaces.json";
        public static void Scan() => ScanTo(Report);
        public static void ScanAfter() => ScanTo("Tools/ContentMigration/world-boundary-surfaces-after.json");
        static void ScanTo(string output)
        {
            if(!Application.isBatchMode)throw new InvalidOperationException("Run the boundary audit in batch mode");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var previous=JObject.Parse(File.ReadAllText("Tools/ContentMigration/whole-world-seam-audit.json"));
            var ids=previous["discontinuities"].SelectMany(g=>g["ids"].Values<string>()).Distinct().ToHashSet();
            var report=new JObject();
            foreach(var tile in WholeWorldMigration.Catalog.Tiles.Where(t=>ids.Contains(t.Id)))
            {
                string path=EnvironmentWorldWorkspace.ScenePath(tile.Id),hash=EnvironmentWorldWorkspace.Hash(path);
                var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                try
                {
                    var root=scene.GetRootGameObjects().Single();
                    var surface=root.GetComponentsInChildren<FFOne.Runtime.World.WorldTerrainParameters>(true)
                        .Select(p=>p.GetComponent<MeshFilter>()).Single(m=>m && m.GetComponent<MeshCollider>());
                    var collider=surface.GetComponent<MeshCollider>();Physics.SyncTransforms();
                    var edges=new JObject();
                    foreach(string side in new[]{"left","right","bottom","top"})
                    {
                        var samples=new JArray();
                        for(int n=0;n<=256;n++)
                        {
                            bool vertical=side=="left" || side=="right";
                            float along=n*2;
                            var point=new Vector3(vertical?(side=="left"?tile.Minimum.x+.02f:tile.Maximum.x-.02f):tile.Minimum.x+along,
                                10000,vertical?tile.Minimum.z+along:(side=="bottom"?tile.Minimum.z+.02f:tile.Maximum.z-.02f));
                            var ray=new Ray(point,Vector3.down);
                            bool hit=collider.Raycast(ray,out var terrainHit,20000);
                            bool solid=Physics.Raycast(ray,out var solidHit,20000,~0,QueryTriggerInteraction.Ignore);
                            samples.Add(new JObject{{"along",along},{"terrain",hit?(JToken)terrainHit.point.y:JValue.CreateNull()},
                                {"terrainNormalY",hit?(JToken)terrainHit.normal.y:JValue.CreateNull()},
                                {"solid",solid?(JToken)solidHit.point.y:JValue.CreateNull()},
                                {"solidName",solid?solidHit.collider.name:""}});
                        }
                        edges[side]=samples;
                    }
                    var vertices=surface.sharedMesh.vertices.Select(surface.transform.TransformPoint).ToArray();
                    var boundary=new JArray();
                    for(int i=0;i<vertices.Length;i++)
                    {
                        var p=vertices[i];
                        if(Mathf.Min(Mathf.Abs(p.x-tile.Minimum.x),Mathf.Abs(p.x-tile.Maximum.x),Mathf.Abs(p.z-tile.Minimum.z),Mathf.Abs(p.z-tile.Maximum.z))<=.005f)
                            boundary.Add(new JArray(i,p.x,p.y,p.z));
                    }
                    var water=root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.sharedMaterials.Any(m=>m && m.shader && m.shader.name.IndexOf("water",StringComparison.OrdinalIgnoreCase)>=0))
                        .Select(r=>new {name=r.name,minimum=new[]{r.bounds.min.x,r.bounds.min.y,r.bounds.min.z},maximum=new[]{r.bounds.max.x,r.bounds.max.y,r.bounds.max.z}}).ToArray();
                    var blockers=root.GetComponentsInChildren<Collider>(true).Where(c=>c.enabled && !c.isTrigger && c!=collider)
                        .Select(c=>new{name=c.name,minimum=new[]{c.bounds.min.x,c.bounds.min.y,c.bounds.min.z},maximum=new[]{c.bounds.max.x,c.bounds.max.y,c.bounds.max.z}}).ToArray();
                    report[tile.Id]=JObject.FromObject(new{sceneHash=hash,terrainMesh=AssetDatabase.GetAssetPath(surface.sharedMesh),edges,boundary,water,blockers});
                }
                finally{EditorSceneManager.CloseScene(scene,true);}
                if(EnvironmentWorldWorkspace.Hash(path)!=hash)throw new InvalidDataException("Read-only scan changed scene "+tile.Id);
                WholeWorldMigration.Write(output,report);EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();
                Debug.Log("BOUNDARY SURFACE SCAN "+tile.Id);
            }
        }
        public static void ApplyReviewedRepairs()
        {
            if(!Application.isBatchMode)throw new InvalidOperationException("Run reviewed repairs in batch mode");
            const string receiptPath="Tools/ContentMigration/world-boundary-applied.json";
            var plan=JObject.Parse(File.ReadAllText("Tools/ContentMigration/world-boundary-repair-plan.json"));
            var receipt=File.Exists(receiptPath)?JObject.Parse(File.ReadAllText(receiptPath)):new JObject();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            foreach(var property in ((JObject)plan["cells"]).Properties())
            {
                string id=property.Name,path=EnvironmentWorldWorkspace.ScenePath(id);
                if(receipt[id]!=null)continue; // Never regenerate an adopted authoring surface.
                if(EnvironmentWorldWorkspace.Hash(path)!=(string)property.Value["sceneHash"])throw new InvalidDataException("Authoring changed since review: "+id);
                string meshPath=EnvironmentWorldWorkspace.Folder(id)+"/Terrain/BoundarySurface.asset";
                if(File.Exists(meshPath))throw new IOException("Repair mesh exists without receipt; inspect before recovery: "+id);
                var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                try
                {
                    var surface=scene.GetRootGameObjects().Single().GetComponentsInChildren<FFOne.Runtime.World.WorldTerrainParameters>(true)
                        .Select(p=>p.GetComponent<MeshFilter>()).Single(m=>m && m.GetComponent<MeshCollider>());
                    string originalMesh=AssetDatabase.GetAssetPath(surface.sharedMesh),originalHash=EnvironmentWorldWorkspace.Hash(originalMesh);
                    var mesh=UnityEngine.Object.Instantiate(surface.sharedMesh);var vertices=mesh.vertices;var changed=new HashSet<int>();
                    foreach(var change in property.Value["changes"])
                    {
                        int index=(int)change["index"];var before=surface.transform.TransformPoint(vertices[index]);
                        if(Mathf.Abs(before.y-(float)change["beforeY"])>.0001f)throw new InvalidDataException("Vertex changed since review "+id);
                        vertices[index]=surface.transform.InverseTransformPoint(new Vector3(before.x,(float)change["y"],before.z));changed.Add(index);
                    }
                    mesh.vertices=vertices;mesh.RecalculateBounds();
                    // Update only incident normals/vertex lighting, retaining all unrelated authored channels.
                    var triangles=mesh.triangles;var affected=new HashSet<int>();var sums=new Vector3[vertices.Length];
                    for(int i=0;i<triangles.Length;i+=3)
                    {
                        int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                        if(changed.Contains(a)||changed.Contains(b)||changed.Contains(c)){affected.Add(a);affected.Add(b);affected.Add(c);}
                        var face=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]);sums[a]+=face;sums[b]+=face;sums[c]+=face;
                    }
                    var normals=mesh.normals;var colors=mesh.colors;
                    foreach(int index in affected)
                    {
                        normals[index]=sums[index].normalized;
                        if(colors.Length==vertices.Length){float value=Mathf.Clamp01(.2f+Mathf.Max(Vector3.Dot(normals[index],new Vector3(-.89370085f,.30901706f,.32528046f)),0));colors[index]=new Color(value,value,value,colors[index].a);}
                    }
                    mesh.normals=normals;mesh.colors=colors;mesh.name=id+" Reviewed Boundary Surface";AssetDatabase.CreateAsset(mesh,meshPath);
                    surface.sharedMesh=mesh;var collider=surface.GetComponent<MeshCollider>();collider.sharedMesh=mesh;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(surface);PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
                    EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new IOException("Save failed "+id);
                    if(EnvironmentWorldWorkspace.Hash(originalMesh)!=originalHash)throw new InvalidDataException("Shared source changed "+id);
                    receipt[id]=JObject.FromObject(new{sceneSha256=EnvironmentWorldWorkspace.Hash(path),previousSceneSha256=(string)property.Value["sceneHash"],meshPath,meshGuid=AssetDatabase.AssetPathToGUID(meshPath),originalMesh,originalHash,verticesAdjusted=changed.Count});
                    WholeWorldMigration.Write(receiptPath,receipt);
                }
                finally{EditorSceneManager.CloseScene(scene,true);}
                EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();
            }
            AssetDatabase.SaveAssets();
        }
    }
}
