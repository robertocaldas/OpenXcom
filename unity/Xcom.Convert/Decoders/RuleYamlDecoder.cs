using System.Collections.Generic;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Xcom.Convert.Decoders
{
    public sealed class ConvertedStats
    {
        public int TimeUnits;
        public int Stamina;
        public int Health;
        public int Bravery;
        public int Reactions;
        public int Firing;
        public int Throwing;
        public int Strength;
        public int Melee;
    }

    public sealed class ConvertedUnit
    {
        public string Id;
        public ConvertedStats Stats;
        public string ArmorId; // null when this unit's armor isn't converted this slice
    }

    public sealed class ConvertedArmor
    {
        public string Id;
        public int Front;
        public int Side;
        public int Rear;
        public int Under;
    }

    public sealed class ConvertedItem
    {
        public string Id;
        public bool TwoHanded;
        public int Power;
        public int DamageType;
        public int AccuracySnap;
        public int AccuracyAimed;
        public int AccuracyAuto;
        public int TuSnap;
        public int TuAimed;
        public int TuAuto;
    }

    /// <summary>
    /// Reads specific named entries out of OXCE's ruleset YAML
    /// (bin/standard/xcom1/*.rul at the repo root) into Convert's own DTOs.
    /// Deliberately narrow: looks up exactly the IDs a caller asks for, not a
    /// general schema for every field/entry in these files - see the phase 6
    /// design spec §6 ("no general .rul YAML converter").
    /// </summary>
    public static class RuleYamlDecoder
    {
        private static readonly IDeserializer Deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        public static ConvertedUnit LoadAlienUnit(string unitsRulPath, string typeId)
        {
            var file = Deserializer.Deserialize<RawUnitsFile>(File.ReadAllText(unitsRulPath));
            var raw = file.Units.Find(u => u.Type == typeId)
                ?? throw new InvalidDataException($"{typeId} not found in {unitsRulPath}");
            return new ConvertedUnit { Id = raw.Type, Stats = ToStats(raw.Stats), ArmorId = raw.Armor };
        }

        public static ConvertedUnit LoadSoldierUnit(string soldiersRulPath, string typeId)
        {
            var file = Deserializer.Deserialize<RawSoldiersFile>(File.ReadAllText(soldiersRulPath));
            var raw = file.Soldiers.Find(s => s.Type == typeId)
                ?? throw new InvalidDataException($"{typeId} not found in {soldiersRulPath}");
            return new ConvertedUnit { Id = raw.Type, Stats = ToStats(raw.MinStats), ArmorId = null };
        }

        public static ConvertedArmor LoadArmor(string armorsRulPath, string typeId)
        {
            var file = Deserializer.Deserialize<RawArmorsFile>(File.ReadAllText(armorsRulPath));
            var raw = file.Armors.Find(a => a.Type == typeId)
                ?? throw new InvalidDataException($"{typeId} not found in {armorsRulPath}");
            return new ConvertedArmor
            {
                Id = raw.Type, Front = raw.FrontArmor, Side = raw.SideArmor,
                Rear = raw.RearArmor, Under = raw.UnderArmor,
            };
        }

        public static ConvertedItem LoadWeapon(string itemsRulPath, string weaponTypeId, string clipTypeId)
        {
            var file = Deserializer.Deserialize<RawItemsFile>(File.ReadAllText(itemsRulPath));
            var weapon = file.Items.Find(i => i.Type == weaponTypeId)
                ?? throw new InvalidDataException($"{weaponTypeId} not found in {itemsRulPath}");
            var clip = file.Items.Find(i => i.Type == clipTypeId)
                ?? throw new InvalidDataException($"{clipTypeId} not found in {itemsRulPath}");
            return new ConvertedItem
            {
                Id = weapon.Type,
                TwoHanded = weapon.TwoHanded,
                Power = clip.Power,
                DamageType = clip.DamageType,
                AccuracySnap = weapon.AccuracySnap,
                AccuracyAimed = weapon.AccuracyAimed,
                AccuracyAuto = weapon.AccuracyAuto,
                TuSnap = weapon.TuSnap,
                TuAimed = weapon.TuAimed,
                TuAuto = weapon.TuAuto,
            };
        }

        private static ConvertedStats ToStats(RawStats s) => new()
        {
            TimeUnits = s.Tu, Stamina = s.Stamina, Health = s.Health, Bravery = s.Bravery,
            Reactions = s.Reactions, Firing = s.Firing, Throwing = s.Throwing,
            Strength = s.Strength, Melee = s.Melee,
        };

        // --- YAML-shaped DTOs matching bin/standard/xcom1/*.rul field names ---

        private sealed class RawStats
        {
            public int Tu { get; set; }
            public int Stamina { get; set; }
            public int Health { get; set; }
            public int Bravery { get; set; }
            public int Reactions { get; set; }
            public int Firing { get; set; }
            public int Throwing { get; set; }
            public int Strength { get; set; }
            public int Melee { get; set; }
        }

        private sealed class RawUnit
        {
            public string Type { get; set; } = "";
            public RawStats Stats { get; set; } = new();
            public string Armor { get; set; } = "";
        }

        private sealed class RawUnitsFile
        {
            public List<RawUnit> Units { get; set; } = new();
        }

        private sealed class RawSoldier
        {
            public string Type { get; set; } = "";
            public RawStats MinStats { get; set; } = new();
        }

        private sealed class RawSoldiersFile
        {
            public List<RawSoldier> Soldiers { get; set; } = new();
        }

        private sealed class RawArmor
        {
            public string Type { get; set; } = "";
            public int FrontArmor { get; set; }
            public int SideArmor { get; set; }
            public int RearArmor { get; set; }
            public int UnderArmor { get; set; }
        }

        private sealed class RawArmorsFile
        {
            public List<RawArmor> Armors { get; set; } = new();
        }

        private sealed class RawItem
        {
            public string Type { get; set; } = "";
            public bool TwoHanded { get; set; }
            public int AccuracySnap { get; set; }
            public int AccuracyAimed { get; set; }
            public int AccuracyAuto { get; set; }
            public int TuSnap { get; set; }
            public int TuAimed { get; set; }
            public int TuAuto { get; set; }
            public int Power { get; set; }
            public int DamageType { get; set; }
        }

        private sealed class RawItemsFile
        {
            public List<RawItem> Items { get; set; } = new();
        }
    }
}
