using UnityEngine;

[CreateAssetMenu(menuName = "Skills/Effects/Slow Apply")]
public class SlowApplyEffect : SkillEffect
{
    public int movePenalty = 1;
    public int durationTurns = 2;

    public override void Apply(Unit attacker, Unit defender)
    {
        if (defender == null || defender.IsDead)
            return;

        defender.AddOrRefreshStatus(new SlowStatus(movePenalty, durationTurns, attacker));
    }
}