using System.Collections.Generic;
using UnityEngine;

public class TileHighlighter : MonoBehaviour
{
    [Header("Refs")]
    public GridManager grid;

    [Header("Prefabs")]
    public GameObject moveTilePrefab;   // Blue
    public GameObject pathTilePrefab;     // Blue Strong (강조)
    public GameObject rangePrefab;      // Red (사거리)
    public GameObject targetPrefab;     // Red Strong (타겟 강조)  <-- 추가

    private readonly Dictionary<Vector2Int, GameObject> moveActive  = new(128);
    private readonly Dictionary<Vector2Int, GameObject> rangeActive = new(256);
    private readonly Dictionary<Vector2Int, GameObject> targetActive= new(64);
    //private readonly Dictionary<Vector2Int, GameObject> pathActive = new(64);

    private readonly Dictionary<Vector2Int, (Sprite sprite, Color color)> moveBackup  = new(64);
    private readonly Dictionary<Vector2Int, (Sprite sprite, Color color)> rangeBackup = new(64);
    private readonly HashSet<Vector2Int> pathOverlaySet   = new(64);
    private readonly HashSet<Vector2Int> targetOverlaySet = new(64);

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

    public void ShowMoveTiles(IEnumerable<Vector2Int> moveTiles)
    {
        if (!grid) grid = GridManager.I;
        if (!grid || !moveTilePrefab) return;

        ClearAll();
        if (moveTiles == null) return;

        foreach (var p in moveTiles)
        {
            if (!grid.InBounds(p)) continue;
            SpawnTile(p, moveTilePrefab, moveActive);
        }
    }

    // -------------------------
    // Range (Red)
    // -------------------------
    public void ShowRangeTiles(IEnumerable<Vector2Int> tiles)
    {
        if (!grid) grid = GridManager.I;
        if (!grid || !rangePrefab) return;

        ClearRange();
        ClearTargetOverlay();
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
        // A안: range 타일이 존재하면 spawn 대신 승격 (겹쳐서 진해짐 방지)
        ClearTargetOverlay();
        ClearTarget();
        var refSr = targetPrefab.GetComponentInChildren<SpriteRenderer>();
        if (refSr == null) return;

        foreach (var p in tiles)
        {
            if (!grid.InBounds(p)) continue;
            if (rangeActive.TryGetValue(p, out var rgo) && rgo)
            {
                var rsr = rgo.GetComponentInChildren<SpriteRenderer>();
                if (rsr != null)
                {
                    if (!rangeBackup.ContainsKey(p)) rangeBackup[p] = (rsr.sprite, rsr.color);
                    rsr.sprite = refSr.sprite;
                    rsr.color  = refSr.color;
                    targetOverlaySet.Add(p);
                    continue;
                }
            }
            // range가 없는 곳은 기존처럼 별도 target 타일 생성
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

    // -------------------------
    // Hover overlays (no extra spawn)
    // -------------------------
    public void ClearPath()
    {
        foreach (var p in pathOverlaySet)
        {
            if (moveActive.TryGetValue(p, out var go) && go)
            {
                var sr = go.GetComponentInChildren<SpriteRenderer>();
                if (sr != null && moveBackup.TryGetValue(p, out var bak))
                {
                    sr.sprite = bak.sprite;
                    sr.color  = bak.color;
                }
            }
        }
        pathOverlaySet.Clear();
        moveBackup.Clear();
    }

    public void ClearTargetOverlay()
    {
        foreach (var p in targetOverlaySet)
        {
            if (rangeActive.TryGetValue(p, out var go) && go)
            {
                var sr = go.GetComponentInChildren<SpriteRenderer>();
                if (sr != null && rangeBackup.TryGetValue(p, out var bak))
                {
                    sr.sprite = bak.sprite;
                    sr.color  = bak.color;
                }
            }
        }
        targetOverlaySet.Clear();
        rangeBackup.Clear();
    }

    public void ShowPathTiles(IEnumerable<Vector2Int> tiles)
    {
        if (!grid) grid = GridManager.I;
        if (!grid || !pathTilePrefab) return;

        ClearPath();
        var refSr = pathTilePrefab.GetComponentInChildren<SpriteRenderer>();
        if (refSr == null) return;

        foreach (var p in tiles)
        {
            if (!grid.InBounds(p)) continue;
            if (!moveActive.TryGetValue(p, out var go) || !go) continue; // move 타일이 깔린 곳만 승격

            var sr = go.GetComponentInChildren<SpriteRenderer>();
            if (sr == null) continue;

            if (!moveBackup.ContainsKey(p)) moveBackup[p] = (sr.sprite, sr.color);
            sr.sprite = refSr.sprite;
            sr.color  = refSr.color;
            pathOverlaySet.Add(p);
        }
    }
}