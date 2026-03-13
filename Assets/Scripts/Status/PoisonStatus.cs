using UnityEngine;

public sealed class PoisonStatus : StatusEffect
{
    public PoisonStatus(int damagePerTurn, int duration, Unit source)
        : base(StatusId.Poison, damagePerTurn, duration, source)
    {
    }

    public int DamagePerTurn => power;

    public override void OnTurnStart(Unit target)
    {
        if (target == null || target.IsDead)
        {
            TickDuration();
            return;
        }

        if (power > 0)
            target.TakeDamage(power);

        TickDuration();
    }
}