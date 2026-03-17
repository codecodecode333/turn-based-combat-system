using System.Collections.Generic;
using UnityEngine;

public static class HazardUtility
{
    public const float HAZARD_BURN_BASE_PENALTY = 5.0f;
    public const float HAZARD_POISON_BASE_PENALTY = 6.5f;
    public const float HAZARD_EXPLOSION_BASE_PENALTY = 10.0f;

    public const float HAZARD_POWER_BONUS = 1.5f;
    public const float HAZARD_EXPLOSION_POWER_MULTIPLIER = 1.5f;

    public const float LOW_HP_HAZARD_MULTIPLIER = 1.2f;
    public const float CRITICAL_HP_HAZARD_MULTIPLIER = 1.5f;

    public static bool HasHazard(TileData td)
    {
        return td != null
            && td.hazardType != HazardType.None
            && td.hazardTrigger != HazardTriggerType.None;
    }

    public static bool HasHazard(GridManager grid, Vector2Int tile)
    {
        if (grid == null)
            return false;

        var td = grid.GetTileData(tile);
        return HasHazard(td);
    }

    public static bool HasHazard(GridManager grid, Vector2Int tile, HazardTriggerType trigger)
    {
        if (grid == null)
            return false;

        var td = grid.GetTileData(tile);
        if (!HasHazard(td))
            return false;

        return td.hazardTrigger == trigger;
    }

    public static float GetSingleTileHazardPenalty(Unit actor, GridManager grid, Vector2Int tile)
    {
        if (actor == null || grid == null)
            return 0f;

        var td = grid.GetTileData(tile);
        if (td == null)
            return 0f;

        if (td.hazardType == HazardType.None)
            return 0f;

        if (td.hazardTrigger != HazardTriggerType.OnEnter)
            return 0f;

        float hpRatio = actor.maxHP > 0 ? (float)actor.currentHP / actor.maxHP : 1f;
        float hpFactor = 1f;

        if (hpRatio <= 0.25f) hpFactor = CRITICAL_HP_HAZARD_MULTIPLIER;
        else if (hpRatio <= 0.5f) hpFactor = LOW_HP_HAZARD_MULTIPLIER;

        float powerBonus = Mathf.Max(0, td.hazardPower - 1) * HAZARD_POWER_BONUS;

        switch (td.hazardType)
        {
            case HazardType.Burn:
                return (HAZARD_BURN_BASE_PENALTY + powerBonus) * hpFactor;

            case HazardType.Poison:
                return (HAZARD_POISON_BASE_PENALTY + powerBonus) * hpFactor;

            case HazardType.Explosion:
                return (HAZARD_EXPLOSION_BASE_PENALTY + (powerBonus * HAZARD_EXPLOSION_POWER_MULTIPLIER)) * hpFactor;

            default:
                return 0f;
        }
    }

    public static float GetPathHazardPenalty(
        Unit actor,
        GridManager grid,
        Vector2Int destination,
        Dictionary<Vector2Int, Vector2Int> cameFrom)
    {
        if (actor == null || grid == null)
            return 0f;

        if (destination == actor.GridPos)
            return 0f;

        var path = grid.ReconstructPath(actor.GridPos, destination, cameFrom);
        if (path == null || path.Count == 0)
            return 0f;

        float sum = 0f;

        foreach (var step in path)
            sum += GetSingleTileHazardPenalty(actor, grid, step);

        return sum;
    }

    public static bool PathContainsLethalExplosion(
        Unit actor,
        GridManager grid,
        Vector2Int destination,
        Dictionary<Vector2Int, Vector2Int> cameFrom)
    {
        if (actor == null || grid == null)
            return false;

        var path = grid.ReconstructPath(actor.GridPos, destination, cameFrom);
        if (path == null || path.Count == 0)
            return false;

        foreach (var step in path)
        {
            var td = grid.GetTileData(step);
            if (td == null) continue;

            if (td.hazardTrigger != HazardTriggerType.OnEnter) continue;
            if (td.hazardType != HazardType.Explosion) continue;

            if (actor.currentHP <= td.hazardPower)
                return true;
        }

        return false;
    }

    public static List<Vector2Int> ExtractHazardPathTiles(
        List<Vector2Int> path,
        GridManager grid,
        HazardTriggerType trigger = HazardTriggerType.OnEnter)
    {
        var result = new List<Vector2Int>();
        if (path == null || grid == null)
            return result;

        for (int i = 0; i < path.Count; i++)
        {
            var step = path[i];
            var td = grid.GetTileData(step);
            if (td == null) continue;

            if (td.hazardType == HazardType.None) continue;
            if (td.hazardTrigger != trigger) continue;

            result.Add(step);
        }

        return result;
    }
}