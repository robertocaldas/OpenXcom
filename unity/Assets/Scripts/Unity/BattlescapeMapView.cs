using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenXcom.Core.Battle;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;
using OpenXcom.Unity.Rendering;
using UnityEngine;

namespace OpenXcom.Unity
{
    /// <summary>
    /// Runs the real terrain-script interpreter to assemble a full farmland
    /// level (Small Scout UFO + Skyranger + random farmland fill, per the
    /// real FARM script) and instantiates one TileRenderer per tile. Reads
    /// converted PNG/JSON directly from Assets/GameData/ (gitignored,
    /// produced locally by `dotnet run` in Xcom.Convert - never committed)
    /// rather than importing it as a Unity asset, since the output is a
    /// regenerable derivative of copyrighted art.
    ///
    /// Runs in Awake (not Start) so BattlescapeBootstrap - which needs this
    /// component's Grid/RouteNodes to already exist - can safely read them
    /// from its own Start(): Unity guarantees every component's Awake() on
    /// a GameObject runs before any component's Start() on that same
    /// GameObject.
    /// </summary>
    public sealed class BattlescapeMapView : MonoBehaviour
    {
        [SerializeField] private string terrainName = "CULTA";
        [SerializeField] private string craftTerrainName = "PLANE";
        [SerializeField] private string ufoTerrainName = "UFO1A";
        [SerializeField] private int mapSizeXBlocks = 5;
        [SerializeField] private int mapSizeYBlocks = 5;
        [SerializeField] private int rngSeed = 1;

        /// <summary>The battle grid built from the generated level. Populated by Awake().</summary>
        public TileGrid Grid { get; private set; }

        /// <summary>Every placed piece's route nodes, offset into the merged grid. Populated by Awake().</summary>
        public IReadOnlyList<DataLoader.RawRouteNode> RouteNodes { get; private set; }

        private void Awake()
        {
            string gameDataDir = Path.Combine(Application.dataPath, "GameData");

            var farmland = DataLoader.LoadTerrain(gameDataDir, terrainName);
            var craftTerrain = DataLoader.LoadTerrain(gameDataDir, craftTerrainName);
            var ufoTerrain = DataLoader.LoadTerrain(gameDataDir, ufoTerrainName);
            var script = DataLoader.LoadMapScript(gameDataDir, farmland.Script);

            var layout = MapScriptInterpreter.Generate(
                farmland, script, mapSizeXBlocks, mapSizeYBlocks, craftTerrain, ufoTerrain, new Rng((uint)rngSeed));

            var terrainsByName = new Dictionary<string, RuleTerrain>
            {
                [farmland.Name] = farmland, [craftTerrain.Name] = craftTerrain, [ufoTerrain.Name] = ufoTerrain,
            };
            var datasetTilesByTerrain = new Dictionary<string, IReadOnlyDictionary<string, List<MapDataTile>>>();
            var datasetAtlases = new Dictionary<string, (Texture2D texture, List<Rect> frameRects)>();

            foreach (var terrain in terrainsByName.Values)
            {
                var tilesByDataset = new Dictionary<string, List<MapDataTile>>();
                foreach (var ds in terrain.DataSets)
                {
                    if (!datasetAtlases.ContainsKey(ds.Name))
                    {
                        tilesByDataset[ds.Name] = DataLoader.LoadTiles(gameDataDir, ds.Name);
                        datasetAtlases[ds.Name] = AtlasLoader.Load(gameDataDir, $"terrain-{ds.Name}");
                    }
                    else
                    {
                        tilesByDataset[ds.Name] = DataLoader.LoadTiles(gameDataDir, ds.Name);
                    }
                }
                datasetTilesByTerrain[terrain.Name] = tilesByDataset;
            }

            var (grid, routeNodes) = MapGenerator.BuildFromLayout(
                layout,
                name => terrainsByName[name],
                name => DataLoader.LoadMapBlock(gameDataDir, name),
                name => datasetTilesByTerrain[name]);
            Grid = grid;
            RouteNodes = routeNodes;

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
