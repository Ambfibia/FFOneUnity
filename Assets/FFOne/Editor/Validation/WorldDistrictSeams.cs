using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FFOne.Editor
{
    public static class WorldDistrictSeams
    {
        const string Receipt="Tools/ContentMigration/world-district-seams.json";
        sealed class Surface
        {
            public string Id;public Scene Scene;public MeshFilter Filter;public MeshCollider Collider;
            public Vector3[] Vertices;public bool Changed;
        }
        sealed class Vertex { public Surface Surface;public int Index;public Vector3 World; }
        public static void Audit() => Run(false);
        public static void FinishInitialDistrict() => Run(!File.Exists(Receipt));
        static void Run(bool stitch)
        {
            if(!Application.isBatchMode)throw new InvalidOperationException("Run district seam acceptance in batch mode.");
            EditorSceneManager.OpenScene(WorldDistrictAcceptance.DemoPath,OpenSceneMode.Single);
            var expected=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText("Tools/ContentMigration/world-district-scenes.json"))["cells"]
                .ToDictionary(row=>(string)row["id"],row=>(string)row["sha256"]);
            var surfaces=new List<Surface>();var groups=new Dictionary<Vector2,List<Vertex>>();
            try
            {
                foreach(string id in EnvironmentWorldWorkspace.Neighbors("map_12_01"))
                {
                    string path=EnvironmentWorldWorkspace.ScenePath(id);
                    if(stitch && EnvironmentWorldWorkspace.Hash(path)!=expected[id])throw new InvalidOperationException("Scene has author changes; automatic initial seam adjustment refused: "+id);
                    var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                    var cell=scene.GetRootGameObjects().Single().GetComponent<EnvironmentCellBehaviour>();
                    var terrain=cell.transform.Find("Terrain");
                    var surface=new Surface { Id=id,Scene=scene,Filter=terrain.GetComponent<MeshFilter>(),Collider=terrain.GetComponent<MeshCollider>() };
                    surfaces.Add(surface);
                    if(!surface.Filter || !surface.Collider || surface.Filter.sharedMesh!=surface.Collider.sharedMesh)throw new InvalidDataException("Terrain render/collision mismatch "+id);
                    surface.Vertices=surface.Filter.sharedMesh.vertices;
                    for(int i=0;i<surface.Vertices.Length;i++)
                    {
                        var world=surface.Filter.transform.TransformPoint(surface.Vertices[i]);var d=cell.Definition;
                        if(Mathf.Min(Mathf.Abs(world.x-d.Minimum.x),Mathf.Abs(world.x-d.Maximum.x),Mathf.Abs(world.z-d.Minimum.z),Mathf.Abs(world.z-d.Maximum.z))>.005f)continue;
                        var key=new Vector2(Mathf.Round(world.x*1000),Mathf.Round(world.z*1000));
                        if(!groups.TryGetValue(key,out var group))groups.Add(key,group=new List<Vertex>());
                        group.Add(new Vertex { Surface=surface,Index=i,World=world });
                    }
                }
                float maximum=0;int changed=0,shared=0;
                foreach(var group in groups.Values.Where(g=>g.Select(v=>v.Surface.Id).Distinct().Count()>1))
                {
                    shared++;
                    float delta=group.Max(v=>v.World.y)-group.Min(v=>v.World.y);maximum=Mathf.Max(maximum,delta);
                    if(delta>.5f)throw new InvalidDataException("Large terrain discontinuity requires manual review: "+delta+" at "+group[0].World);
                    if(!stitch || delta<=.0001f)continue;
                    float height=group.Average(v=>v.World.y);
                    foreach(var vertex in group)
                    {
                        var point=vertex.World;point.y=height;
                        vertex.Surface.Vertices[vertex.Index]=vertex.Surface.Filter.transform.InverseTransformPoint(point);
                        vertex.Surface.Changed=true;changed++;
                    }
                }
                if(shared==0)throw new InvalidDataException("No shared terrain edges found");
                foreach(var surface in surfaces.Where(s=>s.Changed))
                    if(File.Exists(EnvironmentWorldWorkspace.Folder(surface.Id)+"/Terrain/Surface.asset"))throw new IOException("Authored surface already exists: "+surface.Id);
                foreach(var surface in surfaces.Where(s=>s.Changed))
                {
                    var mesh=UnityEngine.Object.Instantiate(surface.Filter.sharedMesh);mesh.name=surface.Id+" Authored Surface";mesh.vertices=surface.Vertices;mesh.RecalculateBounds();
                    AssetDatabase.CreateAsset(mesh,EnvironmentWorldWorkspace.Folder(surface.Id)+"/Terrain/Surface.asset");
                    surface.Filter.sharedMesh=mesh;surface.Collider.sharedMesh=mesh;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(surface.Filter);PrefabUtility.RecordPrefabInstancePropertyModifications(surface.Collider);
                    EditorSceneManager.MarkSceneDirty(surface.Scene);EditorSceneManager.SaveScene(surface.Scene);
                }
                var report=new { sharedEdgeCoordinates=shared,maximumGapBefore=maximum,verticesAdjusted=changed,cellsWithOwnedSurface=surfaces.Where(s=>s.Changed).Select(s=>s.Id).ToArray(),sourceMeshesModified=false };
                if(stitch)File.WriteAllText(Receipt,Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
                else
                {
                    File.WriteAllText("Tools/ContentMigration/world-district-seam-audit.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
                    if(maximum>.001f)throw new InvalidDataException("Terrain seam gap: "+maximum);
                }
                Debug.Log("WORLD DISTRICT TERRAIN EDGES: "+shared+" shared coordinates; maximum gap="+maximum+"; adjusted vertices="+changed);
            }
            finally { foreach(var surface in surfaces)if(surface.Scene.IsValid() && surface.Scene.isLoaded)EditorSceneManager.CloseScene(surface.Scene,true); }
            AssetDatabase.SaveAssets();
            if(stitch){WorldDistrictAcceptance.Validate();Run(false);}
        }
    }
}
