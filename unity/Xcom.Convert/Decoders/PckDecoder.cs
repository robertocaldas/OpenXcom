using System.Collections.Generic;

namespace Xcom.Convert.Decoders
{
    /// <summary>One decoded 8-bit indexed sprite frame. Index 0 = transparent.</summary>
    public sealed class IndexedFrame
    {
        public int Width;
        public int Height;
        public byte[] Pixels = System.Array.Empty<byte>();
    }

    /// <summary>
    /// Decodes PCK+TAB sprite sets. Port of Engine/SurfaceSet.cpp loadPck.
    /// TAB holds per-frame offsets: if the first 4 bytes are non-zero the offsets
    /// are 16-bit (nframes = size/2), else 32-bit (nframes = size/4).
    /// PCK per frame: first byte = number of fully-transparent rows to skip; then
    /// a stream where 0xFF ends the frame, 0xFE n writes n transparent pixels, and
    /// any other byte is a palette index written to the next pixel.
    /// </summary>
    public static class PckDecoder
    {
        public static List<IndexedFrame> Load(byte[] pck, byte[] tab, int width, int height)
        {
            int nframes;
            if (tab.Length >= 4)
            {
                int first = tab[0] | (tab[1] << 8) | (tab[2] << 16) | (tab[3] << 24);
                nframes = first != 0 ? tab.Length / 2 : tab.Length / 4;
            }
            else
            {
                nframes = 1;
            }

            var frames = new List<IndexedFrame>(nframes);
            int pckPos = 0;

            for (int frame = 0; frame < nframes; frame++)
            {
                var pixels = new byte[width * height];
                int dst = 0;

                // First byte: count of leading transparent rows.
                byte lead = pck[pckPos++];
                dst += lead * width; // pixels default to 0 (transparent)

                byte value;
                while ((value = pck[pckPos++]) != 0xFF)
                {
                    if (value == 0xFE)
                    {
                        byte count = pck[pckPos++];
                        dst += count; // transparent run
                    }
                    else
                    {
                        if (dst < pixels.Length) pixels[dst] = value;
                        dst++;
                    }
                }

                frames.Add(new IndexedFrame { Width = width, Height = height, Pixels = pixels });
            }

            // TAB offsets aren't needed to walk frames: each frame ends with 0xFF,
            // and frames are stored contiguously. TAB is used only for the count.
            return frames;
        }
    }
}
