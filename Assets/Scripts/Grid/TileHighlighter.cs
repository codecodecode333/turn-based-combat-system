using System.Collections.Generic;
using UnityEngine;

public class TileHighlighter : MonoBehaviour
{
    [Header("Refs")]
    public GridManager grid;

    [Header("Prefabs")]
    public GameObject moveTilePrefab;
    public GameObject pathTilePrefab;
    public GameObject rangePrefab;
    public GameObject targetPrefab;
    public GameObject ghostTilePrefab;
    public GameObject invalidTilePrefab;
    [SerializeField] private GameObject hazardPathPrefab;

    private readonly Dictionary<Vector2Int, GameObject> moveActive = new(128);
    private readonly Dictionary<Vector2Int, GameObject> rangeActive = new(256);
    private readonly Dictionary<Vector2Int, GameObject> targetActive = new(64);
    private readonly Dictionary<Vector2Int, GameObject> pathActive = new(64);
    private readonly Dictionary<Vector2Int, GameObject> plannedPathActive = new(64);
    private readonly Dictionary<Vector2Int, GameObject> ghostActive = new();
    private readonly Dictionary<Vector2Int, GameObject> invalidActive = new();

    private readonly Dictionary<Vector2Int, (Sprite sprite, Color color)> moveBackup = new(64);
    private readonly Dictionary<Vector2Int, (Sprite sprite, Color color)> rangeBackup = new(64);
    private readonly HashSet<Vector2Int> pathOverlaySet = new(64);
    private readonly HashSet<Vector2Int> targetOverlaySet = new(64);

    private readonly List<GameObject> hoverHazardPathPool = new List<GameObject>();
    private readonly List<GameObject> plannedHazardPathPool = new List<GameObject>();

    void Awake()
    {
        if (!grid) grid = GridManager.I;
    }

    public void ClearAll()
    {
        ClearMove();
        ClearRange();
        ClearTarget();
        ClearPath();
        ClearPlannedPath();
        ClearGhost();
        ClearInvalid();
        ClearHazardPath();
    }

    public void ClearMove() => ClearDict(moveActive);
    public void ClearRange() => ClearDict(rangeActive);
    public void ClearTarget() => ClearDict(targetActive);
    public void ClearGhost() => ClearDict(ghostActive);
    public void ClearInvalid() => ClearDict(invalidActive);
    public void ClearPlannedPath() => ClearDict(plannedPathActive);

    void ClearDict(Dictionary<Vector2Int, GameObject> dict)
    {
        foreach (var kv in dict)
            if (kv.Value) Destroy(kv.Value);
        dict.Clear();
    }

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

    public void ShowRangeTiles(IEnumerable<Vector2Int> tiles)
    {
        if (!grid) grid = GridManager.I;
        if (!grid || !rangePrefab) return;

        ClearRange();
        ClearTargetOverlay();
        ClearTarget();

        foreach (var p in tiles)
        {
            if (!grid.InBounds(p)) continue;
            SpawnTile(p, rangePrefab, rangeActive);
        }
    }

    public void ShowTargetTiles(IEnumerable<Vector2Int> tiles)
    {
        if (!grid) grid = GridManager.I;
        if (!grid || !targetPrefab) return;

        ClearTarget();
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
                    rsr.color = refSr.color;
                    targetOverlaySet.Add(p);
                    continue;
                }
            }
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
                    sr.color = bak.color;
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
                    sr.color = bak.color;
                }
            }
        }
        targetOverlaySet.Clear();
        rangeBackup.Clear();
    }

    // hover path: move 타일 승격
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
            if (!moveActive.TryGetValue(p, out var go) || !go) continue;

            var sr = go.GetComponentInChildren<SpriteRenderer>();
            if (sr == null) continue;

            if (!moveBackup.ContainsKey(p)) moveBackup[p] = (sr.sprite, sr.color);
            sr.sprite = refSr.sprite;
            sr.color = refSr.color;
            pathOverlaySet.Add(p);
        }
    }

    // planned/ghost path: 별도 spawn
    public void ShowPlannedPathTiles(IEnumerable<Vector2Int> tiles)
    {
        ClearPlannedPath();

        if (!grid) grid = GridManager.I;
        if (!grid || !pathTilePrefab || tiles == null) return;

        foreach (var p in tiles)
        {
            if (!grid.InBounds(p)) continue;
            SpawnTile(p, pathTilePrefab, plannedPathActive);
        }
    }

    public void ShowGhostTile(Vector2Int tile)
    {
        ClearGhost();

        if (ghostTilePrefab == null) return;

        Vector3 pos = GridManager.I.GridToWorld(tile);
        var go = Instantiate(ghostTilePrefab, pos, Quaternion.identity, transform);
        ghostActive[tile] = go;
    }

    public void ShowInvalidTile(Vector2Int tile)
    {
        ClearInvalid();

        if (invalidTilePrefab == null) return;

        Vector3 pos = GridManager.I.GridToWorld(tile);
        var go = Instantiate(invalidTilePrefab, pos, Quaternion.identity, transform);
        invalidActive[tile] = go;
    }

    public void ShowHoverHazardPathTiles(IEnumerable<Vector2Int> tiles)
    {
        ClearHoverHazardPath();
        if (tiles == null) return;

        foreach (var p in tiles)
            SpawnHazardPathTile(p, hoverHazardPathPool);
    }

    public void ShowPlannedHazardPathTiles(IEnumerable<Vector2Int> tiles)
    {
        ClearPlannedHazardPath();
        if (tiles == null) return;

        foreach (var p in tiles)
            SpawnHazardPathTile(p, plannedHazardPathPool);
    }

    public void ClearHoverHazardPath()
    {
        ClearPool(hoverHazardPathPool);
    }

    public void ClearPlannedHazardPath()
    {
        ClearPool(plannedHazardPathPool);
    }

    public void ClearHazardPath()
    {
        ClearHoverHazardPath();
        ClearPlannedHazardPath();
    }

    private void ClearPool(List<GameObject> pool)
    {
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] != null)
                Destroy(pool[i]);
        }
        pool.Clear();
    }

    private void SpawnHazardPathTile(Vector2Int gridPos, List<GameObject> pool)
    {
        if (hazardPathPrefab == null) return;
        if (!grid) grid = GridManager.I;
        if (!grid || !grid.InBounds(gridPos)) return;

        Vector3 world = grid.GridToWorld(gridPos);
        var go = Instantiate(hazardPathPrefab, world, Quaternion.identity, transform);
        pool.Add(go);
    }
}