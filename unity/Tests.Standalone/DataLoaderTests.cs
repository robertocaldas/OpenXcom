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
        private static readonly string DataDir = TestPaths.RawDataDir;
        private readonly string _outDir = Path.Combine(Path.GetTempPath(), "dataloader-convertjob-" + Guid.NewGuid());

        public DataLoaderTests()
        {
            Directory.CreateDirectory(_dir);
            Directory.CreateDirectory(_outDir);
        }

        public void Dispose()
        {
            Directory.Delete(_dir, recursive: true);
            if (Directory.Exists(_outDir))
                Directory.Delete(_outDir, recursive: true);
        }

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
                    ""PLevel"": 3,
                    ""Loft"": ""AAAAAAAAAAAAAAAA""
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
        public void LoadTiles_ParsesLoftArray()
        {
            string json = @"[
                {
                    ""Frames"": ""/+A/AQIDBAU="",
                    ""ScanG"": 42, ""IsUfoDoor"": false, ""StopLOS"": true, ""NoFloor"": false,
                    ""BigWall"": 2, ""Gravlift"": false, ""IsDoor"": false, ""BlockFire"": false,
                    ""BlockSmoke"": false, ""TuWalk"": 4, ""TuSlide"": 8, ""TuFly"": 1, ""Armor"": 20,
                    ""TLevel"": -1, ""PLevel"": 3,
                    ""Loft"": ""AwAAAAAAAAAAAAAA""
                }
            ]";
            File.WriteAllText(Path.Combine(_dir, "tiles-TEST.json"), json);

            var tiles = DataLoader.LoadTiles(_dir, "TEST");

            // "AwAAAAAAAAAAAAAA" base64-decodes to {3,0,0,0,0,0,0,0,0,0,0,0}.
            Assert.Equal(new[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, tiles[0].Loft);
        }

        [Fact]
        public void LoadLoftemps_ParsesFlatUshortArray()
        {
            File.WriteAllText(Path.Combine(_dir, "loftemps.json"), "[0, 65535, 1, 32768]");

            var loftemps = DataLoader.LoadLoftemps(_dir);

            Assert.Equal(new ushort[] { 0, 65535, 1, 32768 }, loftemps);
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
            string json = @"[{ ""Id"": ""SECTOID_ARMOR0"", ""Front"": 4, ""Side"": 3, ""Rear"": 2, ""Under"": 2, ""Loftemps"": 2 }]";
            File.WriteAllText(Path.Combine(_dir, "armors.json"), json);

            var armors = DataLoader.LoadArmors(_dir);

            Assert.Single(armors);
            Assert.Equal("SECTOID_ARMOR0", armors[0].Id);
            Assert.Equal(4, armors[0].Front);
            Assert.Equal(3, armors[0].Side);
            Assert.Equal(2, armors[0].Rear);
            Assert.Equal(2, armors[0].Under);
            Assert.Equal(2, armors[0].Loftemps);
        }

        [Fact]
        public void LoadUnits_ResolvesArmorIdAndFallsBackToNoneWhenMissing()
        {
            string json = @"[
                { ""Id"": ""STR_SECTOID_SOLDIER"", ""ArmorId"": ""SECTOID_ARMOR0"",
                  ""StandHeight"": 16, ""KneelHeight"": 12, ""FloatHeight"": 0,
                  ""Stats"": { ""TimeUnits"": 54, ""Stamina"": 90, ""Health"": 30, ""Bravery"": 80,
                                ""Reactions"": 63, ""Firing"": 52, ""Throwing"": 58, ""Strength"": 30, ""Melee"": 76 } },
                { ""Id"": ""STR_SOLDIER"", ""ArmorId"": null,
                  ""StandHeight"": 22, ""KneelHeight"": 14, ""FloatHeight"": 0,
                  ""Stats"": { ""TimeUnits"": 50, ""Stamina"": 40, ""Health"": 25, ""Bravery"": 10,
                                ""Reactions"": 30, ""Firing"": 40, ""Throwing"": 50, ""Strength"": 20, ""Melee"": 20 } }
            ]";
            File.WriteAllText(Path.Combine(_dir, "units.json"), json);
            var sectoidArmor = new RuleArmor("SECTOID_ARMOR0", front: 4, side: 3, rear: 2, under: 2, loftemps: 2);
            var armorsById = new Dictionary<string, RuleArmor> { { "SECTOID_ARMOR0", sectoidArmor } };

            var units = DataLoader.LoadUnits(_dir, armorsById);

            Assert.Equal(2, units.Count);
            var sectoid = units.Find(u => u.Id == "STR_SECTOID_SOLDIER");
            Assert.Same(sectoidArmor, sectoid.Armor);
            Assert.Equal(54, sectoid.Stats.TimeUnits);
            Assert.Equal(16, sectoid.StandHeight);
            Assert.Equal(12, sectoid.KneelHeight);
            var soldier = units.Find(u => u.Id == "STR_SOLDIER");
            Assert.Equal(RuleArmor.None.Id, soldier.Armor.Id); // no ArmorId -> falls back to RuleArmor.None
            Assert.Equal(50, soldier.Stats.TimeUnits);
            Assert.Equal(22, soldier.StandHeight);
            Assert.Equal(14, soldier.KneelHeight);
        }

        [Fact]
        public void LoadItems_ParsesWeaponFieldsIncludingClipDerivedPowerAndDamageType()
        {
            string json = @"[{
                ""Id"": ""STR_RIFLE"", ""TwoHanded"": true, ""Power"": 30, ""DamageType"": 1,
                ""AccuracySnap"": 60, ""AccuracyAimed"": 110, ""AccuracyAuto"": 35,
                ""TuSnap"": 25, ""TuAimed"": 80, ""TuAuto"": 35,
                ""HandSprite"": 0, ""BulletSprite"": 70
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
            Assert.Equal(0, rifle.HandSprite);
            Assert.Equal(70, rifle.BulletSprite);
        }

        [Fact]
        public void ConvertJob_WritesUfoAndCraftMapblocksAndTerrains()
        {
            Xcom.Convert.ConvertJob.Run(DataDir, TestPaths.RulesDir, TestPaths.CommonDir, _outDir);

            var ufoTerrain = DataLoader.LoadTerrain(_outDir, "UFO1A");
            Assert.Equal(2, ufoTerrain.DataSets.Count);
            Assert.Equal("BLANKS", ufoTerrain.DataSets[0].Name);
            Assert.Equal("UFO1", ufoTerrain.DataSets[1].Name);

            var ufoBlock = DataLoader.LoadMapBlock(_outDir, "UFO1A");
            Assert.Equal(10, ufoBlock.Width);
            Assert.Equal(10, ufoBlock.Length);
            Assert.Equal(3, ufoBlock.Height);

            var craftTerrain = DataLoader.LoadTerrain(_outDir, "PLANE");
            Assert.Equal(2, craftTerrain.DataSets.Count);
            Assert.Equal("BLANKS", craftTerrain.DataSets[0].Name);
            Assert.Equal("PLANE", craftTerrain.DataSets[1].Name);

            var craftBlock = DataLoader.LoadMapBlock(_outDir, "PLANE");
            Assert.Equal(10, craftBlock.Width);
            Assert.Equal(20, craftBlock.Length);
            Assert.Equal(3, craftBlock.Height);
        }

        [Fact]
        public void LoadTerrain_RealCulta_CarriesTheFullBlockListAndScript()
        {
            Xcom.Convert.ConvertJob.Run(DataDir, TestPaths.RulesDir, TestPaths.CommonDir, _outDir);

            var terrain = DataLoader.LoadTerrain(_outDir, "CULTA");

            Assert.Equal("FARM", terrain.Script);
            Assert.Equal(19, terrain.Blocks.Count);
            Assert.Equal("CULTA00", terrain.Blocks[0].Name);
            Assert.Contains(1, terrain.Blocks[0].Groups);
        }

        [Fact]
        public void LoadMapScript_RealFarmScript_ThreeCommandsWithTypedEnum()
        {
            Xcom.Convert.ConvertJob.Run(DataDir, TestPaths.RulesDir, TestPaths.CommonDir, _outDir);

            var script = DataLoader.LoadMapScript(_outDir, "FARM");

            Assert.Equal(3, script.Count);
            Assert.Equal(MapScriptCommandType.AddUfo, script[0].Type);
            Assert.Equal(MapScriptCommandType.AddCraft, script[1].Type);
            Assert.Equal(MapScriptCommandType.FillArea, script[2].Type);
            Assert.Equal(18, script[2].Blocks.Count);
        }
    }
}
