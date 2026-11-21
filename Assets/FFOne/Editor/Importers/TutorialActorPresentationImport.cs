using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FFOne.Content;
using FFOne.Tutorial;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    // Resolve the accepted NPC table and GLB ownership at authoring time. No
    // guessed names, global material writes or runtime filesystem searches.
    public static class TutorialActorPresentationImport
    {
        public static string[] SoundVariants(string cue)
        {
            var match = Regex.Match(cue, @"\(RAND:(\d+)-(\d+)\)");
            if (!match.Success) return new[] { cue };
            int first = int.Parse(match.Groups[1].Value), last = int.Parse(match.Groups[2].Value);
            return Enumerable.Range(first, last - first + 1).Select(i => cue.Substring(0, match.Index) + i + cue.Substring(match.Index + match.Length)).ToArray();
        }

        public static string[] Repair()
        {
            var content = ContentAssets.Load<TutorialContent>("Tutorial/Content");
            var catalog = ContentAssets.Load<NativeModelCatalog>(NativeModelAssets.EditableRoot + "/Index");
            var xdt = NativeData.Object("data/tables/xdt");
            var routes = xdt["_ffone"]["tables"].Single(t => (string)t["name"] == "native_asset_routes")["value"]["m_pAudioData"];
            var textureRows = NativeData.Object("data/tables/npc_texture_overrides")["textures"];
            var gaps = new HashSet<string>();
            var pending = new List<Action>();
            Texture2D Texture(string name)
            {
                var row = textureRows.SingleOrDefault(t => (string)t["trueName"] == name);
                if (row == null)
                {
                    if (name == "npc_numbuhtwo_face_talk" || name == "npc_numbuhtwo_face_talksad")
                        return Asset<Texture2D>("characters/npcs/npc_numbuhtwo_future/textures/" + name + ".png");
                    gaps.Add("texture:" + name); return null;
                }
                return Asset<Texture2D>((string)row["path"]);
            }
            foreach (var actor in content.Actors)
            {
                string resource = ContentPaths.Resource(AssetDatabase.GetAssetPath(actor.Model));
                var binding = catalog.Bindings.Single(b => b.EditableResource == resource || b.OriginalResource == resource);
                var manifest = JsonConvert.DeserializeObject<NativeAssetPublisher.Manifest>(File.ReadAllText(
                    ContentPaths.Resolve(NativeAssetPublisher.ManifestRoot + "/" + binding.OriginalResource + "/publication.json")));
                var doc = NativeGlbDocument.Read(AssetDatabase.GUIDToAssetPath(manifest.SourceGuid)).Json;
                var row = xdt["m_pNpcTable"]["m_pNpcData"][actor.NpcType];
                var meshRow = xdt["m_pNpcTable"]["m_pNpcMeshData"][(int)row["m_iMesh"]];
                string main = (string)meshRow["m_pstrMTextureString"], sub = (string)meshRow["m_pstrMTextureString2"];
                var textures = new List<TutorialTextureBinding>();
                foreach (var primitive in doc["meshes"].SelectMany(m => m["primitives"]))
                {
                    if ((bool?)primitive["extras"]?["npcTableTextureWritable"] == false) continue;
                    var material = doc["materials"][(int)primitive["material"]];
                    string name = (string)material["name"], shader = (string)material["extras"]?["ffone"]?["declaredShaderName"];
                    string selected = sub != "null" && (name.Contains("sub") || shader == "Skin_FusionEffect_blendSrcalphaInvsrcalpha") ? sub
                        : main != "null" && name.Contains("main") ? main : null;
                    if (selected != null) textures.Add(new TutorialTextureBinding { Material = name, Texture = Texture(selected) });
                }
                var events = (doc["animations"] ?? new JArray()).SelectMany(a => a["extras"]?["nonTrs"]?["events"] ?? new JArray()).ToArray();
                var sounds = events.Where(e => (string)e["functionName"] == "sound").Select(e => (string)e["stringParameter"]).Distinct().Select(cue => {
                    var russian = new List<AudioClip>();
                    var clips = SoundVariants(cue).Select(variant => {
                        string key = Path.GetFileNameWithoutExtension(variant);
                        var matches = routes.Where(r => string.Equals((string)r["trueName"], key, StringComparison.OrdinalIgnoreCase)).ToArray();
                        if (matches.Length == 0)
                        {
                            russian.Add(null);
                            // Tools/recover_tutorial_presentation.py pins these
                            // exact Tutorial.resourceFile AudioClip owners.
                            if (new[] { "Spawn_CS_Run1_1", "Spawn_CS_Run1_2", "Spawn_CS_Skill0", "Spawn_CS_Melee2_1", "Spawn_CS_Melee2_2", "Spawn_CS_Melee2_3" }
                                .Contains(key, StringComparer.OrdinalIgnoreCase))
                                return Asset<AudioClip>("audio/sfx/creatures/spawn_cs/" + key.Substring("Spawn_CS_".Length).ToLowerInvariant() + ".ogg");
                            gaps.Add("sound:" + key); return null;
                        }
                        if (matches.Length != 1) throw new InvalidDataException("Ambiguous animation audio: " + key);
                        string path = (string)matches[0]["path"];
                        if ((string)matches[0]["category"] == "voice" && !path.StartsWith("audio/", StringComparison.Ordinal)) path = "audio/voice/en/" + path;
                        russian.Add(path.Contains("/voice/en/") ? AssetDatabase.LoadAssetAtPath<AudioClip>(ContentPaths.Resolve("Assets/Resources/" + path.Replace("/en/", "/ru/"))) : null);
                        return Asset<AudioClip>(path);
                    }).ToArray();
                    return new TutorialAnimationSound { Cue = cue, Variants = clips, RussianVariants = russian.ToArray() };
                }).ToArray();
                var faces = events.Where(e => (string)e["functionName"] == "emote_face_on").Select(e => (string)e["stringParameter"]).Distinct()
                    .Select(name => new TutorialFaceTexture { Name = name, Texture = Texture(Path.GetFileNameWithoutExtension(name)) }).ToArray();
                pending.Add(() => { actor.Textures = textures.ToArray(); actor.AnimationSounds = sounds; actor.Faces = faces; });
            }
            foreach (var apply in pending) apply();
            EditorUtility.SetDirty(content); AssetDatabase.SaveAssetIfDirty(content);
            return gaps.OrderBy(g => g, StringComparer.Ordinal).ToArray();
        }
        static T Asset<T>(string path) where T : UnityEngine.Object
        {
            string local = ContentPaths.Resolve("Assets/Resources/" + path);
            var asset = AssetDatabase.LoadAssetAtPath<T>(local);
            if (!asset) throw new InvalidDataException("Missing accepted presentation dependency: " + local);
            return asset;
        }
    }
}
