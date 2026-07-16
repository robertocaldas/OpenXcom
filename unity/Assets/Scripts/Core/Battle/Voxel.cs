using OpenXcom.Core.Common;

namespace OpenXcom.Core.Battle
{
    /// <summary>
    /// What a traced voxel line hit. Mirrors OXCE's VoxelType enum
    /// (src/Battlescape/Position.h) - tile-part indices 0-3 match this
    /// rewrite's Tile.Floor/WestWall/NorthWall/Object field order exactly.
    /// </summary>
    public enum VoxelType
    {
        OutOfBounds = -2,
        Empty = -1,
        Floor = 0,
        WestWall = 1,
        NorthWall = 2,
        Object = 3,
        Unit = 4,
    }

    /// <summary>Result of TileEngine.VoxelCheck/CalculateLine: what was hit, where, and (for a unit hit) who.</summary>
    public readonly struct VoxelHit
    {
        public readonly VoxelType Type;
        public readonly Position Voxel;
        public readonly BattleUnit Unit;

        public VoxelHit(VoxelType type, Position voxel, BattleUnit unit = null)
        {
            Type = type;
            Voxel = voxel;
            Unit = unit;
        }

        public static VoxelHit Empty(Position voxel) => new(VoxelType.Empty, voxel);

        /// <summary>True for anything that should stop a trace: a real hit
        /// (terrain or unit) OR the ray exiting the map - false only for
        /// Empty (still-passable air). Mirrors the real engine's own stop
        /// condition (TileEngine::calculateLineVoxel, TileEngine.cpp:4376,
        /// checks `result != V_EMPTY`, meaning V_OUTOFBOUNDS stops the trace
        /// same as any real hit). Excluding OutOfBounds from this (an earlier
        /// version of this property did) let CalculateLine's loop keep
        /// stepping straight through the map's edge all the way to a fully
        /// extended miss target - possibly thousands of voxel units past the
        /// actual map - instead of stopping where the ray actually left the
        /// map.</summary>
        public bool IsHit => Type != VoxelType.Empty;
    }
}
