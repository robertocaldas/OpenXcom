using OpenXcom.Core.Rules;

namespace OpenXcom.Core.Battle
{
    /// <summary>
    /// One battlefield tile's mutable state. Slice 1 models only what movement/LOS
    /// need: whether the floor is walkable and whether a wall blocks sight/movement.
    /// OXCE's real Tile carries per-part MapData (floor/walls/object), fire, smoke,
    /// inventory, etc. — added in later slices.
    /// </summary>
    public sealed class Tile
    {
        /// <summary>Can a unit stand here (a floor exists and it is not solid).</summary>
        public bool Walkable = true;

        /// <summary>Blocks both movement into it and line of sight through it.</summary>
        public bool BlocksSight;

        /// <summary>Extra TU cost to enter this tile beyond the base move cost.</summary>
        public int ExtraMoveCost;

        /// <summary>Permanent fog-of-war reveal: has any unit ever seen this tile? Never un-set once true.</summary>
        public bool Discovered;

        /// <summary>The unit currently occupying this tile, if any.</summary>
        public BattleUnit Occupant;

        /// <summary>Resolved per-part terrain records, set by MapGenerator. Null = nothing in that slot.</summary>
        public MapDataTile Floor;
        public MapDataTile WestWall;
        public MapDataTile NorthWall;
        public MapDataTile Object;
    }
}
