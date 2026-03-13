public sealed class InvincibleStatus : StatusEffect
{
    public InvincibleStatus(int duration, Unit source)
        : base(StatusId.Invincible, 1, duration, source)
    {
    }

    public override bool IsInvincible => true;

    public override void OnTurnStart(Unit target)
    {
        TickDuration();
    }
}