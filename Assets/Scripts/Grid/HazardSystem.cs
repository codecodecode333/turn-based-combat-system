using UnityEngine;

public static class HazardSystem
{
    public struct ResolveResult
    {
        public bool triggered;
        public bool stopMovement;
        public bool consumedTile;
    }

    public static ResolveResult Resolve(
        Unit unit,
        Vector2Int tilePos,
        HazardTriggerType trigger,
        GridManager grid)
    {
        var result = new ResolveResult();

        if (unit == null || unit.IsDead || grid == null)
            return result;

        var tile = grid.GetTileView(tilePos);
        if (tile == null || tile.tileData == null)
            return result;

        if (tile.HazardType == HazardType.None)
            return result;

        if (tile.HazardTrigger != trigger)
            return result;

        int power = Mathf.Max(1, tile.HazardPower);
        int duration = Mathf.Max(1, tile.HazardDuration);

        switch (tile.HazardType)
        {
            case HazardType.Burn:
                unit.AddOrRefreshStatus(new BurnStatus(power, duration, unit));
                result.triggered = true;
                break;

            case HazardType.Poison:
                unit.AddOrRefreshStatus(new PoisonStatus(power, duration, unit));
                result.triggered = true;
                break;

            case HazardType.Explosion:
                unit.TakeDamage(power);
                result.triggered = true;
                break;
        }

        return result;
    }
}