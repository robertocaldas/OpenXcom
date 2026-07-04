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

    public sealed class ProjectileFiredEvent : BattleEvent
    {
        public BattleUnit Attacker { get; }
        public BattleUnit Defender { get; }
        public bool Hit { get; }

        public ProjectileFiredEvent(BattleUnit attacker, BattleUnit defender, bool hit)
        {
            Attacker = attacker;
            Defender = defender;
            Hit = hit;
        }
    }

    public sealed class UnitHitEvent : BattleEvent
    {
        public BattleUnit Unit { get; }
        public int Damage { get; }
        public UnitSide Side { get; }

        public UnitHitEvent(BattleUnit unit, int damage, UnitSide side)
        {
            Unit = unit;
            Damage = damage;
            Side = side;
        }
    }

    public sealed class UnitDiedEvent : BattleEvent
    {
        public BattleUnit Unit { get; }

        public UnitDiedEvent(BattleUnit unit)
        {
            Unit = unit;
        }
    }

    public enum BattleOutcome
    {
        PlayerVictory,
        HostileVictory,
        Draw,
    }

    public sealed class BattleOverEvent : BattleEvent
    {
        public BattleOutcome Outcome { get; }

        public BattleOverEvent(BattleOutcome outcome)
        {
            Outcome = outcome;
        }
    }
}
