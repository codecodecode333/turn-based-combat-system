using UnityEngine;

[CreateAssetMenu(menuName = "Skills/Effects/Shield Apply")]
public class ShieldApplyEffect : SkillEffect
{
    public int shieldAmount = 5;
    public int durationTurns = 2;

    public override void Apply(Unit attacker, Unit defender)
    {
        if (defender == null || defender.IsDead)
            return;

        defender.AddOrRefreshStatus(new ShieldStatus(shieldAmount, durationTurns, attacker));
    }
}