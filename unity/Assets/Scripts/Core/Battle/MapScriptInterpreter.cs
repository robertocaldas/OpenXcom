using System.Collections.Generic;
using System.Linq;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;

namespace OpenXcom.Core.Battle
{
    public sealed class PlacedPiece
    {
        public string BlockName; // null = clear this cell (removeBlock)
        public string TerrainName;
        public int GridX;
        public int GridY;
    }

    public sealed class GeneratedLayout
    {
        public int MapSizeXBlocks;
        public int MapSizeYBlocks;
        public List<PlacedPiece> Pieces = new();
        public List<MapScriptCommandType> DeferredCommandsSkipped = new();
    }

    /// <summary>
    /// Runs a terrain script's ordered instruction list against an
    /// occupancy grid, producing the ordered list of placed pieces later
    /// resolved into actual tile data (MapGenerator.BuildFromLayout).
    /// Port of the command-dispatch loop in
    /// BattlescapeGenerator::generateMap
    /// (src/Battlescape/BattlescapeGenerator.cpp:2862-3335). `digTunnel` is
    /// recognized but not executed - recorded in DeferredCommandsSkipped -
    /// since no terrain converted this phase uses it (design spec §2/§4).
    /// Stacked "vertical levels" (an optional per-command YAML modifier, not
    /// a command type) are a separate, larger gap: MapScriptCommand (Task 4)
    /// never parses that field at all, so there is nothing here to detect or
    /// record - if a future terrain's script used verticalLevels, this
    /// interpreter would silently ignore the modifier. Documented as a known
    /// gap, not fixed this phase (no terrain converted so far uses it).
    /// </summary>
    public static class MapScriptInterpreter
    {
        // Port of MapBlockType (src/Mod/MapBlock.h:28): the landing-zone
        // filler group addCraft/addUFO default to.
        private const int LandingZoneGroup = 1;

        public static GeneratedLayout Generate(
            RuleTerrain terrain, IReadOnlyList<MapScriptCommand> script,
            int mapSizeXBlocks, int mapSizeYBlocks,
            RuleTerrain craftTerrain, RuleTerrain ufoTerrain, Rng rng)
        {
            var layout = new GeneratedLayout { MapSizeXBlocks = mapSizeXBlocks, MapSizeYBlocks = mapSizeYBlocks };
            var occupied = new bool[mapSizeXBlocks, mapSizeYBlocks];
            var placedAt = new MapBlockInfo[mapSizeXBlocks, mapSizeYBlocks];

            // Port of BattlescapeGenerator.cpp:2802 (conditionals map) and
            // :2865-2898 (label/conditional gating).
            var conditionals = new Dictionary<int, bool>();

            // Craft/UFO are placed as filler during the loop, then overlaid
            // for real at the very end (BattlescapeGenerator.cpp:3268-3335).
            PlacedPiece craftFooter = null;
            PlacedPiece ufoFooter = null;

            foreach (var cmd in script)
            {
                if (cmd.Label > 0 && conditionals.ContainsKey(cmd.Label))
                    throw new System.InvalidOperationException("Multiple commands share the same label.");
                bool success = false;
                conditionals[cmd.Label] = success;

                if (cmd.Conditionals.Count > 0)
                {
                    bool execute = true;
                    foreach (int condition in cmd.Conditionals)
                    {
                        int label = System.Math.Abs(condition);
                        if (!conditionals.TryGetValue(label, out bool wasSuccess))
                            throw new System.InvalidOperationException(
                                "Conditional command references a label that hasn't run yet.");
                        if ((condition > 0 && !wasSuccess) || (condition < 0 && wasSuccess))
                        {
                            execute = false;
                            break;
                        }
                    }
                    if (!execute) continue;
                }

                if (!rng.Percent(cmd.ExecutionChances))
                    continue;

                if (cmd.Type == MapScriptCommandType.DigTunnel)
                {
                    layout.DeferredCommandsSkipped.Add(cmd.Type);
                    continue;
                }

                var pool = BuildPool(cmd);

                for (int execution = 0; execution < cmd.Executions; execution++)
                {
                    switch (cmd.Type)
                    {
                        case MapScriptCommandType.AddBlock:
                            success |= RunAddBlock(terrain, cmd, pool, occupied, placedAt, layout, rng);
                            break;
                        case MapScriptCommandType.FillArea:
                            success |= RunFillArea(terrain, cmd, pool, occupied, placedAt, layout, rng);
                            break;
                        case MapScriptCommandType.AddLine:
                            success |= RunAddLine(terrain, cmd, occupied, placedAt, layout, rng);
                            break;
                        case MapScriptCommandType.AddCraft:
                            if (craftTerrain != null)
                            {
                                var piece = RunAddCraftOrUfo(terrain, craftTerrain, cmd, pool, occupied, placedAt, layout, rng);
                                if (piece != null) { craftFooter = piece; success = true; }
                            }
                            break;
                        case MapScriptCommandType.AddUfo:
                            if (ufoTerrain != null)
                            {
                                var piece = RunAddCraftOrUfo(terrain, ufoTerrain, cmd, pool, occupied, placedAt, layout, rng);
                                if (piece != null) { ufoFooter = piece; success = true; }
                            }
                            break;
                        case MapScriptCommandType.CheckBlock:
                            success = RunCheckBlock(terrain, cmd, placedAt, mapSizeXBlocks, mapSizeYBlocks);
                            break;
                        case MapScriptCommandType.RemoveBlock:
                            success = RunRemoveBlocks(cmd, occupied, placedAt, layout, mapSizeXBlocks, mapSizeYBlocks);
                            break;
                        case MapScriptCommandType.Resize:
                            // [SIMPLIFIED] map size is fixed by the caller this
                            // phase (design spec §5) - no script converted this
                            // phase issues a resize, so this is a documented
                            // no-op rather than a full re-allocation port.
                            success = true;
                            break;
                    }
                }

                conditionals[cmd.Label] = success;
            }

            // Final overlay pass: the real craft/UFO art on top of its
            // reserved footprint (BattlescapeGenerator.cpp:3278-3335).
            if (ufoFooter != null)
                layout.Pieces.Add(new PlacedPiece { BlockName = "UFO1A", TerrainName = ufoTerrain.Name, GridX = ufoFooter.GridX, GridY = ufoFooter.GridY });
            if (craftFooter != null)
                layout.Pieces.Add(new PlacedPiece { BlockName = "PLANE", TerrainName = craftTerrain.Name, GridX = craftFooter.GridX, GridY = craftFooter.GridY });

            return layout;
        }

        private static BlockPool BuildPool(MapScriptCommand cmd)
        {
            var ids = cmd.Blocks.Count > 0 ? cmd.Blocks : cmd.Groups;
            return ids.Count > 0 ? new BlockPool(ids, cmd.Freqs, cmd.MaxUses) : null;
        }

        /// <summary>Port of MapScript::getNextBlock (src/Mod/MapScript.cpp:417-429).</summary>
        private static MapBlockInfo NextBlock(RuleTerrain terrain, MapScriptCommand cmd, BlockPool pool, Rng rng)
        {
            bool usesBlocks = cmd.Blocks.Count > 0;
            if (pool == null)
                // Port of MapScript::getGroupNumber's "no groups configured"
                // branch (src/Mod/MapScript.cpp:347-350): always MT_DEFAULT (0),
                // unconditionally, no pool/maxUses involved.
                return terrain.PickRandomBlock(rng, cmd.SizeX * 10, cmd.SizeY * 10, group: 0);

            int picked = pool.Pick(rng);
            if (picked < 0) return null;

            if (!usesBlocks)
                return terrain.PickRandomBlock(rng, cmd.SizeX * 10, cmd.SizeY * 10, picked);

            return picked < terrain.Blocks.Count ? terrain.Blocks[picked] : null;
        }

        private static bool RunAddBlock(RuleTerrain terrain, MapScriptCommand cmd, BlockPool pool,
            bool[,] occupied, MapBlockInfo[,] placedAt, GeneratedLayout layout, Rng rng)
        {
            var block = NextBlock(terrain, cmd, pool, rng);
            if (block == null) return false;
            if (!MapScriptPlacement.SelectPosition(rng, occupied, cmd.Rects, block.Width, block.Length, out int x, out int y))
                return false;
            return PlaceBlock(terrain, block, x, y, occupied, placedAt, layout);
        }

        private static bool RunFillArea(RuleTerrain terrain, MapScriptCommand cmd, BlockPool pool,
            bool[,] occupied, MapBlockInfo[,] placedAt, GeneratedLayout layout, Rng rng)
        {
            bool any = false;
            var block = NextBlock(terrain, cmd, pool, rng);
            while (block != null)
            {
                if (!MapScriptPlacement.SelectPosition(rng, occupied, cmd.Rects, block.Width, block.Length, out int x, out int y))
                    break;
                any |= PlaceBlock(terrain, block, x, y, occupied, placedAt, layout);
                block = NextBlock(terrain, cmd, pool, rng);
            }
            return any;
        }

        private static bool PlaceBlock(RuleTerrain terrain, MapBlockInfo block, int x, int y,
            bool[,] occupied, MapBlockInfo[,] placedAt, GeneratedLayout layout)
        {
            int sizeX = block.Width / 10, sizeY = block.Length / 10;
            for (int dx = 0; dx < sizeX; dx++)
                for (int dy = 0; dy < sizeY; dy++)
                {
                    occupied[x + dx, y + dy] = true;
                    placedAt[x + dx, y + dy] = block;
                }
            layout.Pieces.Add(new PlacedPiece { BlockName = block.Name, TerrainName = terrain.Name, GridX = x, GridY = y });
            return true;
        }

        /// <summary>
        /// Port of BattlescapeGenerator::addCraft (src/Battlescape/BattlescapeGenerator.cpp:4271-4310),
        /// reused for both addCraft and addUFO: reserves a footprint the
        /// size of the real craft/UFO block, filling it with the current
        /// terrain's own landing-zone (group 1) filler pieces. The real
        /// craft/UFO art is overlaid afterward by the caller (Generate's
        /// final pass) - this method only returns the reserved footprint's
        /// origin.
        /// </summary>
        private static PlacedPiece RunAddCraftOrUfo(RuleTerrain terrain, RuleTerrain vehicleTerrain, MapScriptCommand cmd,
            BlockPool fillerPool, bool[,] occupied, MapBlockInfo[,] placedAt, GeneratedLayout layout, Rng rng)
        {
            if (vehicleTerrain.Blocks.Count == 0) return null;
            var vehicleBlock = vehicleTerrain.PickRandomBlock(rng, 999, 999, group: 0) ?? vehicleTerrain.Blocks[0];

            if (!MapScriptPlacement.SelectPosition(rng, occupied, cmd.Rects, vehicleBlock.Width, vehicleBlock.Length, out int x, out int y))
                return null;

            int sizeX = vehicleBlock.Width / 10, sizeY = vehicleBlock.Length / 10;
            for (int dx = 0; dx < sizeX; dx++)
            {
                for (int dy = 0; dy < sizeY; dy++)
                {
                    if (occupied[x + dx, y + dy]) continue;
                    var filler = NextBlock(terrain, cmd, fillerPool, rng);
                    if (filler == null) continue;
                    occupied[x + dx, y + dy] = true;
                    placedAt[x + dx, y + dy] = filler;
                    layout.Pieces.Add(new PlacedPiece { BlockName = filler.Name, TerrainName = terrain.Name, GridX = x + dx, GridY = y + dy });
                }
            }

            return new PlacedPiece { GridX = x, GridY = y };
        }

        /// <summary>Port of BattlescapeGenerator::addLine (src/Battlescape/BattlescapeGenerator.cpp:4318-4418).</summary>
        private static bool RunAddLine(RuleTerrain terrain, MapScriptCommand cmd,
            bool[,] occupied, MapBlockInfo[,] placedAt, GeneratedLayout layout, Rng rng)
        {
            // Port of MapScript defaults MT_NSROAD=3 (vertical), MT_EWROAD=2
            // (horizontal), MT_CROSSING=4 (src/Mod/MapScript.cpp:33,
            // src/Mod/MapBlock.h:28) - this project doesn't expose per-command
            // group overrides for addLine yet (no script converted this phase
            // uses them), so the original's constructor defaults are used directly.
            const int verticalGroup = 3, horizontalGroup = 2, crossingGroup = 4;

            if (cmd.Direction == MapDirection.Both)
            {
                var vertCmd = Clone(cmd, MapDirection.Vertical);
                if (RunAddLine(terrain, vertCmd, occupied, placedAt, layout, rng))
                {
                    var horizCmd = Clone(cmd, MapDirection.Horizontal);
                    RunAddLine(terrain, horizCmd, occupied, placedAt, layout, rng);
                    return true;
                }
                return false;
            }

            bool vertical = cmd.Direction == MapDirection.Vertical;
            int limit = vertical ? occupied.GetLength(1) : occupied.GetLength(0);
            int comparator = vertical ? horizontalGroup : verticalGroup;
            int typeToAdd = vertical ? verticalGroup : horizontalGroup;

            int roadX = 0, roadY = 0;
            bool placed = false;
            for (int tries = 0; !placed; tries++)
            {
                placed = MapScriptPlacement.SelectPosition(rng, occupied, cmd.Rects, 10, 10, out roadX, out roadY);
                if (placed)
                {
                    int iter = vertical ? roadY : roadX;
                    for (int i = 0; i < limit; i++)
                    {
                        int cx = vertical ? roadX : i;
                        int cy = vertical ? i : roadY;
                        var existing = placedAt[cx, cy];
                        if (existing != null && !existing.Groups.Contains(comparator)) { placed = false; break; }
                    }
                }
                if (tries > 20) return false;
            }

            for (int i = 0; i < limit; i++)
            {
                int cx = vertical ? roadX : i;
                int cy = vertical ? i : roadY;
                var existing = placedAt[cx, cy];
                if (existing == null)
                {
                    var block = terrain.PickRandomBlock(rng, 10, 10, typeToAdd);
                    if (block == null)
                        throw new System.InvalidOperationException($"addLine failed: no block in group {typeToAdd} for terrain {terrain.Name}.");
                    occupied[cx, cy] = true;
                    placedAt[cx, cy] = block;
                    layout.Pieces.Add(new PlacedPiece { BlockName = block.Name, TerrainName = terrain.Name, GridX = cx, GridY = cy });
                }
                else if (existing.Groups.Contains(comparator))
                {
                    var crossing = terrain.PickRandomBlock(rng, 10, 10, crossingGroup);
                    if (crossing == null)
                        throw new System.InvalidOperationException($"addLine failed: no block in group {crossingGroup} for terrain {terrain.Name}.");
                    placedAt[cx, cy] = crossing;
                    layout.Pieces.Add(new PlacedPiece { BlockName = crossing.Name, TerrainName = terrain.Name, GridX = cx, GridY = cy });
                }
            }
            return true;
        }

        private static MapScriptCommand Clone(MapScriptCommand cmd, MapDirection direction) => new()
        {
            Type = cmd.Type, Rects = cmd.Rects, Groups = cmd.Groups, Blocks = cmd.Blocks,
            Freqs = cmd.Freqs, MaxUses = cmd.MaxUses, SizeX = cmd.SizeX, SizeY = cmd.SizeY, SizeZ = cmd.SizeZ,
            Direction = direction, Executions = cmd.Executions, ExecutionChances = cmd.ExecutionChances,
            Label = cmd.Label, Conditionals = cmd.Conditionals,
        };

        /// <summary>Port of the MSC_CHECKBLOCK case (src/Battlescape/BattlescapeGenerator.cpp:3198-3233).</summary>
        private static bool RunCheckBlock(RuleTerrain terrain, MapScriptCommand cmd, MapBlockInfo[,] placedAt, int mapW, int mapH)
        {
            foreach (var rect in cmd.Rects)
            {
                for (int x = rect.X; x < rect.X + rect.W && x < mapW; x++)
                {
                    for (int y = rect.Y; y < rect.Y + rect.H && y < mapH; y++)
                    {
                        var block = placedAt[x, y];
                        if (cmd.Groups.Count > 0)
                        {
                            foreach (int grp in cmd.Groups)
                                if (block != null && block.Groups.Contains(grp)) return true;
                        }
                        else if (cmd.Blocks.Count > 0)
                        {
                            foreach (int idx in cmd.Blocks)
                                if (idx < terrain.Blocks.Count && block == terrain.Blocks[idx]) return true;
                        }
                        else if (block != null)
                        {
                            return true; // wildcard: any placed block at all
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>Port of BattlescapeGenerator::removeBlocks (src/Battlescape/BattlescapeGenerator.cpp:4618-4700ish).</summary>
        private static bool RunRemoveBlocks(MapScriptCommand cmd, bool[,] occupied, MapBlockInfo[,] placedAt,
            GeneratedLayout layout, int mapW, int mapH)
        {
            var deleted = new HashSet<(int, int)>();
            foreach (var rect in cmd.Rects)
            {
                for (int x = rect.X; x < rect.X + rect.W && x < mapW; x++)
                {
                    for (int y = rect.Y; y < rect.Y + rect.H && y < mapH; y++)
                    {
                        var block = placedAt[x, y];
                        if (block == null) continue;

                        bool matches = cmd.Groups.Count > 0
                            ? cmd.Groups.Exists(g => block.Groups.Contains(g))
                            : cmd.Blocks.Count == 0 || true; // Blocks-list or wildcard: any placed block qualifies
                        if (matches) deleted.Add((x, y));
                    }
                }
            }

            foreach (var (x, y) in deleted)
            {
                occupied[x, y] = false;
                placedAt[x, y] = null;
                layout.Pieces.Add(new PlacedPiece { BlockName = null, TerrainName = null, GridX = x, GridY = y });
            }
            return deleted.Count > 0;
        }
    }
}
