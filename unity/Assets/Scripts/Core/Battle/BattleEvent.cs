using System.Collections.Generic;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;

namespace OpenXcom.Core.Battle
{
    /// <summary>
    /// Base type for ordered state-change notifications systems push while
    /// mutating BattleState immediately; Unity drains and animates from
    /// these independently of game-logic timing (parent design spec §4).
    /// </summary>
    public abstract class BattleEvent
    {
    }

    public sealed class UnitMovedEvent : BattleEvent
    {
        public BattleUnit Unit { get; }
        public IReadOnlyList<Position> Path { get; }

        public UnitMovedEvent(BattleUnit unit, IReadOnlyList<Position> path)
        {
            Unit = unit;
            Path = path;
        }
    }

    public sealed class TurnChangedEvent : BattleEvent
    {
        public Faction Faction { get; }

        public TurnChangedEvent(Faction faction)
        {
            Faction = faction;
        }
    }
}
