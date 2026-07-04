using OpenXcom.Core.Common;

namespace OpenXcom.Core.Battle
{
    /// <summary>
    /// The battlefield: a 3D array of <see cref="Tile"/>. Slice 1 uses a single
    /// level (depth 1). Indexing is [x, y, z]. Out-of-bounds reads return null.
    /// Mirrors the role of OXCE's <c>SavedBattleGame</c> tile store.
    /// </summary>
    public sealed class TileGrid
    {
        public int Width { get; }
        public int Length { get; }
        public int Height { get; }

        private readonly Tile[,,] _tiles;

        public TileGrid(int width, int length, int height = 1)
        {
            Width = width;
            Length = length;
            Height = height;
            _tiles = new Tile[width, length, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < length; y++)
                    for (int z = 0; z < height; z++)
                        _tiles[x, y, z] = new Tile();
        }

        public bool InBounds(Position p) =>
            p.X >= 0 && p.X < Width && p.Y >= 0 && p.Y < Length && p.Z >= 0 && p.Z < Height;

        public Tile this[Position p] => InBounds(p) ? _tiles[p.X, p.Y, p.Z] : null;

        public Tile At(int x, int y, int z = 0) => this[new Position(x, y, z)];

        /// <summary>True if a unit could stand on this tile (walkable and unoccupied).</summary>
        public bool IsFree(Position p)
        {
            var t = this[p];
            return t != null && t.Walkable && !t.BlocksSight && t.Occupant == null;
        }
    }
}
