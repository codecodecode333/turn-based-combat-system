public sealed class FreezeStatus : StatusEffect
{
    public FreezeStatus(int duration, Unit source)
        : base(StatusId.Freeze, 1, duration, source)
    {
    }

    public override bool BlocksAction => true;
    public override bool BlocksMove => true;

    public override void OnTurnStart(Unit target)
    {
        TickDuration();
    }
}