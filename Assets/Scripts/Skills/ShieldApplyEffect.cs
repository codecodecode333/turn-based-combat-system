using UnityEngine;

[CreateAssetMenu(menuName = "Skills/Effects/Shield Apply")]
public class ShieldApplyEffect : ApplyBuffStatusEffectBase
{
    public int shieldAmount = 5;
    public int durationTurns = 2;

    public override StatusId StatusId => StatusId.Shield;
    public override int Power => shieldAmount;
    public override int DurationTurns => durationTurns;

    public override void Apply(Unit attacker, Unit defender)
    {
        if (defender == null || defender.IsDead) return;
        defender.AddOrRefreshStatus(new ShieldStatus(shieldAmount, durationTurns, attacker));
    }
}