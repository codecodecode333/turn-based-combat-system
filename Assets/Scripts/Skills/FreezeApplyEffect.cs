using UnityEngine;

[CreateAssetMenu(menuName = "Skills/Effects/Freeze Apply")]
public class FreezeApplyEffect : SkillEffect
{
    public int durationTurns = 1;

    public override void Apply(Unit attacker, Unit defender)
    {
        if (defender == null || defender.IsDead)
            return;

        defender.AddOrRefreshStatus(new FreezeStatus(durationTurns, attacker));
    }
}