using System;
using System.Collections.Generic;
using System.Linq;
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
        public CreationCatalog()
        {
            appearance = Read("appearance");
            customization = Read("customization");
            names = Read("name_wheel");
        }
        static JObject Read(string file)
        {
            return FFOne.Content.NativeData.Object("data/character_creation/" + file);
        }
        public List<JObject> Choices(string category, int gender) => appearance["choices"].OfType<JObject>()
            .Where(c => (string)c["category"] == category && (string)c["gender"] == (gender == 1 ? "male" : "female")).ToList();
        public List<JObject> Names(int part) => names[new[] { "firstNames", "middleNames", "lastNames" }[part]].OfType<JObject>().ToList();
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
