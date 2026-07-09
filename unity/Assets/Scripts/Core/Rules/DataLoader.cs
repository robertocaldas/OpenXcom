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
    }
}
