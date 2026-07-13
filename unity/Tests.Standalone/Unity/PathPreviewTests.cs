using System.Collections.Generic;
using OpenXcom.Core.Battle;
using OpenXcom.Core.Common;
using OpenXcom.Unity.Rendering;
using Xunit;

namespace OpenXcom.Core.Tests.Unity
{
    public class PathPreviewTests
    {
        private static List<PathStep> Path3StepsCosting4Each() => new()
        {
            new PathStep { Position = new Position(1, 0, 0), StepCost = 4 },
            new PathStep { Position = new Position(2, 0, 0), StepCost = 4 },
            new PathStep { Position = new Position(3, 0, 0), StepCost = 4 },
        };

        [Fact]
        public void ComputeAffordability_EnoughTuForWholePath_AllAffordable()
        {
            var result = PathPreview.ComputeAffordability(Path3StepsCosting4Each(), availableTu: 12);
            Assert.All(result, a => Assert.Equal(PathPreview.Affordability.Affordable, a));
        }

        [Fact]
        public void ComputeAffordability_TuRunsOutPartway_MarksTheRestUnaffordable()
        {
            // Cumulative costs: 4, 8, 12. With 10 TU: step 1&2 affordable (4,8 <= 10),
            // step 3 (12) is not.
            var result = PathPreview.ComputeAffordability(Path3StepsCosting4Each(), availableTu: 10);
            Assert.Equal(PathPreview.Affordability.Affordable, result[0]);
            Assert.Equal(PathPreview.Affordability.Affordable, result[1]);
            Assert.Equal(PathPreview.Affordability.Unaffordable, result[2]);
        }

        [Fact]
        public void ComputeAffordability_ZeroTu_FirstStepAlreadyUnaffordable()
        {
            var result = PathPreview.ComputeAffordability(Path3StepsCosting4Each(), availableTu: 0);
            Assert.Equal(PathPreview.Affordability.Unaffordable, result[0]);
        }

        [Fact]
        public void ComputeAffordability_EmptyPath_ReturnsEmptyArray()
        {
            var result = PathPreview.ComputeAffordability(new List<PathStep>(), availableTu: 10);
            Assert.Empty(result);
        }
    }
}
