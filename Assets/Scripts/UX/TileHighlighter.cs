using System.Collections.Generic;
using UnityEngine;

public class TileHighlighter : MonoBehaviour
{
    [Header("Refs")]
    public GridManager grid;

    [Header("Prefabs")]
    public GameObject moveTilePrefab;   // Blue
    public GameObject rangePrefab;      // Red (사거리)
    public GameObject targetPrefab;     // Red Strong (타겟 강조)  <-- 추가

    private readonly Dictionary<Vector2Int, GameObject> moveActive  = new(128);
    private readonly Dictionary<Vector2Int, GameObject> rangeActive = new(256);
    private readonly Dictionary<Vector2Int, GameObject> targetActive= new(64);

    void Awake()
    {
        if (!grid) grid = GridManager.I;
    }

    public void ClearAll()
    {
        ClearDict(moveActive);
        ClearDict(rangeActive);
        ClearDict(targetActive);
    }

    public void ClearMove()   => ClearDict(moveActive);
    public void ClearRange()  => ClearDict(rangeActive);
    public void ClearTarget() => ClearDict(targetActive);

    void ClearDict(Dictionary<Vector2Int, GameObject> dict)
    {
        foreach (var kv in dict)
            if (kv.Value) Destroy(kv.Value);
        dict.Clear();
    }

    // -------------------------
    // Move (Blue)
    // -------------------------
    public void ShowReachableMoveTiles(Unit unit)
    {
        if (unit == null) return;
        if (!grid) grid = GridManager.I;
        if (!grid || !moveTilePrefab) return;

        ClearAll();

        var costs = grid.GetReachableCosts(unit, unit.moveRange);
        foreach (var p in costs.Keys)
            SpawnTile(p, moveTilePrefab, moveActive);
    }

    // -------------------------
    // Range (Red)
    // -------------------------
    public void ShowRangeTiles(IEnumerable<Vector2Int> tiles)
    {
        if (!grid) grid = GridManager.I;
        if (!grid || !rangePrefab) return;

        ClearRange();
        ClearTarget(); // 범위가 바뀌면 타겟도 리셋

        foreach (var p in tiles)
        {
            if (!grid.InBounds(p)) continue;
            SpawnTile(p, rangePrefab, rangeActive);
        }
    }

    // -------------------------
    // Targets (Strong Red)
    // -------------------------
    public void ShowTargetTiles(IEnumerable<Vector2Int> tiles)
    {
        if (!grid) grid = GridManager.I;
        if (!grid || !targetPrefab) return;

        ClearTarget();

        foreach (var p in tiles)
        {
            if (!grid.InBounds(p)) continue;
            SpawnTile(p, targetPrefab, targetActive);
        }
    }

    void SpawnTile(Vector2Int gridPos, GameObject prefab, Dictionary<Vector2Int, GameObject> dict)
    {
        if (dict.ContainsKey(gridPos)) return;

        Vector3 world = grid.GridToWorld(gridPos);
        var go = Instantiate(prefab, world, Quaternion.identity, transform);

        var sr = go.GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
            sr.sortingOrder = -Mathf.RoundToInt(world.y * 100f) - 1;

        dict[gridPos] = go;
    }
}