using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using OpenXcom.Core.Battle;
using OpenXcom.Core.Rules;
using Xunit;

namespace OpenXcom.Core.Tests
{
    /// <summary>
    /// Exercises the real pipeline end-to-end: decode real MAP/RMP/MCD bytes
    /// via Xcom.Convert's decoders (same as ConvertJob would), serialize to
    /// JSON the same way ConvertJob does, then parse via Core's DataLoader
    /// and build a TileGrid via MapGenerator. This proves the JSON schema
    /// Convert emits and the schema Core parses actually agree, not just that
    /// each side's hand-written fixtures happen to match.
    /// </summary>
    public class MapGeneratorTests : System.IDisposable
    {
        private static readonly string DataDir = OpenXcom.Core.Tests.TestPaths.RawDataDir;

        private readonly string _outDir = Path.Combine(Path.GetTempPath(), "mapgen-" + System.Guid.NewGuid());

        public MapGeneratorTests() => Directory.CreateDirectory(_outDir);
        public void Dispose() => Directory.Delete(_outDir, recursive: true);

        private (RuleTerrain terrain, Dictionary<string, List<MapDataTile>> datasetTiles, DataLoader.RawMapBlockData block) LoadReal()
        {
            // Reuse the real ConvertJob to produce genuine GameData files, then
            // read them back exactly as the shipped pipeline would.
            Xcom.Convert.ConvertJob.Run(DataDir, _outDir);

            var terrain = DataLoader.LoadTerrain(_outDir, "CULTA");
            var datasetTiles = new Dictionary<string, List<MapDataTile>>
            {
                ["BLANKS"] = DataLoader.LoadTiles(_outDir, "BLANKS"),
                ["CULTIVAT"] = DataLoader.LoadTiles(_outDir, "CULTIVAT"),
                ["BARN"] = DataLoader.LoadTiles(_outDir, "BARN"),
            };
            var block = DataLoader.LoadMapBlock(_outDir, "CULTA00");

            return (terrain, datasetTiles, block);
        }

        [Fact]
        public void Build_GridDimensionsMatchMapblock()
        {
            var (terrain, datasetTiles, block) = LoadReal();

            var grid = MapGenerator.Build(block, terrain, datasetTiles);

            Assert.Equal(10, grid.Width);
            Assert.Equal(10, grid.Length);
            Assert.Equal(1, grid.Height);
        }

        [Fact]
        public void Build_ResolvesEveryNonZeroTilePartToAMapDataTileInTheCorrectDataset()
        {
            var (terrain, datasetTiles, block) = LoadReal();
            var grid = MapGenerator.Build(block, terrain, datasetTiles);

            bool foundAtLeastOneResolvedPart = false;
            for (int y = 0; y < grid.Length; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var tile = grid.At(x, y, 0);
                    int rawIdx = block.Tiles[block.Width * y + x /* z=0 only level */].Floor;
                    if (rawIdx > 0)
                    {
                        Assert.NotNull(tile.Floor);
                        var (expectedDataset, expectedLocal) = terrain.Resolve(rawIdx);
                        Assert.Equal(expectedDataset, tile.Floor.DatasetName);
                        Assert.Equal(expectedLocal, tile.Floor.LocalIndex);
                        foundAtLeastOneResolvedPart = true;
                    }
                    else
                    {
                        Assert.Null(tile.Floor);
                    }
                }
            }
            Assert.True(foundAtLeastOneResolvedPart, "Expected at least one nonzero floor index in CULTA00.");
        }

        [Fact]
        public void Build_TileWithNoFloorRecordIsNotWalkable()
        {
            var (terrain, datasetTiles, block) = LoadReal();
            var grid = MapGenerator.Build(block, terrain, datasetTiles);

            bool foundNoFloorTile = false;
            bool foundNormalFloorTile = false;
            for (int y = 0; y < grid.Length; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var tile = grid.At(x, y, 0);
                    if (tile.Floor == null) continue;

                    if (tile.Floor.NoFloor)
                    {
                        Assert.False(tile.Walkable);
                        foundNoFloorTile = true;
                    }
                    else
                    {
                        Assert.True(tile.Walkable);
                        foundNormalFloorTile = true;
                    }
                }
            }
            Assert.True(foundNormalFloorTile, "Expected at least one normal walkable floor tile in CULTA00.");
            // CULTA00 may or may not contain a NoFloor record; this assertion
            // only fires the NoFloor branch above if one exists, it does not
            // require one to exist.
        }

        [Fact]
        public void Build_TileWithoutAnyFloorIsNotWalkable()
        {
            var (terrain, datasetTiles, block) = LoadReal();
            var grid = MapGenerator.Build(block, terrain, datasetTiles);

            for (int y = 0; y < grid.Length; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var tile = grid.At(x, y, 0);
                    if (tile.Floor == null)
                        Assert.False(tile.Walkable);
                }
            }
        }

        [Fact]
        public void Build_BlocksSightMatchesAnyPartsStopLOSFlag()
        {
            var (terrain, datasetTiles, block) = LoadReal();
            var grid = MapGenerator.Build(block, terrain, datasetTiles);

            for (int y = 0; y < grid.Length; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var tile = grid.At(x, y, 0);
                    bool expected = (tile.WestWall?.StopLOS ?? false)
                        || (tile.NorthWall?.StopLOS ?? false)
                        || (tile.Object?.StopLOS ?? false);
                    Assert.Equal(expected, tile.BlocksSight);
                }
            }
        }
    }
}
