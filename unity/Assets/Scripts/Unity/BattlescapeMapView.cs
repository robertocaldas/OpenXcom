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

        /// <summary>
        /// The subset of RouteNodes that belong to the real craft piece
        /// itself (its own .RMP data, spanning all of its floors) - used to
        /// spawn soldiers by the Skyranger rather than scattered across
        /// farmland. MapScriptInterpreter.Generate always appends the craft
        /// piece last (BattlescapeGenerator.cpp:3278-3335's "overlay for
        /// real at the very end"), and MapGenerator.BuildFromLayout appends
        /// route nodes in that same piece order, so the craft's own nodes
        /// are always the trailing slice of RouteNodes. Populated by
        /// Awake().
        /// </summary>
        public IReadOnlyList<DataLoader.RawRouteNode> CraftRouteNodes { get; private set; }

        /// <summary>RouteNodes minus CraftRouteNodes - every other placed piece's nodes (farmland, the UFO). Populated by Awake().</summary>
        public IReadOnlyList<DataLoader.RawRouteNode> NonCraftRouteNodes { get; private set; }

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
                    tilesByDataset[ds.Name] = DataLoader.LoadTiles(gameDataDir, ds.Name);
                    if (!datasetAtlases.ContainsKey(ds.Name))
                    {
                        datasetAtlases[ds.Name] = AtlasLoader.Load(gameDataDir, $"terrain-{ds.Name}");
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

            var craftPiece = layout.Pieces.LastOrDefault(p => p.TerrainName == craftTerrainName);
            int craftNodeCount = craftPiece != null
                ? DataLoader.LoadMapBlock(gameDataDir, craftPiece.BlockName).RouteNodes.Count
                : 0;
            CraftRouteNodes = routeNodes.Skip(routeNodes.Count - craftNodeCount).ToList();
            NonCraftRouteNodes = routeNodes.Take(routeNodes.Count - craftNodeCount).ToList();

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
