using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FFOne.World
{
    internal static class WorldBehaviourAnimation
    {
        internal sealed class Scalar
        {
            internal float[] Times, Values, In, Out;
            internal int Pre, Post;
            internal float Evaluate(float elapsed, float clipTime, bool material)
            {
                float time = Pre == 2 || Post == 2 ? elapsed : clipTime;
                int last = Times.Length - 1;
                float period = Times[last] - Times[0];
                if (period > .00000001f && (material && (Pre == 2 || Post == 2) || time > Times[last] && Post == 2 || time < Times[0] && Pre == 2))
                    time = Times[0] + Mathf.Repeat(time - Times[0], period);
                if (last == 0 || time <= Times[0]) return Values[0];
                if (time >= Times[last]) return Values[last];
                int lo = 0, hi = last;
                while (hi - lo > 1) { int mid = (lo + hi) / 2; if (Times[mid] <= time) lo = mid; else hi = mid; }
                float span = Times[hi] - Times[lo]; if (span <= .00000001f) return Values[hi];
                float outgoing = Out[lo], incoming = In[hi];
                if (float.IsInfinity(outgoing) || float.IsInfinity(incoming)) return Values[lo];
                float u = (time - Times[lo]) / span, u2 = u*u, u3 = u2*u;
                return (2*u3-3*u2+1)*Values[lo] + (u3-2*u2+u)*span*outgoing
                    + (-2*u3+3*u2)*Values[hi] + (u3-u2)*span*incoming;
            }
        }
        internal sealed class Channel { internal string Target, Property; internal Scalar[] Axes; internal bool Closed, Moving; }
        internal sealed class FloatCurve { internal string Target, Attribute; internal int Class; internal Scalar Curve; }
        internal sealed class Clip
        {
            internal string Id, Name; internal float Duration; internal bool Looped, ClosedMotion;
            internal Channel[] Channels; internal FloatCurve[] Floats; internal int References;
        }
        sealed class TransformTrack { internal Transform Target; internal string Property; internal Scalar[] Axes; }
        internal sealed class MaterialTrack
        {
            internal Material Material; internal string Property; internal int Id;
            internal Scalar[] Axes; internal Vector4 Base; internal bool Offset, Scale, Rotation;
        }
        internal sealed class Player
        {
            internal Clip Clip; internal bool Enabled, Auto, VisibleOnly, Physics, TransformRepeat, MaterialRepeat;
            internal Renderer[] Renderers;
            internal float Elapsed;
            readonly List<TransformTrack> transforms = new List<TransformTrack>();
            readonly List<MaterialTrack> materials = new List<MaterialTrack>();
            internal void Update(float delta)
            {
                if (!Enabled || !Auto) return;
                bool visible = Renderers.Length == 0 || WorldBehaviour.AnyVisible(Renderers);
                if (VisibleOnly && !visible) return;
                Elapsed += delta;
                if (!Physics && !visible) return;
                float trsTime = Time(Elapsed, Clip.Duration, TransformRepeat), materialTime = Time(Elapsed, Clip.Duration, MaterialRepeat);
                foreach (var entry in transforms)
                {
                    if (!entry.Target) continue;
                    float x = entry.Axes[0].Evaluate(Elapsed, trsTime, false), y = entry.Axes[1].Evaluate(Elapsed, trsTime, false), z = entry.Axes[2].Evaluate(Elapsed, trsTime, false);
                    if (entry.Property == "translation") entry.Target.localPosition = new Vector3(-x, y, z);
                    else if (entry.Property == "scale") entry.Target.localScale = new Vector3(x, y, z);
                    else
                    {
                        float w = entry.Axes[3].Evaluate(Elapsed, trsTime, false);
                        var rotation = new Quaternion(x, -y, -z, w);
                        if (x*x+y*y+z*z+w*w > .00000001f) entry.Target.localRotation = rotation.normalized;
                    }
                }
                foreach (var entry in materials)
                {
                    Vector4 value = entry.Base;
                    for (int i = 0; i < entry.Axes.Length; i++) if (entry.Axes[i] != null) value[i] = entry.Axes[i].Evaluate(Elapsed, materialTime, true);
                    if (entry.Offset) entry.Material.mainTextureOffset = new Vector2(value.x, value.y);
                    else if (entry.Scale) entry.Material.mainTextureScale = new Vector2(value.x, value.y);
                    else if (entry.Rotation) entry.Material.SetFloat(entry.Id, value.x);
                    else entry.Material.SetVector(entry.Id, value);
                }
            }
            static float Time(float elapsed, float duration, bool repeat) => duration <= .00000001f ? 0 : repeat ? Mathf.Repeat(elapsed, duration) : Mathf.Min(elapsed, duration);
            internal void AddTransform(Transform target, Channel channel) => transforms.Add(new TransformTrack { Target = target, Property = channel.Property, Axes = channel.Axes });
            internal void AddMaterial(MaterialTrack entry) => materials.Add(entry);
        }
        static readonly Dictionary<string, Clip> cache = new Dictionary<string, Clip>();
        internal static Clip Acquire(JToken definition)
        {
            string id = (string)definition["id"];
            if (cache.TryGetValue(id, out var cached)) { cached.References++; return cached; }
            var clip = new Clip { Id = id, Name = (string)definition["name"], Duration = (float)definition["duration"],
                Looped = (bool?)definition["looped"] ?? false, References = 1 };
            clip.Channels = (definition["channels"] ?? new JArray()).Select(CompileChannel).ToArray();
            clip.Floats = (definition["floatCurves"] ?? new JArray()).Select(c => new FloatCurve { Target = (string)c["targetPath"],
                Attribute = (string)c["attribute"], Class = (int)c["classId"], Curve = CompileScalar(c, -1) }).ToArray();
            clip.ClosedMotion = clip.Name == "nif-default" && clip.Channels.Length > 0
                && clip.Channels.All(c => c.Closed) && clip.Channels.Any(c => c.Moving);
            if ((definition["events"] as JArray)?.Count > 0) throw new InvalidDataException("Animation events require a handler: " + id);
            cache.Add(id, clip); return clip;
        }
        internal static void Release(Clip clip) { if (--clip.References == 0) cache.Remove(clip.Id); }
        static Channel CompileChannel(JToken definition)
        {
            string property = (string)definition["property"];
            if (property != "translation" && property != "rotation" && property != "scale") throw new InvalidDataException("Unsupported TRS property " + property);
            int count = property == "rotation" ? 4 : 3;
            var result = new Channel { Target = (string)definition["targetPath"], Property = property,
                Axes = Enumerable.Range(0, count).Select(i => CompileScalar(definition, i)).ToArray() };
            var values = (JArray)definition["values"];
            if (count == 4)
            {
                Quaternion Rotation(JToken v) => new Quaternion((float)v[0], (float)v[1], (float)v[2], (float)v[3]).normalized;
                var first = Rotation(values[0]); result.Closed = Mathf.Abs(Quaternion.Dot(first, Rotation(values[values.Count-1]))) >= .9995f;
                result.Moving = values.Skip(1).Any(v => Mathf.Abs(Quaternion.Dot(first, Rotation(v))) < .9995f);
            }
            else
            {
                var first = WorldBehaviour.Vector(values[0]); var last = WorldBehaviour.Vector(values[values.Count-1]);
                float tolerance = Mathf.Max(1, Mathf.Max(Max(first), Max(last))) * .0005f;
                result.Closed = Max(first-last) <= tolerance; result.Moving = values.Skip(1).Any(v => Max(WorldBehaviour.Vector(v)-first) > tolerance);
            }
            return result;
        }
        static float Max(Vector3 value) => Mathf.Max(Mathf.Abs(value.x), Mathf.Max(Mathf.Abs(value.y), Mathf.Abs(value.z)));
        internal static Scalar CompileScalar(JToken definition, int axis)
        {
            var times = definition["times"] as JArray; var values = definition["values"] as JArray;
            if (times == null || values == null || times.Count == 0 || times.Count != values.Count) throw new InvalidDataException("Empty or inconsistent animation keys");
            float Read(JToken t, int i) => (float)(axis < 0 ? t[i] : t[i][axis]);
            var result = new Scalar { Times = times.Select(t => (float)t).ToArray(),
                Values = Enumerable.Range(0, times.Count).Select(i => Read(values, i)).ToArray(), In = new float[times.Count], Out = new float[times.Count],
                Pre = (int?)definition["preInfinity"] ?? 1, Post = (int?)definition["postInfinity"] ?? 1 };
            for (int i = 0; i < times.Count; i++)
            {
                if (definition["inTangents"] is JArray incoming && i < incoming.Count) result.In[i] = Read(incoming, i);
                if (definition["outTangents"] is JArray outgoing && i < outgoing.Count) result.Out[i] = Read(outgoing, i);
            }
            return result;
        }
        internal static Player Bind(WorldBehaviour owner, Clip clip, JToken record,
            Dictionary<string, Transform> transforms, Dictionary<string, Renderer[]> renderers, Renderer[] visibility)
        {
            bool repeats = clip.Looped || (int?)record["wrapMode"] == 2 || clip.ClosedMotion;
            var player = new Player { Clip = clip, Enabled = (bool?)record["enabled"] ?? true, Auto = (bool?)record["playAutomatically"] ?? true,
                VisibleOnly = (bool?)record["animateOnlyIfVisible"] ?? false, Physics = (bool?)record["animatePhysics"] ?? false,
                TransformRepeat = repeats, MaterialRepeat = repeats || clip.Name == "nif-default", Renderers = visibility };
            foreach (var channel in clip.Channels)
            {
                if (transforms.TryGetValue(channel.Target, out var target)) { player.AddTransform(target, channel); WorldObjectRange.MarkDynamic(target); owner.TransformChannels++; }
                else owner.Problem("Missing animated TRS target " + channel.Target + " in " + clip.Id);
            }
            var tracks = new Dictionary<(Material, string), MaterialTrack>();
            foreach (var curve in clip.Floats)
            {
                // Retained source specular curves have no term in either accepted fixed-function replacement.
                if (curve.Class == 21 && curve.Attribute.StartsWith("_SpecColor.", StringComparison.Ordinal)) { owner.RetainedUnusedMaterialCurves++; continue; }
                if (curve.Class != 21) { owner.Problem("Unsupported curve class " + curve.Class, true); continue; }
                if (!renderers.TryGetValue(curve.Target, out var targets)) { owner.Problem("Missing animated material target " + curve.Target + " in " + clip.Id); continue; }
                string property; int axis; bool offset = false, scale = false, rotation = false;
                if (curve.Attribute.StartsWith("_MainTex.offset.", StringComparison.Ordinal)) { property = "offset"; axis = curve.Attribute.EndsWith("x") ? 0 : 1; offset = true; }
                else if (curve.Attribute.StartsWith("_MainTex.scale.", StringComparison.Ordinal)) { property = "scale"; axis = curve.Attribute.EndsWith("x") ? 0 : 1; scale = true; }
                else if (curve.Attribute == "_MainTex.rotation") { property = "_MainTexRotation"; axis = 0; rotation = true; }
                else
                {
                    int separator = curve.Attribute.LastIndexOf('.');
                    if (separator < 0 || "rgba".IndexOf(curve.Attribute[curve.Attribute.Length-1]) < 0) { owner.Problem("Unsupported material curve " + curve.Attribute, true); continue; }
                    property = curve.Attribute.Substring(0, separator); axis = "rgba".IndexOf(curve.Attribute[curve.Attribute.Length-1]);
                }
                // A retained source target can deliberately have no published renderer (three schoolyard utility planes).
                // The native material binder likewise has no material handle for these curves.
                if (targets.Length == 0) { owner.UnrenderedMaterialCurves++; continue; }
                foreach (var renderer in targets)
                    foreach (var material in owner.Materials(renderer))
                    {
                        if (!material) continue;
                        if (!offset && !scale && !material.HasProperty(property)) { owner.Problem("Shader lacks " + property + " on " + material.name, true); continue; }
                        var key = (material, property);
                        if (!tracks.TryGetValue(key, out var track))
                        {
                            Vector4 initial = offset ? (Vector4)new Vector2(material.mainTextureOffset.x, material.mainTextureOffset.y)
                                : scale ? (Vector4)new Vector2(material.mainTextureScale.x, material.mainTextureScale.y)
                                : rotation ? new Vector4(material.GetFloat(property), 0, 0, 0) : material.GetVector(property);
                            track = new MaterialTrack { Material = material, Property = property, Id = Shader.PropertyToID(property),
                                Axes = new Scalar[rotation ? 1 : offset || scale ? 2 : 4], Base = initial, Offset = offset, Scale = scale, Rotation = rotation };
                            tracks.Add(key, track); player.AddMaterial(track);
                        }
                        track.Axes[axis] = curve.Curve; owner.MaterialCurves++;
                    }
            }
            return player;
        }
    }
}
