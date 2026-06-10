using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GridManager : MonoBehaviour
{
    public static GridManager I { get; private set; }

    [Header("Grid")]
    public int width = 8;
    public int height = 6;
    public Vector3 origin = Vector3.zero;

    [Header("Height Visual")]
    public float heightWorldOffsetY = 0.2f;

    [Header("World Mapping")]
    public bool isIsometric = true;
    public float cellSize = 1f;
    public float isoTileWidth = 1f;
    public float isoTileHeight = 0.5f;

    [Header("Movement")]
    public float tilesPerSecond = 3.5f;   // ✅ 추천: 3~4 (낮을수록 느림)
    public float minMoveDuration = 0.12f; // ✅ 너무 짧아지는 것 방지

    // 좌표 -> 유닛 점유 맵
    private readonly Dictionary<Vector2Int, Unit> occ = new Dictionary<Vector2Int, Unit>();
    // 유닛 -> 좌표 역맵
    private readonly Dictionary<Unit, Vector2Int> unitPos = new Dictionary<Unit, Vector2Int>();
    //타일 조회
    Dictionary<Vector2Int, TileView> tiles = new Dictionary<Vector2Int, TileView>();


    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
    }

    // -----------------------------
    // Basic Queries
    // -----------------------------
    public bool InBounds(Vector2Int p)
        => p.x >= 0 && p.y >= 0 && p.x < width && p.y < height;

    public bool IsOccupied(Vector2Int p)
        => occ.TryGetValue(p, out var u) && u != null && !u.IsDead;

    public Unit GetUnitAt(Vector2Int p)
        => occ.TryGetValue(p, out var u) ? u : null;

    public bool CanStand(Vector2Int p)
    {
        if (!InBounds(p)) return false;
        if (!IsPassableTile(p)) return false;
        if (IsOccupied(p)) return false;
        return true;
    }
    // -----------------------------
    // Placement (Warp)
    // -----------------------------
    /// <summary>
    /// 전투 시작 배치 등: 논리/월드 모두 즉시 확정(워프)
    /// </summary>
    public bool TryPlace(Unit u, Vector2Int p)
    {
        if (u == null) return false;
        if (!CanStand(p)) return false;

        Remove(u);
        occ[p] = u;
        unitPos[u] = p;

        // 논리 좌표 갱신(외부에서 setter 막혀있으면 Unit에 SetGridPosOnly 필요)
        // 현재는 GridManager가 '표시 위치'를 책임지므로 transform을 직접 이동
        SetUnitGridPos(u, p);
        WarpToGrid(u, p);

        return true;
    }

    // -----------------------------
    // Movement (Logical + Animated)
    // -----------------------------
    /// <summary>
    /// 논리 이동만 먼저 확정(점유/그리드좌표). 월드 이동은 코루틴에서 처리.
    /// </summary>
    public bool TryMoveLogical(Unit u, Vector2Int to)
    {
        if (u == null) return false;
        if (!CanStand(to)) return false;

        Remove(u);
        occ[to] = u;
        unitPos[u] = to;

        SetUnitGridPos(u, to);
        return true;
    }

    /// <summary>
    /// 워프 이동(디버그/텔레포트용). TryMoveLogical과 같이 쓰지 말 것.
    /// </summary>
    public bool TryMoveWarp(Unit u, Vector2Int to)
    {
        if (!TryMoveLogical(u, to)) return false;
        WarpToGrid(u, to);
        return true;
    }

    /// <summary>
    /// (권장) 논리 이동 확정 + 보간 이동 코루틴을 반환.
    /// BattleController에서 yield return 으로 기다린 뒤 공격 가능.
    /// </summary>
    public IEnumerator MoveRoutine(Unit u, Vector2Int to, float duration = -1f)
    {
        if (u == null) yield break;

        Vector2Int from = u.GridPos;           // ✅ 이동 전 그리드 좌표 백업
        if (!TryMoveLogical(u, to)) yield break;

        Vector3 start = u.transform.position;
        Vector3 end = GridToWorldWithHeight(to);

        // ✅ duration 자동 계산(타일/초 기반)
        if (duration < 0f)
        {
            int manhattan = Mathf.Abs(to.x - from.x) + Mathf.Abs(to.y - from.y);
            manhattan = Mathf.Max(1, manhattan);
            duration = manhattan / Mathf.Max(0.01f, tilesPerSecond);
            if (duration < minMoveDuration) duration = minMoveDuration;
        }

        // ✅ 좌/우 반전(flipX): x방향으로 의미있는 이동일 때만
        var sr = u.sr;
        int dx = to.x - from.x;
        int dy = to.y - from.y;

        // top-down이면 dx만, iso면 (dx - dy)가 화면 x방향
        int screenXDir = isIsometric ? (dx - dy) : dx;

        if (screenXDir != 0)
            u.SetFacingX(screenXDir < 0 ? -1 : 1);

        u.SetMoving(true);

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / duration);
            u.transform.position = Vector3.Lerp(start, end, a);
            yield return null;
        }

        u.transform.position = end;
        u.SetMoving(false);
        
    }

    // -----------------------------
    // Remove
    // -----------------------------
    public void Remove(Unit u)
    {
        if (u == null) return;

        if (unitPos.TryGetValue(u, out var p))
        {
            if (occ.TryGetValue(p, out var cur) && cur == u)
                occ.Remove(p);

            unitPos.Remove(u);
        }
    }

    // -----------------------------
    // Grid <-> World
    // -----------------------------
    public Vector3 GridToWorld(Vector2Int p)
    {
        if (!isIsometric)
        {
            return origin + new Vector3(p.x * cellSize, p.y * cellSize, 0f);
        }
        else
        {
            float x = (p.x - p.y) * (isoTileWidth * 0.5f);
            float y = (p.x + p.y) * (isoTileHeight * 0.5f);
            return origin + new Vector3(x, y, 0f);
        }
    }

    public List<Vector2Int> GetNeighbors4(Vector2Int p)
    {
        return new List<Vector2Int>
        {
            new Vector2Int(p.x+1, p.y),
            new Vector2Int(p.x-1, p.y),
            new Vector2Int(p.x, p.y+1),
            new Vector2Int(p.x, p.y-1),
        };
    }

    // -----------------------------
    // Internal helpers
    // -----------------------------
    void WarpToGrid(Unit u, Vector2Int p)
    {
        u.transform.position = GridToWorldWithHeight(p);
    }

    void SetUnitGridPos(Unit u, Vector2Int p)
    {
        // ✅ GridPos setter가 private면 여기서 직접 못 바꿈
        // 해결책:
        // 1) Unit에 public void SetGridPosOnly(Vector2Int p) 만들고 여기서 호출
        // 2) 또는 Unit.SetGridPosAndWarp(p, this) 를 쓰되, 그 메서드가 워프까지 해버리면 MoveRoutine이 무의미해짐
        //
        // 따라서 Unit에 SetGridPosOnly를 만드는 걸 강력 추천.
        //
        // 아래는 "SetGridPosOnly"를 우선 호출 시도 (없으면 reflection 없이 안전하게 무시)
        u.SetGridPosOnly(p);
    }
    public bool CanStandFor(Unit mover, Vector2Int from, Vector2Int to)
    {
        if (!InBounds(to)) return false;
        if (!IsPassableTile(to)) return false;
        if (!CanClimb(mover, from, to)) return false;
        // 비어있으면 OK
        if (!occ.TryGetValue(to, out var u) || u == null || u.IsDead) return true;

        // 자기 자리면 OK (BFS 시작점 처리)
        return u == mover;
    }

    /// <summary>
    /// BFS로 moveRange 내 도달 가능한 타일과, 그 타일까지의 최소 이동 비용(맨해튼 step)을 반환.
    /// - mover 본인 위치는 비용 0으로 포함.
    /// - 다른 유닛이 점유한 칸은 통과/도착 불가.
    /// </summary>
    public Dictionary<Vector2Int, int> GetReachableCosts(Unit mover, int moveRange)
    {
        var result = new Dictionary<Vector2Int, int>();
        if (mover == null) return result;

        Vector2Int start = mover.GridPos;
        var q = new Queue<Vector2Int>();

        result[start] = 0;
        q.Enqueue(start);

        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            int curCost = result[cur];

            if (curCost >= moveRange) continue;

            foreach (var nb in GetNeighbors4(cur))
            {
                if (!CanStandFor(mover, cur, nb)) continue;

                int nextCost = curCost + 1;
                if (nextCost > moveRange) continue;

                if (result.TryGetValue(nb, out int old) && old <= nextCost) continue;

                result[nb] = nextCost;
                q.Enqueue(nb);
            }
        }

        return result;
    }

    public struct ReachableData
    {
        public Dictionary<Vector2Int, int> cost;
        public Dictionary<Vector2Int, Vector2Int> cameFrom;
    }

    public Vector3 GridToWorldWithHeight(Vector2Int p, int heightLevel)
    {
        Vector3 world = GridToWorld(p);
        world.y += heightLevel * heightWorldOffsetY;
        return world;
    }

    public ReachableData GetReachableData(Unit mover, int moveRange)
    {
        var cost = new Dictionary<Vector2Int, int>();
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        if (mover == null) return new ReachableData { cost = cost, cameFrom = cameFrom };

        Vector2Int start = mover.GridPos;
        var q = new Queue<Vector2Int>();

        cost[start] = 0;
        q.Enqueue(start);

        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            int curCost = cost[cur];

            if (curCost >= moveRange) continue;

            foreach (var nb in GetNeighbors4(cur))
            {
                if (!CanStandFor(mover, cur, nb)) continue;

                int nextCost = curCost + 1;
                if (nextCost > moveRange) continue;

                // 더 좋은(짧은) 경로로 갱신
                if (cost.TryGetValue(nb, out int oldCost) && oldCost <= nextCost) continue;

                cost[nb] = nextCost;
                cameFrom[nb] = cur;
                q.Enqueue(nb);
            }
        }

        return new ReachableData { cost = cost, cameFrom = cameFrom };
    }

    public List<Vector2Int> ReconstructPath(Vector2Int start, Vector2Int goal, Dictionary<Vector2Int, Vector2Int> cameFrom)
    {
        if (start == goal) return new List<Vector2Int>();
        if (cameFrom == null) return null;
        if (!cameFrom.ContainsKey(goal)) return null;

        var path = new List<Vector2Int>();
        var cur = goal;

        // 무한 루프 / 깨진 체인 방어
        int guard = 0;
        const int maxSteps = 512;

        while (cur != start)
        {
            path.Add(cur);

            if (!cameFrom.TryGetValue(cur, out var prev))
            {
                Debug.LogWarning($"[ReconstructPath] Broken path chain. start={start}, goal={goal}, missing={cur}");
                return null;
            }

            cur = prev;

            guard++;
            if (guard > maxSteps)
            {
                Debug.LogWarning($"[ReconstructPath] Guard overflow. start={start}, goal={goal}");
                return null;
            }
        }

        path.Reverse();
        return path;
    }

    /// <summary>
    /// mover가 moveRange 안에서 goal까지 갈 수 있으면 최단 경로를 반환한다.
    /// (갈 수 없으면 null)
    /// </summary>
    public List<Vector2Int> FindPathWithinRange(Unit mover, Vector2Int goal, int moveRange)
    {
        if (mover == null) return null;
        var data = GetReachableData(mover, moveRange);
        return ReconstructPath(mover.GridPos, goal, data.cameFrom);
    }

    /// <summary>
    /// 경로(path)를 따라 1타일씩 이동한다.
    /// - path는 FindPathWithinRange/ReconstructPath 형태(= start 제외, goal 포함)로 들어오는 것을 전제.
    /// - 이동 도중 막히면 즉시 중단한다(턴제에서는 보통 충분).
    /// </summary>
    public IEnumerator MovePathRoutine(Unit u, List<Vector2Int> path, System.Action<Unit, Vector2Int> onStepEntered = null)
    {
        if (u == null || path == null) yield break;

        foreach (var step in path)
        {
            if (!CanStandFor(u, u.GridPos, step))
                yield break;

            yield return StartCoroutine(MoveRoutine(u, step));

            onStepEntered?.Invoke(u, step);

            if (u == null || u.IsDead)
                yield break;
        }
    }

    public TileData GetTileData(Vector2Int pos)
    {
        var tile = GetTileView(pos);
        return tile != null ? tile.tileData : null;
    }

    public TileView GetTile(Vector2Int p)
    {
        tiles.TryGetValue(p, out var t);
        return t;
    }

    public bool BlocksLOS(Vector2Int p)
    {
        var t = GetTile(p);
        if (t == null) return false;
        return t.BlocksLOS;
    }

    public bool HasLineOfSight(Vector2Int from, Vector2Int to)
    {
        int x0 = from.x;
        int y0 = from.y;
        int x1 = to.x;
        int y1 = to.y;

        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);

        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;

        int err = dx - dy;

        while (true)
        {
            var p = new Vector2Int(x0, y0);

            if (p != from && p != to)
            {
                var tile = GetTileView(p);
                if (tile != null && tile.tileData != null && tile.tileData.blocksLOS)
                    return false;
            }

            if (x0 == x1 && y0 == y1)
                break;

            int e2 = 2 * err;

            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }

        return true;
    }

    public TileView GetTileView(Vector2Int pos)
    {
        var views = FindObjectsOfType<TileView>();

        foreach (var v in views)
        {
            if (v.GridPos == pos)
                return v;
        }

        return null;
    }

    public bool IsPassableTile(Vector2Int pos)
    {
        var tile = GetTileView(pos);
        if (tile == null) return false; 
        if (tile.tileData == null) return true;
        return tile.tileData.passable;
    }

    public void RegisterTile(TileView tile)
    {
        if (tile == null) return;
        tiles[tile.GridPos] = tile;
    }

    public int GetTileHeight(Vector2Int pos)
    {
        var tile = GetTileView(pos);
        if (tile == null || tile.tileData == null) return 0;
        return tile.tileData.heightLevel;
    }

    public Vector3 GridToWorldWithHeight(Vector2Int p)
    {
        Vector3 world = GridToWorld(p);

        var tile = GetTile(p);
        if (tile != null)
            world.y += tile.HeightLevel * heightWorldOffsetY;

        return world;
    }

    public int GetHeightDelta(Vector2Int from, Vector2Int to)
    {
        return GetTileHeight(to) - GetTileHeight(from);
    }

    public bool CanClimb(Unit mover, Vector2Int from, Vector2Int to)
    {
        if (mover == null) return false;

        var fromTile = GetTileView(from);
        var toTile = GetTileView(to);

        int fromH = fromTile != null ? fromTile.HeightLevel : 0;
        int toH = toTile != null ? toTile.HeightLevel : 0;

        int dh = Mathf.Abs(toH - fromH);
        return dh <= mover.maxClimbDelta;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!Application.isPlaying && I == null) I = this;

        Gizmos.color = new Color(1f, 1f, 1f, 0.25f);

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            Vector2Int p = new Vector2Int(x, y);
            Vector3 w = GridToWorld(p);

            if (isIsometric)
            {
                float hw = isoTileWidth * 0.5f;
                float hh = isoTileHeight * 0.5f;

                Vector3 top    = w + new Vector3(0,  hh, 0);
                Vector3 right  = w + new Vector3(hw, 0, 0);
                Vector3 bottom = w + new Vector3(0, -hh, 0);
                Vector3 left   = w + new Vector3(-hw, 0, 0);

                Gizmos.DrawLine(top, right);
                Gizmos.DrawLine(right, bottom);
                Gizmos.DrawLine(bottom, left);
                Gizmos.DrawLine(left, top);
            }
            else
            {
                Gizmos.DrawWireCube(w, new Vector3(cellSize, cellSize, 0.01f));
            }
        }
    }
#endif
}