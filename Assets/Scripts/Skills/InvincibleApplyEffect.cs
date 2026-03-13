using UnityEngine;

[CreateAssetMenu(menuName = "Skills/Effects/Invincible Apply")]
public class InvincibleApplyEffect : SkillEffect
{
    public int durationTurns = 1;

    public override void Apply(Unit attacker, Unit defender)
    {
        if (defender == null || defender.IsDead)
            return;

        defender.AddOrRefreshStatus(new InvincibleStatus(durationTurns, attacker));
    }
}