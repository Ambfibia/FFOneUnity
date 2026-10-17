using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FFOne.World
{
    // Explicit command-line acceptance scenario; inert in normal gameplay.
    public sealed class WorldDistrictSmoke : MonoBehaviour
    {
        bool launcher;
        static bool WholeWorld => Environment.GetCommandLineArgs().Contains("--whole-world-smoke") || Environment.GetCommandLineArgs().Contains("--whole-world-demo");
        [Serializable] sealed class Step { public string label; public Vector3 point; public bool render; }
        [Serializable] sealed class Observation { public string label; public string[] cells; public bool terrainHit; public float groundHeight; }
        [Serializable] sealed class SourceGap { public string cell; public int count; public string[] notes; }
        [Serializable] sealed class Result { public bool passed; public string error; public string[] visited; public int maxResident,groundHits,steps,renderedFrames; public long peakAllocatedBytes; public bool unloaded,reloaded,cancelledLoadReleased; public int expectedCells,expectedSteps,edgeCrossings; public string[] centersVisited; public List<Observation> observations=new List<Observation>(); public List<SourceGap> sourceCoverage=new List<SourceGap>(); }
        Result result=new Result();
        RenderTexture capture;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            var args=Environment.GetCommandLineArgs();
            if(!WholeWorld && !args.Contains("--world-district-smoke") && !args.Contains("--world-district-demo"))return;
            var go=new GameObject("District launcher");DontDestroyOnLoad(go);go.AddComponent<WorldDistrictSmoke>().launcher=true;
        }
        IEnumerator Start()
        {
            if(launcher)
            {
                yield return null;
                string scene=WholeWorld ? "WorldOverview" : "WorldDistrict";
                if(SceneManager.GetActiveScene().name!=scene)yield return SceneManager.LoadSceneAsync(scene,LoadSceneMode.Single);
                Destroy(gameObject);yield break;
            }
            if(!Environment.GetCommandLineArgs().Contains("--world-district-smoke") && !Environment.GetCommandLineArgs().Contains("--whole-world-smoke"))yield break;
            var routine=Exercise();
            // The exercised routine owns waits directly so exceptions are reported in JSON.
            while(true)
            {
                object next;
                try { if(!routine.MoveNext())break;next=routine.Current; }
                catch(Exception error){result.error=error.ToString();Debug.LogException(error);break;}
                yield return next;
            }
            string path=ReportPath();Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(result,true));
            Debug.Log(result.passed ? "WORLD DISTRICT PLAYER PASSED" : "WORLD DISTRICT PLAYER FAILED: "+result.error);
            Application.Quit(result.passed ? 0 : 1);
        }
        static string ReportPath()
        {
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--world-district-report");
            return index>=0 && index+1<args.Length ? Path.GetFullPath(args[index+1]) : Path.Combine(Application.persistentDataPath,"WorldDistrictPlayer.json");
        }
        IEnumerator Exercise()
        {
            var stream=GetComponent<WorldStreamer>();var view=GetComponent<WorldViewer>();if(view)view.enabled=false;
            float deadline=Time.realtimeSinceStartup+180;
            while(!stream.Catalog){if(Time.realtimeSinceStartup>deadline)throw new TimeoutException("Catalog startup");yield return null;}
            var refs=stream.Catalog.Tiles;var visited=new HashSet<string>();var centers=new HashSet<string>();result.expectedCells=WholeWorld ? 170 : 9;
            if(refs.Length!=result.expectedCells)throw new InvalidOperationException("Unexpected catalog size: "+refs.Length);
            capture=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);capture.Create();stream.View.targetTexture=capture;
            var points=new List<Step>();
            if(WholeWorld)
            {
                foreach(var tile in refs.OrderBy(t=>t.Z).ThenBy(t=>t.Z%2==0 ? t.X : -t.X))
                {
                    points.Add(new Step{label="center:"+tile.Id,point=(tile.Minimum+tile.Maximum)*.5f,render=true});
                    foreach(var neighbor in refs.Where(b=>(b.X==tile.X+1 && b.Z==tile.Z)||(b.Z==tile.Z+1 && b.X==tile.X)))
                    {
                        bool east=neighbor.X!=tile.X;
                        var midpoint=east ? new Vector3(tile.Maximum.x,0,(tile.Minimum.z+tile.Maximum.z)*.5f) : new Vector3((tile.Minimum.x+tile.Maximum.x)*.5f,0,tile.Maximum.z);
                        var delta=east ? Vector3.right*2 : Vector3.forward*2;
                        points.Add(new Step{label="edge:"+tile.Id+"/"+neighbor.Id+":before",point=midpoint-delta});
                        points.Add(new Step{label="edge:"+tile.Id+"/"+neighbor.Id+":after",point=midpoint+delta});
                        result.edgeCrossings++;
                    }
                }
            }
            else
            {
                points.Add(new Step{label="entry",point=new Vector3(6374.3188f,-56.4746f,668.2039f)});
                foreach(float offset in new[]{-16f,16f})
                {
                    points.Add(new Step{point=new Vector3(6144+offset,30,768)});points.Add(new Step{point=new Vector3(6656+offset,30,768)});
                    points.Add(new Step{point=new Vector3(6400,30,512+offset)});points.Add(new Step{point=new Vector3(6400,30,1024+offset)});
                }
                points.AddRange(refs.Select(t=>new Step{label="center:"+t.Id,point=(t.Minimum+t.Maximum)*.5f}));
            }
            result.expectedSteps=points.Count;
            foreach(var step in points)
            {
                var point=step.point;stream.Anchor=point;
                stream.View.transform.position=point+new Vector3(-70,110,-70);stream.View.transform.LookAt(point);
                deadline=Time.realtimeSinceStartup+180;
                yield return null;
                while(!stream.IsReady)
                {
                    if(!string.IsNullOrEmpty(stream.LastError))throw new InvalidOperationException(stream.LastError);
                    if(Time.realtimeSinceStartup>deadline)throw new TimeoutException("Cell streaming at "+point+" pending="+stream.PendingTiles);
                    result.peakAllocatedBytes=Math.Max(result.peakAllocatedBytes,UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong());
                    yield return null;
                }
                if(stream.LoadedSceneTileIds.Length!=stream.LoadedTileIds.Length)throw new InvalidOperationException("Legacy tile appeared inside scene district");
                foreach(string id in stream.LoadedSceneTileIds)visited.Add(id);
                var cells=FindObjectsByType<EnvironmentCellBehaviour>(FindObjectsInactive.Include);
                foreach(var cell in cells.Where(c=>c.Runtime && c.Runtime.SourceCoverageNotes.Count>0))
                    if(!result.sourceCoverage.Any(c=>c.cell==cell.TileId))result.sourceCoverage.Add(new SourceGap{cell=cell.TileId,count=cell.Runtime.SourceCoverageGaps,notes=cell.Runtime.SourceCoverageNotes.ToArray()});
                if(cells.GroupBy(c=>c.TileId).Any(g=>g.Count()>1))throw new InvalidOperationException("Duplicate cell scene");
                var ray=new Ray(new Vector3(point.x,10000,point.z),Vector3.down);
                var groundHits=cells.SelectMany(c=>c.GetComponentsInChildren<FFOne.Runtime.World.WorldTerrainParameters>(true)).Select(p=>p.GetComponent<MeshCollider>()).Where(c=>c && c.enabled)
                    .Select(c=>{RaycastHit hit;return new {hit=c.Raycast(ray,out hit,20000),height=hit.point.y};}).Where(h=>h.hit).ToArray();
                bool terrainHit=groundHits.Length>0;float height=terrainHit ? groundHits.Max(h=>h.height) : point.y;
                if(terrainHit)result.groundHits++;
                if(WholeWorld && !terrainHit)throw new InvalidOperationException("Terrain collision missing at "+step.label+" "+point);
                if(step.label!=null && step.label.StartsWith("center:"))
                {string id=step.label.Substring(7);if(!stream.LoadedSceneTileIds.Contains(id))throw new InvalidOperationException("Center cell absent: "+id);centers.Add(id);}
                result.observations.Add(new Observation{label=step.label,cells=stream.LoadedSceneTileIds,terrainHit=terrainHit,groundHeight=height});
                if(WholeWorld){point.y=height;stream.View.transform.position=point+new Vector3(-70,110,-70);stream.View.transform.LookAt(point);}
                result.maxResident=Math.Max(result.maxResident,stream.ResidentTiles);result.steps++;
                if(WholeWorld ? step.render : result.steps==1 || result.steps==5 || result.steps==9)
                {
                    for(int frame=0;frame<3;frame++)yield return null;
                    stream.View.Render();
                    var previous=RenderTexture.active;RenderTexture.active=capture;
                    var image=new Texture2D(capture.width,capture.height,TextureFormat.RGB24,false);
                    try
                    {
                        image.ReadPixels(new Rect(0,0,capture.width,capture.height),0,0);image.Apply();
                        var pixels=image.GetPixels32();int varied=pixels.Select(c=>(c.r>>3)*1024+(c.g>>3)*32+(c.b>>3)).Distinct().Count();
                        if((!WholeWorld && varied<32) || pixels.All(c=>c.r<2 && c.g<2 && c.b<2))throw new InvalidOperationException("World camera produced an empty/flat frame: "+varied+" colors");
                        string path=result.steps==1 ? Path.ChangeExtension(ReportPath(),"png") : Path.ChangeExtension(ReportPath(),null)+"-step"+result.steps+".png";
                        if(!WholeWorld || centers.Count%15==1 || centers.Count==170)File.WriteAllBytes(path,image.EncodeToPNG());result.renderedFrames++;
                    }
                    finally {RenderTexture.active=previous;Destroy(image);}
                }
                Debug.Log("WORLD DISTRICT STEP "+result.steps+": "+point+" scenes="+string.Join(",",stream.LoadedSceneTileIds));
                result.visited=visited.OrderBy(x=>x).ToArray();result.centersVisited=centers.OrderBy(x=>x).ToArray();File.WriteAllText(ReportPath(),JsonUtility.ToJson(result,true));
                for(int frame=0;frame<3;frame++)yield return null;
            }
            if(visited.Count!=result.expectedCells || centers.Count!=result.expectedCells || result.groundHits==0)throw new InvalidOperationException("Incomplete district/collision coverage");
            result.visited=visited.OrderBy(x=>x).ToArray();
            stream.Anchor=new Vector3(-100000,0,-100000);deadline=Time.realtimeSinceStartup+120;
            do { if(Time.realtimeSinceStartup>deadline)throw new TimeoutException("Scene unload");yield return null; }while(stream.ResidentTiles>0);
            if(FindObjectsByType<EnvironmentCellBehaviour>(FindObjectsInactive.Include).Length!=0)throw new InvalidOperationException("Cell survived unload");
            result.unloaded=true;stream.Anchor=new Vector3(6374.3188f,-56.4746f,668.2039f);deadline=Time.realtimeSinceStartup+180;yield return null;
            while(!stream.IsReady){if(!string.IsNullOrEmpty(stream.LastError))throw new InvalidOperationException(stream.LastError);if(Time.realtimeSinceStartup>deadline)throw new TimeoutException("Reload");yield return null;}
            result.reloaded=stream.LoadedSceneTileIds.Contains("map_12_01");
            // Destroy the owner while a new scene request is in flight.
            stream.Anchor=new Vector3(6912,100,1280);deadline=Time.realtimeSinceStartup+120;yield return null;
            while(stream.PendingSceneLoads==0)
            {
                if(!string.IsNullOrEmpty(stream.LastError))throw new InvalidOperationException(stream.LastError);
                if(Time.realtimeSinceStartup>deadline)throw new TimeoutException("Could not exercise an in-flight scene cancellation");
                yield return null;
            }
            Destroy(stream);yield return null;
            deadline=Time.realtimeSinceStartup+120;
            int stable=0;
            while(stable<120)
            {
                if(Time.realtimeSinceStartup>deadline)throw new TimeoutException("Scene ownership release on destruction");
                stable=FindObjectsByType<EnvironmentCellBehaviour>(FindObjectsInactive.Include).Length==0 ? stable+1 : 0;
                yield return null;
            }
            result.cancelledLoadReleased=true;result.passed=result.reloaded && result.maxResident<=WorldStreamer.MaximumRoots;
        }
        void OnDestroy(){if(capture){capture.Release();Destroy(capture);}}
    }
}
