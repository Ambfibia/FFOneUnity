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
                    ("shirt", Item(character, 1)), ("pants", Item(character, 2)), ("shoes", Item(character, 3))
                };
                var skin = ResolveTexture(gender == "male" ? "m_skin" : "f_skin");
                var skinColor = ColorAt("skin", style.SkinColor);
                var hairColor = ColorAt("hair", style.HairColor);
                foreach (var (category, itemId) in parts)
                {
                    var choice = rig["creatorChoices"]!.FirstOrDefault(c => (string)c["appearanceCategory"] == category && (int)c["itemNumber"]! == itemId);
                    var item = items["items"]!.SingleOrDefault(x => (string)x["category"] == (category == "hair" ? "head" : category) && (int)x["itemNumber"]! == itemId);
                    var visual = item?[gender];
                    if (visual == null) throw new InvalidDataException($"Missing accepted {gender} {category} item {itemId}.");
                    string path = (string)choice?["glb"], exactRoute = (string)choice?["exactRoute"];
                    if (choice == null && (category == "shirt" || category == "pants" || category == "shoes"))
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
                    var textureName = (string)visual["primaryTexture"]?["trueName"];
                    if (category == "face")
                    {
                        if (style.EyeColor < 1 || style.EyeColor > ((JArray)customization["eye"]!).Count) throw new InvalidDataException("Invalid eye color code.");
                        textureName += "_" + (char)('a' + style.EyeColor - 1);
                    }
                    if (category == "hair") textureName += "_a";
                    Texture2D primary = !string.IsNullOrEmpty(textureName) ? ResolveTexture(textureName) : null;
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
                            bool isSkin = source.name.Contains("sub", StringComparison.OrdinalIgnoreCase);
                            if (isSkin) { material.SetTexture("_MainTex", skin); Tint(material, skinColor); }
                            else if (primary) material.SetTexture("_MainTex", primary);
                            if (source.name.Contains("hair", StringComparison.OrdinalIgnoreCase)) Tint(material, hairColor);
                            NativeAvatarMaterials.Configure(material, category);
                            return material;
                        }).ToArray();
                        renderer.sharedMaterials = replacements;
                    }
                }
                AddEquippedParts(character, gender, candidate, bones, actorRootPath, pendingParts, pendingMaterials);
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
                actor = candidate; actorGender = gender; playerAnimation = nextAnimation; actorBones = bones;
                foreach (var part in displayedParts) part.SetActive(true);
                FinishPose();
                ready = actor.GetComponentsInChildren<Renderer>(true).Length > 0;
                LastError = null;
                RestorePersonalVehicle();
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
        static int Item(CharacterInfo character, int slot)
        {
            if (character.Equipment == null || character.Equipment.Length <= slot) throw new InvalidDataException("Character equipment is incomplete.");
            return character.Equipment[slot].ItemId;
        }
        static void Tint(Material material, Color tint)
        {
            material.SetColor("_Color", new Color(tint.r * .5f, tint.g * .5f, tint.b * .5f, 1));
            material.SetColor("_Emission", material.GetColor("_Emission") * tint);
        }
        static Color ColorAt(string field, int code)
        {
            var rows = customization[field] as JArray ?? throw new InvalidDataException("Missing color table " + field);
            if (code < 1 || code > rows.Count) throw new InvalidDataException($"Invalid {field} code {code}.");
            var row = rows[code - 1];
            return new Color((float)row[0], (float)row[1], (float)row[2], (float)row[3]);
        }
        static Texture2D ResolveTexture(string trueName)
        {
            var replacement = customization["textures"]?.SingleOrDefault(t => (string)t["trueName"] == trueName);
            if (replacement != null) return Load<Texture2D>((string)replacement["image"]!["path"]);
            var row = textureCatalog["textures"]!.SingleOrDefault(t => (string)t["trueName"] == trueName);
            if (row == null) throw new InvalidDataException("Missing accepted texture identity: " + trueName);
            return Load<Texture2D>((string)row["nativeAsset"]!["path"]);
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
        }
        void LateUpdate() => FinishPose();
        public void Clear()
        {
            ClearPersonalVehicle();
            if (actor) { actor.SetActive(false); Release(actor); }
            actor = null;
            actorGender = null; playerAnimation = null; actorBones = null; displayedParts.Clear();
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
