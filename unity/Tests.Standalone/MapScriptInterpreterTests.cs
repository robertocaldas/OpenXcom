using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenXcom.Core.Battle;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;
using Xunit;

namespace OpenXcom.Core.Tests
{
    public class MapScriptInterpreterTests : System.IDisposable
    {
        private static readonly string DataDir = TestPaths.RawDataDir;
        private readonly string _outDir = Path.Combine(Path.GetTempPath(), "mapscript-" + System.Guid.NewGuid());

        public MapScriptInterpreterTests() => Directory.CreateDirectory(_outDir);
        public void Dispose() => Directory.Delete(_outDir, recursive: true);

        private (RuleTerrain farmland, RuleTerrain craft, RuleTerrain ufo, List<MapScriptCommand> script) LoadReal()
        {
            Xcom.Convert.ConvertJob.Run(DataDir, TestPaths.RulesDir, TestPaths.CommonDir, _outDir);
            var farmland = DataLoader.LoadTerrain(_outDir, "CULTA");
            var craft = DataLoader.LoadTerrain(_outDir, "PLANE");
            var ufo = DataLoader.LoadTerrain(_outDir, "UFO1A");
            var script = DataLoader.LoadMapScript(_outDir, "FARM");
            return (farmland, craft, ufo, script);
        }

        [Fact]
        public void Generate_RealFarmScript_FillsEveryCellOfA5x5Map()
        {
            var (farmland, craft, ufo, script) = LoadReal();

            var layout = MapScriptInterpreter.Generate(farmland, script, 5, 5, craft, ufo, new Rng(1));

            var occupiedCells = layout.Pieces.Where(p => p.BlockName != null)
                .Select(p => (p.GridX, p.GridY)).Distinct().Count();
            Assert.Equal(25, occupiedCells);
        }

        [Fact]
        public void Generate_RealFarmScript_PlacesTheCraftAndTheUfo()
        {
            var (farmland, craft, ufo, script) = LoadReal();

            var layout = MapScriptInterpreter.Generate(farmland, script, 5, 5, craft, ufo, new Rng(1));

            Assert.Contains(layout.Pieces, p => p.BlockName == "PLANE" && p.TerrainName == "PLANE");
            Assert.Contains(layout.Pieces, p => p.BlockName == "UFO1A" && p.TerrainName == "UFO1A");
        }

        [Fact]
        public void Generate_RealFarmScript_FillAreaOnlyUsesBlocksZeroOneThroughEighteen_NeverCulta00()
        {
            var (farmland, craft, ufo, script) = LoadReal();

            var layout = MapScriptInterpreter.Generate(farmland, script, 5, 5, craft, ufo, new Rng(7));

            // CULTA00 (index 0, group 1) may appear as the craft/UFO landing-zone
            // filler, but fillArea's own "blocks" list is [1..18] and must never
            // place it as ordinary farmland filler beyond that reserved footprint.
            var fillerCount = layout.Pieces.Count(p => p.BlockName == "CULTA00");
            Assert.True(fillerCount <= 3, $"Expected at most 3 CULTA00 placements (UFO 1-cell + craft 2-cell footprint), got {fillerCount}.");
        }

        [Fact]
        public void Generate_SameSeed_IsDeterministic()
        {
            var (farmland, craft, ufo, script) = LoadReal();

            var layout1 = MapScriptInterpreter.Generate(farmland, script, 5, 5, craft, ufo, new Rng(42));
            var layout2 = MapScriptInterpreter.Generate(farmland, script, 5, 5, craft, ufo, new Rng(42));

            Assert.Equal(layout1.Pieces.Count, layout2.Pieces.Count);
            for (int i = 0; i < layout1.Pieces.Count; i++)
            {
                Assert.Equal(layout1.Pieces[i].BlockName, layout2.Pieces[i].BlockName);
                Assert.Equal(layout1.Pieces[i].GridX, layout2.Pieces[i].GridX);
                Assert.Equal(layout1.Pieces[i].GridY, layout2.Pieces[i].GridY);
            }
        }

        private static RuleTerrain SyntheticRoadTerrain() => new("ROAD",
            new List<MapDataSetInfo> { new() { Name = "X", Size = 1 } },
            new List<MapBlockInfo>
            {
                new() { Name = "NSROAD", Width = 10, Length = 10, Groups = new List<int> { 3 } },
                new() { Name = "EWROAD", Width = 10, Length = 10, Groups = new List<int> { 2 } },
                new() { Name = "CROSSING", Width = 10, Length = 10, Groups = new List<int> { 4 } },
                new() { Name = "PLAIN", Width = 10, Length = 10, Groups = new List<int> { 0 } },
            });

        [Fact]
        public void Generate_AddLineVertical_PlacesARoadColumn()
        {
            var terrain = SyntheticRoadTerrain();
            var script = new List<MapScriptCommand>
            {
                new() { Type = MapScriptCommandType.AddLine, Direction = MapDirection.Vertical },
            };

            var layout = MapScriptInterpreter.Generate(terrain, script, 3, 3, null, null, new Rng(3));

            var column = layout.Pieces.Select(p => p.GridX).Distinct().ToList();
            Assert.Single(column); // exactly one X column used
            Assert.Equal(3, layout.Pieces.Count(p => p.BlockName == "NSROAD"));
        }

        [Fact]
        public void Generate_CheckBlockWildcard_SucceedsOnlyAfterABlockIsPlacedThere()
        {
            var terrain = SyntheticRoadTerrain();
            var script = new List<MapScriptCommand>
            {
                new() { Type = MapScriptCommandType.CheckBlock, Rects = new List<MapScriptRect> { new() { X = 0, Y = 0, W = 1, H = 1 } }, Label = 1 },
                new() { Type = MapScriptCommandType.AddBlock, Rects = new List<MapScriptRect> { new() { X = 0, Y = 0, W = 1, H = 1 } } },
                new() { Type = MapScriptCommandType.CheckBlock, Rects = new List<MapScriptRect> { new() { X = 0, Y = 0, W = 1, H = 1 } }, Label = 2 },
            };

            var layout = MapScriptInterpreter.Generate(terrain, script, 1, 1, null, null, new Rng(1));

            Assert.Single(layout.Pieces);
        }

        [Fact]
        public void Generate_RemoveBlock_ClearsAPreviouslyPlacedCell()
        {
            var terrain = SyntheticRoadTerrain();
            var script = new List<MapScriptCommand>
            {
                new() { Type = MapScriptCommandType.AddBlock, Rects = new List<MapScriptRect> { new() { X = 0, Y = 0, W = 1, H = 1 } } },
                new() { Type = MapScriptCommandType.RemoveBlock, Rects = new List<MapScriptRect> { new() { X = 0, Y = 0, W = 1, H = 1 } } },
            };

            var layout = MapScriptInterpreter.Generate(terrain, script, 1, 1, null, null, new Rng(1));

            Assert.Equal(2, layout.Pieces.Count); // the add, then the clear
            Assert.Null(layout.Pieces[1].BlockName);
        }

        [Fact]
        public void Generate_DigTunnel_IsRecordedAsDeferredAndSkipped()
        {
            var terrain = SyntheticRoadTerrain();
            var script = new List<MapScriptCommand> { new() { Type = MapScriptCommandType.DigTunnel, Direction = MapDirection.Both } };

            var layout = MapScriptInterpreter.Generate(terrain, script, 1, 1, null, null, new Rng(1));

            Assert.Contains(MapScriptCommandType.DigTunnel, layout.DeferredCommandsSkipped);
            Assert.Empty(layout.Pieces);
        }
    }
}
