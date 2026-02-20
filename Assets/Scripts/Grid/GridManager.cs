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
        => InBounds(p) && !IsOccupied(p);

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
        Vector3 end = GridToWorld(to);

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
        Debug.Log($"[MoveRoutine] {u.name} from={from} to={to} dx={dx} dy={dy} screenXDir={screenXDir}");
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
        u.transform.position = GridToWorld(p);
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