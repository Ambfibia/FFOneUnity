using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FFOne.Content;

namespace FFOne.UI
{
    public sealed class GameplayItemPresentation
    {
        public int ItemType { get; internal set; }
        public int ItemId { get; internal set; }
        public string Name { get; internal set; }
        public string Description { get; internal set; }
        public string IconAddress { get; internal set; }
        public int RequiredLevel { get; internal set; }
        public int GeneralKind { get; internal set; }
        public int LinkedSkillId { get; internal set; }
        public int MaxStack { get; internal set; }
        public int SellPrice { get; internal set; }
        public int BuyPrice { get; internal set; }
        public int Rarity { get; internal set; }
        public bool Sellable { get; internal set; }
        public int? NanoStyle { get; internal set; }
        public int? NanoMaxStamina { get; internal set; }
        public string NanoModelResource { get; internal set; }
        public string NanoReadyIconAddress { get; internal set; }
        public int NanoSortOrder { get; internal set; }
        public float? AttackRange { get; internal set; }
        public int VehicleSpeed { get; internal set; }
        public int VehicleEquipType { get; internal set; }
        public string VehicleEngineSound { get; internal set; }
    }

    public sealed class GameplayMissionPresentation
    {
        public int TaskId { get; internal set; }
        public int MissionId { get; internal set; }
        public string Title { get; internal set; }
        public string Objective { get; internal set; }
        public string OfferDescription { get; internal set; }
        public string ActiveDescription { get; internal set; }
        public string CompletionDescription { get; internal set; }
        public string CompletedSummary { get; internal set; }
        public string RewardDescription { get; internal set; }
    }

    /// <summary>Compact typed lookups projected from exact XDT IDs/row references; never matches by display names.</summary>
    public sealed class GameplayUiCatalog
    {
        sealed class ItemRow
        {
            public int Type, Id, Level, Stack, SellPrice, BuyPrice, Rarity;
            public bool Sellable;
            public int? NanoStyle, NanoMaxStamina;
            public string NameKey, Name, DescriptionKey, Description, Icon, NanoModel, NanoReadyIcon;
            public int NanoSortOrder;
            public float? AttackRange;
            public int VehicleSpeed, VehicleEquipType, GeneralKind, LinkedSkillId;
            public string VehicleEngineSound;
        }
        sealed class MissionRow
        {
            public int Id, Mission;
            public string TitleKey, Title, ObjectiveKey, Objective;
            public int Offer, Active, Completion, CompletedSummary, Reward;
        }
        sealed class MissionRewardRow { public int Taros, FusionMatter; public int[] Items, Types; }
        readonly Dictionary<(int, int), ItemRow> items = new Dictionary<(int, int), ItemRow>();
        readonly Dictionary<int, MissionRow> missions = new Dictionary<int, MissionRow>();
        readonly Dictionary<int, string> missionStrings = new();
        readonly Dictionary<int, MissionRewardRow> missionRewards = new();
        readonly Dictionary<int, string> buffIcons = new Dictionary<int, string>();
        readonly Dictionary<int, (int loop, int instant)> buffEffects = new Dictionary<int, (int, int)>();
        readonly Dictionary<int, string> skillIcons = new Dictionary<int, string>();
        readonly Dictionary<int, string> skillNames = new Dictionary<int, string>();
        readonly Dictionary<int, string> npcPortraits = new Dictionary<int, string>();
        static GameplayUiCatalog cached;
        public static GameplayUiCatalog Load() => cached ?? (cached = ContentAssets.Read<NativeDataDocument, GameplayUiCatalog>(
            NativeData.Resource("data/tables/xdt"), FromDocument));

        public GameplayItemPresentation Item(int itemType, int itemId, string language = "en")
        {
            if (!items.TryGetValue((itemType, itemId), out var row)) return null;
            return new GameplayItemPresentation { ItemType = itemType, ItemId = itemId, Name = Localize(row.NameKey, row.Name, language),
                Description = Localize(row.DescriptionKey, row.Description, language), IconAddress = row.Icon, RequiredLevel = row.Level, MaxStack = row.Stack, GeneralKind = row.GeneralKind, LinkedSkillId = row.LinkedSkillId,
                NanoStyle = row.NanoStyle, NanoMaxStamina = row.NanoMaxStamina, NanoModelResource = row.NanoModel,
                NanoReadyIconAddress = row.NanoReadyIcon, NanoSortOrder = row.NanoSortOrder, AttackRange = row.AttackRange,
                VehicleSpeed = row.VehicleSpeed, VehicleEquipType = row.VehicleEquipType, VehicleEngineSound = row.VehicleEngineSound,
                SellPrice = row.SellPrice, BuyPrice = row.BuyPrice, Rarity = row.Rarity, Sellable = row.Sellable };
        }

        public GameplayItemPresentation Nano(int id, string language = "en") => Item(19, id, language);
        public IEnumerable<int> NanoIds => items.Keys.Where(key => key.Item1 == 19).Select(key => key.Item2).OrderBy(id => id);
        public IEnumerable<GameplayItemPresentation> Nanos(string language = "en") => NanoIds.Select(id => Nano(id, language));
        public string SkillIcon(int id) => skillIcons.TryGetValue(id, out var icon) ? icon : null;
        public string SkillName(int id, string language = "en") => skillNames.TryGetValue(id, out var name)
            ? Localize("content.tabledata.skill.skill_string." + id + ".str_name", name, language) : "";
        public string NpcPortrait(int npcId) => npcPortraits.TryGetValue(npcId, out var icon) ? icon : null;

        public string BuffIcon(int id) => buffIcons.TryGetValue(id, out var icon) ? icon : null;
        public int BuffEffect(int id, bool instant = false) => buffEffects.TryGetValue(id, out var value) ? instant ? value.instant : value.loop : 0;

        public GameplayMissionPresentation Mission(int taskId, string language = "en")
        {
            if (!missions.TryGetValue(taskId, out var row)) return null;
            return new GameplayMissionPresentation { TaskId = taskId, MissionId = row.Mission,
                Title = Localize(row.TitleKey, row.Title, language), Objective = Localize(row.ObjectiveKey, row.Objective, language),
                OfferDescription = MissionLine(row.Offer, language), ActiveDescription = MissionLine(row.Active, language), CompletionDescription = MissionLine(row.Completion, language),
                CompletedSummary = MissionLine(row.CompletedSummary, language),
                RewardDescription = MissionRewardDescription(row.Reward, language) };
        }

        string MissionRewardDescription(int id, string language)
        {
            if (!missionRewards.TryGetValue(id, out var row)) return "";
            var parts = new List<string>();
            if (row.Taros > 0) parts.Add(row.Taros + " Taros");
            if (row.FusionMatter > 0) parts.Add(row.FusionMatter + (language == "ru" ? " Фузионной материи" : " Fusion Matter"));
            for (int i = 0; i < row.Items.Length; i++) if (row.Items[i] > 0) parts.Add(Item(row.Types[i], row.Items[i], language)?.Name ?? row.Items[i].ToString());
            return parts.Count == 0 ? "" : (language == "ru" ? "НАГРАДА: " : "REWARD: ") + string.Join(", ", parts);
        }

        public string MissionLine(int stringId, string language = "en") => stringId > 0 && missionStrings.TryGetValue(stringId, out string text)
            ? Localize(MissionKey(stringId), text, language) : "";

        static string Localize(string key, string fallback, string language)
        {
            if (string.IsNullOrEmpty(key)) return fallback ?? "";
            string value = LocalizationCatalog.Lookup(key, language);
            return value == key ? fallback ?? "" : value;
        }

        public static GameplayUiCatalog FromDocument(NativeDataDocument document)
        {
            if (!document || document.Nodes.Length == 0) throw new InvalidDataException("Missing gameplay XDT.");
            var result = new GameplayUiCatalog();
            // eItemType + RustyFusion tabledata.rs: type and item number jointly identify an item.
            string[] tables = { "Weapon", "Shirts", "Pants", "Shoes", "Hat", "Glass", "Back", "General", "Quest", "Chest", "Vehicle" };
            string[] keys = { "weapon", "shirts", "pants", "shoes", "hat", "glass", "back", "general", "quest", "chest", "vehicle" };
            for (int type = 0; type < tables.Length; type++) result.ReadItems(document, type, "m_p" + tables[type] + "ItemTable", keys[type] + "_item", false);
            result.ReadItems(document, 19, "m_pNanoTable", "nano", true);
            int npcTable = Child(document, 0, "m_pNpcTable");
            int[] npcIconRows = Rows(document, Child(document, npcTable, "m_pNpcIconData"));
            foreach (int row in Rows(document, Child(document, npcTable, "m_pNpcData")))
            {
                int id = Number(document, row, "m_iNpcNumber"), iconReference = Number(document, row, "m_iIcon1");
                // Row zero is the unassigned NPC icon; the first real portrait may
                // still have atlas number zero. Neither number is the NPC identity.
                if (id <= 0 || iconReference <= 0) continue;
                int icon = At(npcIconRows, iconReference);
                if (icon >= 0) result.npcPortraits[id] = IconAddress(Number(document, icon, "m_iIconType"), Number(document, icon, "m_iIconNumber"));
            }
            int skillTable = Child(document, 0, "m_pSkillTable");
            int[] skillIconRows = Rows(document, Child(document, skillTable, "m_pSkillIconData"));
            int[] skillStringRows = Rows(document, Child(document, skillTable, "m_pSkillStringData"));
            foreach (int row in Rows(document, Child(document, skillTable, "m_pSkillBuffData")))
            {
                int id = Number(document, row, "m_iBuffNumber"); if (id <= 0) continue;
                int icon = At(skillIconRows, Number(document, row, "m_iBuffIcon"));
                result.buffIcons[id] = icon < 0 ? null : IconAddress(Number(document, icon, "m_iIconType"), Number(document, icon, "m_iIconNumber"));
                result.buffEffects[id] = (Number(document, row, "m_iBuffEffect"), Number(document, row, "m_iBuffEffectInstant"));
            }
            foreach (int row in Rows(document, Child(document, skillTable, "m_pSkillData")))
            {
                int id = Number(document, row, "m_iSkillNumber"); if (id <= 0) continue;
                int icon = At(skillIconRows, Number(document, row, "m_iIcon"));
                result.skillIcons[id] = icon < 0 ? null : IconAddress(Number(document, icon, "m_iIconType"), Number(document, icon, "m_iIconNumber"));
                // SkillElement has no name-reference field in the accepted XDT;
                // m_pSkillStringData uses the skill-number index directly.
                int name = At(skillStringRows, id);
                if (name >= 0) result.skillNames[id] = Text(document, name, "m_strName");
            }
            int mission = Child(document, 0, "m_pMissionTable"), data = Child(document, mission, "m_pMissionData");
            int[] strings = Rows(document, Child(document, mission, "m_pMissionStringData"));
            for (int index = 0; index < strings.Length; index++) result.missionStrings[index] = Text(document, strings[index], "m_pstrNameString");
            int[] journal = Rows(document, Child(document, mission, "m_pJournalData"));
            foreach (int row in Rows(document, Child(document, mission, "m_pRewardData")))
            {
                int id = Number(document, row, "m_iMissionRewardID"); if (id <= 0) continue;
                int[] Numbers(string key) => Rows(document, Child(document, row, key)).Select(node => int.Parse(document.Nodes[node].Value, CultureInfo.InvariantCulture)).ToArray();
                result.missionRewards[id] = new MissionRewardRow { Taros = Number(document, row, "m_iCash"), FusionMatter = Number(document, row, "m_iFusionMatter"),
                    Items = Numbers("m_iMissionRewardItemID"), Types = Numbers("m_iMissionRewarItemType") };
            }
            foreach (int row in Rows(document, data))
            {
                int id = Number(document, row, "m_iHTaskID"); if (id <= 0) continue;
                int title = Number(document, row, "m_iHMissionName"), objective = Number(document, row, "m_iHCurrentObjective");
                int journalRow = At(journal, Number(document, row, "m_iSTJournalIDAdd"));
                var value = new MissionRow { Id = id, Mission = Number(document, row, "m_iHMissionID"),
                    Offer = Number(document, journalRow, "m_iDetaileMissionDesc"), Active = Number(document, journalRow, "m_iDetailedTaskDesc"),
                    Completion = Number(document, journalRow, "m_iDetaileMissionCompleteSummary"),
                    CompletedSummary = Number(document, journalRow, "m_iMissionCompleteSummary"),
                    Reward = Number(document, row, "m_iSUReward"),
                    TitleKey = MissionKey(title), ObjectiveKey = MissionKey(objective),
                    Title = Text(document, At(strings, title), "m_pstrNameString"), Objective = Text(document, At(strings, objective), "m_pstrNameString") };
                if (result.missions.ContainsKey(id)) throw new InvalidDataException("Duplicate task ID: " + id);
                result.missions.Add(id, value);
            }
            return result;
        }

        void ReadItems(NativeDataDocument document, int type, string tableName, string localization, bool nano)
        {
            int table = Child(document, 0, tableName); if (table < 0) return;
            string prefix = nano ? "Nano" : "Item";
            int[] strings = Rows(document, Child(document, table, "m_p" + prefix + "StringData"));
            int[] icons = Rows(document, Child(document, table, "m_p" + prefix + "IconData"));
            int[] meshes = nano ? Rows(document, Child(document, table, "m_pNanoMeshData")) : Array.Empty<int>();
            int[] sounds = type == 10 ? Rows(document, Child(document, table, "m_pItemSoundData")) : Array.Empty<int>();
            foreach (int row in Rows(document, Child(document, table, "m_p" + prefix + "Data")))
            {
                int id = Number(document, row, nano ? "m_iNanoNumber" : "m_iItemNumber"); if (id <= 0) continue;
                int name = Number(document, row, nano ? "m_iNanoName" : "m_iItemName");
                int description = Number(document, row, type == 9 ? "m_iChestDesc" : "m_iComment");
                int iconRow = At(icons, Number(document, row, nano ? "m_iIcon1" : "m_iIcon"));
                string model = nano ? Text(document, At(meshes, Number(document, row, "m_iMesh")), "m_pstrMMeshModelString") : null;
                string nanoIconSlug = nano ? NanoIconSlug(model, iconRow < 0 ? -1 : Number(document, iconRow, "m_iIconType"),
                    iconRow < 0 ? -1 : Number(document, iconRow, "m_iIconNumber")) : null;
                var value = new ItemRow { Type = type, Id = id,
                    GeneralKind = type == 7 ? Number(document, row, "m_iItemType") : 0,
                    LinkedSkillId = type == 7 ? Number(document, row, "m_iLinkSkill") : 0, AttackRange = type == 0 ? Number(document, row, "m_iAtkRange") / 100f : (float?)null,
                    VehicleSpeed = type == 10 ? Number(document, row, "m_iUp_runSpeed") : 0,
                    VehicleEquipType = type == 10 ? Number(document, row, "m_iEquipType") : 0,
                    VehicleEngineSound = type == 10 ? Text(document, At(sounds, Number(document, row, "m_iSound1")), "m_pstrSoundString1") : null,
                    SellPrice = Number(document, row, "m_iItemSellPrice"), BuyPrice = Number(document, row, "m_iItemPrice"), Rarity = Number(document, row, "m_iRarity"), Sellable = Number(document, row, "m_iSellAble") != 0,
                    Name = Text(document, At(strings, name), "m_strName"), Description = Text(document, At(strings, description), "m_strComment"),
                    NameKey = "content.tabledata." + localization + "." + (nano ? "nano_string." : "item_string.") + name + ".str_name",
                    DescriptionKey = "content.tabledata." + localization + "." + (nano ? "nano_string." : "item_string.") + description + ".str_comment",
                    Icon = nano ? "icons/entities/nanos/nanoicon_" + nanoIconSlug
                        : iconRow < 0 ? null : IconAddress(Number(document, iconRow, "m_iIconType"), Number(document, iconRow, "m_iIconNumber")),
                    NanoReadyIcon = nano ? "icons/entities/nanos/ready/nanoready_" + nanoIconSlug : null,
                    NanoSortOrder = nano ? Number(document, row, "m_iNanoSet") : 0,
                    NanoModel = string.IsNullOrEmpty(model) || model == "null" ? null : "characters/nanos/" + model + "/" + model,
                    Level = Number(document, row, "m_iMinReqLev"), Stack = Number(document, row, "m_iStackNumber"),
                    NanoStyle = nano && Child(document, row, "m_iStyle") >= 0 ? Number(document, row, "m_iStyle") : (int?)null,
                    NanoMaxStamina = nano && Child(document, row, "m_iNanoBattery1") >= 0 ? Number(document, row, "m_iNanoBattery1") : (int?)null };
                if (items.TryGetValue((type, id), out var existing))
                {
                    // Accepted XDT contains two identical Chest274 rows. Identical presentation is
                    // harmless; different links for one wire identity must remain an import error.
                    if (existing.NameKey == value.NameKey && existing.DescriptionKey == value.DescriptionKey &&
                        existing.Name == value.Name && existing.Description == value.Description && existing.Icon == value.Icon &&
                        existing.Level == value.Level && existing.Stack == value.Stack && existing.NanoStyle == value.NanoStyle && existing.NanoMaxStamina == value.NanoMaxStamina && existing.NanoModel == value.NanoModel &&
                        existing.NanoReadyIcon == value.NanoReadyIcon && existing.NanoSortOrder == value.NanoSortOrder) continue;
                    throw new InvalidDataException("Conflicting item ID: " + type + "/" + id);
                }
                items.Add((type, id), value);
            }
        }

        static string MissionKey(int index) => "content.tabledata.mission.mission_string." + index + ".str_name_string";

        // Existing semantic paths are the accepted AvatarUtil icon-type dispatch. Nano icon numbers
        // refer to the source atlas ordering, not Nano gameplay IDs or the localized character name.
        public static string IconAddress(int type, int number)
        {
            if (number < 0) return null;
            string suffix = number.ToString("D2", CultureInfo.InvariantCulture);
            switch (type)
            {
                case 0: return "icons/items/weapons/wpnicon_" + suffix;
                case 1:
                case 5:
                    string slug = AtlasNanoIconSlug(number);
                    return slug == null ? null : "icons/entities/nanos/" + (type == 5 ? "ready/nanoready_" : "nanoicon_") + slug;
                case 2: return "icons/skills/skillicon_" + suffix;
                case 3: return "icons/items/cosmetics/cosicon_" + suffix;
                case 4: return "icons/entities/npc/npcicon_" + suffix;
                case 7: return "icons/items/general/generalitemicon_" + suffix;
                case 8: return "icons/entities/mobs/mobicon_" + suffix;
                case 10: return "icons/entities/hnpc/hnpcicon_" + suffix;
                case 11: return "icons/transport/transport_" + suffix;
                case 12: return "icons/items/vehicles/vehicle_" + suffix;
                default: return null;
            }
        }
        static readonly string[] NanoIconSlugs = { "samurai-jack", "eduardo", "mojo-jojo", "numbuh-five", "mandark", "fourarms", "billy", "blossom", "dexter", "ed",
            "grim", "juniper-lee", "mandy", "aku", "bloo", "coco", "courage", "dee-dee", "demongo", "edd", "eddy", "humongosaur", "hex", "him", "mac", "megas",
            "numbuh-one", "numbuh-four", "numbuh-two", "numbuh-three", "swampfire", "prof-utonium", "vilgax", "wilt", "buttercup", "bubbles", "missing", "ben-10",
            "johnny-bravo", "cheese", "finn", "flapjack", "holo-nano", "belladonna", "computress", "runty", "finn", "coop", "panini", "jack-olantern" };
        // Later XDT atlas numbers, verified against the row's native mesh identity and published icon assets.
        static readonly Dictionary<int, string> ExtendedNanoIconSlugs = new() {
            { 50, "titan" }, { 51, "gumball" }, { 52, "chowder" }, { 53, "zak-saturday" }, { 54, "rath" },
            { 57, "van-kleiss" }, { 58, "mordecai" }, { 59, "darwin" }, { 60, "p-bubblegum" }
        };

        static string AtlasNanoIconSlug(int number) => number < 0 ? null : number < NanoIconSlugs.Length ? NanoIconSlugs[number]
            : ExtendedNanoIconSlugs.TryGetValue(number, out var slug) ? slug : null;

        static string NanoIconSlug(string model, int iconType, int iconNumber)
        {
            // Accepted native additions share legacy atlas numbers with unrelated Nanos.
            // Their exact mesh identity owns the custom portrait and matching authored ready
            // image. Display names and gameplay IDs are separate namespaces and cannot fix
            // these collisions. Source: accepted native XDT mesh links and installed icon art.
            if (model != null && NativeNanoIconSlugs.TryGetValue(model, out var native)) return native;
            // The Academy incubator additions have no authored portraits. Keep their real
            // missing-Nano images rather than dispatching their zeroed rows as weapons.
            return iconType == 1 ? AtlasNanoIconSlug(iconNumber) ?? "missing" : "missing";
        }

        static readonly Dictionary<string, string> NativeNanoIconSlugs = new(StringComparer.Ordinal) {
            { "nano_holonano", "holo-nano" }, { "nano_finn", "finn" }, { "nano_rex", "rex" },
            { "nano_alienx", "alien-x" }, { "nano_cheese", "cheese" }, { "nano_rigby", "rigby" },
            { "nano_jake", "jake" }, { "nano_chowder", "chowder" }, { "nano_johnnytest", "johnny-test" },
            { "nano_titan", "titan" }, { "nano_zaksaturday", "zak-saturday" }, { "nano_iceking", "ice-king" },
            { "nano_gumball", "gumball" }, { "nano_ampfibian", "ampfibian" }, { "nano_rath", "rath" },
            { "nano_darwin", "darwin" }, { "nano_mordecai", "mordecai" }, { "nano_princessbubblegum", "p-bubblegum" },
            { "nano_vankleiss", "van-kleiss" }, { "nano_ghostfreak", "ghostfreak" }, { "nano_upgrade", "upgrade" }
        };

        static int Child(NativeDataDocument document, int parent, string key)
        {
            if (parent < 0) return -1;
            foreach (int index in document.Children(parent)) if (document.Nodes[index].Key == key) return index;
            return -1;
        }
        static int[] Rows(NativeDataDocument document, int parent)
            => parent < 0 ? Array.Empty<int>() : new List<int>(document.Children(parent)).ToArray();
        static int At(int[] rows, int index) => index >= 0 && index < rows.Length ? rows[index] : -1;
        static string Text(NativeDataDocument document, int parent, string key)
        { int index = Child(document, parent, key); return index < 0 ? "" : document.Nodes[index].Value ?? ""; }
        static int Number(NativeDataDocument document, int parent, string key)
        {
            int index = Child(document, parent, key); if (index < 0) return 0;
            if (!int.TryParse(document.Nodes[index].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) throw new InvalidDataException("Invalid numeric XDT field: " + key);
            return value;
        }
    }
}
