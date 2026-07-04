using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Xcom.Convert.Output
{
    public sealed class AtlasFrame
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
    }

    public sealed class AtlasResult
    {
        public Image<Rgba32> Image { get; set; } = null!;
        public List<AtlasFrame> Frames { get; set; } = new();
    }

    /// <summary>
    /// Packs indexed frames into a fixed-column grid atlas, applying the palette.
    /// All frames are assumed the same size (true for a single PCK set).
    /// </summary>
    public static class AtlasWriter
    {
        public static AtlasResult Build(IReadOnlyList<Decoders.IndexedFrame> frames,
            Rgba32[] palette, int columns = 16)
        {
            if (frames.Count == 0)
                return new AtlasResult { Image = new Image<Rgba32>(1, 1) };

            int fw = frames[0].Width, fh = frames[0].Height;
            int cols = System.Math.Min(columns, frames.Count);
            int rows = (frames.Count + cols - 1) / cols;
            var image = new Image<Rgba32>(cols * fw, rows * fh);
            var rects = new List<AtlasFrame>(frames.Count);

            for (int i = 0; i < frames.Count; i++)
            {
                int cx = (i % cols) * fw;
                int cy = (i / cols) * fh;
                var f = frames[i];
                for (int y = 0; y < fh; y++)
                    for (int x = 0; x < fw; x++)
                        image[cx + x, cy + y] = palette[f.Pixels[y * fw + x]];
                rects.Add(new AtlasFrame { X = cx, Y = cy, W = fw, H = fh });
            }

            return new AtlasResult { Image = image, Frames = rects };
        }

        public static void Save(AtlasResult atlas, string pngPath, string jsonPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(pngPath)!);
            atlas.Image.SaveAsPng(pngPath);
            var json = JsonConvert.SerializeObject(new { frames = atlas.Frames }, Formatting.Indented);
            File.WriteAllText(jsonPath, json);
        }
    }
}
