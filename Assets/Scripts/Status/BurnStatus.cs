using UnityEngine;

public sealed class BurnStatus : StatusEffect
{
    public BurnStatus(int damagePerTurn, int duration, Unit source)
        : base(StatusId.Burn, damagePerTurn, duration, source)
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
        {
            target.TakeDamage(power);

            // 👉 Tick FX
            var fx = target.GetComponent<UnitFxPlayer>();
            if (fx != null)
            {
                fx.PlayBurnTickFx();
            }
        }
        TickDuration();
    }
}