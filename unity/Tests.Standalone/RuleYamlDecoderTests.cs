using System.Linq;
using Xcom.Convert.Decoders;
using Xunit;

namespace OpenXcom.Core.Tests
{
    public class RuleYamlDecoderTests
    {
        [Fact]
        public void LoadTerrainBlocks_RealCulta_HasAllNineteenBlocksAndTheFarmScript()
        {
            var terrain = RuleYamlDecoder.LoadTerrainBlocks(
                System.IO.Path.Combine(TestPaths.RulesDir, "terrains.rul"), "CULTA");

            Assert.Equal("CULTA", terrain.Name);
            Assert.Equal("FARM", terrain.Script);
            Assert.Equal(new[] { "BLANKS", "CULTIVAT", "BARN" }, terrain.Datasets);
            Assert.Equal(19, terrain.Blocks.Count);
            Assert.Equal("CULTA00", terrain.Blocks[0].Name);
            Assert.Equal(10, terrain.Blocks[0].Width);
            Assert.Equal(10, terrain.Blocks[0].Length);
            Assert.Equal(new[] { 1 }, terrain.Blocks[0].Groups); // landing-zone filler
            Assert.Equal("CULTA18", terrain.Blocks[18].Name);
            Assert.Equal(new[] { 0 }, terrain.Blocks[18].Groups); // no groups: -> default [0]
        }

        [Fact]
        public void LoadMapScript_RealFarmScript_MatchesTheRealThreeCommands()
        {
            var script = RuleYamlDecoder.LoadMapScript(
                System.IO.Path.Combine(TestPaths.RulesDir, "mapScripts.rul"), "FARM");

            Assert.Equal(3, script.Count);

            Assert.Equal("addUFO", script[0].Type);
            Assert.Equal(new[] { 1 }, script[0].Groups); // addUFO's push_back(1) default

            Assert.Equal("addCraft", script[1].Type);
            Assert.Equal(new[] { 1 }, script[1].Groups);

            Assert.Equal("fillArea", script[2].Type);
            Assert.Equal(Enumerable.Range(1, 18).ToArray(), script[2].Blocks.ToArray());
            Assert.Equal(18, script[2].Freqs.Count);
            Assert.All(script[2].Freqs, f => Assert.Equal(1, f));
            Assert.Equal(18, script[2].MaxUses.Count);
            Assert.All(script[2].MaxUses, m => Assert.Equal(3, m));
        }

        [Fact]
        public void LoadMapScript_DefaultsMatchTheOriginalConstructor()
        {
            var script = RuleYamlDecoder.LoadMapScript(
                System.IO.Path.Combine(TestPaths.RulesDir, "mapScripts.rul"), "FARM");

            var fillArea = script[2];
            Assert.Equal(1, fillArea.SizeX);
            Assert.Equal(1, fillArea.SizeY);
            Assert.Equal(0, fillArea.SizeZ);
            Assert.Equal("none", fillArea.Direction);
            Assert.Equal(1, fillArea.Executions);
            Assert.Equal(100, fillArea.ExecutionChances);
            Assert.Equal(0, fillArea.Label);
            Assert.Empty(fillArea.Conditionals);
            Assert.Empty(fillArea.Rects);
        }
    }
}
