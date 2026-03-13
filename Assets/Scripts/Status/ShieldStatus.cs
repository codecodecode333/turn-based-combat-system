using UnityEngine;

public sealed class ShieldStatus : StatusEffect
{
    public ShieldStatus(int shieldAmount, int duration, Unit source)
        : base(StatusId.Shield, shieldAmount, duration, source)
    {
    }

    public override int ShieldAmount => power;

    public override void OnTurnStart(Unit target)
    {
        TickDuration();
    }

    public int Absorb(int incomingDamage)
    {
        if (incomingDamage <= 0 || power <= 0)
            return incomingDamage;

        int absorbed = Mathf.Min(power, incomingDamage);
        power -= absorbed;

        int remain = incomingDamage - absorbed;
        return Mathf.Max(0, remain);
    }

    public override void MergeFrom(StatusEffect incoming)
    {
        if (incoming == null || incoming.Id != Id)
            return;

        if (incoming.power > power)
            power = incoming.power;

        remainingTurns = Mathf.Max(remainingTurns, incoming.remainingTurns);
    }

    public override string GetDisplayText()
    {
        return $"{Id} {power} ({remainingTurns})";
    }
}