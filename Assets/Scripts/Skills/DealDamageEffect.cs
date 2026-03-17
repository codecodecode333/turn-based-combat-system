using UnityEngine;

[CreateAssetMenu(menuName = "Skills/Effects/Deal Damage")]
public class DealDamageEffect : SkillEffect
{
    public int damage = 5;

    public override SkillEffectCategory Category => SkillEffectCategory.Damage;
    public override bool IsOffensive => true;
    public override bool IsHelpful => false;
    public override int OrderPriority => 10;

    public override void Apply(Unit attacker, Unit defender)
    {
        if (defender == null || defender.IsDead) return;
        defender.TakeDamage(damage, attacker != null ? attacker.transform.position : defender.transform.position);
    }
}