using UnityEngine;

public static class SkillMetaUtility
{
    public static bool ContainsOffensiveEffect(SkillData skill)
    {
        if (skill == null || skill.effects == null)
            return false;

        for (int i = 0; i < skill.effects.Length; i++)
        {
            var e = skill.effects[i];
            if (e == null) continue;
            if (e.IsOffensive) return true;
        }

        return false;
    }

    public static bool ContainsHelpfulEffect(SkillData skill)
    {
        if (skill == null || skill.effects == null)
            return false;

        for (int i = 0; i < skill.effects.Length; i++)
        {
            var e = skill.effects[i];
            if (e == null) continue;
            if (e.IsHelpful) return true;
        }

        return false;
    }

    public static bool IsMostlyHelpfulSkill(SkillData skill)
    {
        if (skill == null || skill.effects == null)
            return false;

        float helpful = 0f;
        float harmful = 0f;

        foreach (var e in skill.effects)
        {
            if (e == null) continue;

            if (e.IsHelpful) helpful += EstimateHelpfulWeight(e);
            if (e.IsOffensive) harmful += EstimateHarmfulWeight(e);
        }

        return helpful > harmful;
    }

    static float EstimateHelpfulWeight(SkillEffect e)
    {
        if (e is HealEffect he) return he.healAmount;
        if (e is ShieldApplyEffect sha) return sha.shieldAmount;
        if (e is ApplyStatusEffectBase se)
        {
            switch (se.StatusId)
            {
                case StatusId.Invincible: return 8f * Mathf.Max(1, se.DurationTurns);
                case StatusId.Counter: return 4f * Mathf.Max(1, se.DurationTurns);
                case StatusId.Shield: return Mathf.Max(1, se.Power) * 0.85f * Mathf.Max(1, se.DurationTurns);
            }
        }

        return 2f;
    }

    static float EstimateHarmfulWeight(SkillEffect e)
    {
        if (e is DealDamageEffect dd) return dd.damage;
        if (e is ApplyStatusEffectBase se)
        {
            switch (se.StatusId)
            {
                case StatusId.Burn: return se.Power * se.DurationTurns;
                case StatusId.Poison: return se.Power * se.DurationTurns;
                case StatusId.Stun: return 8f * Mathf.Max(1, se.DurationTurns);
                case StatusId.Freeze: return 10f * Mathf.Max(1, se.DurationTurns);
                case StatusId.Slow: return Mathf.Max(1, se.Power) * Mathf.Max(1, se.DurationTurns) * 2f;
            }
        }

        return 2f;
    }
}