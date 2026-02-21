using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class AIPlanner
{
    public struct ActionCandidate
    {
        public Vector2Int moveTile;
        public SkillData skill;
        public Unit target;     // SingleEnemy/SingleAlly에서 사용
        public float score;

        public override string ToString()
            => $"tile={moveTile} skill={(skill ? skill.skillName : "null")} target={(target ? target.name : "null")} score={score:0.00}";
    }

    // ===== Public Entry =====
    public static ActionCandidate Plan(
        Unit actor,
        List<Unit> actorAllies,
        List<Unit> actorEnemies,
        SkillData[] skillPool,
        GridManager grid,
        AIProfile profile,
        SkillData[] opponentSkillPool
    )
    {
        // 안전장치
        if (actor == null || actor.IsDead || grid == null || profile == null || skillPool == null || skillPool.Length == 0)
            return default;

        // 1) 이동 후보(도달 타일 + 비용)
        var costs = grid.GetReachableCosts(actor, actor.moveRange);
        var tiles = BuildTileCandidates(actor, actorEnemies, costs, profile);

        // 2) 행동 후보 생성 + 점수화
        var candidates = new List<ActionCandidate>(64);

        foreach (var tile in tiles)
        {
            foreach (var skill in skillPool)
            {
                if (skill == null) continue;

                // 타겟 선택(스킬 유형별)
                EvaluateSkillAtTile(actor, actorAllies, actorEnemies, tile, skill, grid, profile, opponentSkillPool, candidates);
            }
        }

        // 후보가 없으면 "제자리 + 첫 스킬" 같은 최소값
        if (candidates.Count == 0)
        {
            // 공격 후보가 하나도 없으면: "접근 이동" 후보를 만들어서 이동은 하게 만든다.
            ActionCandidate best = default;
            best.moveTile = actor.GridPos;
            best.skill = skillPool[0];
            best.target = null;
            best.score = float.NegativeInfinity;

            foreach (var tile in tiles)
            {
                int nearest = NearestDistanceToAny(tile, actorEnemies); // 적에게 가까워질수록 좋다
                float threat = EstimateThreatCount(tile, actorEnemies, opponentSkillPool);
                float score = (-nearest * profile.weightApproach) - (threat * profile.weightThreat);

                // 이동 비용도 약간 반영하고 싶으면 여기서 costs[tile]를 써도 됨(지금은 생략)
                if (score > best.score)
                {
                    best.score = score;
                    best.moveTile = tile;
                }
            }

            return best;
        }

        // 3) 선택(상위 K + 실수 확률)
        candidates.Sort((a, b) => b.score.CompareTo(a.score));
        int k = Mathf.Clamp(profile.topK, 1, candidates.Count);

        // 실수: 상위 K에서 랜덤 선택
        if (UnityEngine.Random.value < profile.mistakeChance)
            return candidates[UnityEngine.Random.Range(0, k)];

        // 아니면 베스트(또는 상위 K 중 약한 랜덤도 가능)
        return candidates[0];
    }

    // ===== Candidate Tiles =====
    static List<Vector2Int> BuildTileCandidates(Unit actor, List<Unit> enemies, Dictionary<Vector2Int, int> costs, AIProfile profile)
    {
        // 모든 타일을 전부 평가하면 비싸질 수 있으니 컷오프
        // 우선순위:
        // 1) 적에게 가까워지는 타일
        // 2) 이동 비용 낮은 타일
        var scored = new List<(Vector2Int tile, int moveCost, int nearestDist)>();

        foreach (var kv in costs)
        {
            var tile = kv.Key;
            int moveCost = kv.Value;
            int nearest = NearestDistanceToAny(tile, enemies);
            scored.Add((tile, moveCost, nearest));
        }

        scored.Sort((a, b) =>
        {
            int c = a.nearestDist.CompareTo(b.nearestDist);
            if (c != 0) return c;
            return a.moveCost.CompareTo(b.moveCost);
        });

        int take = Mathf.Clamp(profile.maxTilesToEvaluate, 1, scored.Count);
        var result = scored.Take(take).Select(x => x.tile).ToList();

        // 현재 위치는 항상 포함(안전)
        if (!result.Contains(actor.GridPos)) result.Add(actor.GridPos);

        return result;
    }

    // ===== Evaluate Skills at Tile =====
    static void EvaluateSkillAtTile(
        Unit actor,
        List<Unit> allies,
        List<Unit> enemies,
        Vector2Int fromTile,
        SkillData skill,
        GridManager grid,
        AIProfile profile,
        SkillData[] opponentSkillPool,
        List<ActionCandidate> outList
    )
    {
        switch (skill.targetType)
        {
            case SkillTargetType.Self:
            {
                float s = ScoreAction(actor, fromTile, skill, actor, allies, enemies, grid, profile, opponentSkillPool);
                outList.Add(new ActionCandidate { moveTile = fromTile, skill = skill, target = actor, score = s });
                break;
            }

            case SkillTargetType.SingleEnemy:
            {
                // 후보 타겟들 중 최고 점수 하나만 뽑아서 후보로 추가
                Unit bestT = null;
                float bestS = float.NegativeInfinity;

                foreach (var t in enemies)
                {
                    if (t == null || t.IsDead) continue;

                    int d = Manhattan(fromTile, t.GridPos);
                    if (d < skill.minRange || d > skill.maxRange) continue;

                    float s = ScoreAction(actor, fromTile, skill, t, allies, enemies, grid, profile, opponentSkillPool);
                    if (s > bestS)
                    {
                        bestS = s;
                        bestT = t;
                    }
                }

                if (bestT != null)
                    outList.Add(new ActionCandidate { moveTile = fromTile, skill = skill, target = bestT, score = bestS });

                break;
            }

            case SkillTargetType.SingleAlly:
            {
                Unit bestT = null;
                float bestS = float.NegativeInfinity;

                foreach (var t in allies)
                {
                    if (t == null || t.IsDead) continue;

                    int d = Manhattan(fromTile, t.GridPos);
                    if (d < skill.minRange || d > skill.maxRange) continue;

                    float s = ScoreAction(actor, fromTile, skill, t, allies, enemies, grid, profile, opponentSkillPool);
                    if (s > bestS)
                    {
                        bestS = s;
                        bestT = t;
                    }
                }

                if (bestT != null)
                    outList.Add(new ActionCandidate { moveTile = fromTile, skill = skill, target = bestT, score = bestS });

                break;
            }

            case SkillTargetType.AllEnemies:
            {
                // 합산 점수(범위/타겟 룰이 단순한 경우에만 추천)
                float sum = 0f;
                bool any = false;

                foreach (var t in enemies)
                {
                    if (t == null || t.IsDead) continue;
                    int d = Manhattan(fromTile, t.GridPos);
                    if (d < skill.minRange || d > skill.maxRange) continue;

                    any = true;
                    sum += ScoreAction(actor, fromTile, skill, t, allies, enemies, grid, profile, opponentSkillPool);
                }

                if (any)
                    outList.Add(new ActionCandidate { moveTile = fromTile, skill = skill, target = null, score = sum });

                break;
            }

            case SkillTargetType.AllAllies:
            {
                float sum = 0f;
                bool any = false;

                foreach (var t in allies)
                {
                    if (t == null || t.IsDead) continue;
                    int d = Manhattan(fromTile, t.GridPos);
                    if (d < skill.minRange || d > skill.maxRange) continue;

                    any = true;
                    sum += ScoreAction(actor, fromTile, skill, t, allies, enemies, grid, profile, opponentSkillPool);
                }

                if (any)
                    outList.Add(new ActionCandidate { moveTile = fromTile, skill = skill, target = null, score = sum });

                break;
            }
        }
    }

    // ===== Utility Score =====
    static float ScoreAction(
        Unit actor,
        Vector2Int fromTile,
        SkillData skill,
        Unit target,
        List<Unit> allies,
        List<Unit> enemies,
        GridManager grid,
        AIProfile profile,
        SkillData[] opponentSkillPool
    )
    {
        // 1) 공격/효과 가치
        float value = EstimateSkillValue(skill, target, profile);

        // 2) 타겟 선호(HP 낮은 적, 가까운 적 등) — “재밌게 보이는 AI”의 핵심
        if (target != null && !target.IsDead)
        {
            // 낮은 HP 선호(단, 너무 과하면 항상 약한 애만 패니까 가중치는 작게)
            float hp01 = target.maxHP <= 0 ? 1f : (target.currentHP / (float)target.maxHP);
            value += (1f - hp01) * profile.weightFocusLowHP;

            // 가까운 적 선호(접근/포커싱 유도)
            int d = Manhattan(fromTile, target.GridPos);
            value += Mathf.Max(0f, (10f - d)) * profile.weightNearest * 0.1f;
        }

        // 3) 위험(위협도) 페널티: “그 타일에 서면 다음에 몇 명에게 맞는가”
        float threat = EstimateThreatCount(fromTile, enemies, opponentSkillPool);
        float risk = threat * profile.weightThreat;

        return value - risk;
    }

    // 스킬의 “대략적 기대값”만 뽑는다 (나중에 훨씬 확장 가능)
    static float EstimateSkillValue(SkillData skill, Unit target, AIProfile profile)
    {
        float dmg = 0f;
        float heal = 0f;
        float burn = 0f;

        if (skill == null || skill.effects == null) return 0f;

        foreach (var e in skill.effects)
        {
            if (e == null) continue;

            // 현재 프로젝트에 이미 있는 이펙트들 기준 
            if (e is DealDamageEffect dd)
                dmg += dd.damage;

            if (e is HealEffect he)
                heal += he.healAmount;

            if (e is BurnApplyEffect ba)
                burn += ba.damagePerTurn * ba.durationTurns;
        }

        float value = 0f;

        if (target != null)
        {
            // 딜은 타겟 HP까지만 의미가 있다고 보고 캡
            float capped = Mathf.Min(dmg, target.currentHP);
            value += capped * profile.weightDamage;

            // 처치 보너스
            if (dmg >= target.currentHP && dmg > 0)
                value += profile.weightKill;

            // Burn 기대값(가중 낮게)
            value += burn * profile.weightBurn;
        }
        else
        {
            // 타겟이 null인 스킬(AllEnemies/AllAllies)용 대충값
            value += dmg * profile.weightDamage;
            value += burn * profile.weightBurn;
            value += heal * 0.7f; // 힐은 딜보다 가중 낮게(임시)
        }

        return value;
    }

    // 위협도(간단 버전): "상대팀 유닛 중, 내 위치를 사거리 내로 넣을 수 있는 수"
    // - 지금은 '상대의 스킬풀'까지 보지 않고, "상대의 기본 maxRange"에 준하는 간단 기준을 쓴다.
    // - 확장할 때: 상대의 SkillPool을 넣고, 예상 피해량 기반으로 바꾸면 됨.
    static float EstimateThreatCount(
        Vector2Int myTile,
        List<Unit> opponents,              // 나를 때릴 수 있는 상대들
        SkillData[] opponentSkillPool       // 그 상대들이 가진 스킬풀(간단히 공용풀로 시작)
    )
    {
        int count = 0;

        foreach (var op in opponents)
        {
            if (op == null || op.IsDead) continue;

            bool threatens = false;

            if (opponentSkillPool != null && opponentSkillPool.Length > 0)
            {
                foreach (var s in opponentSkillPool)
                {
                    if (s == null) continue;

                    int d = Manhattan(op.GridPos, myTile);
                    if (d >= s.minRange && d <= s.maxRange)
                    {
                        threatens = true;
                        break;
                    }
                }
            }
            else
            {
                // 스킬풀 없으면 근접(1)로 폴백
                int d = Manhattan(op.GridPos, myTile);
                threatens = (d <= 1);
            }

            if (threatens) count++;
        }

        return count;
    }

    // ===== Utils =====
    static int Manhattan(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    static int NearestDistanceToAny(Vector2Int from, List<Unit> candidates)
    {
        int best = int.MaxValue;
        foreach (var u in candidates)
        {
            if (u == null || u.IsDead) continue;
            int d = Manhattan(from, u.GridPos);
            if (d < best) best = d;
        }
        return best == int.MaxValue ? 9999 : best;
    }
}