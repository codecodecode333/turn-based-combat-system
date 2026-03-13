using UnityEngine;

[CreateAssetMenu(menuName = "Skills/Effects/Counter Apply")]
public class CounterApplyEffect : SkillEffect
{
    public int durationTurns = 1;

    public override void Apply(Unit attacker, Unit defender)
    {
        if (defender == null || defender.IsDead)
            return;

        defender.AddOrRefreshStatus(new CounterStatus(durationTurns, attacker));
    }
}