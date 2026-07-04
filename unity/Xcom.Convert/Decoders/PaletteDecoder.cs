using SixLabors.ImageSharp.PixelFormats;

namespace Xcom.Convert.Decoders
{
    /// <summary>
    /// Decodes an 8-bit palette from GEODATA/PALETTES.DAT.
    /// Port of Engine/Palette.cpp loadDat: palettes are 774 bytes apart
    /// (palOffset), each color is 3 bytes of 6-bit channels scaled *4;
    /// index 0 is the transparent color.
    /// </summary>
    public static class PaletteDecoder
    {
        public const int PaletteStride = 768 + 6; // 774
        public const int BattlescapePalette = 4;

        public static Rgba32[] Load(byte[] datBytes, int paletteIndex = BattlescapePalette)
        {
            int offset = paletteIndex * PaletteStride;
            var colors = new Rgba32[256];
            for (int i = 0; i < 256; i++)
            {
                int p = offset + i * 3;
                byte r = (byte)(datBytes[p + 0] * 4);
                byte g = (byte)(datBytes[p + 1] * 4);
                byte b = (byte)(datBytes[p + 2] * 4);
                byte a = (byte)(i == 0 ? 0 : 255);
                colors[i] = new Rgba32(r, g, b, a);
            }
            return colors;
        }
    }
}
