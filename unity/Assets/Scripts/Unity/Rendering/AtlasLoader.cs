using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OpenXcom.Unity.Rendering
{
    /// <summary>
    /// Loads one converted sprite atlas (PNG + frame-rect JSON) from
    /// Assets/GameData/. Shared by BattlescapeMapView (tile atlases) and
    /// BattlescapeBootstrap (unit atlases) - extracted from
    /// BattlescapeMapView's original private LoadAtlas so both can use it
    /// without duplicating the atlas-frame-rect-flip logic.
    /// </summary>
    public static class AtlasLoader
    {
        public static (Texture2D texture, List<Rect> frameRects) Load(string gameDataDir, string baseName)
        {
            byte[] pngBytes = File.ReadAllBytes(Path.Combine(gameDataDir, $"{baseName}.png"));
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.LoadImage(pngBytes);

            string framesJson = File.ReadAllText(Path.Combine(gameDataDir, $"{baseName}.frames.json"));
            var parsed = JsonUtility.FromJson<AtlasFramesJson>(framesJson);

            var rects = new List<Rect>(parsed.frames.Length);
            foreach (var f in parsed.frames)
            {
                // Atlas frame rects are in top-down image pixel space (AtlasWriter);
                // Unity's Sprite.Create rect is bottom-up texture pixel space.
                float flippedY = texture.height - f.y - f.h;
                rects.Add(new Rect(f.x, flippedY, f.w, f.h));
            }
            return (texture, rects);
        }

        [System.Serializable]
        private struct AtlasFrameJson { public int x, y, w, h; }

        [System.Serializable]
        private struct AtlasFramesJson { public AtlasFrameJson[] frames; }
    }
}
