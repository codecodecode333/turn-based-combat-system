using UnityEngine;

[CreateAssetMenu(menuName = "Battle/SkillEffect/DealDamage")]
public class DealDamageEffect : SkillEffect
{
    public int damage;

    public override void Apply(Unit attacker, Unit defender)
    {
        defender.TakeDamage(damage, attacker.transform.position);
    }
}
