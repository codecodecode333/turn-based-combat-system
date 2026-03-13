using UnityEngine;

public sealed class SlowStatus : StatusEffect
{
    public SlowStatus(int movePenalty, int duration, Unit source)
        : base(StatusId.Slow, movePenalty, duration, source)
    {
    }

    public override int MoveRangeDelta => -power;

    public override void OnTurnStart(Unit target)
    {
        TickDuration();
    }
}