using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public class StatusTestDriver : MonoBehaviour
{
    [Header("References")]
    public Unit target;
    public Unit source;

    [Header("Auto restore between tests")]
    public bool restoreHPBeforeEachTest = true;
    public bool clearStatusesBeforeEachTest = true;

    private int cachedTargetHP = -1;

    void Awake()
    {
        if (target != null)
            cachedTargetHP = Mathf.Max(1, target.currentHP > 0 ? target.currentHP : target.maxHP);
    }

    [ContextMenu("00. Dump Target Statuses")]
    public void DumpTargetStatuses()
    {
        if (!ValidateTarget()) return;

        var list = GetStatusList(target);
        Debug.Log($"[StatusTest] Status count = {list.Count}");

        for (int i = 0; i < list.Count; i++)
        {
            var s = list[i];
            if (s == null)
            {
                Debug.Log($"[StatusTest] [{i}] <null>");
                continue;
            }

            Debug.Log($"[StatusTest] [{i}] id={s.Id}, power={s.power}, turns={s.remainingTurns}");
        }
    }

    [ContextMenu("01. Clear Target Statuses")]
    public void ClearTargetStatuses()
    {
        if (!ValidateTarget()) return;

        ClearStatuses(target);
        Debug.Log("[StatusTest] Cleared all target statuses.");
    }

    [ContextMenu("02. Reset Target HP")]
    public void ResetTargetHP()
    {
        if (!ValidateTarget()) return;

        if (cachedTargetHP < 0)
            cachedTargetHP = target.maxHP;

        target.currentHP = Mathf.Clamp(cachedTargetHP, 0, target.maxHP);
        Debug.Log($"[StatusTest] Target HP reset to {target.currentHP}/{target.maxHP}");
    }

    [ContextMenu("10. Apply Burn(5,2)")]
    public void ApplyBurn5x2()
    {
        if (!ValidateRefs()) return;
        PrepareSingleTest();

        target.AddOrRefreshStatus(new BurnStatus(5, 2, source));
        Debug.Log("[StatusTest] Applied Burn(5,2)");
        DumpTargetStatuses();
    }

    [ContextMenu("11. Burn Tick Test")]
    public void BurnTickTest()
    {
        if (!ValidateRefs()) return;
        PrepareSingleTest();

        target.AddOrRefreshStatus(new BurnStatus(5, 2, source));

        int hp0 = target.currentHP;
        int burnTurns0 = GetRemainingTurns(target, StatusId.Burn);

        target.OnTurnStart();
        int hp1 = target.currentHP;
        int burnTurns1 = GetRemainingTurns(target, StatusId.Burn);

        target.OnTurnStart();
        int hp2 = target.currentHP;
        int burnTurns2 = GetRemainingTurns(target, StatusId.Burn);

        bool okDamage1 = (hp1 == hp0 - 5);
        bool okDamage2 = (hp2 == hp1 - 5);
        bool okRemoved = (burnTurns2 <= 0 && !HasStatus(target, StatusId.Burn));

        Debug.Log(
            "[StatusTest] Burn Tick Test\n" +
            $"- Start HP: {hp0}, BurnTurns: {burnTurns0}\n" +
            $"- After TurnStart #1 -> HP: {hp1}, BurnTurns: {burnTurns1}, PassDamage1: {okDamage1}\n" +
            $"- After TurnStart #2 -> HP: {hp2}, BurnTurns: {burnTurns2}, PassDamage2: {okDamage2}, Removed: {okRemoved}"
        );
    }

    [ContextMenu("12. Burn Merge Test")]
    public void BurnMergeTest()
    {
        if (!ValidateRefs()) return;
        PrepareSingleTest();

        target.AddOrRefreshStatus(new BurnStatus(5, 2, source));
        target.AddOrRefreshStatus(new BurnStatus(3, 5, source));

        var burn = FindStatus(target, StatusId.Burn);

        bool exists = burn != null;
        bool powerOk = exists && burn.power == 5;
        bool turnsOk = exists && burn.remainingTurns == 5;

        Debug.Log(
            "[StatusTest] Burn Merge Test\n" +
            $"- Exists: {exists}\n" +
            $"- Power: {(exists ? burn.power : -1)} (expected 5) => {powerOk}\n" +
            $"- Turns: {(exists ? burn.remainingTurns : -1)} (expected 5) => {turnsOk}"
        );
    }

    [ContextMenu("20. Apply Stun(1)")]
    public void ApplyStun1()
    {
        if (!ValidateRefs()) return;
        PrepareSingleTest();

        target.AddOrRefreshStatus(new StunStatus(1, source));
        bool canAct = EvaluateCanAct(target);

        Debug.Log($"[StatusTest] Applied Stun(1), EvaluateCanAct() = {canAct} (expected false)");
        DumpTargetStatuses();
    }

    [ContextMenu("21. Stun CanAct Test")]
    public void StunCanActTest()
    {
        if (!ValidateRefs()) return;
        PrepareSingleTest();

        target.AddOrRefreshStatus(new StunStatus(1, source));

        bool canAct = EvaluateCanAct(target);
        bool pass = (canAct == false);

        Debug.Log(
            "[StatusTest] Stun CanAct Test\n" +
            $"- EvaluateCanAct(): {canAct}\n" +
            $"- Expected: false\n" +
            $"- Pass: {pass}"
        );
    }

    [ContextMenu("30. Apply Slow(1,2)")]
    public void ApplySlow1x2()
    {
        if (!ValidateRefs()) return;
        PrepareSingleTest();

        target.AddOrRefreshStatus(new SlowStatus(1, 2, source));
        int effective = EvaluateEffectiveMoveRange(target);

        Debug.Log($"[StatusTest] Applied Slow(1,2), EffectiveMoveRange = {effective}, BaseMoveRange = {target.moveRange}");
        DumpTargetStatuses();
    }

    [ContextMenu("31. Slow MoveRange Test")]
    public void SlowMoveRangeTest()
    {
        if (!ValidateRefs()) return;
        PrepareSingleTest();

        int baseRange = target.moveRange;
        target.AddOrRefreshStatus(new SlowStatus(1, 2, source));

        int effective = EvaluateEffectiveMoveRange(target);
        bool pass = (effective == baseRange - 1);

        Debug.Log(
            "[StatusTest] Slow MoveRange Test\n" +
            $"- Base MoveRange: {baseRange}\n" +
            $"- Effective MoveRange: {effective}\n" +
            $"- Expected: {baseRange - 1}\n" +
            $"- Pass: {pass}"
        );
    }

    [ContextMenu("90. Run All Basic Checks")]
    public void RunAllBasicChecks()
    {
        if (!ValidateRefs()) return;

        BurnTickTest();
        BurnMergeTest();
        StunCanActTest();
        SlowMoveRangeTest();
    }

    // =========================
    // Internal helpers
    // =========================

    private bool ValidateTarget()
    {
        if (target != null) return true;

        Debug.LogWarning("[StatusTest] target is null.");
        return false;
    }

    private bool ValidateRefs()
    {
        if (target == null)
        {
            Debug.LogWarning("[StatusTest] target is null.");
            return false;
        }

        if (source == null)
        {
            Debug.LogWarning("[StatusTest] source is null.");
            return false;
        }

        return true;
    }

    private void PrepareSingleTest()
    {
        if (!ValidateTarget()) return;

        if (cachedTargetHP <= 0)
            cachedTargetHP = Mathf.Max(1, target.maxHP);

        if (restoreHPBeforeEachTest)
            target.currentHP = Mathf.Clamp(cachedTargetHP, 1, target.maxHP);

        if (clearStatusesBeforeEachTest)
            ClearStatuses(target);
    }

    private List<StatusEffect> GetStatusList(Unit unit)
    {
        if (unit == null)
            return new List<StatusEffect>();

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        FieldInfo field = typeof(Unit).GetField("statusEffects", flags);

        if (field == null)
        {
            Debug.LogWarning("[StatusTest] Could not find Unit.statusEffects field via reflection.");
            return new List<StatusEffect>();
        }

        if (field.GetValue(unit) is List<StatusEffect> list && list != null)
            return list;

        return new List<StatusEffect>();
    }

    private void ClearStatuses(Unit unit)
    {
        var list = GetStatusList(unit);
        list.Clear();
    }

    private bool HasStatus(Unit unit, StatusId id)
    {
        var list = GetStatusList(unit);

        for (int i = 0; i < list.Count; i++)
        {
            var s = list[i];
            if (s != null && s.Id == id && !s.IsExpired)
                return true;
        }

        return false;
    }

    private StatusEffect FindStatus(Unit unit, StatusId id)
    {
        var list = GetStatusList(unit);

        for (int i = 0; i < list.Count; i++)
        {
            var s = list[i];
            if (s != null && s.Id == id && !s.IsExpired)
                return s;
        }

        return null;
    }

    private int GetRemainingTurns(Unit unit, StatusId id)
    {
        var s = FindStatus(unit, id);
        return s != null ? s.remainingTurns : 0;
    }

    private bool EvaluateCanAct(Unit unit)
    {
        if (unit == null || unit.IsDead)
            return false;

        // Unit.CanAct()가 있으면 그걸 우선 사용
        MethodInfo canActMethod = typeof(Unit).GetMethod(
            "CanAct",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );

        if (canActMethod != null && canActMethod.ReturnType == typeof(bool))
        {
            object result = canActMethod.Invoke(unit, null);
            if (result is bool b)
                return b;
        }

        // 없으면 status 목록으로 fallback 계산
        var list = GetStatusList(unit);
        for (int i = 0; i < list.Count; i++)
        {
            var s = list[i];
            if (s != null && !s.IsExpired && s.BlocksAction)
                return false;
        }

        return true;
    }

    private int EvaluateEffectiveMoveRange(Unit unit)
    {
        if (unit == null || unit.IsDead)
            return 0;

        // Unit.GetEffectiveMoveRange()가 있으면 그걸 우선 사용
        MethodInfo moveRangeMethod = typeof(Unit).GetMethod(
            "GetEffectiveMoveRange",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );

        if (moveRangeMethod != null && moveRangeMethod.ReturnType == typeof(int))
        {
            object result = moveRangeMethod.Invoke(unit, null);
            if (result is int value)
                return value;
        }

        // 없으면 status 목록으로 fallback 계산
        int valueFallback = unit.moveRange;
        bool blocksMove = false;

        var list = GetStatusList(unit);
        for (int i = 0; i < list.Count; i++)
        {
            var s = list[i];
            if (s == null || s.IsExpired)
                continue;

            valueFallback += s.MoveRangeDelta;

            if (s.BlocksMove)
                blocksMove = true;
        }

        if (blocksMove)
            return 0;

        return Mathf.Max(0, valueFallback);
    }
}