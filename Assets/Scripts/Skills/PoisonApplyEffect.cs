using UnityEngine;

[CreateAssetMenu(menuName = "Skills/Effects/Poison Apply")]
public class PoisonApplyEffect : SkillEffect
{
    public int damagePerTurn = 2;
    public int durationTurns = 2;

    public override void Apply(Unit attacker, Unit defender)
    {
        if (defender == null || defender.IsDead)
            return;

        defender.AddOrRefreshStatus(new PoisonStatus(damagePerTurn, durationTurns, attacker));
    }
}