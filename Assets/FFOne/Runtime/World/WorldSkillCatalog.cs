using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using FFOne.Content;

namespace FFOne.World
{
    public sealed class WorldSkillDefinition
    {
        public int Id, Type, TargetType, TargetingType, TargetCount, TargetEffect;
        public float Range, Area, Angle, Cooldown;
        public bool Passive;
        public int[] MovementPower;
    }
    public sealed class WorldSkillCatalog
    {
        readonly Dictionary<int, WorldSkillDefinition> skills = new();
        static WorldSkillCatalog cached;
        public static WorldSkillCatalog Load() => cached ??= ContentAssets.Read<NativeDataDocument, WorldSkillCatalog>(NativeData.Resource("data/tables/xdt"), Read);
        public WorldSkillDefinition Find(int id) => skills.TryGetValue(id, out var skill) ? skill : null;
        public static WorldSkillCatalog Read(NativeDataDocument d)
        {
            var result = new WorldSkillCatalog(); int table = Child(d, Child(d, 0, "m_pSkillTable"), "m_pSkillData");
            if (table >= 0) foreach (int row in d.Children(table))
            {
                int id = Number(d, row, "m_iSkillNumber"); if (id <= 0) continue;
                result.skills[id] = new WorldSkillDefinition { Id = id, Type = Number(d, row, "m_iSkillType"), TargetType = Number(d, row, "m_iTargetType"),
                    TargetingType = Number(d, row, "m_iEffectTarget"), TargetCount = Number(d, row, "m_iTargetNumber"), TargetEffect = Number(d, row, "m_iTargetEffect"),
                    Range = Number(d, row, "m_iEffectRange") / 100f, Area = Number(d, row, "m_iEffectArea") / 100f,
                    Angle = Number(d, row, "m_iEffectAngle"), Cooldown = Number(d, row, "m_iCoolTime") / 10f,
                    Passive = Number(d, row, "m_iBatteryDrainType") == 2 };
                var definition = result.skills[id];
                int values = Child(d, row, "m_iValueA");
                if (definition.Type is 29 or 40 && definition.TargetType == 2 && Number(d, row, "m_iEffectType") == 1 && values >= 0)
                    definition.MovementPower = d.Children(values).Select(i => int.TryParse(d.Nodes[i].Value, out int power) ? power : 0).ToArray();
            }
            return result;
        }
        static int Child(NativeDataDocument d, int p, string key)
        { if (p < 0) return -1; foreach (int i in d.Children(p)) if (d.Nodes[i].Key == key) return i; return -1; }
        static int Number(NativeDataDocument d, int p, string key)
        { int i = Child(d, p, key); return i >= 0 && int.TryParse(d.Nodes[i].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0; }
    }
}
