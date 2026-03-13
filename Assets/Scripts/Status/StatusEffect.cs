using UnityEngine;

public enum StatusId
{
    Burn,
    Poison,
    Stun,
    Freeze,
    Slow,
    Counter,
    Invincible,
    Shield
}

public abstract class StatusEffect
{
    public StatusId Id { get; protected set; }
    public Unit Source { get; protected set; }

    // 문서 기준 공통 정보
    public int power;
    public int remainingTurns;

    public bool IsExpired => remainingTurns <= 0;

    // ===== 공통 질의값 =====
    public virtual bool BlocksAction => false;
    public virtual bool BlocksMove => false;
    public virtual int MoveRangeDelta => 0; // 음수면 감속
    public virtual bool IsInvincible => false;
    public virtual bool HasCounter => false;
    public virtual int ShieldAmount => 0;

    protected StatusEffect(StatusId id, int power, int duration, Unit source)
    {
        Id = id;
        this.power = Mathf.Max(0, power);
        remainingTurns = Mathf.Max(0, duration);
        Source = source;
    }

    // 적용 직후 1회
    public virtual void OnApply(Unit target) { }

    // 턴 시작 시 tick
    public virtual void OnTurnStart(Unit target)
    {
        TickDuration();
    }

    // 턴 종료 훅
    public virtual void OnTurnEnd(Unit target) { }

    // 제거 직전 1회
    public virtual void OnRemove(Unit target) { }

    protected void TickDuration()
    {
        remainingTurns--;
    }

    // ===== StrongestOnly + DurationMax 공통 merge =====
    public virtual void MergeFrom(StatusEffect incoming)
    {
        if (incoming == null || incoming.Id != Id)
            return;

        if (incoming.power > power)
            power = incoming.power;

        remainingTurns = Mathf.Max(remainingTurns, incoming.remainingTurns);
    }

    // UI/HUD용 최소 정보
    public virtual string GetDisplayText()
    {
        return $"{Id} ({remainingTurns})";
    }
}