using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FFOne.Content;
using Newtonsoft.Json.Linq;

namespace FFOne.World
{
    public sealed class WorldWeaponDefinition
    {
        public int Id, Mode, Targets, Effect, BoostedEffect;
        public float Range, Radius, Angle, Delay, Duration, GrenadeVerticalSpeed;
        public bool Projectile => Mode == 5 || Mode == 6;
    }
    public sealed class WorldBulletDefinition
    {
        public int Id, Particle, Impact;
        public float Scale, ImpactScale, Lifetime;
        public string ImpactSound;
    }
    public sealed class WorldWeaponCatalog
    {
        readonly Dictionary<int, WorldWeaponDefinition> weapons = new();
        readonly Dictionary<int, WorldBulletDefinition> bullets = new();
        static WorldWeaponCatalog cached;
        public IEnumerable<WorldWeaponDefinition> Weapons => weapons.Values;
        public WorldWeaponDefinition Find(int id) => weapons.TryGetValue(id, out var weapon) ? weapon : null;
        public WorldBulletDefinition Bullet(int id) => bullets.TryGetValue(id, out var bullet) ? bullet : null;
        public static WorldWeaponCatalog Load() => cached ??= ContentAssets.Read<NativeDataDocument, WorldWeaponCatalog>(NativeData.Resource("data/tables/xdt"), Read);
        public static WorldWeaponCatalog Read(NativeDataDocument document)
        {
            var result = new WorldWeaponCatalog();
            int Child(int parent, string key) => parent < 0 ? -1 : document.Children(parent).Where(i => document.Nodes[i].Key == key).DefaultIfEmpty(-1).First();
            int table = Child(Child(0, "m_pWeaponItemTable"), "m_pItemData");
            int Number(int row, string key) { int child = Child(row, key); return child < 0 ? 0 : int.Parse(document.Nodes[child].Value, CultureInfo.InvariantCulture); }
            foreach (int row in document.Children(table))
            {
                int id = Number(row, "m_iItemNumber"); if (id <= 0) continue;
                result.weapons[id] = new WorldWeaponDefinition { Id = id, Mode = Number(row, "m_iTargetMode"), Targets = Math.Max(1, Number(row, "m_iTargetNumber")),
                    Effect = Number(row, "m_iEffect1"), BoostedEffect = Number(row, "m_iEffect2"), Range = Number(row, "m_iAtkRange") * .01f,
                    Radius = Number(row, "m_iEffectArea") * .01f, Angle = Number(row, "m_iAtkAngle"), Delay = Math.Max(.1f, Number(row, "m_iDelayTime") * .1f),
                    Duration = Number(row, "m_iDurationTime") * .1f, GrenadeVerticalSpeed = Number(row, "m_iDeliverTime") * .5f };
            }
            var catalog = NativeData.Object("map/shared/projectiles/catalog");
            foreach (var row in catalog["rows"])
            {
                var p = row["parameters"]; int id = (int)row["bulletType"];
                result.bullets[id] = new WorldBulletDefinition { Id = id, Particle = (int)p["particleScript"], Impact = (int)p["successScript"],
                    Scale = (float)p["bulletModelScale"], ImpactScale = (float)p["successModelScale"], Lifetime = (float)p["maximumTimeSeconds"], ImpactSound = (string)p["successSound"] };
            }
            return result;
        }
        public static string AttackClip(int mode) => mode switch {
            1 => "stickattack1upper", 2 => "pistolattack1upper", 3 or 4 => "rifleattack1upper",
            5 => "rocketattack1upper", 6 => "bombattack1upper", _ => "attack1upper" };
    }
}
