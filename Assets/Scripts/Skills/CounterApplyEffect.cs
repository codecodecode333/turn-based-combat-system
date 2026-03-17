using UnityEngine;

[CreateAssetMenu(menuName = "Skills/Effects/Counter Apply")]
public class CounterApplyEffect : ApplyBuffStatusEffectBase
{
    public int durationTurns = 1;

    public override StatusId StatusId => StatusId.Counter;
    public override int Power => 1;
    public override int DurationTurns => durationTurns;

    public override void Apply(Unit attacker, Unit defender)
    {
        if (defender == null || defender.IsDead) return;
        defender.AddOrRefreshStatus(new CounterStatus(durationTurns, attacker));
    }
}