namespace OpenXcom.Core.Rules
{
    /// <summary>
    /// One resolved terrain object record (one tile-part's worth of data),
    /// converted from an MCD record. Port of the fields MapDataSet::loadData
    /// (src/Mod/MapDataSet.cpp:105+) actually needs for static rendering.
    /// </summary>
    public sealed class MapDataTile
    {
        public int[] Frames = new int[8];
        public int ScanG;
        public bool IsUfoDoor;
        public bool StopLOS;
        public bool NoFloor;
        public int BigWall;
        public bool Gravlift;
        public bool IsDoor;
        public bool BlockFire;
        public bool BlockSmoke;
        public int TuWalk;
        public int TuSlide;
        public int TuFly;
        public int Armor;
        public int TerrainLevel; // MCD T_Level
        public int YOffset;      // MCD P_Level

        /// <summary>Per-Z-layer index (0-11) into LOFTEMPS.DAT's templates. MCD bytes 8-19.</summary>
        public int[] Loft = new int[12];

        /// <summary>Which dataset's own atlas this record's Frames index into.</summary>
        public string DatasetName;

        /// <summary>0-based index within DatasetName's own record list.</summary>
        public int LocalIndex;

        /// <summary>
        /// Port of MapData::isBackTileObject (src/Mod/MapData.cpp:135-137):
        /// objects with this flag draw before units; others draw after.
        /// </summary>
        public bool IsBackTileObject => BigWall < 6 || BigWall == 9;
    }
}
