using OpenXcom.Core.Rules;

namespace OpenXcom.Core.Battle
{
    /// <summary>
    /// A concrete item instance carried by a unit (mutable: ammo, etc.).
    /// References its immutable <see cref="RuleItem"/> template.
    /// Slice 1 ignores ammo/clips — weapons fire freely.
    /// </summary>
    public sealed class BattleItem
    {
        public RuleItem Rules { get; }

        public BattleItem(RuleItem rules)
        {
            Rules = rules;
        }
    }
}
