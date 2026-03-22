using System.Collections.Generic;
using UnityEngine;

public static class CombatTargetResolver
{
    public static List<Unit> ResolveTargets(
        SkillData skill,
        Unit caster,
        List<Unit> allies,
        List<Unit> enemies,
        GridManager grid,
        Vector2Int? clickedTile = null,
        Unit clickedUnit = null)
    {
        if (caster == null)
            return new List<Unit>();

        return ResolveTargetsFromPosition(
            skill,
            caster,
            caster.GridPos,
            allies,
            enemies,
            grid,
            clickedTile,
            clickedUnit
        );
    }

    public static List<Unit> ResolveTargetsFromPosition(
        SkillData skill,
        Unit caster,
        Vector2Int casterPos,
        List<Unit> allies,
        List<Unit> enemies,
        GridManager grid,
        Vector2Int? clickedTile = null,
        Unit clickedUnit = null)
    {
        var targets = new List<Unit>();
        if (skill == null || caster == null)
            return targets;

        if (allies == null) allies = new List<Unit>();
        if (enemies == null) enemies = new List<Unit>();

        int Dist(Vector2Int a, Vector2Int b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        }

        bool IsAlive(Unit u)
        {
            return u != null && !u.IsDead;
        }

        bool InCastRange(Vector2Int from, Vector2Int to)
        {
            int d = Dist(from, to);
            return d >= skill.minRange && d <= skill.maxRange;
        }

        bool HasLOS(Vector2Int from, Vector2Int to)
        {
            if (!skill.requiresLineOfSight)
                return true;

            if (grid == null)
                return false;

            return grid.HasLineOfSight(from, to);
        }

        bool CanCastPoint(Vector2Int from, Vector2Int to)
        {
            return InCastRange(from, to) && HasLOS(from, to);
        }

        IEnumerable<Unit> AliveUnits(IEnumerable<Unit> list)
        {
            if (list == null) yield break;

            foreach (var u in list)
            {
                if (IsAlive(u))
                    yield return u;
            }
        }

        void AddUnique(Unit u)
        {
            if (!IsAlive(u)) return;
            if (!targets.Contains(u))
                targets.Add(u);
        }

        Unit FindNearestValid(IEnumerable<Unit> list)
        {
            Unit best = null;
            int bestDist = int.MaxValue;

            foreach (var u in AliveUnits(list))
            {
                if (!CanCastPoint(casterPos, u.GridPos))
                    continue;

                int d = Dist(casterPos, u.GridPos);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = u;
                }
            }

            return best;
        }

        void AddUnitsInAOE(Vector2Int center)
        {
            int r = Mathf.Max(0, skill.aoeRadius);

            foreach (var u in AliveUnits(allies))
            {
                Vector2Int pos = (u == caster) ? casterPos : u.GridPos;
                if (Dist(center, pos) <= r)
                    AddUnique(u);
            }

            foreach (var u in AliveUnits(enemies))
            {
                Vector2Int pos = (u == caster) ? casterPos : u.GridPos;
                if (Dist(center, pos) <= r)
                    AddUnique(u);
            }
        }

        switch (skill.targetMode)
        {
            case SkillTargetMode.AutoNearestSingle:
            {
                var target = FindNearestValid(enemies);
                if (target != null)
                    AddUnique(target);
                break;
            }

            case SkillTargetMode.ClickSingle:
            {
                if (clickedUnit != null && IsAlive(clickedUnit))
                {
                    bool canClickEnemy = enemies.Contains(clickedUnit);
                    bool canClickAlly = allies.Contains(clickedUnit) && SkillMetaUtility.IsMostlyHelpfulSkill(skill);

                    if ((canClickEnemy || canClickAlly) &&
                        CanCastPoint(casterPos, clickedUnit.GridPos))
                    {
                        AddUnique(clickedUnit);
                    }
                }
                break;
            }

            case SkillTargetMode.ClickTileAOE:
            {
                if (!clickedTile.HasValue)
                    break;

                Vector2Int center = clickedTile.Value;
                if (!CanCastPoint(casterPos, center))
                    break;

                AddUnitsInAOE(center);
                break;
            }

            case SkillTargetMode.AllEnemiesInRange:
            {
                foreach (var e in AliveUnits(enemies))
                {
                    if (CanCastPoint(casterPos, e.GridPos))
                        AddUnique(e);
                }
                break;
            }

            case SkillTargetMode.AllEnemiesAnywhere:
            {
                foreach (var e in AliveUnits(enemies))
                {
                    if (HasLOS(casterPos, e.GridPos))
                        AddUnique(e);
                }
                break;
            }

            case SkillTargetMode.AllAlliesInRange:
            {
                foreach (var a in AliveUnits(allies))
                {
                    if (CanCastPoint(casterPos, a.GridPos))
                        AddUnique(a);
                }
                break;
            }

            case SkillTargetMode.AllAlliesAnywhere:
            {
                foreach (var a in AliveUnits(allies))
                {
                    if (HasLOS(casterPos, a.GridPos))
                        AddUnique(a);
                }
                break;
            }
        }

        return targets;
    }

    public static bool HasLOSForSkill(
        SkillData skill,
        Vector2Int from,
        Vector2Int to,
        GridManager grid)
    {
        if (skill == null) return false;
        if (!skill.requiresLineOfSight) return true;
        if (grid == null) return false;

        return grid.HasLineOfSight(from, to);
    }

    public static bool IsPointCastable(
        SkillData skill,
        Vector2Int from,
        Vector2Int to,
        GridManager grid)
    {
        if (skill == null) return false;

        int d = Mathf.Abs(from.x - to.x) + Mathf.Abs(from.y - to.y);
        if (d < skill.minRange || d > skill.maxRange)
            return false;

        return HasLOSForSkill(skill, from, to, grid);
    }
}