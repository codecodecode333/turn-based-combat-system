using UnityEngine;

[CreateAssetMenu(menuName = "Skills/Effects/Stun Apply")]
public class StunApplyEffect : ApplyDebuffStatusEffectBase
{
    public int durationTurns = 1;

    public override StatusId StatusId => StatusId.Stun;
    public override int Power => 1;
    public override int DurationTurns => durationTurns;

    public override void Apply(Unit attacker, Unit defender)
    {
        if (defender == null || defender.IsDead) return;
        defender.AddOrRefreshStatus(new StunStatus(durationTurns, attacker));
    }
}