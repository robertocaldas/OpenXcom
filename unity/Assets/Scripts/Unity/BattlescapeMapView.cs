using System.Collections.Generic;
using System.IO;
using OpenXcom.Core.Battle;
using OpenXcom.Core.Rules;
using OpenXcom.Unity.Rendering;
using UnityEngine;

namespace OpenXcom.Unity
{
    /// <summary>
    /// Loads the CULTA00 mapblock via Core's MapGenerator and instantiates one
    /// TileRenderer per tile. Reads converted PNG/JSON directly from
    /// Assets/GameData/ (gitignored, produced locally by `dotnet run` in
    /// Xcom.Convert — never committed) rather than importing it as a Unity
    /// asset, since the output is a regenerable derivative of copyrighted art.
    ///
    /// NOTE: this class cannot be compiled or run in the environment this was
    /// written in (no Unity Editor / UnityEngine assemblies available this
    /// session). It follows documented Unity API behavior but has not been
    /// visually verified — check it in the Editor before relying on it.
    /// </summary>
    public sealed class BattlescapeMapView : MonoBehaviour
    {
        [SerializeField] private string terrainName = "CULTA";
        [SerializeField] private string mapBlockName = "CULTA00";
        [SerializeField] private string[] datasetNames = { "BLANKS", "CULTIVAT", "BARN" };

        private void Start()
        {
            string gameDataDir = Path.Combine(Application.dataPath, "GameData");

            var terrain = DataLoader.LoadTerrain(gameDataDir, terrainName);
            var datasetTiles = new Dictionary<string, List<MapDataTile>>();
            var datasetAtlases = new Dictionary<string, (Texture2D texture, List<Rect> frameRects)>();

            foreach (var name in datasetNames)
            {
                datasetTiles[name] = DataLoader.LoadTiles(gameDataDir, name);
                datasetAtlases[name] = LoadAtlas(gameDataDir, $"terrain-{name}");
            }

            var block = DataLoader.LoadMapBlock(gameDataDir, mapBlockName);
            var grid = MapGenerator.Build(block, terrain, datasetTiles);

            for (int z = 0; z < grid.Height; z++)
            {
                for (int y = 0; y < grid.Length; y++)
                {
                    for (int x = 0; x < grid.Width; x++)
                    {
                        var tile = grid.At(x, y, z);
                        if (tile.Floor == null && tile.WestWall == null &&
                            tile.NorthWall == null && tile.Object == null)
                            continue;

                        var go = new GameObject($"Tile_{x}_{y}_{z}");
                        go.transform.SetParent(transform, worldPositionStays: false);
                        var renderer = go.AddComponent<TileRenderer>();

                        renderer.Setup(x, y, z, grid.Width, grid.Length,
                            floorSprite: SpriteFor(tile.Floor, datasetAtlases),
                            floorYOffsetPixels: tile.Floor?.YOffset ?? 0,
                            westWallSprite: SpriteFor(tile.WestWall, datasetAtlases),
                            northWallSprite: SpriteFor(tile.NorthWall, datasetAtlases),
                            objectSprite: SpriteFor(tile.Object, datasetAtlases));
                    }
                }
            }
        }

        private static (Texture2D texture, List<Rect> frameRects) LoadAtlas(string gameDataDir, string baseName)
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

        private static Sprite SpriteFor(MapDataTile part, Dictionary<string, (Texture2D texture, List<Rect> frameRects)> atlases)
        {
            if (part == null || part.Frames.Length == 0)
                return null;

            var (texture, frameRects) = atlases[part.DatasetName];
            int frameIndex = part.Frames[0]; // static (unanimated) first frame for this phase
            if (frameIndex < 0 || frameIndex >= frameRects.Count)
                return null;

            var rect = frameRects[frameIndex];
            return Sprite.Create(texture, rect, new Vector2(0.5f, 0f), pixelsPerUnit: 32f);
        }

        [System.Serializable]
        private struct AtlasFrameJson { public int x, y, w, h; }

        [System.Serializable]
        private struct AtlasFramesJson { public AtlasFrameJson[] frames; }
    }
}
