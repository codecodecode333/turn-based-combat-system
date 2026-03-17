using System.Collections.Generic;
using UnityEngine;

public static class SkillEffectFormatter
{
    public static string BuildSkillSummary(SkillData skill)
    {
        if (skill == null)
            return "-";

        var parts = new List<string>();

        parts.Add($"AP {skill.costAP}");
        parts.Add($"R {skill.minRange}-{skill.maxRange}");

        if (skill.targetMode == SkillTargetMode.ClickTileAOE)
            parts.Add($"AOE {Mathf.Max(0, skill.aoeRadius)}");

        if (skill.effects != null)
        {
            for (int i = 0; i < skill.effects.Length; i++)
            {
                var e = skill.effects[i];
                if (e == null) continue;

                string text = FormatEffectShort(e);
                if (!string.IsNullOrEmpty(text))
                    parts.Add(text);
            }
        }

        return string.Join(" | ", parts);
    }

    public static string FormatEffectShort(SkillEffect e)
    {
        if (e == null) return "";

        if (e is DealDamageEffect dd)
            return $"DMG {dd.damage}";

        if (e is HealEffect he)
            return $"HEAL {he.healAmount}";

        if (e is ApplyStatusEffectBase se)
        {
            switch (se.StatusId)
            {
                case StatusId.Burn: return $"BURN {se.Power}x{se.DurationTurns}T";
                case StatusId.Poison: return $"POISON {se.Power}x{se.DurationTurns}T";
                case StatusId.Stun: return $"STUN {se.DurationTurns}T";
                case StatusId.Freeze: return $"FREEZE {se.DurationTurns}T";
                case StatusId.Slow: return $"SLOW {se.Power}/{se.DurationTurns}T";
                case StatusId.Counter: return $"COUNTER {se.DurationTurns}T";
                case StatusId.Invincible: return $"INVINC {se.DurationTurns}T";
                case StatusId.Shield: return $"SHIELD {se.Power}/{se.DurationTurns}T";
            }
        }

        return e.Category.ToString().ToUpperInvariant();
    }
}