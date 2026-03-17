using UnityEngine;

[CreateAssetMenu(menuName = "Skills/Effects/Freeze Apply")]
public class FreezeApplyEffect : ApplyDebuffStatusEffectBase
{
    public int durationTurns = 1;

    public override StatusId StatusId => StatusId.Freeze;
    public override int Power => 1;
    public override int DurationTurns => durationTurns;

    public override void Apply(Unit attacker, Unit defender)
    {
        if (defender == null || defender.IsDead) return;
        defender.AddOrRefreshStatus(new FreezeStatus(durationTurns, attacker));
    }
}