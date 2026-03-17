using UnityEngine;

public enum SkillEffectCategory
{
    Damage,
    Heal,
    StatusApply,
    ForcedMovement,
    Utility
}

public abstract class SkillEffect : ScriptableObject
{
    public virtual SkillEffectCategory Category => SkillEffectCategory.Utility;

    // Counter / AI / UX 용
    public virtual bool IsOffensive => false;
    public virtual bool IsHelpful => false;

    // 낮을수록 먼저 적용
    public virtual int OrderPriority => 100;

    public abstract void Apply(Unit attacker, Unit defender);
    public virtual float EvaluateAIValue(Unit attacker, Unit target, AIProfile profile)
    {
        return 0f;
    }
}

public abstract class ApplyStatusEffectBase : SkillEffect
{
    public override SkillEffectCategory Category => SkillEffectCategory.StatusApply;
    public override int OrderPriority => 20;

    public abstract StatusId StatusId { get; }
    public abstract int Power { get; }
    public abstract int DurationTurns { get; }
}

public abstract class ApplyDebuffStatusEffectBase : ApplyStatusEffectBase
{
    public override bool IsOffensive => true;
    public override bool IsHelpful => false;
}

public abstract class ApplyBuffStatusEffectBase : ApplyStatusEffectBase
{
    public override bool IsOffensive => false;
    public override bool IsHelpful => true;
}