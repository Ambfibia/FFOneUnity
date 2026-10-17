using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FFOne.Content
{
    public sealed partial class NativeAvatarPreview
    {
        static JObject hnpcCatalog, hnpcPalette, npcTextureCatalog;
        /// <summary>Uses accepted HNPC parts/textures directly; NPC appearance indexes are not player item IDs.</summary>
        public void ApplyHnpc(int appearanceIndex)
        {
            GameObject candidate = null; var parts = new List<GameObject>(); var materials = new List<Material>();
            try
            {
                rigCatalog ??= Read("characters/player/shared/player_rig_contract");
                textureCatalog ??= Read("data/character_creation/runtime_textures"); customization ??= Read("data/character_creation/customization");
                hnpcCatalog ??= Read("data/hnpc/catalog");
                var appearance = hnpcCatalog["appearances"].SingleOrDefault(a => (int)a["index"] == appearanceIndex)
                    ?? throw new InvalidDataException("Missing accepted HNPC appearance " + appearanceIndex);
                string gender = (string)appearance["gender"];
                var rig = rigCatalog["genders"].Single(g => (string)g["gender"] == gender);
                string actorRoot = (string)rig["nodes"].Single(n => (int)n["actorBoneIndex"] == 0)["fullPath"];
                candidate = Instantiate(Load<GameObject>((string)rig["skeletonGlb"]), transform, false);
                candidate.name = "HNPC appearance " + appearanceIndex;
                var bones = candidate.GetComponentsInChildren<Transform>(true).ToDictionary(t => RelativePath(t, candidate.transform), StringComparer.Ordinal);
                var skin = ResolveTexture(gender == "male" ? "m_skin" : "f_skin");
                var skinTint = HnpcColor("skin", (int)appearance["skinColor"]); var hairTint = HnpcColor("hair", (int)appearance["hairColor"]);
                foreach (var definition in appearance["parts"])
                {
                    string kind = (string)definition["kind"], path = (string)definition["nativeAsset"]?["path"];
                    if (string.IsNullOrEmpty(path)) throw new InvalidDataException("HNPC part lacks an accepted asset: " + kind);
                    var part = Instantiate(Load<GameObject>(path), candidate.transform, false); parts.Add(part);
                    // HNPC catalog clothes indices identify ActorSkinCombiner parts.
                    // Rigid attachments can contain their own skin (back_plumber's
                    // Box/Bone hierarchy); keep those joints on the attached model.
                    if ((int?)definition["actorSkinCombinerClothesIndex"] != null) BindSharedRig(part, bones, actorRoot);
                    else
                    {
                        AttachRigid(part, kind, bones, actorRoot);
                        foreach (var animation in part.GetComponentsInChildren<Animation>(true))
                        {
                            foreach (AnimationState clip in animation)
                            {
                                clip.wrapMode = WrapMode.Loop;
                                if (clip.name == "nif-default") animation.clip = clip.clip;
                            }
                            animation.wrapMode = WrapMode.Loop;
                            if (animation.clip) animation.Play();
                        }
                    }
                    string textureName = (string)definition["textures"]?.FirstOrDefault();
                    Texture2D primary = string.IsNullOrEmpty(textureName) ? null : ResolveHnpcTexture(textureName);
                    foreach (var renderer in part.GetComponentsInChildren<Renderer>(true))
                    {
                        renderer.sharedMaterials = renderer.sharedMaterials.Select(source => {
                            var material = new Material(source); materials.Add(material);
                            if (source.name.Contains("sub", StringComparison.OrdinalIgnoreCase)) { material.SetTexture("_MainTex", skin); Tint(material, skinTint); }
                            else if (primary) material.SetTexture("_MainTex", primary);
                            if (source.name.Contains("hair", StringComparison.OrdinalIgnoreCase)) Tint(material, hairTint);
                            NativeAvatarMaterials.Configure(material, kind); return material;
                        }).ToArray();
                    }
                }
                if (parts.Count == 0) throw new InvalidDataException("HNPC appearance has no parts: " + appearanceIndex);
                var legacy = candidate.GetComponent<Animation>(); if (legacy) { legacy.enabled = false; Release(legacy); }
                var animator = candidate.AddComponent<NativePlayerAnimator>(); animator.Configure(gender, (int)appearance["height"], (int)appearance["shape"]);
                Clear(); actor = candidate; actorGender = gender; actorBones = bones; playerAnimation = animator;
                displayedParts.AddRange(parts); ownedMaterials.AddRange(materials);
                FinishPose();
                foreach (var node in actor.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = gameObject.layer;
                ready = actor.GetComponentsInChildren<Renderer>(true).Length > 0; LastError = null;
            }
            catch (Exception error)
            {
                foreach (var material in materials) if (material) Release(material);
                if (candidate) { candidate.SetActive(false); Release(candidate); }
                LastError = error.Message; Debug.LogWarning("FFOne HNPC avatar: " + LastError, this);
            }
        }

        public static Color HnpcColor(string field, int selector)
        {
            hnpcPalette ??= Read("data/hnpc/palette");
            var colors = hnpcPalette[field] as JArray ?? throw new InvalidDataException("Missing HNPC palette " + field);
            // Authored HNPC selectors index the complete 20-color palette from zero;
            // the player creator's one-based subset is a different contract.
            if (selector < 0 || selector >= colors.Count) return Color.white;
            var row = colors[selector]; return new Color((float)row[0], (float)row[1], (float)row[2], (float)row[3]);
        }

        static Texture2D ResolveHnpcTexture(string trueName)
        {
            var declared = hnpcCatalog["textures"]?.SingleOrDefault(t => string.Equals((string)t["trueName"], trueName, StringComparison.OrdinalIgnoreCase));
            if (declared?["path"] != null) return Load<Texture2D>((string)declared["path"]);
            // FFOneClient's HNPC runtime library retains literal true names, including spaces.
            var runtime = ContentAssets.Load<Texture2D>("characters/player/hnpc-runtime-textures/" + trueName);
            if (runtime) return runtime;
            npcTextureCatalog ??= Read("data/tables/npc_texture_overrides");
            var row = npcTextureCatalog["textures"].SingleOrDefault(t => (string)t["trueName"] == trueName);
            return row == null ? ResolveTexture(trueName) : Load<Texture2D>((string)row["path"]);
        }

        static void AddEquippedParts(FFOne.Network.CharacterInfo character, string gender, GameObject candidate,
            IReadOnlyDictionary<string, Transform> bones, string actorRoot, List<GameObject> parts, List<Material> materials)
        {
            foreach (var (slot, category, kind) in new[] { (4, "hat", "hat"), (5, "glasses", "glasses"), (6, "back", "back"), (0, "weapon", "rightWeapon") })
            {
                int id = character.Equipment[slot].ItemId; if (id == 0) continue;
                var item = items["items"].SingleOrDefault(i => (string)i["category"] == category && (int)i["itemNumber"] == id);
                var visual = item?[gender]; var models = visual?["models"] as JArray;
                if ((string)visual?["modelStatus"] != "verified_unique" || models?.Count != 1)
                    throw new InvalidDataException($"Equipment {category}:{id} lacks one accepted {gender} model.");
                var part = Instantiate(Load<GameObject>((string)models[0]["nativeAsset"]["path"]), candidate.transform, false);
                part.SetActive(false); parts.Add(part);
                if (part.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0) BindSharedRig(part, bones, actorRoot);
                else AttachRigid(part, kind, bones, actorRoot);
                var texture = visual["primaryTexture"];
                string path = (string)texture?["candidates"]?.FirstOrDefault()?["path"];
                Texture2D primary = path == null ? null : Load<Texture2D>(path);
                foreach (var renderer in part.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(source => {
                        var material = new Material(source); materials.Add(material);
                        if (primary) material.SetTexture("_MainTex", primary);
                        NativeAvatarMaterials.Configure(material, category); return material;
                    }).ToArray();
            }
        }

        /// <summary>Match the complete original hierarchy below the model root, never a first bone-name hit.</summary>
        public static void BindSharedRig(GameObject part, IReadOnlyDictionary<string, Transform> bones, string actorRoot)
        {
            foreach (var renderer in part.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var remapped = new Transform[renderer.bones.Length];
                for (int i = 0; i < remapped.Length; i++)
                {
                    string source = RelativePath(renderer.bones[i], part.transform); int slash = source.IndexOf('/');
                    if (slash < 0) throw new InvalidDataException("Skin joint lacks an exact shared hierarchy: " + source);
                    string destination = actorRoot + source.Substring(slash);
                    if (!bones.TryGetValue(destination, out remapped[i]))
                    {
                        // Match FFOneClient player_shared_rig/operations.rs for published compact
                        // wearables: preserve the ordered tail and accept only one verified actor path.
                        int bip = source.IndexOf("/Bip01/", StringComparison.Ordinal);
                        string tail = bip < 0 ? null : source.Substring(bip + 7);
                        var matches = tail == null ? Array.Empty<KeyValuePair<string, Transform>>() : bones.Where(b => b.Key.StartsWith(actorRoot + "/", StringComparison.Ordinal) && b.Key.EndsWith("/" + tail, StringComparison.Ordinal)).ToArray();
                        if (matches.Length != 1)
                        {
                            string terminal = source.Substring(source.LastIndexOf('/') + 1);
                            matches = bones.Where(b => b.Key.StartsWith(actorRoot + "/", StringComparison.Ordinal) && b.Value.name == terminal).ToArray();
                        }
                        if (matches.Length != 1) throw new InvalidDataException("Shared rig has no unique published joint mapping: " + destination);
                        remapped[i] = matches[0].Value;
                    }
                }
                renderer.bones = remapped; renderer.rootBone = bones[actorRoot]; renderer.updateWhenOffscreen = true;
            }
        }

        // FFOneClient characters/attachment/mod.rs: ActorSkinCombiner.Generate + AttachGO.
        static void AttachRigid(GameObject part, string kind, IReadOnlyDictionary<string, Transform> bones, string actorRoot)
        {
            const string spine = "/Bip01/Bip01 NonAccum/Bip01 Pelvis/Bip01 Spine/Bip01 Spine1";
            string suffix = kind switch {
                "hat" => spine + "/Bip01 Neck/Bip01 Head/Bip01 helmet01",
                "glasses" => spine + "/Bip01 Neck/Bip01 Head/Bip01 glass01",
                "back" => spine + "/Bip01 back01",
                "rightWeapon" => spine + "/Bip01 Neck/Bip01 R Clavicle/Bip01 R UpperArm/Bip01 R Forearm/Bip01 R Hand/Bip01 Rweapon01",
                "leftWeapon" => spine + "/Bip01 Neck/Bip01 L Clavicle/Bip01 L UpperArm/Bip01 L Forearm/Bip01 L Hand/Bip01 Lweapon01",
                _ => throw new InvalidDataException("Unknown rigid HNPC attachment: " + kind) };
            if (!bones.TryGetValue(actorRoot + suffix, out var socket)) throw new InvalidDataException("Missing exact attachment socket: " + kind);
            socket.localScale = Vector3.one; part.transform.SetParent(socket, false);
            part.transform.localPosition = Vector3.zero; part.transform.localRotation = Quaternion.Euler(90, 0, 0); part.transform.localScale = Vector3.one;
        }
    }
}
