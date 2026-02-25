using UnityEngine;

[CreateAssetMenu(menuName = "Battle/SkillEffect/Heal", fileName = "HealEffect_")]
public class HealEffect : SkillEffect
{
    [Min(0)]
    public int healAmount = 10;

    public override void Apply(Unit attacker, Unit target)
    {
        if (target == null || target.IsDead) return;

        target.Heal(healAmount);
    }
}
