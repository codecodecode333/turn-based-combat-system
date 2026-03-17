using UnityEngine;

[CreateAssetMenu(menuName = "Skills/Effects/Burn Apply")]
public class BurnApplyEffect : ApplyDebuffStatusEffectBase
{
    public int damagePerTurn = 2;
    public int durationTurns = 2;

    public override StatusId StatusId => StatusId.Burn;
    public override int Power => damagePerTurn;
    public override int DurationTurns => durationTurns;

    public override void Apply(Unit attacker, Unit defender)
    {
        if (defender == null || defender.IsDead) return;
        defender.AddOrRefreshStatus(new BurnStatus(damagePerTurn, durationTurns, attacker));
    }
}