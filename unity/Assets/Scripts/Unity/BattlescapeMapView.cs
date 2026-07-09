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
    /// Runs in Awake (not Start) so BattlescapeBootstrap - which needs this
    /// component's Grid to already exist - can safely read it from its own
    /// Start(): Unity guarantees every component's Awake() on a GameObject
    /// runs before any component's Start() on that same GameObject.
    /// </summary>
    public sealed class BattlescapeMapView : MonoBehaviour
    {
        [SerializeField] private string terrainName = "CULTA";
        [SerializeField] private string mapBlockName = "CULTA00";
        [SerializeField] private string[] datasetNames = { "BLANKS", "CULTIVAT", "BARN" };

        /// <summary>The battle grid built from the mapblock this view rendered. Populated by Awake().</summary>
        public TileGrid Grid { get; private set; }

        private void Awake()
        {
            string gameDataDir = Path.Combine(Application.dataPath, "GameData");

            var terrain = DataLoader.LoadTerrain(gameDataDir, terrainName);
            var datasetTiles = new Dictionary<string, List<MapDataTile>>();
            var datasetAtlases = new Dictionary<string, (Texture2D texture, List<Rect> frameRects)>();

            foreach (var name in datasetNames)
            {
                datasetTiles[name] = DataLoader.LoadTiles(gameDataDir, name);
                datasetAtlases[name] = AtlasLoader.Load(gameDataDir, $"terrain-{name}");
            }

            var block = DataLoader.LoadMapBlock(gameDataDir, mapBlockName);
            Grid = MapGenerator.Build(block, terrain, datasetTiles);

            for (int z = 0; z < Grid.Height; z++)
            {
                for (int y = 0; y < Grid.Length; y++)
                {
                    for (int x = 0; x < Grid.Width; x++)
                    {
                        var tile = Grid.At(x, y, z);
                        if (tile.Floor == null && tile.WestWall == null &&
                            tile.NorthWall == null && tile.Object == null)
                            continue;

                        var go = new GameObject($"Tile_{x}_{y}_{z}");
                        go.transform.SetParent(transform, worldPositionStays: false);
                        var renderer = go.AddComponent<TileRenderer>();

                        renderer.Setup(x, y, z, Grid.Width, Grid.Length,
                            floorSprite: SpriteFor(tile.Floor, datasetAtlases),
                            floorYOffsetPixels: tile.Floor?.YOffset ?? 0,
                            westWallSprite: SpriteFor(tile.WestWall, datasetAtlases),
                            northWallSprite: SpriteFor(tile.NorthWall, datasetAtlases),
                            objectSprite: SpriteFor(tile.Object, datasetAtlases));
                    }
                }
            }
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
            return Sprite.Create(texture, rect, new Vector2(0.5f, 0f), pixelsPerUnit: Rendering.TileRenderer.PixelsPerUnit);
        }
    }
}
