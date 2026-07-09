using System;
using System.Collections.Generic;
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
            // Bytes {255,224,63,1,2,3,4,5} deliberately encode to a string
            // containing '+' and '/' ("/+A/AQIDBAU=") so this test actually
            // fails if a future change swapped to the base64url alphabet
            // (which uses '-'/'_' instead and would reject or mis-decode this).
            string json = @"[
                {
                    ""Frames"": ""/+A/AQIDBAU="",
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
            // "/+A/AQIDBAU=" base64-decodes to bytes 255,224,63,1,2,3,4,5.
            Assert.Equal(new[] { 255, 224, 63, 1, 2, 3, 4, 5 }, t.Frames);
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

        [Fact]
        public void LoadArmors_ParsesFields()
        {
            string json = @"[{ ""Id"": ""SECTOID_ARMOR0"", ""Front"": 4, ""Side"": 3, ""Rear"": 2, ""Under"": 2 }]";
            File.WriteAllText(Path.Combine(_dir, "armors.json"), json);

            var armors = DataLoader.LoadArmors(_dir);

            Assert.Single(armors);
            Assert.Equal("SECTOID_ARMOR0", armors[0].Id);
            Assert.Equal(4, armors[0].Front);
            Assert.Equal(3, armors[0].Side);
            Assert.Equal(2, armors[0].Rear);
            Assert.Equal(2, armors[0].Under);
        }

        [Fact]
        public void LoadUnits_ResolvesArmorIdAndFallsBackToNoneWhenMissing()
        {
            string json = @"[
                { ""Id"": ""STR_SECTOID_SOLDIER"", ""ArmorId"": ""SECTOID_ARMOR0"",
                  ""Stats"": { ""TimeUnits"": 54, ""Stamina"": 90, ""Health"": 30, ""Bravery"": 80,
                                ""Reactions"": 63, ""Firing"": 52, ""Throwing"": 58, ""Strength"": 30, ""Melee"": 76 } },
                { ""Id"": ""STR_SOLDIER"", ""ArmorId"": null,
                  ""Stats"": { ""TimeUnits"": 50, ""Stamina"": 40, ""Health"": 25, ""Bravery"": 10,
                                ""Reactions"": 30, ""Firing"": 40, ""Throwing"": 50, ""Strength"": 20, ""Melee"": 20 } }
            ]";
            File.WriteAllText(Path.Combine(_dir, "units.json"), json);
            var sectoidArmor = new RuleArmor("SECTOID_ARMOR0", front: 4, side: 3, rear: 2, under: 2);
            var armorsById = new Dictionary<string, RuleArmor> { { "SECTOID_ARMOR0", sectoidArmor } };

            var units = DataLoader.LoadUnits(_dir, armorsById);

            Assert.Equal(2, units.Count);
            var sectoid = units.Find(u => u.Id == "STR_SECTOID_SOLDIER");
            Assert.Same(sectoidArmor, sectoid.Armor);
            Assert.Equal(54, sectoid.Stats.TimeUnits);
            var soldier = units.Find(u => u.Id == "STR_SOLDIER");
            Assert.Equal(RuleArmor.None.Id, soldier.Armor.Id); // no ArmorId -> falls back to RuleArmor.None
            Assert.Equal(50, soldier.Stats.TimeUnits);
        }

        [Fact]
        public void LoadItems_ParsesWeaponFieldsIncludingClipDerivedPowerAndDamageType()
        {
            string json = @"[{
                ""Id"": ""STR_RIFLE"", ""TwoHanded"": true, ""Power"": 30, ""DamageType"": 1,
                ""AccuracySnap"": 60, ""AccuracyAimed"": 110, ""AccuracyAuto"": 35,
                ""TuSnap"": 25, ""TuAimed"": 80, ""TuAuto"": 35
            }]";
            File.WriteAllText(Path.Combine(_dir, "items.json"), json);

            var items = DataLoader.LoadItems(_dir);

            Assert.Single(items);
            var rifle = items[0];
            Assert.Equal("STR_RIFLE", rifle.Id);
            Assert.True(rifle.TwoHanded);
            Assert.Equal(30, rifle.Power);
            Assert.Equal(DamageType.Armor, rifle.DamageType); // raw rul value 1; Combat.cs doesn't branch on DamageType, so this is a label only
            Assert.Equal(110, rifle.AccuracyAimed);
            Assert.Equal(80, rifle.TuAimed);
        }
    }
}
