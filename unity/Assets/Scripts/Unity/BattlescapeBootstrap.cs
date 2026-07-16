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
    /// Assembles a real BattleState (real soldier/Sectoid stats, real
    /// weapons) and binds it to BattleController. Requires
    /// BattlescapeMapView on the same GameObject: reads its already-built
    /// Grid rather than reloading/rebuilding the mapblock a second time.
    ///
    /// Squad is placed at fixed coordinates, not BattleState.SpawnAtRouteNodes
    /// - CULTA00's .RMP has only one route node (verified directly against
    /// the raw file: 24 bytes = one record), so route-node spawning can't
    /// seat a 4-unit squad. Every tile in CULTA00 is walkable, so any
    /// coordinates work; these three put the two soldiers together in one
    /// corner and the two Sectoids together in the opposite corner.
    /// </summary>
    [RequireComponent(typeof(BattlescapeMapView))]
    [RequireComponent(typeof(BattleController))]
    [RequireComponent(typeof(TileCursorView))]
    [RequireComponent(typeof(PathPreviewView))]
    [RequireComponent(typeof(ProjectileView))]
    public sealed class BattlescapeBootstrap : MonoBehaviour
    {
        private static readonly Position SoldierAPos = new(1, 1, 0);
        private static readonly Position SoldierBPos = new(2, 1, 0);
        private static readonly Position SectoidAPos = new(8, 8, 0);
        private static readonly Position SectoidBPos = new(7, 8, 0);

        /// <summary>Initial spawn facing: direction 4 = south / facing the
        /// camera (UnitSprite.cpp:290-369,620; Pathfinding.h:220 for the
        /// direction convention) - units turn freely once the battle starts
        /// (BattleState.TryMove/TryFire), this is just the spawn pose.</summary>
        private const int SouthDirection = 4;

        private void Start()
        {
            string gameDataDir = Path.Combine(Application.dataPath, "GameData");
            var grid = GetComponent<BattlescapeMapView>().Grid;

            var armorsById = DataLoader.LoadArmors(gameDataDir).ToDictionary(a => a.Id);
            var unitsById = DataLoader.LoadUnits(gameDataDir, armorsById).ToDictionary(u => u.Id);
            var itemsById = DataLoader.LoadItems(gameDataDir).ToDictionary(i => i.Id);

            var xcomAtlas = AtlasLoader.Load(gameDataDir, "units-XCOM_0");
            var sectoidAtlas = AtlasLoader.Load(gameDataDir, "units-SECTOID");
            var handobAtlas = AtlasLoader.Load(gameDataDir, "handob");
            var bulletAtlas = AtlasLoader.Load(gameDataDir, "bulletsprites");

            var state = new BattleState(grid);
            state.LoftData = DataLoader.LoadLoftemps(gameDataDir);
            var unitTransforms = new Dictionary<BattleUnit, Transform>();

            Spawn(state, grid, unitTransforms, unitsById["STR_SOLDIER"], itemsById["STR_RIFLE"],
                Faction.Player, "Soldier A", SoldierAPos, xcomAtlas, handobAtlas);
            Spawn(state, grid, unitTransforms, unitsById["STR_SOLDIER"], itemsById["STR_RIFLE"],
                Faction.Player, "Soldier B", SoldierBPos, xcomAtlas, handobAtlas);
            Spawn(state, grid, unitTransforms, unitsById["STR_SECTOID_SOLDIER"], itemsById["STR_PLASMA_PISTOL"],
                Faction.Hostile, "Sectoid A", SectoidAPos, sectoidAtlas, handobAtlas);
            Spawn(state, grid, unitTransforms, unitsById["STR_SECTOID_SOLDIER"], itemsById["STR_PLASMA_PISTOL"],
                Faction.Hostile, "Sectoid B", SectoidBPos, sectoidAtlas, handobAtlas);

            GetComponent<BattleController>().Bind(state, unitTransforms);

            var cursorAtlas = AtlasLoader.Load(gameDataDir, "cursor");
            GetComponent<TileCursorView>().Setup(GetComponent<BattleController>(), cursorAtlas, grid.Width, grid.Length);

            var pathAtlas = AtlasLoader.Load(gameDataDir, "pathfinding");
            GetComponent<PathPreviewView>().Setup(GetComponent<BattleController>(), state, pathAtlas);

            GetComponent<ProjectileView>().Setup(bulletAtlas);
        }

        private void Spawn(BattleState state, OpenXcom.Core.Battle.TileGrid grid,
            Dictionary<BattleUnit, Transform> unitTransforms,
            RuleUnit ruleUnit, RuleItem weapon, Faction faction, string name, Position position,
            (Texture2D texture, List<Rect> frameRects) bodyAtlas,
            (Texture2D texture, List<Rect> frameRects) itemAtlas)
        {
            var unit = new BattleUnit(ruleUnit, faction, name)
            {
                Position = position,
                Direction = SouthDirection,
                RightHand = new BattleItem(weapon),
            };
            grid.At(position.X, position.Y, position.Z).Occupant = unit;
            state.Units.Add(unit);

            var go = new GameObject(name);
            go.transform.SetParent(transform, worldPositionStays: false);
            var renderer = go.AddComponent<UnitRenderer>();
            renderer.Setup(position.X, position.Y, position.Z, grid.Width, grid.Length, bodyAtlas, itemAtlas, weapon);
            renderer.SetFrame(unit.Direction, walkPhase: -1, isAiming: false);

            unitTransforms[unit] = go.transform;
        }
    }
}
