using OpenXcom.Core.Battle;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;
using Xunit;

namespace OpenXcom.Core.Tests
{
    public class BattleStateTurnTests
    {
        [Fact]
        public void EndTurn_SwitchesPlayerToHostile()
        {
            var grid = new TileGrid(5, 5, 1);
            var state = new BattleState(grid);
            Assert.Equal(Faction.Player, state.CurrentTurn);

            state.EndTurn();

            Assert.Equal(Faction.Hostile, state.CurrentTurn);
        }

        [Fact]
        public void EndTurn_OnlyRefreshesUnitsOfTheNewCurrentFaction()
        {
            var grid = new TileGrid(5, 5, 1);
            var state = new BattleState(grid);
            var player = new BattleUnit(RuleUnit.Soldier, Faction.Player);
            var hostile = new BattleUnit(RuleUnit.Sectoid, Faction.Hostile);
            player.TimeUnits = 1;
            hostile.TimeUnits = 1;
            state.Units.Add(player);
            state.Units.Add(hostile);

            state.EndTurn(); // Player -> Hostile

            Assert.Equal(1, player.TimeUnits); // untouched - not the new current faction
            Assert.Equal(hostile.Stats.TimeUnits, hostile.TimeUnits); // refreshed to max
        }

        [Fact]
        public void EndTurn_EnqueuesExactlyOneTurnChangedEvent()
        {
            var grid = new TileGrid(3, 3, 1);
            var state = new BattleState(grid);

            state.EndTurn();

            var events = state.DequeueEvents();
            Assert.Single(events);
            var turnChanged = Assert.IsType<TurnChangedEvent>(events[0]);
            Assert.Equal(Faction.Hostile, turnChanged.Faction);
        }

        [Fact]
        public void EndTurn_WhenHostileSideIsAlreadyWiped_EnqueuesPlayerVictoryBattleOverEvent()
        {
            var grid = new TileGrid(3, 3, 1);
            var state = new BattleState(grid);
            var player = new BattleUnit(RuleUnit.Soldier, Faction.Player);
            var deadHostile = new BattleUnit(RuleUnit.Sectoid, Faction.Hostile);
            deadHostile.Health = 0;
            state.Units.Add(player);
            state.Units.Add(deadHostile);

            state.EndTurn(); // Player -> Hostile; Hostile side is already fully wiped

            var events = state.DequeueEvents();
            Assert.Equal(2, events.Count); // TurnChanged + BattleOver
            Assert.IsType<TurnChangedEvent>(events[0]);
            var over = Assert.IsType<BattleOverEvent>(events[1]);
            Assert.Equal(BattleOutcome.PlayerVictory, over.Outcome);
        }

        [Fact]
        public void EndTurn_WhenNeitherSideWiped_NoBattleOverEvent()
        {
            var grid = new TileGrid(3, 3, 1);
            var state = new BattleState(grid);
            state.Units.Add(new BattleUnit(RuleUnit.Soldier, Faction.Player));
            state.Units.Add(new BattleUnit(RuleUnit.Sectoid, Faction.Hostile));

            state.EndTurn();

            var events = state.DequeueEvents();
            Assert.Single(events); // only TurnChanged
            Assert.IsType<TurnChangedEvent>(events[0]);
        }
    }
}
