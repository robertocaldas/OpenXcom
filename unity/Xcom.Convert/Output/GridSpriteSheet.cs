using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Xcom.Convert.Output
{
    /// <summary>
    /// Converts a sprite sheet that's already a plain RGBA PNG laid out as a
    /// fixed grid of equal-size frames (row-major) — e.g. OXCE's bundled
    /// Resources/Pathfinding/Pathfinding.png — into the same
    /// (png, frames.json) pair AtlasWriter.Save produces for palette-decoded
    /// .PCK sprites, so AtlasLoader.Load can read either kind identically.
    /// No palette or PckDecoder involved: the source is already true-color,
    /// so this only copies bytes and computes frame rects.
    /// </summary>
    public static class GridSpriteSheet
    {
        public static void Convert(string srcPngPath, string outPngPath, string outFramesJsonPath,
            int frameWidth, int frameHeight, int columns, int rows)
        {
            string? outPngDir = Path.GetDirectoryName(outPngPath);
            if (!string.IsNullOrEmpty(outPngDir))
                Directory.CreateDirectory(outPngDir);
            File.Copy(srcPngPath, outPngPath, overwrite: true);

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
    }
}
