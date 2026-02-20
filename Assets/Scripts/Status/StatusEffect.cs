public abstract class StatusEffect
{
    public abstract string Id { get; }
    public int remainingTurns;

    // 턴 시작/종료 훅
    public virtual void OnTurnStart(Unit unit) { }
    public virtual void OnTurnEnd(Unit unit) { }

    public bool IsExpired => remainingTurns <= 0;
}