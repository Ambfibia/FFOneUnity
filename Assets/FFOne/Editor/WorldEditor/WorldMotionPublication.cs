using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FFOne.Content;
using FFOne.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    // Published clips remain ordinary editable Unity assets. Re-publishing unchanged
    // source retains authored curve edits and never opens an authored scene.
    public static class WorldMotionPublication
    {
        const string DirectoryPath = "Assets/FFOne/Content/Environment/Animation/Imported";
        static readonly Dictionary<string,AnimationClip> published = new();
        [MenuItem("FFOne/World/Publish Native Environment Motion")]
        public static void RebuildAll() => WorldCellPublication.RebuildAll();
        public static void PrepareDirectory()
        {
            if (Directory.Exists(DirectoryPath)) return;
            Directory.CreateDirectory(DirectoryPath); AssetDatabase.Refresh();
        }
        static string Hash(string text)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
        public static void Prepare(WorldCellDefinition cell)
        {
            PrepareDirectory();
            var document = JObject.Parse(NativeData.Read("map/tiles/" + cell.TileId + "/behaviour"));
            var bindings = new List<WorldMotionBinding>();
            foreach (var source in document["animationClips"] ?? new JArray())
            {
                var channels = (source["channels"] ?? new JArray()).ToArray();
                var clips = channels.Select((channel, i) => Publish((string)source["id"] + "_" + i, channel, () => CompileChannel(channel))).ToArray();
                bindings.Add(new WorldMotionBinding { SourceId = (string)source["id"], SourceHash = Hash(source.ToString(Formatting.None)), Channels = clips });
            }
            var waypoints = (document["waypoints"] ?? new JArray()).ToDictionary(w => (string)w["node"]);
            foreach (var platform in (document["triggers"] ?? new JArray()).Where(t => (string)t["kind"] == "platform" && (bool?)t["enabled"] != false))
            {
                var path = new List<Vector3>(); string next = (string)platform["head"];
                for (int i = 0, count = Math.Min(4096, ((int?)platform["waypointCount"] ?? 0) + 1); i < count && next != null; i++)
                {
                    if (!waypoints.TryGetValue(next, out var point)) throw new InvalidDataException("Missing platform waypoint " + next);
                    path.Add(Vector(point["point"])); next = (string)point["next"];
                }
                string id = "platform:" + (string)platform["node"];
                var recipe = new JObject { ["record"] = platform.DeepClone(), ["path"] = JArray.FromObject(path.Select(p => new[] { p.x,p.y,p.z })) };
                bindings.Add(new WorldMotionBinding { SourceId = id, SourceHash = Hash(recipe.ToString(Formatting.None)),
                    Channels = new[] { Publish(id, recipe, () => CompilePlatform(platform, path.ToArray())) } });
            }
            if ((document["effectEmitters"] ?? new JArray()).Any(e => WorldBehaviour.IsRotatingNanoSign((string)e["effectName"])))
                bindings.Add(new WorldMotionBinding { SourceId = "world-spin", SourceHash = "1", Channels = new[] {
                    Publish("world-spin", new JValue(1), () => { var c = NewClip(); c.SetCurve("", typeof(Transform), "localEulerAnglesRaw.y", AnimationCurve.Linear(0,0,4,360)); return c; }) } });
            bool changed = cell.MotionVersion != 1 || cell.Motion == null || cell.Motion.Length != bindings.Count;
            if (!changed) for (int i = 0; i < bindings.Count; i++)
                if (cell.Motion[i].SourceId != bindings[i].SourceId || cell.Motion[i].SourceHash != bindings[i].SourceHash || !cell.Motion[i].Channels.SequenceEqual(bindings[i].Channels)) { changed = true; break; }
            if (!changed) return;
            cell.MotionVersion = 1; cell.Motion = bindings.ToArray(); EditorUtility.SetDirty(cell);
        }
        static AnimationClip Publish(string id, JToken recipe, Func<AnimationClip> compile)
        {
            string digest = Hash("native-motion-v2:" + recipe.ToString(Formatting.None));
            // Hash includes source data. Identical channels share one asset across cells.
            string path = DirectoryPath + "/" + digest + ".anim";
            if (published.TryGetValue(path,out var created) && created) return created;
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing) { published[path]=existing; return existing; }
            var clip = compile(); clip.name = id.Replace(':', '_').Replace('/', '_');
            AssetDatabase.CreateAsset(clip, path); published[path]=clip; return clip;
        }
        static AnimationClip NewClip() => new AnimationClip { legacy = true, wrapMode = WrapMode.ClampForever, frameRate = 60 };
        public static AnimationClip CompileChannel(JToken channel)
        {
            var clip = NewClip(); string property = (string)channel["property"];
            string target = property == "translation" ? "localPosition" : property == "rotation" ? "localRotation" : property == "scale" ? "localScale" : throw new InvalidDataException("Unknown motion property " + property);
            int axes = property == "rotation" ? 4 : 3;
            for (int axis = 0; axis < axes; axis++)
            {
                float sign = property == "translation" && axis == 0 || property == "rotation" && (axis == 1 || axis == 2) ? -1 : 1;
                var times = (JArray)channel["times"]; var keys = new Keyframe[times.Count];
                if (times.Count == 0) throw new InvalidDataException("Empty motion channel");
                for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe((float)times[i], (float)channel["values"][i][axis] * sign,
                    ((float?)channel["inTangents"]?[i]?[axis] ?? 0) * sign, ((float?)channel["outTangents"]?[i]?[axis] ?? 0) * sign);
                clip.SetCurve("", typeof(Transform), target + "." + "xyzw"[axis], new AnimationCurve(keys));
            }
            return clip;
        }
        static Vector3 Vector(JToken t) => t is JArray ? new Vector3((float)t[0],(float)t[1],(float)t[2]) : new Vector3((float?)t?["x"] ?? 0,(float?)t?["y"] ?? 0,(float?)t?["z"] ?? 0);
        public static AnimationClip CompilePlatform(JToken source, Vector3[] path)
        {
            var clip = NewClip(); int mode = (int?)source["moveType"] ?? 0;
            var delta = Vector(source["to"]) - Vector(source["from"]); float spin = (float?)source["spinVelocity"] ?? 0;
            bool followsPath = mode >= 8; float duration = followsPath ? 1 : 2;
            var times = new SortedSet<float> { 0, duration };
            // Dense Hermite samples preserve analytic sinusoidal travel to sub-millimetre
            // precision even for the largest source platform. Path corners stay exact.
            if (followsPath) { for (int i = 0; i < path.Length; i++) times.Add((float)i / Math.Max(1,path.Length-1)); }
            else { for (int i = 0; i <= 512; i++) times.Add(i/256f); times.Add(.99999994f); }
            var values = new List<(float Time, Vector3 Point, float Degrees)>();
            foreach (float phase in times)
            {
                bool returning = !followsPath && phase >= 1; float t = returning ? 2-phase : phase;
                float eased = Mathf.Clamp01(t - Mathf.Sin(t*Mathf.PI*2)*.15f), angle = phase*Mathf.PI + Mathf.PI/2, radius = delta.magnitude*.5f;
                Vector3 point = Vector3.zero;
                switch (mode)
                {
                    case 0: case 6: case 7: point = delta*eased; break;
                    case 1: point = delta*.5f + new Vector3(0,Mathf.Sin(angle),Mathf.Cos(angle))*radius; break;
                    case 2: point = delta*.5f + new Vector3(-Mathf.Sin(angle),Mathf.Cos(angle),0)*radius; break;
                    case 3: point = delta*.5f + new Vector3(-Mathf.Sin(angle),0,Mathf.Cos(angle))*radius; break;
                    case 8: case 9: case 10:
                        if (path.Length >= 2) { float p = t*(path.Length-1); int i = Mathf.Min((int)p,path.Length-2); point = Vector3.Lerp(path[i],path[i+1],p-i); } break;
                }
                values.Add((phase, point, (followsPath ? t*360 : (returning ? -1 : 1)*eased*180)*spin));
            }
            for (int axis = 0; axis < 4; axis++)
            {
                var keys = new Keyframe[values.Count];
                for (int i = 0; i < keys.Length; i++)
                {
                    float Value(int k) => axis < 3 ? values[k].Point[axis] : values[k].Degrees;
                    float incoming = i == 0 ? 0 : (Value(i)-Value(i-1))/(values[i].Time-values[i-1].Time);
                    float outgoing = i == keys.Length-1 ? 0 : (Value(i+1)-Value(i))/(values[i+1].Time-values[i].Time);
                    if (!followsPath)
                    {
                        float Derivative(bool incomingSide)
                        {
                            float phase = values[i].Time;
                            bool returning = phase > 1 || phase == 1 && !incomingSide;
                            float t = returning ? 2-phase : phase;
                            float eased = (1-.3f*Mathf.PI*Mathf.Cos(t*Mathf.PI*2))*(returning?-1:1);
                            if (axis == 3) return (returning?-1:1)*eased*180*spin;
                            if (mode is 0 or 6 or 7) return delta[axis]*eased;
                            float angle = phase*Mathf.PI+Mathf.PI/2, r = delta.magnitude*.5f*Mathf.PI;
                            var tangent = mode == 1 ? new Vector3(0,Mathf.Cos(angle),-Mathf.Sin(angle))*r
                                : mode == 2 ? new Vector3(-Mathf.Cos(angle),-Mathf.Sin(angle),0)*r
                                : mode == 3 ? new Vector3(-Mathf.Cos(angle),0,-Mathf.Sin(angle))*r : Vector3.zero;
                            return tangent[axis];
                        }
                        incoming = Derivative(true); outgoing = Derivative(false);
                        // The last representable phase before the source spin jump
                        // must not interpolate through that jump over a normal frame.
                        if (axis == 3 && values[i].Time == .99999994f) outgoing = float.PositiveInfinity;
                    }
                    keys[i] = new Keyframe(values[i].Time, Value(i), incoming, outgoing);
                }
                if (axis < 3) clip.SetCurve("",typeof(Transform),"localPosition."+"xyz"[axis],new AnimationCurve(keys));
                else if (mode is 4 or 6 or 9 or 5 or 7 or 10) clip.SetCurve("",typeof(Transform),"localEulerAnglesRaw."+(mode is 4 or 6 or 9 ? "x" : "z"),new AnimationCurve(keys));
            }
            return clip;
        }
    }
}
