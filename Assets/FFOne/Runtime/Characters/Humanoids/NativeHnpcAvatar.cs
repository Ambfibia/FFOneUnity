using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FFOne.Content
{
    public sealed partial class NativeAvatarPreview
    {
        static JObject hnpcCatalog, hnpcPalette, npcTextureCatalog;
        static Task hnpcCatalogWarmup;
        static Task<JObject> npcTextureWarmup;

        internal sealed class HnpcPart
        {
            internal JToken Definition;
            internal string ModelPath, TextureName, SecondaryTextureName;
            internal GameObject Model;
            internal Texture2D Texture, SecondaryTexture;
        }

        internal sealed class HnpcAppearance
        {
            internal int Index;
            internal string Gender, ActorRoot, SkeletonPath, SkinPath;
            internal int Height, Shape;
            internal Color SkinTint, HairTint;
            internal GameObject Skeleton;
            internal Texture2D Skin;
            internal NativePlayerAnimationProfile Profile;
            internal HnpcPart[] Parts;
        }

        static HnpcAppearance DescribeHnpc(int index)
        {
            var appearance = hnpcCatalog["appearances"].SingleOrDefault(a => (int)a["index"] == index)
                ?? throw new InvalidDataException("Missing accepted HNPC appearance " + index);
            string gender = (string)appearance["gender"];
            var rig = rigCatalog["genders"].Single(g => (string)g["gender"] == gender);
            return new HnpcAppearance {
                Index = index, Gender = gender, Height = (int)appearance["height"], Shape = (int)appearance["shape"],
                ActorRoot = (string)rig["nodes"].Single(n => (int)n["actorBoneIndex"] == 0)["fullPath"],
                SkeletonPath = (string)rig["skeletonGlb"], SkinPath = TexturePath(gender == "male" ? "m_skin" : "f_skin"),
                SkinTint = HnpcColor("skin", (int)appearance["skinColor"]), HairTint = HnpcColor("hair", (int)appearance["hairColor"]),
                Parts = appearance["parts"].Select(p => new HnpcPart {
                    Definition = p, ModelPath = (string)p["nativeAsset"]?["path"],
                    TextureName = (string)p["textures"]?.FirstOrDefault(),
                    SecondaryTextureName = (string)p["textures"]?.ElementAtOrDefault(1)
                }).ToArray()
            };
        }

        static async Task PrepareHnpcCatalogsAsync(CancellationToken token)
        {
            var catalog = NativeData.ObjectAsync("data/hnpc/catalog", token);
            var palette = NativeData.ObjectAsync("data/hnpc/palette", token);
            await Task.WhenAll(catalog, palette, EnsureAvatarCatalogsAsync());
            token.ThrowIfCancellationRequested();
            hnpcCatalog = catalog.Result; hnpcPalette = palette.Result;
        }

        internal static async Task<HnpcAppearance> PrepareHnpcAsync(int index, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (hnpcCatalogWarmup == null || hnpcCatalogWarmup.IsFaulted || hnpcCatalogWarmup.IsCanceled)
                hnpcCatalogWarmup = PrepareHnpcCatalogsAsync(preloadLifetime.Token);
            await ContentAssets.AwaitSharedAsync(hnpcCatalogWarmup, token);
            var result = DescribeHnpc(index);
            var skeleton = NativeModelAssets.PreloadAsync(result.SkeletonPath, token);
            var skin = LoadTextureAsync(result.SkinPath, token);
            var profile = ContentAssets.GetAsync<NativePlayerAnimationProfile>(NativePlayerAnimator.ProfileAddress(result.Gender), token);
            var pending = new List<Task> { skeleton, skin, profile };
            foreach (var part in result.Parts) pending.Add(LoadPart(part));
            await Task.WhenAll(pending);
            token.ThrowIfCancellationRequested();
            result.Skeleton = skeleton.Result; result.Skin = skin.Result; result.Profile = profile.Result;
            return result;

            async Task LoadPart(HnpcPart part)
            {
                var model = NativeModelAssets.PreloadAsync(part.ModelPath, token);
                var texture = LoadHnpcTextureAsync(part.TextureName, token);
                var secondary = LoadHnpcTextureAsync(part.SecondaryTextureName, token);
                await Task.WhenAll(model, texture, secondary);
                part.Model = model.Result; part.Texture = texture.Result; part.SecondaryTexture = secondary.Result;
            }
        }

        /// <summary>Uses accepted HNPC parts/textures directly; NPC appearance indexes are not player item IDs.</summary>
        public void ApplyHnpc(int appearanceIndex)
        {
            try
            {
                rigCatalog ??= Read("characters/player/shared/player_rig_contract");
                textureCatalog ??= Read("data/character_creation/runtime_textures"); customization ??= Read("data/character_creation/customization");
                hnpcCatalog ??= Read("data/hnpc/catalog");
                var appearance = DescribeHnpc(appearanceIndex);
                appearance.Skeleton = Load<GameObject>(appearance.SkeletonPath);
                appearance.Skin = Load<Texture2D>(appearance.SkinPath);
                appearance.Profile = ContentAssets.Load<NativePlayerAnimationProfile>(NativePlayerAnimator.ProfileAddress(appearance.Gender));
                foreach (var part in appearance.Parts)
                {
                    part.Model = Load<GameObject>(part.ModelPath);
                    if (!string.IsNullOrEmpty(part.TextureName)) part.Texture = ResolveHnpcTexture(part.TextureName);
                    if (!string.IsNullOrEmpty(part.SecondaryTextureName)) part.SecondaryTexture = ResolveHnpcTexture(part.SecondaryTextureName);
                }
                ApplyHnpc(appearance);
            }
            catch (Exception error) { ReportHnpcError(error); }
        }

        internal void ApplyHnpc(HnpcAppearance appearance)
        {
            GameObject candidate = null; var parts = new List<GameObject>(); var materials = new List<Material>();
            try
            {
                if (!appearance.Skeleton) throw new FileNotFoundException("Missing HNPC skeleton", appearance.SkeletonPath);
                string actorRoot = appearance.ActorRoot;
                candidate = Instantiate(appearance.Skeleton, transform, false);
                candidate.name = "HNPC appearance " + appearance.Index;
                var bones = candidate.GetComponentsInChildren<Transform>(true).ToDictionary(t => RelativePath(t, candidate.transform), StringComparer.Ordinal);
                foreach (var loaded in appearance.Parts)
                {
                    var definition = loaded.Definition;
                    string kind = (string)definition["kind"];
                    if (!loaded.Model) throw new FileNotFoundException("Missing HNPC part " + kind, loaded.ModelPath);
                    var part = Instantiate(loaded.Model, candidate.transform, false); parts.Add(part);
                    // HNPC catalog clothes indices identify ActorSkinCombiner parts.
                    // Rigid attachments can contain their own skin (back_plumber's
                    // Box/Bone hierarchy); keep those joints on the attached model.
                    bool sharedSkin = (int?)definition["actorSkinCombinerClothesIndex"] != null;
                    if (sharedSkin) BindSharedRig(part, bones, actorRoot);
                    else
                    {
                        AttachRigid(part, kind, bones, actorRoot);
                        PlayAttachmentLoops(part);
                    }
                    foreach (var renderer in part.GetComponentsInChildren<Renderer>(true))
                    {
                        renderer.sharedMaterials = renderer.sharedMaterials.Select(source => {
                            var material = new Material(source); materials.Add(material);
                            NativeAvatarMaterials.Bind(material, kind, loaded.Texture, loaded.SecondaryTexture,
                                appearance.Skin, appearance.SkinTint, appearance.HairTint);
                            return material;
                        }).ToArray();
                    }
                }
                if (parts.Count == 0) throw new InvalidDataException("HNPC appearance has no parts: " + appearance.Index);
                var legacy = candidate.GetComponent<Animation>(); if (legacy) { legacy.enabled = false; Release(legacy); }
                var animator = candidate.AddComponent<NativePlayerAnimator>(); animator.Configure(appearance.Profile, appearance.Height, appearance.Shape);
                Clear(); actor = candidate; actorGender = appearance.Gender; actorBones = bones; playerAnimation = animator;
                displayedParts.AddRange(parts); ownedMaterials.AddRange(materials);
                FinishPose();
                foreach (var node in actor.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = gameObject.layer;
                ready = actor.GetComponentsInChildren<Renderer>(true).Length > 0; LastError = null;
            }
            catch (Exception error)
            {
                foreach (var material in materials) if (material) Release(material);
                if (candidate) { candidate.SetActive(false); Release(candidate); }
                ReportHnpcError(error);
            }
        }

        void ReportHnpcError(Exception error)
        {
            LastError = error.Message;
            Debug.LogWarning("FFOne HNPC avatar: " + LastError, this);
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

        static string DeclaredHnpcTexturePath(string trueName) => (string)hnpcCatalog["textures"]?
            .SingleOrDefault(t => string.Equals((string)t["trueName"], trueName, StringComparison.OrdinalIgnoreCase))?["path"];

        static string HnpcTextureAddress(string trueName) => "characters/player/hnpc-runtime-textures/" + trueName;

        static string FallbackHnpcTexturePath(string trueName)
        {
            var row = npcTextureCatalog["textures"].SingleOrDefault(t => (string)t["trueName"] == trueName);
            return row == null ? TexturePath(trueName) : (string)row["path"];
        }

        static Texture2D ResolveHnpcTexture(string trueName)
        {
            string path = DeclaredHnpcTexturePath(trueName);
            if (path != null) return Load<Texture2D>(path);
            // FFOneClient's HNPC runtime library retains literal true names, including spaces.
            var runtime = ContentAssets.Load<Texture2D>(HnpcTextureAddress(trueName));
            if (runtime) return runtime;
            npcTextureCatalog ??= Read("data/tables/npc_texture_overrides");
            return Load<Texture2D>(FallbackHnpcTexturePath(trueName));
        }

        static async Task<Texture2D> LoadHnpcTextureAsync(string trueName, CancellationToken token)
        {
            if (string.IsNullOrEmpty(trueName)) return null;
            string path = DeclaredHnpcTexturePath(trueName);
            if (path == null)
            {
                var runtime = await ContentAssets.GetAsync<Texture2D>(HnpcTextureAddress(trueName), token);
                if (runtime) return runtime;
                if (npcTextureCatalog == null)
                {
                    if (npcTextureWarmup == null || npcTextureWarmup.IsFaulted || npcTextureWarmup.IsCanceled)
                        npcTextureWarmup = NativeData.ObjectAsync("data/tables/npc_texture_overrides", preloadLifetime.Token);
                    var pending = npcTextureWarmup;
                    await ContentAssets.AwaitSharedAsync(pending, token);
                    npcTextureCatalog = pending.Result;
                }
                path = FallbackHnpcTexturePath(trueName);
            }
            return await LoadTextureAsync(path, token);
        }

        static async Task<Texture2D> LoadTextureAsync(string path, CancellationToken token)
        {
            var texture = await ContentAssets.GetAsync<Texture2D>(path.Substring(0, path.LastIndexOf('.')), token);
            return texture ? texture : throw new FileNotFoundException("Missing HNPC texture", path);
        }

        static string EquippedTexturePath(JToken visual, string field)
        {
            var texture = visual?[field] as JObject;
            var candidates = texture?["candidates"] as JArray;
            return (string)texture?["status"] == "verified_unique" && candidates?.Count == 1
                ? (string)candidates[0]["path"] : null;
        }

        static void AddEquippedParts(FFOne.Network.CharacterInfo character, string gender, GameObject candidate,
            IReadOnlyDictionary<string, Transform> bones, string actorRoot, List<GameObject> parts, List<Material> materials, Color hairTint, bool showGlasses)
        {
            foreach (var (slot, category, kind) in new[] { (4, "hat", "hat"), (5, "glasses", "glasses"), (6, "back", "back"), (0, "weapon", "rightWeapon") })
            {
                int id = AppearanceItem(character, slot);
                if (id == 0 || category == "glasses" && !showGlasses) continue;
                var item = items["items"].SingleOrDefault(i => (string)i["category"] == category && (int)i["itemNumber"] == id);
                var visual = item?[gender]; var models = visual?["models"] as JArray;
                if ((string)visual?["modelStatus"] != "verified_unique" || models?.Count != 1)
                    throw new InvalidDataException($"Equipment {category}:{id} lacks one accepted {gender} model.");
                var part = Instantiate(Load<GameObject>((string)models[0]["nativeAsset"]["path"]), candidate.transform, false);
                part.SetActive(false); parts.Add(part);
                if (category == "back" && (int)item["equipType"] == 1) BindSharedRig(part, bones, actorRoot);
                else AttachRigid(part, kind, bones, actorRoot);
                string primaryPath = EquippedTexturePath(visual, "primaryTexture");
                string secondaryPath = EquippedTexturePath(visual, "secondaryTexture");
                var primary = primaryPath == null ? null : Load<Texture2D>(primaryPath);
                var secondary = secondaryPath == null ? null : Load<Texture2D>(secondaryPath);
                foreach (var renderer in part.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(source => {
                        var material = new Material(source); materials.Add(material);
                        NativeAvatarMaterials.Bind(material, category, primary, secondary, null, Color.white, hairTint);
                        return material;
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

        static void PlayAttachmentLoops(GameObject part)
        {
            foreach (var animation in part.GetComponentsInChildren<Animation>(true))
            {
                AnimationClip onlyClip = null;
                foreach (AnimationState clip in animation)
                {
                    clip.wrapMode = WrapMode.Loop;
                    if (clip.name == "nif-default") animation.clip = clip.clip;
                    onlyClip = clip.clip;
                }
                if (!animation.clip && animation.GetClipCount() == 1) animation.clip = onlyClip;
                animation.wrapMode = WrapMode.Loop;
                if (animation.clip) animation.Play();
            }
        }
    }
}
