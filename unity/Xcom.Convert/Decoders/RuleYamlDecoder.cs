using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public int StandHeight;
        public int KneelHeight;
        public int FloatHeight;
    }

    public sealed class ConvertedArmor
    {
        public string Id;
        public int Front;
        public int Side;
        public int Rear;
        public int Under;
        public int Loftemps;
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
        public int HandSprite;
        public int BulletSprite;
    }

    public sealed class ConvertedTerrainBlock
    {
        public string Name;
        public int Width;
        public int Length;
        public List<int> Groups;
    }

    public sealed class ConvertedTerrain
    {
        public string Name;
        public string Script;
        public List<string> Datasets;
        public List<ConvertedTerrainBlock> Blocks;
    }

    public sealed class ConvertedMapScriptCommand
    {
        public string Type;
        public List<int[]> Rects = new();
        public List<int> Groups = new();
        public List<int> Blocks = new();
        public List<int> Freqs = new();
        public List<int> MaxUses = new();
        public int SizeX = 1;
        public int SizeY = 1;
        public int SizeZ = 0;
        public string Direction = "none";
        public int Executions = 1;
        public int ExecutionChances = 100;
        public int Label = 0;
        public List<int> Conditionals = new();
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
            return new ConvertedUnit
            {
                Id = raw.Type, Stats = ToStats(raw.Stats), ArmorId = raw.Armor,
                StandHeight = raw.StandHeight, KneelHeight = raw.KneelHeight, FloatHeight = raw.FloatHeight,
            };
        }

        public static ConvertedUnit LoadSoldierUnit(string soldiersRulPath, string typeId)
        {
            var file = Deserializer.Deserialize<RawSoldiersFile>(File.ReadAllText(soldiersRulPath));
            var raw = file.Soldiers.Find(s => s.Type == typeId)
                ?? throw new InvalidDataException($"{typeId} not found in {soldiersRulPath}");
            return new ConvertedUnit
            {
                Id = raw.Type, Stats = ToStats(raw.MinStats), ArmorId = null,
                StandHeight = raw.StandHeight, KneelHeight = raw.KneelHeight, FloatHeight = raw.FloatHeight,
            };
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
                Loftemps = raw.LoftempsSet.Count > 0 ? raw.LoftempsSet[0] : 0,
            };
        }

        /// <summary>
        /// Reads a weapon plus its clip's power/damageType, and the weapon's
        /// own handSprite/bulletSprite (RuleItem.h:390) - bulletSprite is
        /// stored already multiplied by 35 (RuleItem.cpp:352-353's
        /// loadSpriteOffset(..., "Projectiles", 35)), matching the real
        /// engine's actual atlas base-offset value, not the raw .rul number.
        /// </summary>
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
                HandSprite = weapon.HandSprite,
                BulletSprite = weapon.BulletSprite * 35,
            };
        }

        public static ConvertedTerrain LoadTerrainBlocks(string terrainsRulPath, string terrainName)
        {
            var file = Deserializer.Deserialize<RawTerrainsFile>(File.ReadAllText(terrainsRulPath));
            var raw = file.Terrains.Find(t => t.Name == terrainName)
                ?? throw new InvalidDataException($"{terrainName} not found in {terrainsRulPath}");

            var blocks = new List<ConvertedTerrainBlock>(raw.MapBlocks.Count);
            foreach (var b in raw.MapBlocks)
            {
                blocks.Add(new ConvertedTerrainBlock
                {
                    Name = b.Name,
                    Width = b.Width,
                    Length = b.Length,
                    // Port of MapBlock::load's groups field (src/Mod/MapBlock.cpp:33,59-68):
                    // scalar or sequence, defaults to [0] (MT_DEFAULT) when absent.
                    Groups = AsIntList(b.Groups, defaultWhenEmpty: 0),
                });
            }

            return new ConvertedTerrain
            {
                Name = raw.Name,
                Script = raw.Script,
                Datasets = raw.MapDataSets,
                Blocks = blocks,
            };
        }

        public static List<ConvertedMapScriptCommand> LoadMapScript(string mapScriptsRulPath, string scriptName)
        {
            var file = Deserializer.Deserialize<RawMapScriptsFile>(File.ReadAllText(mapScriptsRulPath));
            var raw = file.MapScripts.Find(s => s.Type == scriptName)
                ?? throw new InvalidDataException($"{scriptName} not found in {mapScriptsRulPath}");

            var result = new List<ConvertedMapScriptCommand>(raw.Commands.Count);
            foreach (var c in raw.Commands)
            {
                var cmd = new ConvertedMapScriptCommand { Type = c.Type };

                foreach (var r in c.Rects ?? new List<List<int>>())
                    cmd.Rects.Add(new[] { r[0], r[1], r[2], r[3] });

                // Port of MapScript::load (src/Mod/MapScript.cpp:65-73): addCraft
                // and addUFO default to group 1 (landing-zone filler) unless the
                // command overrides "groups"/"blocks" itself.
                var groups = AsIntList(c.Groups, defaultWhenEmpty: null);
                if (groups.Count == 0 && (c.Type == "addCraft" || c.Type == "addUFO"))
                    groups = new List<int> { 1 };
                var blocks = AsIntList(c.Blocks, defaultWhenEmpty: null);

                // blocks always wins over groups when both given (MapScript.cpp:174-190).
                if (blocks.Count > 0)
                {
                    cmd.Blocks = blocks;
                }
                else
                {
                    cmd.Groups = groups;
                }
                int selectionSize = cmd.Blocks.Count > 0 ? cmd.Blocks.Count : cmd.Groups.Count;

                // Port of MapScript.cpp:192-230: freqs/maxUses always resized to
                // selectionSize with defaults (1, -1), then overridden entry-by-entry.
                cmd.Freqs = Enumerable.Repeat(1, selectionSize).ToList();
                cmd.MaxUses = Enumerable.Repeat(-1, selectionSize).ToList();
                var freqOverrides = AsIntList(c.Freqs, defaultWhenEmpty: null);
                for (int i = 0; i < freqOverrides.Count && i < selectionSize; i++)
                    cmd.Freqs[i] = freqOverrides[i];
                var maxUseOverrides = AsIntList(c.MaxUses, defaultWhenEmpty: null);
                for (int i = 0; i < maxUseOverrides.Count && i < selectionSize; i++)
                    cmd.MaxUses[i] = maxUseOverrides[i];

                // Port of MapScript.cpp:136-157 (size) and :83-87 (resize's
                // sizeX=sizeY=0 default override).
                if (c.Type == "resize") { cmd.SizeX = 0; cmd.SizeY = 0; }
                var size = AsIntList(c.Size, defaultWhenEmpty: null);
                if (size.Count == 1) { cmd.SizeX = size[0]; cmd.SizeY = size[0]; }
                else if (size.Count >= 2)
                {
                    cmd.SizeX = size[0];
                    cmd.SizeY = size[1];
                    if (size.Count >= 3) cmd.SizeZ = size[2];
                }

                if (!string.IsNullOrEmpty(c.Direction))
                    cmd.Direction = c.Direction.ToLowerInvariant() switch
                    {
                        var d when d.StartsWith("v") => "vertical",
                        var d when d.StartsWith("h") => "horizontal",
                        var d when d.StartsWith("b") => "both",
                        _ => "none",
                    };

                cmd.Executions = c.Executions ?? 1;
                cmd.ExecutionChances = c.ExecutionChances ?? 100;
                // Port of MapScript::load's label field (src/Mod/MapScript.cpp:280).
                cmd.Label = System.Math.Abs(c.Label ?? 0);
                cmd.Conditionals = AsIntList(c.Conditionals, defaultWhenEmpty: null);

                result.Add(cmd);
            }
            return result;
        }

        /// <summary>
        /// Normalizes a YAML field that may be a single scalar or a sequence
        /// (the convention throughout OXCE's ruleset format for these
        /// selection fields, e.g. MapScript.cpp:159-230). Empty/absent ->
        /// [defaultWhenEmpty] if given, else an empty list.
        /// </summary>
        private static List<int> AsIntList(object raw, int? defaultWhenEmpty)
        {
            if (raw is List<object> seq)
                return seq.Select(o => System.Convert.ToInt32(o)).ToList();
            if (raw != null)
                return new List<int> { System.Convert.ToInt32(raw) };
            return defaultWhenEmpty.HasValue ? new List<int> { defaultWhenEmpty.Value } : new List<int>();
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
            public int StandHeight { get; set; }
            public int KneelHeight { get; set; }
            public int FloatHeight { get; set; }
        }

        private sealed class RawUnitsFile
        {
            public List<RawUnit> Units { get; set; } = new();
        }

        private sealed class RawSoldier
        {
            public string Type { get; set; } = "";
            public RawStats MinStats { get; set; } = new();
            public int StandHeight { get; set; }
            public int KneelHeight { get; set; }
            public int FloatHeight { get; set; }
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
            public List<int> LoftempsSet { get; set; } = new();
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
            public int HandSprite { get; set; }
            public int BulletSprite { get; set; }
        }

        private sealed class RawItemsFile
        {
            public List<RawItem> Items { get; set; } = new();
        }

        private sealed class RawTerrainMapBlock
        {
            public string Name { get; set; } = "";
            public int Width { get; set; }
            public int Length { get; set; }
            public object Groups { get; set; } // scalar or sequence
        }

        private sealed class RawTerrainEntry
        {
            public string Name { get; set; } = "";
            public string Script { get; set; } = "";
            public List<string> MapDataSets { get; set; } = new();
            public List<RawTerrainMapBlock> MapBlocks { get; set; } = new();
        }

        private sealed class RawTerrainsFile
        {
            public List<RawTerrainEntry> Terrains { get; set; } = new();
        }

        private sealed class RawMapScriptCommandEntry
        {
            public string Type { get; set; } = "";
            public List<List<int>> Rects { get; set; }
            public object Groups { get; set; }
            public object Blocks { get; set; }
            public object Freqs { get; set; }
            public object MaxUses { get; set; }
            public object Size { get; set; }
            public string Direction { get; set; }
            public int? Executions { get; set; }
            public int? ExecutionChances { get; set; }
            public int? Label { get; set; }
            public object Conditionals { get; set; }
        }

        private sealed class RawMapScriptEntry
        {
            public string Type { get; set; } = "";
            public List<RawMapScriptCommandEntry> Commands { get; set; } = new();
        }

        private sealed class RawMapScriptsFile
        {
            public List<RawMapScriptEntry> MapScripts { get; set; } = new();
        }
    }
}
