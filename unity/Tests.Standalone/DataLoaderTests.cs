using System;
using System.IO;
using OpenXcom.Core.Rules;
using Xunit;

namespace OpenXcom.Core.Tests
{
    public class DataLoaderTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "dataloader-" + Guid.NewGuid());

        public DataLoaderTests() => Directory.CreateDirectory(_dir);
        public void Dispose() => Directory.Delete(_dir, recursive: true);

        [Fact]
        public void LoadTiles_ParsesFieldsAndBase64EncodedByteArray()
        {
            // Newtonsoft serializes a C# byte[] as a base64 string; confirm
            // System.Text.Json round-trips that same convention correctly.
            string json = @"[
                {
                    ""Frames"": ""AQIDBAUGBwg="",
                    ""ScanG"": 42,
                    ""IsUfoDoor"": false,
                    ""StopLOS"": true,
                    ""NoFloor"": false,
                    ""BigWall"": 2,
                    ""Gravlift"": false,
                    ""IsDoor"": false,
                    ""BlockFire"": false,
                    ""BlockSmoke"": false,
                    ""TuWalk"": 4,
                    ""TuSlide"": 8,
                    ""TuFly"": 1,
                    ""Armor"": 20,
                    ""TLevel"": -1,
                    ""PLevel"": 3
                }
            ]";
            File.WriteAllText(Path.Combine(_dir, "tiles-TEST.json"), json);

            var tiles = DataLoader.LoadTiles(_dir, "TEST");

            Assert.Single(tiles);
            var t = tiles[0];
            // "AQIDBAUGBwg=" base64-decodes to bytes 1,2,3,4,5,6,7,8.
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, t.Frames);
            Assert.Equal(42, t.ScanG);
            Assert.True(t.StopLOS);
            Assert.Equal(2, t.BigWall);
            Assert.Equal(4, t.TuWalk);
            Assert.Equal(-1, t.TerrainLevel);
            Assert.Equal(3, t.YOffset);
            Assert.Equal("TEST", t.DatasetName);
            Assert.Equal(0, t.LocalIndex);
            Assert.True(t.IsBackTileObject); // BigWall=2 < 6
        }

        [Fact]
        public void LoadTerrain_ParsesOrderedDatasetList()
        {
            string json = @"{
                ""Name"": ""CULTA"",
                ""Datasets"": [
                    { ""Name"": ""BLANKS"", ""Size"": 2 },
                    { ""Name"": ""CULTIVAT"", ""Size"": 37 },
                    { ""Name"": ""BARN"", ""Size"": 29 }
                ]
            }";
            File.WriteAllText(Path.Combine(_dir, "terrain-CULTA.datasets.json"), json);

            var terrain = DataLoader.LoadTerrain(_dir, "CULTA");

            Assert.Equal("CULTA", terrain.Name);
            Assert.Equal(3, terrain.DataSets.Count);
            Assert.Equal(("CULTIVAT", 5), terrain.Resolve(7)); // 2 (BLANKS) + 5
        }

        [Fact]
        public void LoadMapBlock_ParsesDimsTilesAndRouteNodes()
        {
            string json = @"{
                ""Width"": 2, ""Length"": 1, ""Height"": 1,
                ""Tiles"": [
                    { ""Floor"": 5, ""WestWall"": 0, ""NorthWall"": 0, ""Object"": 0 },
                    { ""Floor"": 6, ""WestWall"": 1, ""NorthWall"": 0, ""Object"": 0 }
                ],
                ""RouteNodes"": [
                    { ""X"": 1, ""Y"": 0, ""Z"": 0, ""Type"": 1, ""Rank"": 0, ""Flags"": 0, ""Priority"": 5, ""Links"": [-1,-1,-1,-1,-1] }
                ]
            }";
            File.WriteAllText(Path.Combine(_dir, "mapblock-TESTBLOCK.json"), json);

            var block = DataLoader.LoadMapBlock(_dir, "TESTBLOCK");

            Assert.Equal(2, block.Width);
            Assert.Equal(1, block.Length);
            Assert.Equal(1, block.Height);
            Assert.Equal(2, block.Tiles.Count);
            Assert.Equal(5, block.Tiles[0].Floor);
            Assert.Equal(6, block.Tiles[1].Floor);
            Assert.Equal(1, block.Tiles[1].WestWall);
            Assert.Single(block.RouteNodes);
            Assert.Equal(5, block.RouteNodes[0].Priority);
        }
    }
}
