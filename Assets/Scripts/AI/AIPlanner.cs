using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Utility 기반 AI 플래너.
/// 6차 목표 패치:
/// - AP(cost) 반영
/// - LOS 반영
/// - Height 이동 가능성은 GridManager reachable 결과 재사용
/// - FriendlyFirePenalty / HazardTilePenalty 반영
/// - 원거리 유닛의 "공격 가능 + 안전 우선" 폴백 적용
///
/// </summary>
public static class AIPlanner
{
    // ===== 임시 로컬 가중치 =====
    // 지금은 AIProfile에 필드가 없으므로 내부 상수로 둔다.
    // 6.5~7차에서 AIProfile로 승격 권장.
    const float COST_PENALTY_PER_AP = 0.35f;
    const float FRIENDLY_FIRE_MULTIPLIER = 1.15f;
    const float HAZARD_TILE_PENALTY = 6.0f;
    const float SELF_HAZARD_EXTRA_PENALTY = 2.5f;

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

        var usableSkills = new List<SkillData>(skillPool.Length);
        foreach (var skill in skillPool)
        {
            if (skill == null) continue;
            if (!actor.CanPayAP(skill.costAP)) continue;
            usableSkills.Add(skill);
        }

        var band = GetDesiredRangeBand(usableSkills.ToArray(), profile);

        var costs = grid.GetReachableCosts(actor, actor.moveRange);
        var tiles = BuildTileCandidates(actor, actorEnemies, usableSkills, costs, profile, band, grid);

        var candidates = new List<ActionCandidate>(128);

        foreach (var tile in tiles)
        {
            foreach (var skill in usableSkills)
            {
                EvaluateSkillAtTile(
                    actor,
                    actorAllies,
                    actorEnemies,
                    tile,
                    skill,
                    grid,
                    profile,
                    opponentSkillPool,
                    usableSkills,
                    candidates
                );
            }
        }

        var actionable = candidates.Where(c => c.targetCount > 0).ToList();
        if (actionable.Count > 0)
            candidates = actionable;

        if (candidates.Count == 0)
        {
            return BuildApproachFallback(
                actor,
                actorAllies,
                actorEnemies,
                usableSkills,
                costs,
                grid,
                profile,
                opponentSkillPool,
                band
            );
        }

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
        List<SkillData> usableSkills,
        Dictionary<Vector2Int, int> costs,
        AIProfile profile,
        (int desiredMin, int desiredMax) band,
        GridManager grid = null
    )
    {
        var scored = new List<(Vector2Int tile, int moveCost, int primary, int nearestDist, bool hasHazard, bool canAttack)>(costs.Count);

        foreach (var kv in costs)
        {
            var tile = kv.Key;

            if (grid != null && !grid.InBounds(tile))
                continue;

            int moveCost = kv.Value;
            int nearest = NearestDistanceToAny(tile, enemies);
            bool hasHazard = TileHasHazard(grid, tile);
            bool canAttack = CanAnySkillHitFromTile(actor, tile, usableSkills, actorAllies: null, actorEnemies: enemies, grid);

            int primary;
            if (canAttack)
            {
                primary = 0; // 6차: 공격 가능 타일 우선
            }
            else if (band.desiredMin == 0 && band.desiredMax == 0)
            {
                primary = nearest; // 근접: 가까워지기
            }
            else
            {
                primary = DistanceOutsideBand(nearest, band.desiredMin, band.desiredMax); // 원거리: 밴드 유지
            }

            scored.Add((tile, moveCost, primary, nearest, hasHazard, canAttack));
        }

        scored.Sort((a, b) =>
        {
            int c = a.primary.CompareTo(b.primary);
            if (c != 0) return c;

            // 위험 타일 회피 우선
            c = a.hasHazard.CompareTo(b.hasHazard);
            if (c != 0) return c;

            c = a.moveCost.CompareTo(b.moveCost);
            if (c != 0) return c;

            if (band.desiredMin == 0 && band.desiredMax == 0)
                return a.nearestDist.CompareTo(b.nearestDist);
            else
                return b.nearestDist.CompareTo(a.nearestDist);
        });

        int take = Mathf.Clamp(profile.maxTilesToEvaluate, 1, scored.Count);
        var result = scored.Take(take).Select(x => x.tile).ToList();

        if (!result.Contains(actor.GridPos))
            result.Add(actor.GridPos);

        return result;
    }

    static ActionCandidate BuildApproachFallback(
        Unit actor,
        List<Unit> allies,
        List<Unit> enemies,
        List<SkillData> usableSkills,
        Dictionary<Vector2Int, int> costs,
        GridManager grid,
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

            int nearest = NearestDistanceToAny(tile, enemies);
            float threat = EstimateThreatCount(tile, enemies, opponentSkillPool, grid);
            float hazardPenalty = GetHazardPenalty(grid, tile);
            bool canAttack = CanAnySkillHitFromTile(actor, tile, usableSkills, allies, enemies, grid);

            float score;
            if (canAttack)
            {
                // 6차: 원거리 폴백 핵심
                // "공격 가능 + 안전"이 밴드보다 우선
                score = 8f - (threat * profile.weightThreat) - hazardPenalty;
            }
            else if (band.desiredMin == 0 && band.desiredMax == 0)
            {
                score = (-nearest * profile.weightApproach)
                      - (threat * profile.weightThreat)
                      - hazardPenalty;
            }
            else
            {
                int outDist = DistanceOutsideBand(nearest, band.desiredMin, band.desiredMax);
                score = (-outDist * profile.weightKeepRange)
                      - (threat * profile.weightThreat)
                      - hazardPenalty;
            }

            // 너무 정지하지 않게 아주 작은 타이브레이커
            score += moveCost * 0.001f;
            if (tile == actor.GridPos) score -= 0.0005f;

            if (score > best.score)
            {
                best.score = score;
                best.moveTile = tile;
            }
        }

        return best;
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
        List<SkillData> ownUsableSkills,
        List<ActionCandidate> outList
    )
    {
        if (actor == null || skill == null || grid == null) return;
        if (!actor.CanPayAP(skill.costAP)) return;

        float threat = EstimateThreatCount(fromTile, enemies, opponentSkillPool, grid);
        float risk = threat * profile.weightThreat;
        float hazardPenalty = GetHazardPenalty(grid, fromTile);
        float apPenalty = skill.costAP * COST_PENALTY_PER_AP;

        switch (skill.targetMode)
        {
            case SkillTargetMode.AutoNearestSingle:
            {
                var clickedUnit = ChooseNearestValidEnemy(enemies, fromTile, skill, grid);
                if (clickedUnit == null) break;

                var targets = CombatTargetResolver.ResolveTargetsFromPosition(
                    skill,
                    actor,
                    fromTile,
                    allies,
                    enemies,
                    grid,
                    null,
                    clickedUnit
                );
                if (targets.Count <= 0) break;

                float value = ScoreResolvedTargetsValue(actor, fromTile, skill, allies, enemies, targets, profile);
                outList.Add(new ActionCandidate
                {
                    moveTile = fromTile,
                    skill = skill,
                    clickedUnit = null,
                    clickedTile = null,
                    targetCount = CountEnemyTargets(targets, enemies),
                    score = value - risk - hazardPenalty - apPenalty
                });
                break;
            }

            case SkillTargetMode.ClickSingle:
            {
                Unit bestT = null;
                float bestScore = float.NegativeInfinity;
                int bestCount = 0;

                foreach (var t in enemies)
                {
                    if (!IsAlive(t)) continue;

                    var targets = CombatTargetResolver.ResolveTargetsFromPosition(
                        skill,
                        actor,
                        fromTile,
                        allies,
                        enemies,
                        grid,
                        null,
                        t
                    );
                    if (targets.Count <= 0) continue;

                    float value = ScoreResolvedTargetsValue(actor, fromTile, skill, allies, enemies, targets, profile);
                    int count = CountEnemyTargets(targets, enemies);
                    float score = value - risk - hazardPenalty - apPenalty;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestT = t;
                        bestCount = count;
                    }
                }

                if (bestT != null)
                {
                    outList.Add(new ActionCandidate
                    {
                        moveTile = fromTile,
                        skill = skill,
                        clickedUnit = bestT,
                        clickedTile = null,
                        targetCount = bestCount,
                        score = bestScore
                    });
                }
                break;
            }

            case SkillTargetMode.ClickTileAOE:
            {
                if (enemies == null || enemies.Count == 0) break;

                var centers = BuildAOECenterCandidates(fromTile, enemies, skill, grid);

                Vector2Int? bestCenter = null;
                float bestScore = float.NegativeInfinity;
                int bestCount = 0;

                foreach (var center in centers)
                {
                    var targets = CombatTargetResolver.ResolveTargetsFromPosition(
                        skill,
                        actor,
                        fromTile,
                        allies,
                        enemies,
                        grid,
                        center,
                        null
                    );
                    
                    if (targets.Count <= 0) continue;

                    int enemyCount = CountEnemyTargets(targets, enemies);
                    if (enemyCount <= 0) continue;

                    float value = ScoreResolvedTargetsValue(actor, fromTile, skill, allies, enemies, targets, profile);
                    float score = value - risk - hazardPenalty - apPenalty;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestCenter = center;
                        bestCount = enemyCount;
                    }
                }

                if (bestCenter.HasValue)
                {
                    outList.Add(new ActionCandidate
                    {
                        moveTile = fromTile,
                        skill = skill,
                        clickedTile = bestCenter,
                        clickedUnit = null,
                        targetCount = bestCount,
                        score = bestScore
                    });
                }
                break;
            }

            case SkillTargetMode.AllEnemiesInRange:
            case SkillTargetMode.AllEnemiesAnywhere:
            case SkillTargetMode.AllAlliesInRange:
            case SkillTargetMode.AllAlliesAnywhere:
            {
                var targets = CombatTargetResolver.ResolveTargetsFromPosition(
                    skill,
                    actor,
                    fromTile,
                    allies,
                    enemies,
                    grid,
                    null,
                    null
                );
                if (targets.Count <= 0) break;

                float value = ScoreResolvedTargetsValue(actor, fromTile, skill, allies, enemies, targets, profile);

                outList.Add(new ActionCandidate
                {
                    moveTile = fromTile,
                    skill = skill,
                    clickedTile = null,
                    clickedUnit = null,
                    targetCount = CountEnemyTargets(targets, enemies),
                    score = value - risk - hazardPenalty - apPenalty
                });
                break;
            }
        }
    }

    // ===== Scoring =====

    static float ScoreResolvedTargetsValue(
        Unit actor,
        Vector2Int fromTile,
        SkillData skill,
        List<Unit> allies,
        List<Unit> enemies,
        List<Unit> targets,
        AIProfile profile
    )
    {
        if (targets == null || targets.Count == 0) return 0f;

        float sum = 0f;

        foreach (var t in targets)
        {
            if (!IsAlive(t)) continue;

            float v = ScoreSingleTargetValue(actor, fromTile, skill, t, profile);

            if (enemies != null && enemies.Contains(t))
            {
                sum += v;
            }
            else if (allies != null && allies.Contains(t))
            {
                // Friendly Fire / 아군 오타겟은 강한 감점
                sum -= v * FRIENDLY_FIRE_MULTIPLIER;
            }
        }

        return sum;
    }

    static float ScoreSingleTargetValue(Unit actor, Vector2Int fromTile, SkillData skill, Unit target, AIProfile profile)
    {
        float value = EstimateSkillValue(skill, target, profile);

        if (target != null && !target.IsDead)
        {
            float hp01 = target.maxHP <= 0 ? 1f : (target.currentHP / (float)target.maxHP);
            value += (1f - hp01) * profile.weightFocusLowHP;

            int d = Manhattan(fromTile, target.GridPos);
            value += Mathf.Max(0f, (10f - d)) * profile.weightNearest * 0.1f;
        }

        return value;
    }

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

    // ===== Threat / Hazard =====

    static float EstimateThreatCount(Vector2Int myTile, List<Unit> opponents, SkillData[] opponentSkillPool, GridManager grid)
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
                    if (!CombatTargetResolver.IsPointCastable(s, op.GridPos, myTile, grid)) continue;

                    threatens = true;
                    break;
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

    static float GetHazardPenalty(GridManager grid, Vector2Int tile)
    {
        if (grid == null) return 0f;

        var tv = grid.GetTileView(tile);
        if (tv == null || tv.tileData == null) return 0f;

        if (string.IsNullOrEmpty(tv.tileData.hazardType))
            return 0f;

        return HAZARD_TILE_PENALTY;
    }

    static bool TileHasHazard(GridManager grid, Vector2Int tile)
    {
        return GetHazardPenalty(grid, tile) > 0f;
    }

    // ===== AOE center candidates =====
    static List<Vector2Int> BuildAOECenterCandidates(Vector2Int fromTile, List<Unit> enemies, SkillData skill, GridManager grid)
    {
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

        var list = new List<Vector2Int>(set.Count);
        foreach (var p in set)
        {
            if (grid != null && !grid.InBounds(p)) continue;
            if (!CombatTargetResolver.IsPointCastable(skill, fromTile, p, grid)) continue;
            list.Add(p);
        }

        list.Sort((a, b) => Manhattan(fromTile, a).CompareTo(Manhattan(fromTile, b)));

        const int HARD_LIMIT = 60;
        if (list.Count > HARD_LIMIT)
            list = list.Take(HARD_LIMIT).ToList();

        return list;
    }

    // ===== Helpers =====

    static bool IsAlive(Unit u) => u != null && !u.IsDead;

    static int CountEnemyTargets(List<Unit> targets, List<Unit> enemies)
    {
        int count = 0;
        if (targets == null || enemies == null) return 0;

        foreach (var t in targets)
        {
            if (t != null && enemies.Contains(t))
                count++;
        }

        return count;
    }

    static bool CanAnySkillHitFromTile(
        Unit actor,
        Vector2Int fromTile,
        List<SkillData> skills,
        List<Unit> actorAllies,
        List<Unit> actorEnemies,
        GridManager grid
    )
    {
        if (skills == null || skills.Count == 0) return false;
        if (actor == null || actorEnemies == null || grid == null) return false;

        foreach (var skill in skills)
        {
            if (skill == null) continue;
            if (!actor.CanPayAP(skill.costAP)) continue;

            switch (skill.targetMode)
            {
                case SkillTargetMode.AutoNearestSingle:
                {
                    var t = ChooseNearestValidEnemy(actorEnemies, fromTile, skill, grid);
                    if (t != null) return true;
                    break;
                }

                case SkillTargetMode.ClickSingle:
                {
                    foreach (var e in actorEnemies)
                    {
                        if (!IsAlive(e)) continue;
                        var targets = CombatTargetResolver.ResolveTargetsFromPosition(
                            skill,
                            actor,
                            fromTile,
                            actorAllies,
                            actorEnemies,
                            grid,
                            null,
                            e
                        );
                        if (CountEnemyTargets(targets, actorEnemies) > 0)
                            return true;
                    }
                    break;
                }

                case SkillTargetMode.ClickTileAOE:
                {
                    var centers = BuildAOECenterCandidates(fromTile, actorEnemies, skill, grid);
                    foreach (var c in centers)
                    {
                        var targets = CombatTargetResolver.ResolveTargetsFromPosition(
                            skill,
                            actor,
                            fromTile,
                            actorAllies,
                            actorEnemies,
                            grid,
                            c,
                            null
                        );
                        if (CountEnemyTargets(targets, actorEnemies) > 0)
                            return true;
                    }
                    break;
                }

                default:
                {
                    var targets = CombatTargetResolver.ResolveTargetsFromPosition(
                            skill,
                            actor,
                            fromTile,
                            actorAllies,
                            actorEnemies,
                            grid,
                            null,
                            null
                        );
                    if (CountEnemyTargets(targets, actorEnemies) > 0)
                        return true;
                    break;
                }
            }
        }

        return false;
    }

    static Unit ChooseBestClickSingleTarget(
        Unit actor,
        List<Unit> allies,
        List<Unit> enemies,
        Vector2Int fromTile,
        SkillData skill,
        GridManager grid,
        AIProfile profile
    )
    {
        Unit best = null;
        float bestScore = float.NegativeInfinity;

        foreach (var t in enemies)
        {
            if (!IsAlive(t)) continue;

            var targets = CombatTargetResolver.ResolveTargetsFromPosition(
                skill,
                actor,
                fromTile,
                allies,
                enemies,
                grid,
                null,
                t
            );
            if (targets.Count <= 0) continue;

            float score = ScoreResolvedTargetsValue(actor, fromTile, skill, allies, enemies, targets, profile);
            if (score > bestScore)
            {
                bestScore = score;
                best = t;
            }
        }

        return best;
    }

    static Unit ChooseNearestValidEnemy(List<Unit> enemies, Vector2Int fromPos, SkillData skill, GridManager grid)
    {
        Unit best = null;
        int bestDist = int.MaxValue;

        foreach (var u in enemies)
        {
            if (!IsAlive(u)) continue;
            if (!CombatTargetResolver.IsPointCastable(skill, fromPos, u.GridPos, grid)) continue;

            int d = Manhattan(fromPos, u.GridPos);
            if (d < bestDist)
            {
                bestDist = d;
                best = u;
            }
        }

        return best;
    }
    
    static int Manhattan(Vector2Int a, Vector2Int b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

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
            if (s.maxRange <= 1) continue;

            if (s.maxRange > bestMax)
            {
                bestMax = s.maxRange;
                bestMin = s.minRange;
            }
        }

        if (bestMax < 0)
            return (0, 0);

        int buffer = Mathf.Max(0, profile.keepRangeBuffer);
        int desiredMax = Mathf.Max(1, bestMax - buffer);
        int desiredMin = Mathf.Max(bestMin, 2);

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