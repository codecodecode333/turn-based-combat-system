public class BurnStatus : StatusEffect
{
    public override string Id => "BURN";
    public int damagePerTurn;

    // “유닛 턴 시작 시” 데미지
    public override void OnTurnStart(Unit unit)
    {
        if (unit == null || unit.IsDead) return;

        unit.TakeDamage(damagePerTurn);
        remainingTurns--;
    }
}