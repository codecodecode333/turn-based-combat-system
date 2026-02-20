using UnityEngine;

[CreateAssetMenu(menuName = "Battle/SkillEffect/BurnApply", fileName = "BurnApply_")]
public class BurnApplyEffect : SkillEffect
{
    [Min(0)] public int damagePerTurn = 3;
    [Min(1)] public int durationTurns = 3;

    public override void Apply(Unit attacker, Unit target)
    {
        if (target == null || target.IsDead) return;

        var burn = new BurnStatus
        {
            damagePerTurn = damagePerTurn,
            remainingTurns = durationTurns
        };

        target.AddOrRefreshStatus(burn);
    }
}
