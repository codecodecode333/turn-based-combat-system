public sealed class StunStatus : StatusEffect
{
    public StunStatus(int duration, Unit source)
        : base(StatusId.Stun, 1, duration, source)
    {
    }

    public override bool BlocksAction => true;
    public override bool BlocksMove => true;

    public override void OnTurnStart(Unit target)
    {
        TickDuration();
    }
}