using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace OpenXcom.Core.Rules
{
    /// <summary>
    /// Reads Xcom.Convert's emitted JSON from a GameData directory into
    /// Core's rule/data model. Core never parses YAML or references
    /// Xcom.Convert's types directly — only this converted JSON shape.
    /// Uses Newtonsoft.Json to match the serializer Xcom.Convert writes with.
    /// </summary>
    public static class DataLoader
    {
        public static List<MapDataTile> LoadTiles(string gameDataDir, string datasetName)
        {
            string path = Path.Combine(gameDataDir, $"tiles-{datasetName}.json");
            string json = File.ReadAllText(path);
            var raw = JsonConvert.DeserializeObject<List<RawMcdRecord>>(json);

            var result = new List<MapDataTile>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                var r = raw[i];
                var frames = new int[r.Frames.Length];
                for (int f = 0; f < r.Frames.Length; f++) frames[f] = r.Frames[f];

                result.Add(new MapDataTile
                {
                    Frames = frames,
                    ScanG = r.ScanG,
                    IsUfoDoor = r.IsUfoDoor,
                    StopLOS = r.StopLOS,
                    NoFloor = r.NoFloor,
                    BigWall = r.BigWall,
                    Gravlift = r.Gravlift,
                    IsDoor = r.IsDoor,
                    BlockFire = r.BlockFire,
                    BlockSmoke = r.BlockSmoke,
                    TuWalk = r.TuWalk,
                    TuSlide = r.TuSlide,
                    TuFly = r.TuFly,
                    Armor = r.Armor,
                    TerrainLevel = r.TLevel,
                    YOffset = r.PLevel,
                    Loft = ToIntArray(r.Loft),
                    DatasetName = datasetName,
                    LocalIndex = i,
                });
            }
            return result;
        }

        public static RuleTerrain LoadTerrain(string gameDataDir, string terrainName)
        {
            string path = Path.Combine(gameDataDir, $"terrain-{terrainName}.datasets.json");
            string json = File.ReadAllText(path);
            var raw = JsonConvert.DeserializeObject<RawTerrainDatasets>(json);

            var dataSets = new List<MapDataSetInfo>(raw.Datasets.Count);
            foreach (var d in raw.Datasets)
                dataSets.Add(new MapDataSetInfo { Name = d.Name, Size = d.Size });

            return new RuleTerrain(raw.Name, dataSets);
        }

        public static RawMapBlockData LoadMapBlock(string gameDataDir, string blockName)
        {
            string path = Path.Combine(gameDataDir, $"mapblock-{blockName}.json");
            string json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<RawMapBlockData>(json);
        }

        public static List<RuleArmor> LoadArmors(string gameDataDir)
        {
            string path = Path.Combine(gameDataDir, "armors.json");
            string json = File.ReadAllText(path);
            var raw = JsonConvert.DeserializeObject<List<RawArmorEntry>>(json);

            var result = new List<RuleArmor>(raw.Count);
            foreach (var r in raw)
                result.Add(new RuleArmor(r.Id, r.Front, r.Side, r.Rear, r.Under, r.Loftemps));
            return result;
        }

        public static List<RuleUnit> LoadUnits(string gameDataDir, IReadOnlyDictionary<string, RuleArmor> armorsById)
        {
            string path = Path.Combine(gameDataDir, "units.json");
            string json = File.ReadAllText(path);
            var raw = JsonConvert.DeserializeObject<List<RawUnitEntry>>(json);

            var result = new List<RuleUnit>(raw.Count);
            foreach (var r in raw)
            {
                var armor = !string.IsNullOrEmpty(r.ArmorId) && armorsById.TryGetValue(r.ArmorId, out var a)
                    ? a : RuleArmor.None;
                var stats = new UnitStats
                {
                    TimeUnits = r.Stats.TimeUnits, Stamina = r.Stats.Stamina, Health = r.Stats.Health,
                    Bravery = r.Stats.Bravery, Reactions = r.Stats.Reactions, Firing = r.Stats.Firing,
                    Throwing = r.Stats.Throwing, Strength = r.Stats.Strength, Melee = r.Stats.Melee,
                };
                result.Add(new RuleUnit(r.Id, stats, armor, r.StandHeight, r.KneelHeight, r.FloatHeight));
            }
            return result;
        }

        /// <summary>Loads the flat LOFTEMPS.DAT voxel bitmask table. Port of MapDataSet::loadLOFTEMPS (src/Mod/MapDataSet.cpp:272).</summary>
        public static ushort[] LoadLoftemps(string gameDataDir)
        {
            string path = Path.Combine(gameDataDir, "loftemps.json");
            string json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<ushort[]>(json);
        }

        public static List<RuleItem> LoadItems(string gameDataDir)
        {
            string path = Path.Combine(gameDataDir, "items.json");
            string json = File.ReadAllText(path);
            var raw = JsonConvert.DeserializeObject<List<RawItemEntry>>(json);

            var result = new List<RuleItem>(raw.Count);
            foreach (var r in raw)
            {
                result.Add(new RuleItem(r.Id)
                {
                    TwoHanded = r.TwoHanded,
                    Power = r.Power,
                    DamageType = (DamageType)r.DamageType,
                    AccuracySnap = r.AccuracySnap,
                    AccuracyAimed = r.AccuracyAimed,
                    AccuracyAuto = r.AccuracyAuto,
                    TuSnap = r.TuSnap,
                    TuAimed = r.TuAimed,
                    TuAuto = r.TuAuto,
                });
            }
            return result;
        }

        // --- JSON-shaped DTOs matching Xcom.Convert's emitted field names ---

        private sealed class RawMcdRecord
        {
            public byte[] Frames { get; set; }
            public int ScanG { get; set; }
            public bool IsUfoDoor { get; set; }
            public bool StopLOS { get; set; }
            public bool NoFloor { get; set; }
            public int BigWall { get; set; }
            public bool Gravlift { get; set; }
            public bool IsDoor { get; set; }
            public bool BlockFire { get; set; }
            public bool BlockSmoke { get; set; }
            public int TuWalk { get; set; }
            public int TuSlide { get; set; }
            public int TuFly { get; set; }
            public int Armor { get; set; }
            public int TLevel { get; set; }
            public int PLevel { get; set; }
            public byte[] Loft { get; set; }
        }

        private sealed class RawDatasetInfo
        {
            public string Name { get; set; }
            public int Size { get; set; }
        }

        private sealed class RawTerrainDatasets
        {
            public string Name { get; set; }
            public List<RawDatasetInfo> Datasets { get; set; }
        }

        public sealed class RawMapBlockTile
        {
            public int Floor { get; set; }
            public int WestWall { get; set; }
            public int NorthWall { get; set; }
            public int Object { get; set; }
        }

        public sealed class RawRouteNode
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int Z { get; set; }
            public int Type { get; set; }
            public int Rank { get; set; }
            public int Flags { get; set; }
            public int Priority { get; set; }
            public int[] Links { get; set; }
        }

        public sealed class RawMapBlockData
        {
            public int Width { get; set; }
            public int Length { get; set; }
            public int Height { get; set; }
            public List<RawMapBlockTile> Tiles { get; set; }
            public List<RawRouteNode> RouteNodes { get; set; }
        }

        private sealed class RawUnitStats
        {
            public int TimeUnits { get; set; }
            public int Stamina { get; set; }
            public int Health { get; set; }
            public int Bravery { get; set; }
            public int Reactions { get; set; }
            public int Firing { get; set; }
            public int Throwing { get; set; }
            public int Strength { get; set; }
            public int Melee { get; set; }
        }

        private sealed class RawUnitEntry
        {
            public string Id { get; set; }
            public RawUnitStats Stats { get; set; }
            public string ArmorId { get; set; }
            public int StandHeight { get; set; }
            public int KneelHeight { get; set; }
            public int FloatHeight { get; set; }
        }

        private sealed class RawArmorEntry
        {
            public string Id { get; set; }
            public int Front { get; set; }
            public int Side { get; set; }
            public int Rear { get; set; }
            public int Under { get; set; }
            public int Loftemps { get; set; }
        }

        private sealed class RawItemEntry
        {
            public string Id { get; set; }
            public bool TwoHanded { get; set; }
            public int Power { get; set; }
            public int DamageType { get; set; }
            public int AccuracySnap { get; set; }
            public int AccuracyAimed { get; set; }
            public int AccuracyAuto { get; set; }
            public int TuSnap { get; set; }
            public int TuAimed { get; set; }
            public int TuAuto { get; set; }
        }

        private static int[] ToIntArray(byte[] bytes)
        {
            var result = new int[bytes.Length];
            for (int i = 0; i < bytes.Length; i++) result[i] = bytes[i];
            return result;
        }
    }
}
