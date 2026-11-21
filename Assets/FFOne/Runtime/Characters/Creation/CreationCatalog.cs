using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Network;
using Newtonsoft.Json.Linq;
using UnityEngine;
using CharacterInfo = FFOne.Network.CharacterInfo;

namespace FFOne.Creation
{
    public sealed class CreationCatalog
    {
        readonly JObject appearance;
        readonly JObject customization;
        readonly JObject names;
        readonly Dictionary<(string, int), List<JObject>> choices = new();
        readonly List<JObject>[] nameParts;
        public CreationCatalog() : this(Read("appearance"), Read("customization"), Read("name_wheel")) { }
        CreationCatalog(JObject appearance, JObject customization, JObject names)
        {
            this.appearance = appearance;
            this.customization = customization;
            this.names = names;
            nameParts = new[] { "firstNames", "middleNames", "lastNames" }.Select(part => names[part].OfType<JObject>().ToList()).ToArray();
        }
        public static async Task<CreationCatalog> LoadAsync(CancellationToken cancellation = default)
        {
            var documents = await Task.WhenAll(new[] { "appearance", "customization", "name_wheel" }
                .Select(file => FFOne.Content.NativeData.ObjectAsync("data/character_creation/" + file, cancellation)));
            return new CreationCatalog(documents[0], documents[1], documents[2]);
        }
        // Name reservation has no saved clothing yet. Present an accepted starter
        // appearance on a copy; the authoritative roster remains resumable.
        public static async Task<CharacterInfo> RosterPreviewAsync(CharacterInfo source, CancellationToken cancellation = default)
        {
            var preview = FFOne.Content.NativeAvatarPreview.Snapshot(source);
            if (source.Style.AppearanceComplete) return preview;
            var appearance = await FFOne.Content.NativeData.ObjectAsync("data/character_creation/appearance", cancellation);
            string gender = source.Style.Gender == 2 ? "female" : "male";
            int First(string category) => (int)appearance["choices"].First(c =>
                (string)c["category"] == category && (string)c["gender"] == gender)["value"];
            preview.Style = new CharacterStyle {
                Gender = source.Style.Gender, Height = 2, Body = 1,
                FaceStyle = First("face"), HairStyle = First("hair"), NameCheck = source.Style.NameCheck
            };
            preview.Equipment = new EquipmentItem[9];
            preview.Equipment[1] = Item(1, First("shirt"));
            preview.Equipment[2] = Item(2, First("pants"));
            preview.Equipment[3] = Item(3, First("shoes"));
            return preview;
        }
        static JObject Read(string file)
        {
            return FFOne.Content.NativeData.Object("data/character_creation/" + file);
        }
        public List<JObject> Choices(string category, int gender)
        {
            var key = (category, gender);
            if (!choices.TryGetValue(key, out var result))
                choices[key] = result = appearance["choices"].OfType<JObject>()
                    .Where(c => (string)c["category"] == category && (string)c["gender"] == (gender == 1 ? "male" : "female")).ToList();
            return result;
        }
        public List<JObject> Names(int part) => nameParts[part];
        public Color[] Palette(string category)
        {
            var colors = customization[category].Select(c => new Color((float)c[0], (float)c[1], (float)c[2], (float)c[3]));
            if (category == "skin") colors = colors.Take((int)customization["creationSkinCount"]);
            return colors.ToArray();
        }
        public static CharacterInfo Preview(CharacterCreation draft) => new CharacterInfo {
            Slot = draft.Slot, FirstName = draft.FirstName, LastName = draft.LastName, Level = 1,
            Style = draft.Style,
            Equipment = new[] { new EquipmentItem(), Item(1, draft.UpperBodyId), Item(2, draft.LowerBodyId),
                Item(3, draft.FootId), new EquipmentItem(), new EquipmentItem(), new EquipmentItem(), new EquipmentItem(), new EquipmentItem() }
        };
        static EquipmentItem Item(int type, int id) => new EquipmentItem { ItemType = type, ItemId = id };
        public static string Icon(JObject choice)
        {
            var path = (string)choice.SelectToken("icon.candidates[0].path");
            return string.IsNullOrEmpty(path) ? null : path.Substring(0, path.LastIndexOf('.'));
        }
    }
}
