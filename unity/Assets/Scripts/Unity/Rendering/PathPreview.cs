using System.Collections.Generic;
using OpenXcom.Core.Battle;

namespace OpenXcom.Unity.Rendering
{
    /// <summary>
    /// TU-cost affordability per step of a previewed path, for path-preview
    /// arrow tinting. [SIMPLIFIED] port of the affordability half of
    /// Tile::getMarkerColor's role in Pathfinding::previewPath
    /// (src/Battlescape/Pathfinding.cpp, Tile.cpp) - the original also colors
    /// a 3rd state (green, for an already-selected destination) and draws a
    /// separate neutral-tint base layer under the colored overlay
    /// (Map.cpp:1288-1301, :1635-1640); this phase's port only needs the
    /// yellow/red distinction the design calls for, applied directly to a
    /// single arrow sprite per step rather than two layered passes.
    /// </summary>
    public static class PathPreview
    {
        public enum Affordability { Affordable, Unaffordable }

        /// <summary>
        /// One Affordability per element of `path`, in path order. Each
        /// step's affordability is based on the CUMULATIVE TU cost to reach
        /// it (a step past the point where TU runs out is Unaffordable even
        /// if its own StepCost alone would fit).
        /// </summary>
        public static Affordability[] ComputeAffordability(IReadOnlyList<PathStep> path, int availableTu)
        {
            var result = new Affordability[path.Count];
            int cumulative = 0;
            for (int i = 0; i < path.Count; i++)
            {
                cumulative += path[i].StepCost;
                result[i] = cumulative <= availableTu ? Affordability.Affordable : Affordability.Unaffordable;
            }
            return result;
        }
    }
}
