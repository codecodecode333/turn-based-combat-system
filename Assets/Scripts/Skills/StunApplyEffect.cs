using UnityEngine;

[CreateAssetMenu(menuName = "Skills/Effects/Stun Apply")]
public class StunApplyEffect : SkillEffect
{
    public int durationTurns = 1;

    public override void Apply(Unit attacker, Unit defender)
    {
        if (defender == null || defender.IsDead)
            return;

        defender.AddOrRefreshStatus(new StunStatus(durationTurns, attacker));
    }
}