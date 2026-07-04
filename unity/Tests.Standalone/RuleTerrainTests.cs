using System.Collections.Generic;
using OpenXcom.Core.Rules;
using Xunit;

namespace OpenXcom.Core.Tests
{
    public class RuleTerrainTests
    {
        [Fact]
        public void Resolve_WalksDatasetsInOrder_SyntheticSizes()
        {
            // Dataset A size 3, dataset B size 5 (mirrors BLANKS/CULTIVAT ordering).
            var terrain = new RuleTerrain("TEST", new List<MapDataSetInfo>
            {
                new() { Name = "A", Size = 3 },
                new() { Name = "B", Size = 5 },
            });

            Assert.Equal(("A", 0), terrain.Resolve(0));
            Assert.Equal(("A", 2), terrain.Resolve(2));
            Assert.Equal(("B", 0), terrain.Resolve(3));
            Assert.Equal(("B", 4), terrain.Resolve(7));
        }

        [Fact]
        public void Resolve_RealCultaDatasetSizes_BoundaryIndicesLandInExpectedDataset()
        {
            // Real record counts verified against file sizes: BLANKS=2, CULTIVAT=37, BARN=29.
            var terrain = new RuleTerrain("CULTA", new List<MapDataSetInfo>
            {
                new() { Name = "BLANKS", Size = 2 },
                new() { Name = "CULTIVAT", Size = 37 },
                new() { Name = "BARN", Size = 29 },
            });

            Assert.Equal(("BLANKS", 1), terrain.Resolve(1));      // last BLANKS index
            Assert.Equal(("CULTIVAT", 0), terrain.Resolve(2));    // first CULTIVAT index
            Assert.Equal(("CULTIVAT", 36), terrain.Resolve(38));  // last CULTIVAT index
            Assert.Equal(("BARN", 0), terrain.Resolve(39));       // first BARN index
            Assert.Equal(("BARN", 28), terrain.Resolve(67));      // last BARN index
        }

        [Fact]
        public void Resolve_OutOfRangeIndex_FallsBackToFirstDatasetRecordZero()
        {
            var terrain = new RuleTerrain("TEST", new List<MapDataSetInfo>
            {
                new() { Name = "A", Size = 3 },
            });

            Assert.Equal(("A", 0), terrain.Resolve(999));
        }
    }
}
