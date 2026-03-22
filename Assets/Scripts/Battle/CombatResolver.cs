using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class CombatResolver
{
    public sealed class Context
    {
        public GridManager grid;
        public List<Unit> allies;
        public List<Unit> enemies;

        // Counter에 필요한 의존성
        public System.Func<Unit, SkillData[]> getSkillPoolFor;
        public System.Func<SkillData, bool> isOffensiveSkill;
        public System.Func<Unit, Unit, IEnumerator> runCounterAttack;

        public bool isCounterAttackInProgress;
    }

    public struct ResolveResult
    {
        public bool success;
        public bool spentAP;
        public List<Unit> targets;
    }

    public static ResolveResult ResolveForExecution(
        SkillData skill,
        Unit attacker,
        Context ctx,
        Vector2Int? clickedTile,
        Unit clickedUnit,
        bool spendAP)
    {
        var result = new ResolveResult
        {
            success = false,
            spentAP = false,
            targets = new List<Unit>()
        };

        if (skill == null || attacker == null || attacker.IsDead || ctx == null)
            return result;

        if (!attacker.CanAct())
            return result;

        result.targets = CombatTargetResolver.ResolveTargets(
            skill,
            attacker,
            ctx.allies,
            ctx.enemies,
            ctx.grid,
            clickedTile,
            clickedUnit
        );

        bool allowEmptyAOE =
            skill.targetMode == SkillTargetMode.ClickTileAOE &&
            clickedTile.HasValue &&
            CombatTargetResolver.IsPointCastable(
                skill,
                attacker.GridPos,
                clickedTile.Value,
                ctx.grid
            );

        if ((result.targets == null || result.targets.Count == 0) && !allowEmptyAOE)
            return result;

        if (spendAP)
        {
            if (!attacker.SpendAP(skill.costAP))
                return result;

            result.spentAP = true;
        }

        result.success = true;
        return result;
    }

    public static IEnumerator ApplyResolvedEffectsAndCounters(
        SkillData skill,
        Unit attacker,
        ResolveResult resolved,
        Context ctx)
    {
        if (!resolved.success || resolved.targets == null)
            yield break;

        for (int i = 0; i < resolved.targets.Count; i++)
        {
            var defender = resolved.targets[i];
            if (defender == null || defender.IsDead)
                continue;

            ApplySkillEffects(skill, attacker, defender);

            if (CanCounter(skill, resolved.targets, defender, ctx))
            {
                if (ctx.runCounterAttack != null)
                    yield return ctx.runCounterAttack(defender, attacker);
            }
        }
    }

    public static void ApplyResolvedEffectsNoCounter(
        SkillData skill,
        Unit attacker,
        ResolveResult resolved)
    {
        if (!resolved.success || resolved.targets == null)
            return;

        for (int i = 0; i < resolved.targets.Count; i++)
        {
            var defender = resolved.targets[i];
            if (defender == null || defender.IsDead)
                continue;

            ApplySkillEffects(skill, attacker, defender);
        }
    }

    static void ApplySkillEffects(SkillData skill, Unit attacker, Unit defender)
    {
        if (skill == null || skill.effects == null)
            return;

        var ordered = new List<SkillEffect>(skill.effects.Length);

        for (int i = 0; i < skill.effects.Length; i++)
        {
            var effect = skill.effects[i];
            if (effect != null)
                ordered.Add(effect);
        }

        ordered.Sort((a, b) => a.OrderPriority.CompareTo(b.OrderPriority));

        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].Apply(attacker, defender);
        }
    }

    static bool CanCounter(
        SkillData skill,
        List<Unit> resolvedTargets,
        Unit defender,
        Context ctx)
    {
        if (skill == null || defender == null || resolvedTargets == null || ctx == null)
            return false;

        if (ctx.isCounterAttackInProgress)
            return false;

        if (defender.IsDead)
            return false;

        if (!defender.CanAct())
            return false;

        if (!defender.HasCounterReady())
            return false;

        if (ctx.isOffensiveSkill == null || !ctx.isOffensiveSkill(skill))
            return false;

        // AOE 반격 금지
        if (skill.targetMode == SkillTargetMode.ClickTileAOE)
            return false;

        // 단일 타겟만 반격 허용
        if (resolvedTargets.Count != 1)
            return false;

        if (resolvedTargets[0] != defender)
            return false;

        // 실제 반격 스킬 존재 여부까지 확인
        var counterSkill = ChooseCounterSkill(defender, attackerPlaceholder: null, ctx);
        return counterSkill != null;
    }

    // 반격 가능 여부 체크용. 실제 공격자는 runCounterAttack 쪽에서 다시 검사.
    static SkillData ChooseCounterSkill(Unit counterUnit, Unit attackerPlaceholder, Context ctx)
    {
        if (counterUnit == null || counterUnit.IsDead || ctx == null || ctx.getSkillPoolFor == null)
            return null;

        var pool = ctx.getSkillPoolFor(counterUnit);
        if (pool == null || pool.Length == 0)
            return null;

        for (int i = 0; i < pool.Length; i++)
        {
            var skill = pool[i];
            if (skill == null) continue;

            if (skill.targetMode != SkillTargetMode.ClickSingle &&
                skill.targetMode != SkillTargetMode.AutoNearestSingle)
                continue;

            return skill;
        }

        return null;
    }
}