using UnityEngine;

[CreateAssetMenu(menuName = "Skills/Effects/Invincible Apply")]
public class InvincibleApplyEffect : ApplyBuffStatusEffectBase
{
    public int durationTurns = 1;

    public override StatusId StatusId => StatusId.Invincible;
    public override int Power => 1;
    public override int DurationTurns => durationTurns;

    public override void Apply(Unit attacker, Unit defender)
    {
        if (defender == null || defender.IsDead) return;
        defender.AddOrRefreshStatus(new InvincibleStatus(durationTurns, attacker));
    }
}