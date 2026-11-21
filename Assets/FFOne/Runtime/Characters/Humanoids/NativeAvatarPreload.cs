using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Network;
using Newtonsoft.Json.Linq;
using UnityEngine;
using CharacterInfo = FFOne.Network.CharacterInfo;

namespace FFOne.Content
{
    public sealed partial class NativeAvatarPreview
    {
        sealed class Warmup { public Task Task; public int Completed, Total = 4; }
        static readonly Dictionary<string, Warmup> warmups = new(StringComparer.Ordinal);
        static Task catalogWarmup;
        static CancellationTokenSource preloadLifetime = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetPreloads()
        {
            preloadLifetime.Cancel(); preloadLifetime.Dispose(); preloadLifetime = new();
            warmups.Clear(); catalogWarmup = null;
            rigCatalog = items = textureCatalog = customization = null;
            hnpcCatalog = hnpcPalette = npcTextureCatalog = null;
            hnpcCatalogWarmup = npcTextureWarmup = null;
        }

        public static CharacterInfo Snapshot(CharacterInfo source)
        {
            var s = source.Style;
            return new CharacterInfo {
                Uid = source.Uid, Slot = source.Slot, Level = source.Level,
                FirstName = source.FirstName, LastName = source.LastName,
                Equipment = (EquipmentItem[])source.Equipment.Clone(),
                Style = new CharacterStyle {
                    Gender = s.Gender, FaceStyle = s.FaceStyle, HairStyle = s.HairStyle,
                    HairColor = s.HairColor, SkinColor = s.SkinColor, EyeColor = s.EyeColor,
                    Height = s.Height, Body = s.Body, Class = s.Class, NameCheck = s.NameCheck,
                    AppearanceComplete = s.AppearanceComplete, TutorialComplete = s.TutorialComplete, Payzone = s.Payzone
                }
            };
        }

        // Prepare exactly the appearance being requested; Apply then assembles only resident assets.
        // Multiple previews share the content cache, while each request retains its own snapshot.
        public static async Task PreloadAsync(CharacterInfo character, CancellationToken cancellation = default, Action<float> progress = null)
        {
            cancellation.ThrowIfCancellationRequested();
            var style = character.Style;
            string key = $"{style.Gender}:{style.FaceStyle}:{style.HairStyle}:{style.EyeColor}";
            for (int slot = 0; slot < character.Equipment.Length; slot++)
                key += $":{AppearanceItem(character, slot)}";
            if (warmups.TryGetValue(key, out var work) && (work.Task.IsFaulted || work.Task.IsCanceled))
            { warmups.Remove(key); work = null; }
            if (work == null)
            {
                work = new Warmup(); warmups.Add(key, work);
                work.Task = PreloadCoreAsync(Snapshot(character), work, preloadLifetime.Token);
            }
            while (!work.Task.IsCompleted)
            {
                progress?.Invoke((float)work.Completed / work.Total);
                await Task.Yield(); cancellation.ThrowIfCancellationRequested();
            }
            await work.Task; cancellation.ThrowIfCancellationRequested(); progress?.Invoke(1);
        }

        static async Task PrepareCatalogsAsync(CancellationToken cancellation)
        {
            var rig = NativeData.ObjectAsync("characters/player/shared/player_rig_contract", cancellation);
            var itemTable = NativeData.ObjectAsync("data/character_creation/avatar_items", cancellation);
            var textures = NativeData.ObjectAsync("data/character_creation/runtime_textures", cancellation);
            var choices = NativeData.ObjectAsync("data/character_creation/customization", cancellation);
            await Task.WhenAll(rig, itemTable, textures, choices);
            cancellation.ThrowIfCancellationRequested();
            rigCatalog = rig.Result; items = itemTable.Result; textureCatalog = textures.Result; customization = choices.Result;
        }

        static Task EnsureAvatarCatalogsAsync()
        {
            if (rigCatalog != null && items != null && textureCatalog != null && customization != null) return Task.CompletedTask;
            if (catalogWarmup == null || catalogWarmup.IsFaulted || catalogWarmup.IsCanceled)
                catalogWarmup = PrepareCatalogsAsync(preloadLifetime.Token);
            return catalogWarmup;
        }

        static async Task PreloadCoreAsync(CharacterInfo character, Warmup work, CancellationToken cancellation)
        {
            await EnsureAvatarCatalogsAsync();
            cancellation.ThrowIfCancellationRequested();
            var style = character.Style;
            string gender = style.Gender == 1 ? "male" : "female";
            var rig = rigCatalog["genders"].Single(g => (string)g["gender"] == gender);
            var models = new HashSet<string>(StringComparer.Ordinal) { (string)rig["skeletonGlb"] };
            var textures = new HashSet<string>(StringComparer.Ordinal) { TexturePath(gender == "male" ? "m_skin" : "f_skin") };
            var (faceVariant, hairVariant, showGlasses) = PlayerHatPolicy(character);
            foreach (var (category, id) in new[] { ("face", style.FaceStyle), ("hair", style.HairStyle),
                ("shirt", AppearanceItem(character, 1)), ("pants", AppearanceItem(character, 2)), ("shoes", AppearanceItem(character, 3)) })
            {
                if (category == "hair" && hairVariant == 0) continue;
                if (id == 0 && category is "shirt" or "pants" or "shoes")
                {
                    models.Add(EmptyApparelModel(gender, category));
                    textures.Add(TexturePath(EmptyApparelTexture(gender)));
                    continue;
                }
                var choice = rig["creatorChoices"].FirstOrDefault(c => (string)c["appearanceCategory"] == category && (int)c["itemNumber"] == id);
                var item = items["items"].SingleOrDefault(i => (string)i["category"] == (category == "hair" ? "head" : category) && (int)i["itemNumber"] == id);
                var visual = item?[gender] ?? throw new InvalidDataException($"Missing accepted {gender} {category} item {id}.");
                string path = (string)choice?["glb"];
                if (choice == null && (string)visual["modelStatus"] == "verified_unique" && visual["models"] is JArray available && available.Count == 1)
                    path = (string)available[0]["nativeAsset"]?["path"];
                var variant = HatBodyModel(visual, category, faceVariant, hairVariant);
                if (variant != null) path = (string)variant["nativeAsset"]["path"];
                if (path == null) throw new InvalidDataException($"No accepted {gender} {category} model for ID {id}.");
                models.Add(path);
                string texture = (string)visual["primaryTexture"]?["trueName"];
                if (category == "face") texture += "_" + (char)('a' + style.EyeColor - 1);
                if (category == "hair") texture += "_a";
                string texturePath = BodyTexturePath(category, visual, texture);
                if (texturePath != null) textures.Add(texturePath);
            }
            foreach (var (slot, category) in new[] { (4, "hat"), (5, "glasses"), (6, "back"), (0, "weapon") })
            {
                int id = AppearanceItem(character, slot);
                if (id == 0 || category == "glasses" && !showGlasses) continue;
                var visual = items["items"].Single(i => (string)i["category"] == category && (int)i["itemNumber"] == id)[gender];
                var available = visual?["models"] as JArray;
                if ((string)visual?["modelStatus"] != "verified_unique" || available?.Count != 1)
                    throw new InvalidDataException($"Equipment {category}:{id} lacks one accepted {gender} model.");
                models.Add((string)available[0]["nativeAsset"]["path"]);
                string primary = EquippedTexturePath(visual, "primaryTexture");
                string secondary = EquippedTexturePath(visual, "secondaryTexture");
                if (primary != null) textures.Add(primary);
                if (secondary != null) textures.Add(secondary);
            }
            work.Completed = 4; work.Total = 4 + models.Count + textures.Count + 1;
            var pending = new List<Task>();
            async Task Complete(Task task) { await task; work.Completed++; }
            async Task Model(string path)
            {
                if (!await NativeModelAssets.PreloadAsync(path, cancellation)) throw new FileNotFoundException("Missing avatar model", path);
            }
            foreach (string path in models) pending.Add(Complete(Model(path)));
            foreach (string path in textures)
                pending.Add(Complete(ContentAssets.GetAsync<Texture2D>(path.Substring(0, path.LastIndexOf('.')), cancellation)));
            pending.Add(Complete(ContentAssets.GetAsync<NativePlayerAnimationProfile>(NativePlayerAnimator.ProfileAddress(gender), cancellation)));
            await Task.WhenAll(pending);
        }
    }
}
