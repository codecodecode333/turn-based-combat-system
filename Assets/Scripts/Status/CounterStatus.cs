public sealed class CounterStatus : StatusEffect
{
    public CounterStatus(int duration, Unit source)
        : base(StatusId.Counter, 1, duration, source)
    {
    }

    public override bool HasCounter => true;

    public override void OnTurnStart(Unit target)
    {
        TickDuration();
    }
}