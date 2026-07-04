using OpenXcom.Core.Battle;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;
using Xunit;

namespace OpenXcom.Core.Tests
{
    public class AiModuleTests
    {
        private static TileGrid MakeCorridor(int width)
        {
            var grid = new TileGrid(width, 1, 1);
            var floor = new MapDataTile { TuWalk = 4 };
            for (int x = 0; x < width; x++)
                grid.At(x, 0, 0).Floor = floor;
            return grid;
        }

        [Fact]
        public void TakeTurn_VisibleEnemyWithEnoughTu_FiresAndReturnsTrue()
        {
            var grid = new TileGrid(10, 10, 1);
            var state = new BattleState(grid);
            var hostile = new BattleUnit(RuleUnit.Sectoid, Faction.Hostile) { Position = new Position(0, 0, 0) };
            hostile.RightHand = new BattleItem(RuleItem.Rifle);
            var player = new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(3, 0, 0) };
            grid.At(0, 0, 0).Occupant = hostile;
            grid.At(3, 0, 0).Occupant = player;
            state.Units.Add(hostile);
            state.Units.Add(player);

            bool result = AiModule.TakeTurn(state, hostile);

            Assert.True(result);
            var events = state.DequeueEvents();
            Assert.Contains(events, e => e is ProjectileFiredEvent);
        }

        [Fact]
        public void TakeTurn_NoVisibleEnemy_ReturnsFalseAndEnqueuesNothing()
        {
            var grid = new TileGrid(50, 50, 1);
            var state = new BattleState(grid);
            var hostile = new BattleUnit(RuleUnit.Sectoid, Faction.Hostile) { Position = new Position(0, 0, 0) };
            var player = new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(49, 0, 0) }; // distance 49 > MaxViewDistance (20)
            state.Units.Add(hostile);
            state.Units.Add(player);

            bool result = AiModule.TakeTurn(state, hostile);

            Assert.False(result);
            Assert.Empty(state.DequeueEvents());
        }

        [Fact]
        public void TakeTurn_CannotAffordToFire_MovesToOrthogonalApproachTileInstead()
        {
            // Hostile starts EAST of the target at x=8; target at x=5.
            // FindApproachTile checks the target's N/E/S/W neighbors in that
            // order - N/S are out of bounds in this 1-row grid, so it picks
            // E=(6,0,0) first. That tile is reachable from x=8 without
            // passing through the target's own occupied tile (8->7->6).
            var grid = MakeCorridor(10);
            var state = new BattleState(grid);
            var hostile = new BattleUnit(RuleUnit.Sectoid, Faction.Hostile) { Position = new Position(8, 0, 0) };
            hostile.RightHand = new BattleItem(RuleItem.Rifle);
            hostile.TimeUnits = 10; // enough for 2 move steps (4 each) but not an aimed shot (Sectoid TimeUnits=54 * TuAimed 55% = 29)
            var player = new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(5, 0, 0) };
            grid.At(8, 0, 0).Occupant = hostile;
            grid.At(5, 0, 0).Occupant = player;
            state.Units.Add(hostile);
            state.Units.Add(player);

            bool result = AiModule.TakeTurn(state, hostile);

            Assert.True(result);
            Assert.Equal(new Position(6, 0, 0), hostile.Position); // walked 8->7->6, 2 steps * 4 TU = 8
            Assert.Equal(2, hostile.TimeUnits); // 10 - 8
        }

        [Fact]
        public void TakeTurn_UnreachableApproachTile_ReturnsFalseWithNoChange()
        {
            // Hostile at x=0 (no weapon - skips straight to the move
            // fallback), target at x=5 in a 1-row corridor. FindApproachTile
            // picks E=(6,0,0) first (N/S out of bounds) - but reaching x=6
            // from x=0 requires stepping through x=5, which is occupied by
            // the target itself. There is no way around it in a 1-row grid,
            // so the move must fail entirely.
            var grid = MakeCorridor(10);
            var state = new BattleState(grid);
            var hostile = new BattleUnit(RuleUnit.Sectoid, Faction.Hostile) { Position = new Position(0, 0, 0) };
            var player = new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(5, 0, 0) };
            grid.At(0, 0, 0).Occupant = hostile;
            grid.At(5, 0, 0).Occupant = player;
            state.Units.Add(hostile);
            state.Units.Add(player);
            int startingTu = hostile.TimeUnits;

            bool result = AiModule.TakeTurn(state, hostile);

            Assert.False(result);
            Assert.Equal(new Position(0, 0, 0), hostile.Position);
            Assert.Equal(startingTu, hostile.TimeUnits);
            Assert.Empty(state.DequeueEvents());
        }

        [Fact]
        public void RunHostileTurn_SkipsDeadUnits()
        {
            var grid = MakeCorridor(10);
            var state = new BattleState(grid);
            var deadHostile = new BattleUnit(RuleUnit.Sectoid, Faction.Hostile) { Position = new Position(0, 0, 0) };
            deadHostile.RightHand = new BattleItem(RuleItem.Rifle);
            deadHostile.Health = 0;
            int deadTu = deadHostile.TimeUnits;
            var player = new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(3, 0, 0) };
            grid.At(0, 0, 0).Occupant = deadHostile;
            grid.At(3, 0, 0).Occupant = player;
            state.Units.Add(deadHostile);
            state.Units.Add(player);

            AiModule.RunHostileTurn(state);

            Assert.Equal(deadTu, deadHostile.TimeUnits); // never acted
            Assert.Empty(state.DequeueEvents());
        }

        [Fact]
        public void RunHostileTurn_UnitWithEnoughTuForMultipleShots_FiresExactlyAsManyTimesAsAffordable()
        {
            var grid = new TileGrid(10, 10, 1);
            var state = new BattleState(grid);
            var hostile = new BattleUnit(RuleUnit.Sectoid, Faction.Hostile) { Position = new Position(0, 0, 0) };
            hostile.RightHand = new BattleItem(RuleItem.Rifle);
            hostile.TimeUnits = 100; // aimed shot costs a fixed 29 (Sectoid Stats.TimeUnits=54 * 55% = 29): 100->71->42->13, 3 affordable shots, 4th (13<29) is not
            var player = new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(3, 0, 0), Health = 1000 }; // health high enough that no plausible roll sequence kills it, keeping it a valid target throughout
            grid.At(0, 0, 0).Occupant = hostile;
            grid.At(3, 0, 0).Occupant = player;
            state.Units.Add(hostile);
            state.Units.Add(player);

            AiModule.RunHostileTurn(state);

            Assert.Equal(13, hostile.TimeUnits);
            var fireEvents = state.DequeueEvents();
            int fireCount = 0;
            foreach (var evt in fireEvents)
                if (evt is ProjectileFiredEvent) fireCount++;
            Assert.Equal(3, fireCount);
        }
    }
}
