using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FFOne.World
{
    // Explicit development-player acceptance. Uses the actual player capsule
    // against loaded Addressable scenes; never snaps to ground while walking.
    public sealed class WorldBoundaryWalkSmoke : MonoBehaviour
    {
        [Serializable] public sealed class Probe { public string id,a,b,expected; public float[] center,direction; }
        [Serializable] public sealed class Plan { public Probe[] probes; }
        [Serializable] public sealed class Observation
        {
            public string id,a,b,expected,error; public bool crossed,grounded; public float progress,lateralError,startY,endY;
            public string[] contacts; public int airFrames;
        }
        [Serializable] sealed class Result { public bool passed; public string error; public List<Observation> observations=new(); }
        public readonly HashSet<string> Contacts=new();
        Result result=new();
        static string Argument(string name)
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,name);
            if(at<0 || at+1>=args.Length)throw new ArgumentException("Missing "+name);return args[at+1];
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            if(!Environment.GetCommandLineArgs().Contains("--world-boundary-smoke"))return;
            var go=new GameObject("World boundary walk acceptance");DontDestroyOnLoad(go);go.AddComponent<WorldBoundaryWalkSmoke>();
        }
        IEnumerator Start()
        {
            var routine=Exercise();
            while(true)
            {
                object next;
                try{if(!routine.MoveNext())break;next=routine.Current;}
                catch(Exception e){result.error=e.ToString();Debug.LogException(e);break;}
                yield return next;
            }
            Save();Debug.Log(result.passed?"WORLD BOUNDARY WALK PASSED":"WORLD BOUNDARY WALK FAILED");
            Application.Quit(result.passed?0:1);
        }
        void Save(){string path=Argument("--world-boundary-report");Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(result,true));}
        IEnumerator Exercise()
        {
            var plan=JsonUtility.FromJson<Plan>(File.ReadAllText(Argument("--world-boundary-plan")));
            WorldStreamer stream=null;float until=Time.realtimeSinceStartup+180;
            while(!stream || !stream.Catalog)
            {
                stream=FindFirstObjectByType<WorldStreamer>();if(Time.realtimeSinceStartup>until)throw new TimeoutException("World initialization");yield return null;
            }
            foreach(var probe in plan.probes)
            {
                var center=new Vector3(probe.center[0],probe.center[1],probe.center[2]);
                var direction=new Vector3(probe.direction[0],0,probe.direction[2]).normalized;
                stream.Anchor=center;until=Time.realtimeSinceStartup+180;
                while(!stream.IsReady || !stream.LoadedSceneTileIds.Contains(probe.a) || !stream.LoadedSceneTileIds.Contains(probe.b))
                {
                    if(!string.IsNullOrEmpty(stream.LastError))throw new InvalidOperationException(stream.LastError);
                    if(Time.realtimeSinceStartup>until)throw new TimeoutException("Boundary load "+probe.id);yield return null;
                }
                yield return null;Physics.SyncTransforms();
                var start=center-direction*6;
                var observation=new Observation{id=probe.id,a=probe.a,b=probe.b,expected=probe.expected};
                if(!Physics.Raycast(new Vector3(start.x,10000,start.z),Vector3.down,out var ground,20000,~0,QueryTriggerInteraction.Ignore))
                    throw new InvalidOperationException("No starting ground "+probe.id);
                start.y=ground.point.y+.04f;observation.startY=start.y;
                var actor=new GameObject("Boundary probe capsule");actor.transform.position=start;
                var contact=actor.AddComponent<WorldBoundaryWalkContact>();
                var motor=actor.AddComponent<CharacterController>();motor.height=1.8f;motor.radius=.45f;motor.center=Vector3.up*.9f;
                motor.stepOffset=.4f;motor.slopeLimit=50;motor.minMoveDistance=0;
                for(int tick=0;tick<240;tick++)
                {
                    motor.Move(direction*.075f+Vector3.down*.15f);
                    if(!motor.isGrounded)observation.airFrames++;
                    var delta=actor.transform.position-start;
                    observation.progress=Vector3.Dot(delta,direction);
                    if(observation.progress>=11.9f)break;
                    if(tick%4==0)yield return null;
                }
                observation.endY=actor.transform.position.y;observation.grounded=motor.isGrounded;
                var horizontal=actor.transform.position-start;horizontal.y=0;
                observation.lateralError=(horizontal-direction*Vector3.Dot(horizontal,direction)).magnitude;
                observation.crossed=observation.progress>=11.9f && observation.grounded && observation.lateralError<.65f;
                observation.contacts=contact.Names.OrderBy(x=>x).ToArray();
                result.observations.Add(observation);Destroy(actor);yield return null;
                Save();Debug.Log("BOUNDARY WALK "+probe.id+" crossed="+observation.crossed+" progress="+observation.progress);
            }
            result.passed=result.observations.Count==plan.probes.Length && result.observations.All(o=>o.expected=="observe" || o.crossed==(o.expected=="pass"));
        }
    }
    public sealed class WorldBoundaryWalkContact : MonoBehaviour
    {
        public readonly HashSet<string> Names=new();
        void OnControllerColliderHit(ControllerColliderHit hit){Names.Add(hit.collider.name);}
    }
}
