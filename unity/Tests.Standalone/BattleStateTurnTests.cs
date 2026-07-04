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

        [Fact]
        public void EndPlayerTurn_NoAiAction_ReturnsToPlayerTurn()
        {
            var grid = new TileGrid(50, 50, 1);
            var state = new BattleState(grid);
            state.Units.Add(new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(0, 0, 0) });
            state.Units.Add(new BattleUnit(RuleUnit.Sectoid, Faction.Hostile) { Position = new Position(49, 49, 0) }); // far apart, mutually invisible

            state.EndPlayerTurn();

            Assert.Equal(Faction.Player, state.CurrentTurn);
            var events = state.DequeueEvents();
            Assert.Equal(2, events.Count); // TurnChanged(Hostile), TurnChanged(Player) - no combat occurred
            var first = Assert.IsType<TurnChangedEvent>(events[0]);
            Assert.Equal(Faction.Hostile, first.Faction);
            var second = Assert.IsType<TurnChangedEvent>(events[1]);
            Assert.Equal(Faction.Player, second.Faction);
        }

        [Fact]
        public void EndPlayerTurn_WhenBattleEndsAfterFirstSwitch_StopsAtHostileTurnWithOnlyOneBattleOverEvent()
        {
            var grid = new TileGrid(10, 10, 1);
            var state = new BattleState(grid);
            var deadPlayer = new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(0, 0, 0) };
            deadPlayer.Health = 0; // already dead going into this turn-end - deterministic, no RNG needed
            var hostile = new BattleUnit(RuleUnit.Sectoid, Faction.Hostile) { Position = new Position(5, 5, 0) };
            state.Units.Add(deadPlayer);
            state.Units.Add(hostile);

            state.EndPlayerTurn();

            Assert.Equal(Faction.Hostile, state.CurrentTurn); // never switched back - AI turn and 2nd EndTurn were skipped
            var events = state.DequeueEvents();
            Assert.Equal(2, events.Count); // TurnChanged(Hostile) + BattleOverEvent only
            Assert.IsType<TurnChangedEvent>(events[0]);
            var over = Assert.IsType<BattleOverEvent>(events[1]);
            Assert.Equal(BattleOutcome.HostileVictory, over.Outcome);
        }
    }
}
