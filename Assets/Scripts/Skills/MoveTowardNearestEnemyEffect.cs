using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Battle/SkillEffect/MoveTowardNearestEnemy", fileName = "MoveTowardNearestEnemy_")]
public class MoveTowardNearestEnemyEffect : SkillEffect
{

    public override void Apply(Unit attacker, Unit targetIgnored)
    {
        if (attacker == null || attacker.IsDead) return;
        var grid = GridManager.I;
        if (grid == null) return;

        // 적 리스트 얻기(아군인지 적군인지 판별은 GridManager에서 못하니, 간단히 BattleController에서 제공하는 게 정석)
        // 지금은 "가장 가까운 점유 유닛"을 적으로 간주하면 오작동 가능.
        // ✅ 그래서 가장 안정적인 방법: attacker가 가진 팀 정보를 Unit에 붙이는 것.
        // 하지만 최소 구현을 위해: BattleController에 전역 접근(싱글톤) 쓰거나, Unit에 isAlly를 넣는 걸 권장.

        // ---- 최소 구현용: Unit에 public bool isAlly;를 두고 세팅했다고 가정 ----
        // 아래는 isAlly 기준으로 적 후보를 찾는다.
        List<Unit> candidates = new List<Unit>();
        // GridManager는 점유 dict에 접근 못 하니, 여기선 씬 전체 Unit에서 찾는 단순 방식(유닛 수 적을 때 OK)
        foreach (var u in GameObject.FindObjectsOfType<Unit>())
        {
            if (u == null || u.IsDead) continue;
            if (u == attacker) continue;

            // isAlly 필드가 없다면 아래 줄에서 에러 -> 아래 "5) 팀 구분" 참고대로 추가해줘
            if (u.isAlly != attacker.isAlly)
                candidates.Add(u);
        }
        if (candidates.Count == 0) return;

        // 가장 가까운 적 찾기(맨해튼)
        Unit nearest = null;
        int bestDist = int.MaxValue;
        foreach (var e in candidates)
        {
            int d = Mathf.Abs(attacker.GridPos.x - e.GridPos.x) + Mathf.Abs(attacker.GridPos.y - e.GridPos.y);
            if (d < bestDist) { bestDist = d; nearest = e; }
        }
        if (nearest == null) return;

        // BFS로 이동 가능한 모든 타일 수집
        var reachable = GetReachableTiles(grid, attacker.GridPos, attacker.moveRange);

        // reachable 중에서 "nearest에게 가장 가까워지는" 타일 선택
        Vector2Int best = attacker.GridPos;
        int bestScore = bestDist;

        foreach (var p in reachable)
        {
            if (p == attacker.GridPos) continue;
            if (!grid.InBounds(p)) continue;
            if (grid.IsOccupied(p)) continue;

            int d = Mathf.Abs(p.x - nearest.GridPos.x) + Mathf.Abs(p.y - nearest.GridPos.y);
            if (d < bestScore)
            {
                bestScore = d;
                best = p;
            }
        }

        if (best != attacker.GridPos)
        {
            var path = grid.FindPathWithinRange(attacker, best, attacker.moveRange);
            if (path != null)
                attacker.StartCoroutine(grid.MovePathRoutine(attacker, path));
        }
    }

    List<Vector2Int> GetReachableTiles(GridManager grid, Vector2Int start, int range)
    {
        var result = new List<Vector2Int>();
        var q = new Queue<(Vector2Int pos, int dist)>();
        var visited = new HashSet<Vector2Int>();

        q.Enqueue((start, 0));
        visited.Add(start);

        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            result.Add(cur.pos);

            if (cur.dist >= range) continue;

            foreach (var nb in grid.GetNeighbors4(cur.pos))
            {
                if (!grid.InBounds(nb)) continue;
                if (visited.Contains(nb)) continue;

                // 통과 가능 조건: 비어있는 칸 또는 시작칸
                if (grid.IsOccupied(nb) && nb != start) continue;

                visited.Add(nb);
                q.Enqueue((nb, cur.dist + 1));
            }
        }

        return result;
    }
}
