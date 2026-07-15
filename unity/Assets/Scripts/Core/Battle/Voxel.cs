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

        /// <summary>True for any real hit (terrain or unit) - false for Empty/OutOfBounds.</summary>
        public bool IsHit => Type != VoxelType.Empty && Type != VoxelType.OutOfBounds;
    }
}
