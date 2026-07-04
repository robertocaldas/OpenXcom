using System.Collections.Generic;

namespace Xcom.Convert.Decoders
{
    public sealed class McdRecord
    {
        public byte[] Frames = new byte[8]; // animation frames (indices into the terrain PCK)
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
        public int HeBlock;
        public int DieMcd;
        public int Flammable;
        public int AltMcd;
        public int TLevel;   // signed
        public int PLevel;
        public int LightBlock;
        public int Footstep;
        public int TileType;
        public int HeType;
        public int HeStrength;
        public int SmokeBlockage;
        public int Fuel;
        public int LightSource;
        public int TargetType;
        public int XcomBase;
    }

    /// <summary>
    /// Decodes TERRAIN/*.MCD. Port of the 62-byte struct MCD in
    /// Mod/MapDataSet.cpp:115. Byte layout (0-indexed), derived by counting the
    /// struct members in order (Frame[8]=0..7, LOFT[12]=8..19, ScanG u16=20..21,
    /// then unused u23..u30 = bytes 22..29):
    ///   30 UFO_Door  31 Stop_LOS  32 No_Floor  33 Big_Wall  34 Gravlift
    ///   35 Door  36 Block_Fire  37 Block_Smoke  38 u39  39 TU_Walk
    ///   40 TU_Slide  41 TU_Fly  42 Armor  43 HE_Block  44 Die_MCD  45 Flammable
    ///   46 Alt_MCD  47 u48  48 T_Level(signed)  49 P_Level  50 u51  51 Light_Block
    ///   52 Footstep  53 Tile_Type  54 HE_Type  55 HE_Strength  56 Smoke_Blockage
    ///   57 Fuel  58 Light_Source  59 Target_Type  60 Xcom_Base  61 u62
    /// </summary>
    public static class McdDecoder
    {
        public const int RecordSize = 62;

        public static List<McdRecord> Load(byte[] mcd)
        {
            int count = mcd.Length / RecordSize;
            var records = new List<McdRecord>(count);
            for (int i = 0; i < count; i++)
            {
                int b = i * RecordSize;
                var r = new McdRecord();
                for (int f = 0; f < 8; f++) r.Frames[f] = mcd[b + f];
                r.ScanG        = mcd[b + 20] | (mcd[b + 21] << 8);
                r.IsUfoDoor    = mcd[b + 30] != 0;
                r.StopLOS      = mcd[b + 31] != 0;
                r.NoFloor      = mcd[b + 32] != 0;
                r.BigWall      = mcd[b + 33];
                r.Gravlift     = mcd[b + 34] != 0;
                r.IsDoor       = mcd[b + 35] != 0;
                r.BlockFire    = mcd[b + 36] != 0;
                r.BlockSmoke   = mcd[b + 37] != 0;
                r.TuWalk       = mcd[b + 39];
                r.TuSlide      = mcd[b + 40];
                r.TuFly        = mcd[b + 41];
                r.Armor        = mcd[b + 42];
                r.HeBlock      = mcd[b + 43];
                r.DieMcd       = mcd[b + 44];
                r.Flammable    = mcd[b + 45];
                r.AltMcd       = mcd[b + 46];
                r.TLevel       = (sbyte)mcd[b + 48];
                r.PLevel       = mcd[b + 49];
                r.LightBlock   = mcd[b + 51];
                r.Footstep     = mcd[b + 52];
                r.TileType     = mcd[b + 53];
                r.HeType       = mcd[b + 54];
                r.HeStrength   = mcd[b + 55];
                r.SmokeBlockage= mcd[b + 56];
                r.Fuel         = mcd[b + 57];
                r.LightSource  = mcd[b + 58];
                r.TargetType   = mcd[b + 59];
                r.XcomBase     = mcd[b + 60];
                records.Add(r);
            }
            return records;
        }
    }
}
