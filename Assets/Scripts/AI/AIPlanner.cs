using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Utility 기반 AI 플래너.
/// - "이동 타일" + "스킬" + (필요 시) "clickedTile / clickedUnit"까지 포함한 실행 계획을 만든다.
/// - BattleController.ResolveTargets()의 SkillTargetMode 규칙과 최대한 동일하게 맞춘다.
/// </summary>
public static class AIPlanner
{
    public struct ActionCandidate
    {
        public Vector2Int moveTile;
        public SkillData skill;

        // SkillTargetMode별 입력(Enemy AI는 클릭이 없으므로 내부적으로 '가상의 클릭'을 만든다)
        public Vector2Int? clickedTile; // ClickTileAOE
        public Unit clickedUnit;        // ClickSingle

        // 디버그/선택용
        public float score;
        public int targetCount;

        public override string ToString()
        {
            string s = (skill ? skill.skillName : "null");
            string ct = clickedTile.HasValue ? clickedTile.Value.ToString() : "null";
            string cu = clickedUnit ? clickedUnit.name : "null";
            return $"tile={moveTile} skill={s} clickedTile={ct} clickedUnit={cu} targets={targetCount} score={score:0.00}";
        }
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
        if (actor == null || actor.IsDead || grid == null || profile == null || skillPool == null || skillPool.Length == 0)
            return default;

        var band = GetDesiredRangeBand(skillPool, profile);
        // 1) 이동 후보(도달 타일 + 비용)
        var costs = grid.GetReachableCosts(actor, actor.moveRange);
        var tiles = BuildTileCandidates(actor, actorEnemies, costs, profile, band, grid);


        // 2) 행동 후보 생성 + 점수화
        var candidates = new List<ActionCandidate>(128);

        foreach (var tile in tiles)
        {
            foreach (var skill in skillPool)
            {
                if (skill == null) continue;
                EvaluateSkillAtTile(actor, actorAllies, actorEnemies, tile, skill, grid, profile, opponentSkillPool, candidates);
            }
        }

        // 공격 가능한 후보가 하나라도 있으면, 그 후보들만으로 선택
        var actionable = candidates.Where(c => c.targetCount > 0).ToList();
        if (actionable.Count > 0) candidates = actionable;

        // 3) 후보가 없으면 접근 이동(스킬은 null) 플랜 반환
        if (candidates.Count == 0)
        {
            return BuildApproachFallback(actor, actorEnemies, costs, profile, opponentSkillPool, band);
        }

        // 4) 선택(상위 K + 실수 확률)
        candidates.Sort((a, b) => b.score.CompareTo(a.score));
        int k = Mathf.Clamp(profile.topK, 1, candidates.Count);

        if (UnityEngine.Random.value < profile.mistakeChance)
            return candidates[UnityEngine.Random.Range(0, k)];

        return candidates[0];
    }

    // ===== Candidate Tiles =====
    static List<Vector2Int> BuildTileCandidates(
        Unit actor,
        List<Unit> enemies,
        Dictionary<Vector2Int, int> costs,
        AIProfile profile,
        (int desiredMin, int desiredMax) band,
        GridManager grid = null
    )
    {
        var scored = new List<(Vector2Int tile, int moveCost, int primary, int nearestDist)>(costs.Count);

        foreach (var kv in costs)
        {
            var tile = kv.Key;

            if (grid != null && !grid.InBounds(tile))
                continue;

            int moveCost = kv.Value;
            int nearest = NearestDistanceToAny(tile, enemies);

            int primary;
            if (band.desiredMin == 0 && band.desiredMax == 0)
                primary = nearest; // 근접: 가까워지기
            else
                primary = DistanceOutsideBand(nearest, band.desiredMin, band.desiredMax); // 원거리: 밴드 유지

            scored.Add((tile, moveCost, primary, nearest));
        }

        scored.Sort((a, b) =>
        {
            int c = a.primary.CompareTo(b.primary);
            if (c != 0) return c;

            c = a.moveCost.CompareTo(b.moveCost);
            if (c != 0) return c;

            if (band.desiredMin == 0 && band.desiredMax == 0)
                return a.nearestDist.CompareTo(b.nearestDist); // 근접: 더 가까운
            else
                return b.nearestDist.CompareTo(a.nearestDist); // 원거리: 너무 붙지 않게
        });

        int take = Mathf.Clamp(profile.maxTilesToEvaluate, 1, scored.Count);
        var result = scored.Take(take).Select(x => x.tile).ToList();

        if (!result.Contains(actor.GridPos))
            result.Add(actor.GridPos);

        return result;
    }

    static ActionCandidate BuildApproachFallback(
        Unit actor,
        List<Unit> enemies,
        Dictionary<Vector2Int, int> costs,   // ✅ tiles 대신 costs
        AIProfile profile,
        SkillData[] opponentSkillPool,
        (int desiredMin, int desiredMax) band
    )
    {
        ActionCandidate best = default;
        best.moveTile = actor.GridPos;
        best.skill = null;
        best.clickedTile = null;
        best.clickedUnit = null;
        best.targetCount = 0;
        best.score = float.NegativeInfinity;

        foreach (var kv in costs)
        {
            Vector2Int tile = kv.Key;
            int moveCost = kv.Value;

            int d = NearestDistanceToAny(tile, enemies);
            float threat = EstimateThreatCount(tile, enemies, opponentSkillPool);

            float score;
            if (band.desiredMin == 0 && band.desiredMax == 0)
            {
                // 근접 성향: 가까워지기
                score = (-d * profile.weightApproach) - (threat * profile.weightThreat);
            }
            else
            {
                // 원거리 성향: 거리 밴드 유지
                int outDist = DistanceOutsideBand(d, band.desiredMin, band.desiredMax);
                score = (-outDist * profile.weightKeepRange) - (threat * profile.weightThreat);
            }

            // ✅ "막혔을 때 아무 것도 안 함" 방지용 타이브레이커
            // - 동점이면 제자리(0) 대신 한 칸이라도 움직이게 유도(아주 작은 값)
            score += moveCost * 0.001f;

            // ✅ 그래도 제자리 고정이 심하면 제자리에 아주 미세한 페널티
            if (tile == actor.GridPos) score -= 0.0005f;

            if (score > best.score)
            {
                best.score = score;
                best.moveTile = tile;
            }
        }

        return best;
    }
    // ===== Evaluate Skills at Tile (SkillTargetMode) =====
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
        // 공통 리스크(이 타일에 섰을 때 맞을 확률)
        float threat = EstimateThreatCount(fromTile, enemies, opponentSkillPool);
        float risk = threat * profile.weightThreat;

        switch (skill.targetMode)
        {
            case SkillTargetMode.AutoNearestSingle:
            {
                var t = ChooseNearestInRangeFromPos(fromTile, enemies, skill.minRange, skill.maxRange);
                if (t == null) break;

                float value = ScoreSingleTargetValue(actor, fromTile, skill, t, profile);
                outList.Add(new ActionCandidate
                {
                    moveTile = fromTile,
                    skill = skill,
                    clickedUnit = null,     // 클릭 없음
                    clickedTile = null,
                    targetCount = 1,
                    score = value - risk
                });
                break;
            }

            case SkillTargetMode.ClickSingle:
            {
                // AI는 "클릭"을 만들어야 하므로, 사거리 내 적 중 best 1개를 고른다.
                Unit bestT = null;
                float bestScore = float.NegativeInfinity;

                foreach (var t in enemies)
                {
                    if (!IsAlive(t)) continue;
                    int d = Manhattan(fromTile, t.GridPos);
                    if (d < skill.minRange || d > skill.maxRange) continue;

                    float v = ScoreSingleTargetValue(actor, fromTile, skill, t, profile);
                    float s = v - risk;
                    if (s > bestScore)
                    {
                        bestScore = s;
                        bestT = t;
                    }
                }

                if (bestT != null)
                {
                    outList.Add(new ActionCandidate
                    {
                        moveTile = fromTile,
                        skill = skill,
                        clickedUnit = bestT, // ✅ RunSkill(..., clickedUnit)로 넘겨야 함
                        clickedTile = null,
                        targetCount = 1,
                        score = bestScore
                    });
                }
                break;
            }

            case SkillTargetMode.ClickTileAOE:
            {
                // AI는 "중심 타일 클릭"을 만들어야 하므로, 가능한 center 후보를 만들고 최고점을 선택한다.
                if (enemies == null || enemies.Count == 0) break;

                var centers = BuildAOECenterCandidates(fromTile, enemies, skill, grid);

                Vector2Int? bestCenter = null;
                float best = float.NegativeInfinity;
                int bestCount = 0;

                foreach (var c in centers)
                {
                    if (!InCastRange(fromTile, c, skill)) continue;

                    int count;
                    float v = ScoreAOEValue(actor, fromTile, skill, c, enemies, profile, out count);
                    if (count <= 0) continue;

                    float s = v - risk;
                    if (s > best)
                    {
                        best = s;
                        bestCenter = c;
                        bestCount = count;
                    }
                }

                if (bestCenter.HasValue)
                {
                    outList.Add(new ActionCandidate
                    {
                        moveTile = fromTile,
                        skill = skill,
                        clickedTile = bestCenter, // ✅ RunSkill(..., clickedTile)로 넘겨야 함
                        clickedUnit = null,
                        targetCount = bestCount,
                        score = best
                    });
                }

                break;
            }

            case SkillTargetMode.AllEnemiesInRange:
            {
                int count;
                float v = ScoreMultiTargetsValue(actor, fromTile, skill, enemies, inRangeOnly: true, profile, out count);
                if (count <= 0) break;

                outList.Add(new ActionCandidate
                {
                    moveTile = fromTile,
                    skill = skill,
                    clickedTile = null,
                    clickedUnit = null,
                    targetCount = count,
                    score = v - risk
                });
                break;
            }

            case SkillTargetMode.AllEnemiesAnywhere:
            {
                int count;
                float v = ScoreMultiTargetsValue(actor, fromTile, skill, enemies, inRangeOnly: false, profile, out count);
                if (count <= 0) break;

                outList.Add(new ActionCandidate
                {
                    moveTile = fromTile,
                    skill = skill,
                    clickedTile = null,
                    clickedUnit = null,
                    targetCount = count,
                    score = v - risk
                });
                break;
            }

            case SkillTargetMode.AllAlliesInRange:
            {
                int count;
                float v = ScoreMultiTargetsValue(actor, fromTile, skill, allies, inRangeOnly: true, profile, out count);
                if (count <= 0) break;

                outList.Add(new ActionCandidate
                {
                    moveTile = fromTile,
                    skill = skill,
                    clickedTile = null,
                    clickedUnit = null,
                    targetCount = count,
                    score = v - risk
                });
                break;
            }

            case SkillTargetMode.AllAlliesAnywhere:
            {
                int count;
                float v = ScoreMultiTargetsValue(actor, fromTile, skill, allies, inRangeOnly: false, profile, out count);
                if (count <= 0) break;

                outList.Add(new ActionCandidate
                {
                    moveTile = fromTile,
                    skill = skill,
                    clickedTile = null,
                    clickedUnit = null,
                    targetCount = count,
                    score = v - risk
                });
                break;
            }
        }
    }

    // ===== Scoring =====

    static float ScoreSingleTargetValue(Unit actor, Vector2Int fromTile, SkillData skill, Unit target, AIProfile profile)
    {
        float value = EstimateSkillValue(skill, target, profile);

        // 타겟 선호(HP 낮은 타겟, 가까운 타겟)
        if (target != null && !target.IsDead)
        {
            float hp01 = target.maxHP <= 0 ? 1f : (target.currentHP / (float)target.maxHP);
            value += (1f - hp01) * profile.weightFocusLowHP;

            int d = Manhattan(fromTile, target.GridPos);
            value += Mathf.Max(0f, (10f - d)) * profile.weightNearest * 0.1f;
        }

        return value;
    }

    static float ScoreAOEValue(
        Unit actor,
        Vector2Int fromTile,
        SkillData skill,
        Vector2Int center,
        List<Unit> enemies,
        AIProfile profile,
        out int hitCount
    )
    {
        hitCount = 0;
        if (enemies == null) return 0f;

        int r = Mathf.Max(0, skill.aoeRadius);
        float sum = 0f;

        foreach (var u in enemies)
        {
            if (!IsAlive(u)) continue;
            if (Manhattan(center, u.GridPos) > r) continue;

            hitCount++;
            sum += ScoreSingleTargetValue(actor, fromTile, skill, u, profile);
        }

        return sum;
    }

    static float ScoreMultiTargetsValue(
        Unit actor,
        Vector2Int fromTile,
        SkillData skill,
        List<Unit> list,
        bool inRangeOnly,
        AIProfile profile,
        out int count
    )
    {
        count = 0;
        if (list == null) return 0f;

        float sum = 0f;

        foreach (var u in list)
        {
            if (!IsAlive(u)) continue;

            if (inRangeOnly)
            {
                int d = Manhattan(fromTile, u.GridPos);
                if (d < skill.minRange || d > skill.maxRange) continue;
            }

            count++;
            sum += ScoreSingleTargetValue(actor, fromTile, skill, u, profile);
        }

        return sum;
    }

    // 스킬의 “대략적 기대값”(현재 프로젝트 이펙트 기준)
    static float EstimateSkillValue(SkillData skill, Unit target, AIProfile profile)
    {
        float dmg = 0f;
        float heal = 0f;
        float burn = 0f;

        if (skill == null || skill.effects == null) return 0f;

        foreach (var e in skill.effects)
        {
            if (e == null) continue;

            if (e is DealDamageEffect dd) dmg += dd.damage;
            if (e is HealEffect he) heal += he.healAmount;
            if (e is BurnApplyEffect ba) burn += ba.damagePerTurn * ba.durationTurns;
        }

        float value = 0f;

        if (target != null)
        {
            if (dmg > 0f)
            {
                float capped = Mathf.Min(dmg, target.currentHP);
                value += capped * profile.weightDamage;

                if (dmg >= target.currentHP)
                    value += profile.weightKill;
            }

            if (heal > 0f)
            {
                float missing = Mathf.Max(0f, target.maxHP - target.currentHP);
                float cappedHeal = Mathf.Min(heal, missing);
                value += cappedHeal * (profile.weightDamage * 0.7f);
            }

            value += burn * profile.weightBurn;
        }
        else
        {
            value += dmg * profile.weightDamage;
            value += burn * profile.weightBurn;
            value += heal * (profile.weightDamage * 0.7f);
        }

        return value;
    }

    // 위협도(간단): "상대 유닛 중, 내 타일을 사거리 안에 넣을 수 있는 수"
    static float EstimateThreatCount(Vector2Int myTile, List<Unit> opponents, SkillData[] opponentSkillPool)
    {
        int count = 0;

        foreach (var op in opponents)
        {
            if (!IsAlive(op)) continue;

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
                int d = Manhattan(op.GridPos, myTile);
                threatens = (d <= 1);
            }

            if (threatens) count++;
        }

        return count;
    }

    // ===== AOE center candidates =====
    static List<Vector2Int> BuildAOECenterCandidates(Vector2Int fromTile, List<Unit> enemies, SkillData skill, GridManager grid)
    {
        // 전수조사 대신: "적 타일" + "적 주변(aoeRadius)"를 중심 후보로 구성
        int r = Mathf.Max(0, skill.aoeRadius);

        var set = new HashSet<Vector2Int>();
        foreach (var e in enemies)
        {
            if (!IsAlive(e)) continue;

            set.Add(e.GridPos);

            for (int dx = -r; dx <= r; dx++)
            {
                int rem = r - Mathf.Abs(dx);
                for (int dy = -rem; dy <= rem; dy++)
                {
                    var p = new Vector2Int(e.GridPos.x + dx, e.GridPos.y + dy);
                    set.Add(p);
                }
            }
        }

        // bounds + 대략 사거리 필터
        var list = new List<Vector2Int>(set.Count);
        foreach (var p in set)
        {
            if (grid != null && !grid.InBounds(p)) continue;
            if (!InCastRange(fromTile, p, skill)) continue;
            list.Add(p);
        }

        // 성능 상한
        list.Sort((a, b) => Manhattan(fromTile, a).CompareTo(Manhattan(fromTile, b)));
        const int HARD_LIMIT = 60;
        if (list.Count > HARD_LIMIT) list = list.Take(HARD_LIMIT).ToList();

        return list;
    }

    // ===== Helpers =====
    static bool IsAlive(Unit u) => u != null && !u.IsDead;

    static bool InCastRange(Vector2Int from, Vector2Int to, SkillData skill)
    {
        int d = Manhattan(from, to);
        return d >= skill.minRange && d <= skill.maxRange;
    }

    static Unit ChooseNearestInRangeFromPos(Vector2Int fromPos, List<Unit> candidates, int minR, int maxR)
    {
        Unit best = null;
        int bestDist = int.MaxValue;

        foreach (var u in candidates)
        {
            if (!IsAlive(u)) continue;

            int d = Manhattan(fromPos, u.GridPos);
            if (d < minR || d > maxR) continue;

            if (d < bestDist)
            {
                bestDist = d;
                best = u;
            }
        }

        return best;
    }

    static int Manhattan(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    static int NearestDistanceToAny(Vector2Int from, List<Unit> candidates)
    {
        int best = int.MaxValue;
        foreach (var u in candidates)
        {
            if (!IsAlive(u)) continue;
            int d = Manhattan(from, u.GridPos);
            if (d < best) best = d;
        }
        return best == int.MaxValue ? 9999 : best;
    }

    static (int desiredMin, int desiredMax) GetDesiredRangeBand(SkillData[] skills, AIProfile profile)
    {
        int bestMax = -1;
        int bestMin = 0;

        foreach (var s in skills)
        {
            if (s == null) continue;
            if (s.maxRange <= 1) continue; // 근접 제외

            if (s.maxRange > bestMax)
            {
                bestMax = s.maxRange;
                bestMin = s.minRange;
            }
        }

        if (bestMax < 0)
            return (0, 0); // 원거리 없음 → 기존 접근 정책

        int buffer = Mathf.Max(0, profile.keepRangeBuffer);
        int desiredMax = Mathf.Max(1, bestMax - buffer);
        int desiredMin = Mathf.Max(bestMin, 2); // 2칸 이내는 피하려는 기본값

        if (desiredMin > desiredMax)
            desiredMin = desiredMax;

        return (desiredMin, desiredMax);
    }

    static int DistanceOutsideBand(int d, int min, int max)
    {
        if (min == 0 && max == 0) return 0;
        if (d < min) return min - d;
        if (d > max) return d - max;
        return 0;
    }
}