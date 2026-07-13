using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Xcom.Convert.Output
{
    /// <summary>
    /// Converts a sprite sheet laid out as a fixed grid of equal-size frames
    /// (row-major) into the same (png, frames.json) pair AtlasWriter.Save
    /// produces for palette-decoded .PCK sprites, so AtlasLoader.Load can
    /// read either kind identically. Colorkeys indexed (palette) source PNGs
    /// using the same "palette index 0 is the transparent color" convention
    /// the original engine enforces for all its indexed image loading
    /// (src/Engine/Surface.cpp:81-90, ~402-449): a source PNG like OXCE's own
    /// Resources/Pathfinding/Pathfinding.png has no tRNS chunk, so a naive
    /// decode (or a raw byte copy, as an earlier version of this method did)
    /// leaves every pixel fully opaque - this reads the PNG's own PLTE chunk
    /// to find index 0's RGB and colorkeys every matching pixel to alpha=0
    /// after decoding. A source PNG with no PLTE chunk (already truecolor
    /// with real alpha) passes through unmodified.
    /// </summary>
    public static class GridSpriteSheet
    {
        public static void Convert(string srcPngPath, string outPngPath, string outFramesJsonPath,
            int frameWidth, int frameHeight, int columns, int rows)
        {
            using var image = Image.Load<Rgba32>(srcPngPath);

            var transparentKey = ReadPaletteIndexZeroColor(srcPngPath);
            if (transparentKey.HasValue)
            {
                var key = transparentKey.Value;
                image.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < accessor.Height; y++)
                    {
                        var row = accessor.GetRowSpan(y);
                        for (int x = 0; x < row.Length; x++)
                        {
                            if (row[x].R == key.R && row[x].G == key.G && row[x].B == key.B)
                                row[x] = new Rgba32(key.R, key.G, key.B, 0);
                        }
                    }
                });
            }

            string? outPngDir = Path.GetDirectoryName(outPngPath);
            if (!string.IsNullOrEmpty(outPngDir))
                Directory.CreateDirectory(outPngDir);
            // Force truecolor+alpha (PNG color-type 6) output explicitly: with
            // a low distinct-color-count source like this one, ImageSharp's
            // default encoder auto-detects and re-quantizes back down to an
            // indexed (color-type 3) palette, which silently drops the
            // per-pixel alpha=0 colorkeying applied above (its default
            // quantizer clusters by RGB only, not alpha).
            image.SaveAsPng(outPngPath, new PngEncoder { ColorType = PngColorType.RgbWithAlpha });

            var frames = new List<AtlasFrame>(columns * rows);
            for (int i = 0; i < columns * rows; i++)
            {
                int cx = (i % columns) * frameWidth;
                int cy = (i / columns) * frameHeight;
                frames.Add(new AtlasFrame { X = cx, Y = cy, W = frameWidth, H = frameHeight });
            }

            string? jsonDir = Path.GetDirectoryName(outFramesJsonPath);
            if (!string.IsNullOrEmpty(jsonDir))
                Directory.CreateDirectory(jsonDir);
            File.WriteAllText(outFramesJsonPath,
                JsonConvert.SerializeObject(new { frames }, Formatting.Indented));
        }

        /// <summary>
        /// Reads the PNG's own PLTE chunk (if present) and returns palette
        /// index 0's RGB, or null for a non-indexed (truecolor) source PNG,
        /// which needs no colorkeying since it already carries real alpha.
        /// </summary>
        private static Rgba32? ReadPaletteIndexZeroColor(string pngPath)
        {
            byte[] bytes = File.ReadAllBytes(pngPath);
            int pos = 8; // skip the 8-byte PNG signature
            while (pos + 8 <= bytes.Length)
            {
                int length = (bytes[pos] << 24) | (bytes[pos + 1] << 16) | (bytes[pos + 2] << 8) | bytes[pos + 3];
                string type = Encoding.ASCII.GetString(bytes, pos + 4, 4);
                if (type == "PLTE" && length >= 3)
                    return new Rgba32(bytes[pos + 8], bytes[pos + 9], bytes[pos + 10], (byte)255);
                pos += 8 + length + 4; // length + type + data + CRC
            }
            return null;
        }
    }
}
