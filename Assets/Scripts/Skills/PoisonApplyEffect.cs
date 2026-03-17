using UnityEngine;

[CreateAssetMenu(menuName = "Skills/Effects/Poison Apply")]
public class PoisonApplyEffect : ApplyDebuffStatusEffectBase
{
    public int damagePerTurn = 2;
    public int durationTurns = 2;

    public override StatusId StatusId => StatusId.Poison;
    public override int Power => damagePerTurn;
    public override int DurationTurns => durationTurns;

    public override void Apply(Unit attacker, Unit defender)
    {
        if (defender == null || defender.IsDead) return;
        defender.AddOrRefreshStatus(new PoisonStatus(damagePerTurn, durationTurns, attacker));
    }
}