namespace Xcom.Convert.Decoders
{
    /// <summary>
    /// Decodes .SPK-format images - despite some carrying a ".PCK" extension
    /// (ICONS.PCK, DETBORD.PCK, etc.), these are NOT sprite-sheet PCK+TAB
    /// files (PckDecoder); the original engine loads them via
    /// Surface::loadSpk (Engine/Surface.cpp:461-480) into one full-screen
    /// canvas, using 16-bit RLE control words rather than PckDecoder's 8-bit
    /// ones: a little-endian uint16 flag, 0xFFFF then a uint16 count means
    /// "count*2 transparent (index 0) pixels", 0xFFFE then a uint16 count
    /// means "count*2 explicit palette-index bytes follow", any other flag
    /// value is silently skipped (2 bytes consumed, no pixels written) -
    /// matches the original's lack of an else branch exactly. Pixels are
    /// written row-major with wraparound (Surface::setPixelIterative).
    /// </summary>
    public static class SpkDecoder
    {
        public static IndexedFrame Load(byte[] data, int width, int height)
        {
            var pixels = new byte[width * height];
            int x = 0, y = 0, pos = 0;

            void SetPixelIterative(byte value)
            {
                if (x >= 0 && x < width && y >= 0 && y < height)
                    pixels[y * width + x] = value;
                x++;
                if (x == width)
                {
                    y++;
                    x = 0;
                }
            }

            ushort ReadUInt16LE()
            {
                ushort v = (ushort)(data[pos] | (data[pos + 1] << 8));
                pos += 2;
                return v;
            }

            while (pos < data.Length - 1)
            {
                ushort flag = ReadUInt16LE();
                if (flag == 65535)
                {
                    ushort count = ReadUInt16LE();
                    for (int i = 0; i < count * 2; i++)
                        SetPixelIterative(0);
                }
                else if (flag == 65534)
                {
                    ushort count = ReadUInt16LE();
                    for (int i = 0; i < count * 2; i++)
                        SetPixelIterative(data[pos++]);
                }
            }

            return new IndexedFrame { Width = width, Height = height, Pixels = pixels };
        }

        /// <summary>Extracts a sub-rectangle (e.g. ICONS.PCK's bottom 56-row icon
        /// bar out of its full 320x200 canvas) as its own frame.</summary>
        public static IndexedFrame Crop(IndexedFrame source, int x, int y, int width, int height)
        {
            var pixels = new byte[width * height];
            for (int row = 0; row < height; row++)
                System.Array.Copy(source.Pixels, (y + row) * source.Width + x, pixels, row * width, width);
            return new IndexedFrame { Width = width, Height = height, Pixels = pixels };
        }
    }
}
