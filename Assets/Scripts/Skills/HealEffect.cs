using UnityEngine;

[CreateAssetMenu(menuName = "Skills/Effects/Heal")]
public class HealEffect : SkillEffect
{
    public int healAmount = 5;

    public override SkillEffectCategory Category => SkillEffectCategory.Heal;
    public override bool IsOffensive => false;
    public override bool IsHelpful => true;
    public override int OrderPriority => 10;

    public override void Apply(Unit attacker, Unit defender)
    {
        if (defender == null || defender.IsDead) return;
        defender.Heal(healAmount);
    }
}