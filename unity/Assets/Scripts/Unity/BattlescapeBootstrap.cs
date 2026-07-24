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
    /// Grid/RouteNodes rather than reloading/rebuilding the level a second
    /// time.
    ///
    /// [SIMPLIFIED] Real deployment data would place soldiers at the
    /// Skyranger's own specific door tiles and aliens at rank-filtered nodes
    /// (design spec §6) - neither is converted this phase. Soldiers instead
    /// get the craft's own real route nodes (BattlescapeMapView.
    /// CraftRouteNodes), restricted to its ground level (Z=0): with no way
    /// yet to hide upper floors from view (a separate, larger gap - every
    /// floor always renders at once), a soldier placed on the Skyranger's
    /// upper deck would be stuck visually buried in its own hull. Sectoids
    /// get every other placed piece's nodes (farmland, the UFO). Not a real
    /// "spawn near the craft" rule, just an honest, working stand-in for
    /// one.
    /// </summary>
    [RequireComponent(typeof(BattlescapeMapView))]
    [RequireComponent(typeof(BattleController))]
    [RequireComponent(typeof(TileCursorView))]
    [RequireComponent(typeof(PathPreviewView))]
    [RequireComponent(typeof(ProjectileView))]
    public sealed class BattlescapeBootstrap : MonoBehaviour
    {
        /// <summary>Initial spawn facing: direction 4 = south / facing the
        /// camera (UnitSprite.cpp:290-369,620; Pathfinding.h:220 for the
        /// direction convention) - units turn freely once the battle starts
        /// (BattleState.TryMove/TryFire), this is just the spawn pose.</summary>
        private const int SouthDirection = 4;
        private const int SoldierCount = 4;
        private const int SectoidCount = 4;

        private void Start()
        {
            string gameDataDir = Path.Combine(Application.dataPath, "GameData");
            var mapView = GetComponent<BattlescapeMapView>();
            var grid = mapView.Grid;

            var armorsById = DataLoader.LoadArmors(gameDataDir).ToDictionary(a => a.Id);
            var unitsById = DataLoader.LoadUnits(gameDataDir, armorsById).ToDictionary(u => u.Id);
            var itemsById = DataLoader.LoadItems(gameDataDir).ToDictionary(i => i.Id);

            var xcomAtlas = AtlasLoader.Load(gameDataDir, "units-XCOM_0");
            var sectoidAtlas = AtlasLoader.Load(gameDataDir, "units-SECTOID");
            var handobAtlas = AtlasLoader.Load(gameDataDir, "handob");
            var bulletAtlas = AtlasLoader.Load(gameDataDir, "bulletsprites");

            var state = new BattleState(grid);
            state.LoftData = DataLoader.LoadLoftemps(gameDataDir);

            var soldierNodes = mapView.CraftRouteNodes.Where(n => n.Z == 0).ToList();
            var sectoidNodes = mapView.NonCraftRouteNodes;

            var soldierUnits = new List<BattleUnit>();
            for (int i = 0; i < SoldierCount; i++)
                soldierUnits.Add(new BattleUnit(unitsById["STR_SOLDIER"], Faction.Player, $"Soldier {(char)('A' + i)}")
                {
                    Direction = SouthDirection,
                    RightHand = new BattleItem(itemsById["STR_RIFLE"]),
                });
            state.SpawnSquad(soldierNodes, soldierUnits);

            var sectoidUnits = new List<BattleUnit>();
            for (int i = 0; i < SectoidCount; i++)
                sectoidUnits.Add(new BattleUnit(unitsById["STR_SECTOID_SOLDIER"], Faction.Hostile, $"Sectoid {(char)('A' + i)}")
                {
                    Direction = SouthDirection,
                    RightHand = new BattleItem(itemsById["STR_PLASMA_PISTOL"]),
                });
            state.SpawnSquad(sectoidNodes, sectoidUnits);

            var unitTransforms = new Dictionary<BattleUnit, Transform>();
            foreach (var unit in soldierUnits.Where(u => state.Units.Contains(u)))
                unitTransforms[unit] = BuildUnitGameObject(unit, xcomAtlas, handobAtlas, grid.Width, grid.Length);
            foreach (var unit in sectoidUnits.Where(u => state.Units.Contains(u)))
                unitTransforms[unit] = BuildUnitGameObject(unit, sectoidAtlas, handobAtlas, grid.Width, grid.Length);

            GetComponent<BattleController>().Bind(state, unitTransforms);

            var cursorAtlas = AtlasLoader.Load(gameDataDir, "cursor");
            GetComponent<TileCursorView>().Setup(GetComponent<BattleController>(), cursorAtlas, grid.Width, grid.Length);

            var pathAtlas = AtlasLoader.Load(gameDataDir, "pathfinding");
            GetComponent<PathPreviewView>().Setup(GetComponent<BattleController>(), state, pathAtlas);

            GetComponent<ProjectileView>().Setup(bulletAtlas);
        }

        /// <summary>Builds the GameObject/UnitRenderer for an already-spawned
        /// unit (Position/RightHand already set by BattleState.SpawnSquad).</summary>
        private Transform BuildUnitGameObject(BattleUnit unit,
            (Texture2D texture, List<Rect> frameRects) bodyAtlas,
            (Texture2D texture, List<Rect> frameRects) itemAtlas,
            int gridWidth, int gridLength)
        {
            var go = new GameObject(unit.Name);
            go.transform.SetParent(transform, worldPositionStays: false);
            var renderer = go.AddComponent<UnitRenderer>();
            renderer.Setup(unit.Position.X, unit.Position.Y, unit.Position.Z, gridWidth, gridLength,
                bodyAtlas, itemAtlas, unit.RightHand.Rules, unit.Rules.StandHeight);
            renderer.SetFrame(unit.Direction, walkPhase: -1, isAiming: false);
            return go.transform;
        }
    }
}
