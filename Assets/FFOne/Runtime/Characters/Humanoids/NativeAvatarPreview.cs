using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Network;
using Newtonsoft.Json.Linq;
using UnityEngine;
using CharacterInfo = FFOne.Network.CharacterInfo;

namespace FFOne.Content
{
    [DefaultExecutionOrder(100)]
    public sealed partial class NativeAvatarPreview : MonoBehaviour
    {
        static JObject rigCatalog, items, textureCatalog, customization;
        readonly List<Material> ownedMaterials = new();
        readonly List<GameObject> displayedParts = new();
        GameObject actor;
        SkirtPose skirtPose;
        string actorGender;
        NativePlayerAnimator playerAnimation;
        Dictionary<string, Transform> actorBones;
        bool ready;
        public string LastError { get; private set; }
        public bool IsReady => actor != null && ready;
        public NativePlayerAnimator PlayerAnimation => playerAnimation;

        public void Apply(CharacterInfo character)
        {
            if (character == null) { Clear(); return; }
            GameObject candidate = null;
            var pendingParts = new List<GameObject>();
            var pendingMaterials = new List<Material>();
            try
            {
                rigCatalog ??= Read("characters/player/shared/player_rig_contract");
                items ??= Read("data/character_creation/avatar_items");
                textureCatalog ??= Read("data/character_creation/runtime_textures");
                customization ??= Read("data/character_creation/customization");
                var style = character.Style;
                string gender = style.Gender == 1 ? "male" : style.Gender == 2 ? "female" : throw new InvalidDataException("Unknown avatar gender: " + style.Gender);
                var rig = rigCatalog["genders"]!.Single(g => (string)g["gender"] == gender);
                string actorRootPath = (string)rig["nodes"]!.Single(n => (int)n["actorBoneIndex"]! == 0)["fullPath"];
                bool reuse = actor && actorGender == gender;
                candidate = reuse ? actor : Instantiate(Load<GameObject>((string)rig["skeletonGlb"]), transform, false);
                candidate.name = "Avatar " + character.Uid;
                var bones = reuse ? actorBones : candidate.GetComponentsInChildren<Transform>(true)
                    .ToDictionary(t => RelativePath(t, candidate.transform), StringComparer.Ordinal);
                var parts = new[] {
                    ("face", style.FaceStyle), ("hair", style.HairStyle),
                    ("shirt", AppearanceItem(character, 1)), ("pants", AppearanceItem(character, 2)), ("shoes", AppearanceItem(character, 3))
                };
                var (faceVariant, hairVariant, showGlasses) = PlayerHatPolicy(character);
                var skin = ResolveTexture(gender == "male" ? "m_skin" : "f_skin");
                var skinColor = ColorAt("skin", style.SkinColor);
                var hairColor = ColorAt("hair", style.HairColor);
                foreach (var (category, itemId) in parts)
                {
                    if (category == "hair" && hairVariant == 0) continue;
                    var choice = rig["creatorChoices"]!.FirstOrDefault(c => (string)c["appearanceCategory"] == category && (int)c["itemNumber"]! == itemId);
                    var item = items["items"]!.SingleOrDefault(x => (string)x["category"] == (category == "hair" ? "head" : category) && (int)x["itemNumber"]! == itemId);
                    var visual = item?[gender];
                    bool emptyApparel = itemId == 0 && category is "shirt" or "pants" or "shoes";
                    if (visual == null && !emptyApparel) throw new InvalidDataException($"Missing accepted {gender} {category} item {itemId}.");
                    string path = emptyApparel ? EmptyApparelModel(gender, category) : (string)choice?["glb"];
                    string exactRoute = (string)choice?["exactRoute"];
                    var variant = HatBodyModel(visual, category, faceVariant, hairVariant);
                    if (variant != null)
                    {
                        path = (string)variant["nativeAsset"]["path"];
                        exactRoute = (string)variant["exactRoute"];
                    }
                    if (!emptyApparel && choice == null && (category == "shirt" || category == "pants" || category == "shoes"))
                    {
                        // Many gameplay items reuse a creator mesh with a different texture. Resolve
                        // the complete item table, retaining its explicit source route.
                        var models = visual["models"] as JArray;
                        if ((string)visual["modelStatus"] == "verified_unique" && models?.Count == 1)
                        { path = (string)models[0]["nativeAsset"]?["path"]; exactRoute = (string)models[0]["exactRoute"]; }
                    }
                    var partContract = exactRoute == null ? null : rig["creatorParts"]!.SingleOrDefault(p => (string)p["exactRoute"] == exactRoute);
                    if (string.IsNullOrEmpty(path))
                        throw new InvalidDataException($"No accepted {gender} {category} model for ID {itemId} ({exactRoute ?? "unresolved model"}).");
                    var prefab = Load<GameObject>(path);
                    var part = Instantiate(prefab, candidate.transform, false);
                    part.SetActive(false);
                    pendingParts.Add(part);
                    var textureName = emptyApparel ? EmptyApparelTexture(gender) : (string)visual["primaryTexture"]?["trueName"];
                    if (category == "face")
                    {
                        if (style.EyeColor < 1 || style.EyeColor > ((JArray)customization["eye"]!).Count) throw new InvalidDataException("Invalid eye color code.");
                        textureName += "_" + (char)('a' + style.EyeColor - 1);
                    }
                    if (category == "hair") textureName += "_a";
                    string texturePath = BodyTexturePath(category, emptyApparel ? null : visual, textureName);
                    Texture2D primary = texturePath == null ? null : Load<Texture2D>(texturePath);
                    if (partContract == null) BindSharedRig(part, bones, actorRootPath);
                    else foreach (var renderer in part.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        // The imported primitive sits under its original renderer node. Hair meshes retain
                        // modeling-tool names such as 'Editable Poly'; these are not renderer identities.
                        string rendererIdentity = renderer.transform.parent.name;
                        var remap = partContract["skins"]!.SingleOrDefault(s => (string)s["rendererTrueName"] == rendererIdentity);
                        if (remap == null) throw new InvalidDataException("Missing accepted skin remap: " + rendererIdentity);
                        var sourcePaths = remap["gltfJointPaths"]!.Select(p => (string)p).ToArray();
                        var actorPaths = remap["actorBonePaths"]!.Select(p => (string)p).ToArray();
                        if (sourcePaths.Length != renderer.bones.Length || actorPaths.Length != renderer.bones.Length)
                            throw new InvalidDataException("Accepted skin remap joint count differs: " + renderer.sharedMesh.name);
                        renderer.bones = renderer.bones.Select((b, index) => {
                            if (RelativePath(b, part.transform) != sourcePaths[index])
                                throw new InvalidDataException("Accepted skin remap joint order differs: " + renderer.sharedMesh.name);
                            string actorPath = actorPaths[index];
                            if (!bones.TryGetValue(actorPath, out var match)) throw new InvalidDataException("Missing actor bone: " + actorPath);
                            return match;
                        }).ToArray();
                        renderer.rootBone = bones[actorRootPath];
                        renderer.updateWhenOffscreen = true;
                    }
                    foreach (var renderer in part.GetComponentsInChildren<Renderer>(true))
                    {
                        var replacements = renderer.sharedMaterials.Select(source => {
                            var material = new Material(source);
                            pendingMaterials.Add(material);
                            NativeAvatarMaterials.Bind(material, category, primary, null, skin, skinColor, hairColor);
                            return material;
                        }).ToArray();
                        renderer.sharedMaterials = replacements;
                    }
                }
                AddEquippedParts(character, gender, candidate, bones, actorRootPath, pendingParts, pendingMaterials, hairColor, showGlasses);
                var nextSkirt = reuse ? skirtPose : SkirtPose.FromRig(bones);
                NativePlayerAnimator nextAnimation;
                if (reuse) nextAnimation = playerAnimation;
                else
                {
                    var legacy = candidate.GetComponent<Animation>();
                    if (legacy) { legacy.enabled = false; Release(legacy); }
                    nextAnimation = candidate.AddComponent<NativePlayerAnimator>();
                    nextAnimation.Configure(gender, style.Height, style.Body);
                }
                nextAnimation.SetBodyShape(style.Height, style.Body);
                foreach (var node in candidate.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = gameObject.layer;
                if (!reuse && actor) { actor.SetActive(false); Release(actor); }
                else foreach (var part in displayedParts) { part.SetActive(false); Release(part); }
                foreach (var material in ownedMaterials) if (material) Release(material);
                displayedParts.Clear(); displayedParts.AddRange(pendingParts);
                ownedMaterials.Clear(); ownedMaterials.AddRange(pendingMaterials);
                if (skirtPose != nextSkirt) skirtPose?.Dispose();
                actor = candidate; actorGender = gender; playerAnimation = nextAnimation; actorBones = bones; skirtPose = nextSkirt;
                skirtPose?.BindGarment(displayedParts);
                foreach (var part in displayedParts)
                {
                    part.SetActive(true);
                    if (part.transform.parent != actor.transform) PlayAttachmentLoops(part);
                }
                FinishPose();
                ready = actor.GetComponentsInChildren<Renderer>(true).Length > 0;
                LastError = null;
                RestorePersonalVehicle();
                if (ziplinePresentation) SetWeaponVisible(false);
            }
            catch (Exception exception)
            {
                foreach (var part in pendingParts) if (part) { part.SetActive(false); Release(part); }
                foreach (var material in pendingMaterials) if (material) Release(material);
                if (candidate && candidate != actor) { candidate.SetActive(false); Release(candidate); }
                LastError = exception.Message;
                Debug.LogWarning("FFOne avatar preview: " + LastError, this);
            }
        }

        static (int face, int hair, bool glasses) PlayerHatPolicy(CharacterInfo character)
        {
            int id = AppearanceItem(character, 4);
            int type = id == 0 ? 0 : (int)items["items"]
                .Single(i => (string)i["category"] == "hat" && (int)i["itemNumber"] == id)["equipType"];
            return type switch {
                0 => (1, 1, true),
                1 => (1, 2, true),
                2 => (2, 0, true),
                3 => (2, 0, false),
                4 => (1, 1, false),
                5 => (1, 2, false),
                _ => throw new InvalidDataException($"Unknown hat equipType {type} for item {id}.")
            };
        }

        static JToken HatBodyModel(JToken visual, string category, int faceVariant, int hairVariant)
        {
            if (!(category == "face" && faceVariant == 2 || category == "hair" && hairVariant == 2)) return null;
            return visual["models"].Single(m => ((string)m["trueName"]).EndsWith("_type02", StringComparison.OrdinalIgnoreCase));
        }
        // FFOneClient resolves zero slots to wear/{m,f}_{shirt,pants,shoes}_naked.nif.
        // The two shoes are stored in different collections in the accepted player-items catalog.
        static string EmptyApparelModel(string gender, string category)
        {
            string model = (gender == "male" ? "m_" : "f_") + category + "_naked";
            string collection = category == "shoes"
                ? "collections/" + (gender == "male" ? "m_shoes_naked" : "f_pants_trandyset")
                : category + "/" + model;
            return "characters/player/items/" + collection + "/models/" + model + "/model.glb";
        }
        static string EmptyApparelTexture(string gender) => gender == "male" ? "m_naked" : "f_naked";

        static int AppearanceItem(CharacterInfo character, int slot)
        {
            if (character.Equipment == null || character.Equipment.Length <= slot) throw new InvalidDataException("Character equipment is incomplete.");
            var item = character.Equipment[slot];
            // The high word of iOpt carries the appearance of combined equipment.
            int appearance = item.Option >> 16;
            return item.ItemId > 0 && item.ItemType is >= 0 and <= 6 && appearance > 0 ? appearance : item.ItemId;
        }
        static Color ColorAt(string field, int code)
        {
            var rows = customization[field] as JArray ?? throw new InvalidDataException("Missing color table " + field);
            if (code < 1 || code > rows.Count) throw new InvalidDataException($"Invalid {field} code {code}.");
            var row = rows[code - 1];
            return new Color((float)row[0], (float)row[1], (float)row[2], (float)row[3]);
        }
        static Texture2D ResolveTexture(string trueName)
            => Load<Texture2D>(TexturePath(trueName));
        static string BodyTexturePath(string category, JToken visual, string trueName)
        {
            if (string.IsNullOrEmpty(trueName)) return null;
            var replacement = customization["textures"]?.SingleOrDefault(t => (string)t["trueName"] == trueName);
            if (replacement != null) return (string)replacement["image"]["path"];
            if (category is "shirt" or "pants" or "shoes")
            {
                string path = EquippedTexturePath(visual, "primaryTexture");
                if (path != null) return path;
            }
            return TexturePath(trueName);
        }
        static string TexturePath(string trueName)
        {
            var replacement = customization["textures"]?.SingleOrDefault(t => (string)t["trueName"] == trueName);
            if (replacement != null) return (string)replacement["image"]!["path"];
            var row = textureCatalog["textures"]!.SingleOrDefault(t => (string)t["trueName"] == trueName);
            if (row == null) throw new InvalidDataException("Missing accepted texture identity: " + trueName);
            return (string)row["nativeAsset"]!["path"];
        }
        static T Load<T>(string relative) where T : UnityEngine.Object
        {
            string resource = relative.Substring(0, relative.LastIndexOf('.'));
            var asset = typeof(T) == typeof(GameObject)
                ? NativeModelAssets.Load(resource) as T : FFOne.Content.ContentAssets.Load<T>(resource);
            return asset ? asset : throw new FileNotFoundException("Import required native asset: " + relative);
        }
        static JObject Read(string relative)
        {
            return NativeData.Object(relative);
        }
        static string RelativePath(Transform target, Transform parent)
        {
            if (target == parent) return "";
            var parts = new List<string>();
            while (target && target != parent) { parts.Add(target.name); target = target.parent; }
            parts.Reverse();
            return string.Join("/", parts);
        }
        public void Rotate(float degrees) => transform.Rotate(Vector3.up, degrees, Space.Self);
        // Imported Mecanim clips contain bind-pose scale channels for sockets. AttachGO's
        // explicit scale-one policy must survive that animation evaluation on each instance.
        public void FinishPose()
        {
            if (!actor) return;
            foreach (var part in displayedParts)
                if (part && part.transform.parent && part.transform.parent != actor.transform)
                    part.transform.parent.localScale = Vector3.one;
            if (HasActiveEmote) skirtPose?.FollowThighs();
            FinishZiplinePose();
        }
        void LateUpdate() { TickEmote(); FinishPose(); }
        public void Clear()
        {
            SetSkywayPresentation(false);
            SetZiplinePresentation(false);
            CancelEmote();
            ClearPersonalVehicle();
            if (actor) { actor.SetActive(false); Release(actor); }
            actor = null;
            actorGender = null; playerAnimation = null; actorBones = null; skirtPose?.Dispose(); skirtPose = null; displayedParts.Clear();
            ready = false;
            foreach (var material in ownedMaterials) if (material) Release(material);
            ownedMaterials.Clear();
            LastError = null;
        }
        static void Release(UnityEngine.Object value)
        {
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
        void OnDestroy() => Clear();
    }
}
