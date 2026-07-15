using System.IO;

namespace Xcom.Convert.Decoders
{
    /// <summary>
    /// Decodes LOFTEMPS.DAT: a flat sequence of little-endian uint16 values,
    /// no header. Every 16 consecutive values form one "loft template" - a
    /// 16x16 voxel bitmask, one uint16 per Y row (bit x = column x solid).
    /// Port of MapDataSet::loadLOFTEMPS (src/Mod/MapDataSet.cpp:272-287).
    /// </summary>
    public static class LoftempsDecoder
    {
        public const int RowsPerTemplate = 16;

        public static ushort[] Load(byte[] data)
        {
            if (data.Length % 2 != 0)
                throw new InvalidDataException("Invalid LOFTEMPS: odd byte length");

            var values = new ushort[data.Length / 2];
            for (int i = 0; i < values.Length; i++)
                values[i] = (ushort)(data[i * 2] | (data[i * 2 + 1] << 8));
            return values;
        }
    }
}
