using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class BattleController : MonoBehaviour
{
    [Header("UX")]
    public TileHighlighter tileHighlighter;

    SkillData selectedSkill = null;
    int selectedSkillIndex = -1;
    Dictionary<Vector2Int,int> reachableMoveCache;
    Dictionary<Vector2Int, Vector2Int> reachableMoveCameFromCache;
    Vector2Int? hoverMoveTile;

    [Header("Teams")]
    public List<Unit> allies = new List<Unit>();
    public List<Unit> enemies = new List<Unit>();

    [Header("Skills")]
    public SkillData[] playerSkills;   // 플레이어(아군) 공용 슬롯 0~2
    public SkillData[] enemySkills;    // 적 공용(지금 단계)

    [Header("Enemy AI")]
    public AIProfile enemyAIProfile;

    [Header("UI")]
    public Button[] skillButtons;      // size 3
    public TMP_Text turnText;

    // ===== Turn Queue =====
    List<Unit> turnOrder = new List<Unit>();
    int turnIndex = 0;

    Unit activeUnit;
    bool waitingInput;
    bool busy;
    bool battleEnded;

    bool IsAlly(Unit u) => allies.Contains(u);
    List<Unit> GetCasterAllies(Unit caster) => IsAlly(caster) ? allies : enemies;
    List<Unit> GetCasterEnemies(Unit caster) => IsAlly(caster) ? enemies : allies;

    public GridManager grid;
    private readonly List<Unit> previewTargetUnits = new List<Unit>(16);

    public bool IsBusy => busy;
    public bool IsWaitingInput => waitingInput;

    // 입력 모드
    public enum PlayerInputMode
    {
        Move,
        SkillPreview
    }
    private PlayerInputMode inputMode = PlayerInputMode.Move;

    private bool hasMovedThisTurn = false;

    public PlayerInputMode InputMode => inputMode;
    public SkillData SelectedSkill => selectedSkill;
    public Unit ActiveUnit => activeUnit;
    private Vector2Int? hoverTile = null;

    void Start()
    {
        SetupSkillButtons();
        StartBattle();
    }
    void Update()
    {
        if (!waitingInput || busy) return;

        if (selectedSkill != null &&
            Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame)
        {   
            selectedSkill = null;
            selectedSkillIndex = -1;
            ClearPreviewTargetIndicators();
            ClearHoverAOEPreview();

            waitingInput = true;
            inputMode = PlayerInputMode.Move;

            if (!hasMovedThisTurn)
            {
                // 4) 이동 캐시 갱신 + Blue 표시 (표시=판정)
                if (tileHighlighter) tileHighlighter.ShowReachableMoveTiles(activeUnit);
            }
            else
            {
                // 이미 이동했으면 이동 범위는 다시 보여주지 않음
                if (tileHighlighter) tileHighlighter.ClearAll();
            }   
        }
    }

    void SetupSkillButtons()
    {
        if (skillButtons == null) return;

        for (int i = 0; i < skillButtons.Length; i++)
        {
            int idx = i;
            if (skillButtons[i] == null) continue;

            skillButtons[i].onClick.RemoveAllListeners();
            skillButtons[i].onClick.AddListener(() => UseSkill(idx));

            // 버튼 텍스트 자동 세팅(선택)
            var label = skillButtons[i].GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                var s = (playerSkills != null && idx < playerSkills.Length) ? playerSkills[idx] : null;
                label.text = s != null ? s.skillName : "-";
            }
        }
    }

    void StartBattle()
    {
        // grid.TryPlace(allies[0], new Vector2Int(1, 2));
        // // 적 3마리
        // grid.TryPlace(enemies[0], new Vector2Int(6, 2));
        // grid.TryPlace(enemies[1], new Vector2Int(6, 3));
        // grid.TryPlace(enemies[2], new Vector2Int(6, 1));

        battleEnded = false;
        waitingInput = false;
        busy = false;

        if (!grid) grid = GridManager.I;
        if (!tileHighlighter) tileHighlighter = FindObjectOfType<TileHighlighter>();

        foreach (var a in allies)
        if (a != null)
            a.isAlly = true;

        foreach (var e in enemies)
            if (e != null)
                e.isAlly = false;

        // ✅ 초기 배치(원하는 대로 바꿔도 됨)
        if (grid != null)
        {
            // 아군은 왼쪽, 적은 오른쪽
            for (int i = 0; i < allies.Count; i++)
                if (allies[i] != null && !allies[i].IsDead)
                    grid.TryPlace(allies[i], new Vector2Int(1, 1 + i));

            for (int i = 0; i < enemies.Count; i++)
                if (enemies[i] != null && !enemies[i].IsDead)
                    grid.TryPlace(enemies[i], new Vector2Int(grid.width - 2, 1 + i));
        }
        
        BuildTurnOrder();
        UpdateTurnOrderHud();
        turnIndex = 0;
        StartNextTurn();
    }

    // =========================
    // Turn Queue (Round-based Speed Order)
    // =========================
    void BuildTurnOrder()
    {   
        turnOrder.Clear();

        // 살아있는 유닛만 모음 (아군+적군)
        foreach (var u in allies)
            if (u != null && !u.IsDead)
                turnOrder.Add(u);

        foreach (var u in enemies)
            if (u != null && !u.IsDead)
                turnOrder.Add(u);

        // speed 내림차순, 동률이면 instanceID 오름차순(안정적)
        turnOrder.Sort((a, b) =>
        {
            int sp = b.speed.CompareTo(a.speed);
            if (sp != 0) return sp;
            return a.GetInstanceID().CompareTo(b.GetInstanceID());
        });
        UpdateTurnOrderHud();

    }

    void StartNextTurn()
    {
        if (battleEnded) return;

        // 승패 체크
        if (IsTeamDead(enemies))
        {
            EndBattle("YOU WIN");
            return;
        }
        if (IsTeamDead(allies))
        {
            EndBattle("YOU LOSE");
            return;
        }

        // 현재 라운드 소진 / 비어있으면 재생성
        if (turnOrder.Count == 0 || turnIndex >= turnOrder.Count)
        {
            BuildTurnOrder();
            turnIndex = 0;
        }

        // 다음 살아있는 유닛 찾기(죽었으면 스킵)
        activeUnit = null;
        while (turnIndex < turnOrder.Count)
        {
            var cand = turnOrder[turnIndex];
            if (cand != null && !cand.IsDead)
            {
                activeUnit = cand;                
                break;
            }
            turnIndex++;
        }
        
        activeUnit.OnTurnStart();
        if (activeUnit.IsDead)
        {
            // 턴 시작 도중 죽었으면 이 유닛 행동 없이 다음으로
            turnIndex++;
            StartNextTurn();
            return;
        }

        // 라운드 끝(전부 죽었거나 스킵되었음) → 다음 라운드
        if (activeUnit == null)
        {
            BuildTurnOrder();
            turnIndex = 0;
            StartNextTurn();
            return;
        }

        busy = false;
        waitingInput = false;

        if (turnText)
            turnText.text = $"{activeUnit.name} TURN (SPD {activeUnit.speed})";

        if (IsAlly(activeUnit))
        {
            // 플레이어 입력 턴
            waitingInput = true;
            hasMovedThisTurn = false;
            SetSkillButtonsInteractable(true);
            var data = grid.GetReachableData(activeUnit, activeUnit.moveRange);
            reachableMoveCache = data.cost;
            reachableMoveCameFromCache = data.cameFrom;
            inputMode = PlayerInputMode.Move;
            // ✅ 입력 대기 상태에서만 표시
            if (tileHighlighter) tileHighlighter.ShowMoveTiles(reachableMoveCache.Keys);
        }
        else
        {
            // 적 AI 턴
            SetSkillButtonsInteractable(false);
            busy = true;

            StartCoroutine(EnemyTurnRoutine(activeUnit, OnActionComplete));
        }
    }

    void OnActionComplete()
    {   
        ClearHoverAOEPreview();
        selectedSkill = null;
        selectedSkillIndex = -1;
        if (tileHighlighter) tileHighlighter.ClearAll();
        ClearPreviewTargetIndicators();
        // 이번 행동자 처리 완료 → 다음 유닛
        busy = false;
        waitingInput = false;
        if (tileHighlighter) tileHighlighter.ClearAll();

        if (activeUnit != null && !activeUnit.IsDead)
            activeUnit.OnTurnEnd();

        turnIndex++;
        StartNextTurn();
    }

    void EndBattle(string msg)
    {
        battleEnded = true;
        waitingInput = false;
        busy = false;
        SetSkillButtonsInteractable(false);

        if (turnText) turnText.text = msg;
    }

    bool IsTeamDead(List<Unit> team)
    {
        foreach (var u in team)
            if (u != null && !u.IsDead)
                return false;
        return true;
    }

    // =========================
    // Player Input
    // =========================
    public void UseSkill(int skillIndex)
    {
        if (battleEnded) return;
        if (!waitingInput || busy) return;

        if (playerSkills == null) return;
        if (skillIndex < 0 || skillIndex >= playerSkills.Length) return;

        SkillData skill = playerSkills[skillIndex];
        if (skill == null) return;

        if (selectedSkill == skill && selectedSkillIndex == skillIndex)
        {
            if (tileHighlighter) tileHighlighter.ClearAll();
            ClearPreviewTargetIndicators();
            if (tileHighlighter) tileHighlighter.ClearAll();

            selectedSkill = null;
            selectedSkillIndex = -1;

            waitingInput = false;
            busy = true;
            SetSkillButtonsInteractable(false);

            StartCoroutine(RunSkill(activeUnit, skill, OnActionComplete));
            return;
        }

        // ✅ 1클릭: 선택 + 사거리 표시(RED)
        selectedSkill = skill;
        selectedSkillIndex = skillIndex;
        inputMode = PlayerInputMode.SkillPreview;

        // (필드명이 다르면 여기만 바꿔)
        int minR = skill.minRange;
        int maxR = skill.maxRange;

        // 하이라이트 초기화
        if (tileHighlighter) tileHighlighter.ClearAll();
        reachableMoveCache = null;
        reachableMoveCameFromCache = null;
        // 1클릭: 사거리 표시(RED)
        var tiles = BuildManhattanRangeTiles(activeUnit.GridPos, minR, maxR);
        if (tileHighlighter) tileHighlighter.ShowRangeTiles(tiles);
        ClearPreviewTargetIndicators();
        ClearHoverAOEPreview();
        // ✅ RunSkill과 동일 로직으로 타겟 계산
        var targets = ResolveTargets(skill, activeUnit, GetCasterAllies(activeUnit), GetCasterEnemies(activeUnit), null, null);
        Debug.Log($"[UseSkill Preview] skill={skill.skillName}, mode={skill.targetMode}, legacyType={skill.targetType}, min={skill.minRange}, max={skill.maxRange}, allies={GetCasterAllies(activeUnit).Count}, enemies={GetCasterEnemies(activeUnit).Count}, targets={targets.Count}");

        // ✅ 타겟 유닛 HUD 아이콘 켜기
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null || t.IsDead) continue;

            var hud = GetHud(t);
            if (hud) hud.SetTargeted(true);

            previewTargetUnits.Add(t);
        }
        // ✅ 3단계: 실제 타겟 강조(진한 RED)
        var targetTiles = GetPreviewTargetTiles(skill, activeUnit);
        if (tileHighlighter) tileHighlighter.ShowTargetTiles(targetTiles);
    }

    SkillData ChooseEnemySkill()
    {
        if (enemySkills == null || enemySkills.Length == 0)
            return null;

        return enemySkills[Random.Range(0, enemySkills.Length)];
    }

    // =========================
    // Core Skill Execution (Target system included)
    // =========================
    IEnumerator RunSkill(Unit attacker, SkillData skill, System.Action onComplete)
    {
        yield return RunSkill(attacker, skill, onComplete, null, null);
    }

    IEnumerator RunSkill(Unit attacker, SkillData skill, System.Action onComplete, Vector2Int? clickedTile, Unit clickedUnit)
    {
        if (attacker == null || skill == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        var targets = ResolveTargets(skill, attacker, GetCasterAllies(attacker), GetCasterEnemies(attacker), clickedTile, clickedUnit);
        if (targets.Count == 0)
        {
            onComplete?.Invoke();
            yield break;
        }

        bool hitDone = false;
        bool endDone = false;

        void OnHit()
        {
            if (hitDone) return;
            hitDone = true;
            foreach (var t in targets)
                if (t != null && !t.IsDead)
                    ApplySkillEffects(skill, attacker, t);
        }

        void OnEnd() { endDone = true; }

        attacker.AttackHitEvent += OnHit;
        attacker.AttackEndEvent += OnEnd;

        attacker.PlayAttack(skill.animationTrigger);

        float timeout = 1.5f;
        float t = 0f;

        if (skill.timing == SkillTiming.Immediate)
        {
            foreach (var tt in targets)
                if (tt != null && !tt.IsDead)
                    ApplySkillEffects(skill, attacker, tt);
            hitDone = true;
        }
        else if (skill.timing == SkillTiming.OnAttackHit)
        {
            while (!hitDone && t < timeout) { t += Time.deltaTime; yield return null; }
        }
        else if (skill.timing == SkillTiming.OnAttackEnd)
        {
            while (!endDone && t < timeout) { t += Time.deltaTime; yield return null; }
            if (!hitDone)
            {
                foreach (var tt in targets)
                    if (tt != null && !tt.IsDead)
                        ApplySkillEffects(skill, attacker, tt);
                hitDone = true;
            }
        }

        t = 0f;
        while (!endDone && t < timeout) { t += Time.deltaTime; yield return null; }

        attacker.AttackHitEvent -= OnHit;
        attacker.AttackEndEvent -= OnEnd;

        onComplete?.Invoke();
    }

    void ApplySkillEffects(SkillData skill, Unit attacker, Unit defender)
    {
        foreach (var effect in skill.effects)
            effect.Apply(attacker, defender);
    }

    void SetSkillButtonsInteractable(bool v)
    {
        if (skillButtons == null) return;
        foreach (var b in skillButtons)
            if (b != null) b.interactable = v;
    }

    // =========================
    // Target resolver
    // =========================
    List<Unit> ResolveTargets(
        SkillData skill,
        Unit caster,
        List<Unit> allies,
        List<Unit> enemies,
        Vector2Int? clickedTile = null,
        Unit clickedUnit = null
    )
    {
        var targets = new List<Unit>();
        if (skill == null || caster == null) return targets;

        int Dist(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

        bool InCastRange(Vector2Int from, Vector2Int to)
        {
            int d = Dist(from, to);
            return d >= skill.minRange && d <= skill.maxRange;
        }

        IEnumerable<Unit> Alive(IEnumerable<Unit> list)
        {
            foreach (var u in list)
                if (u != null && !u.IsDead)
                    yield return u;
        }

        Unit NearestInRange(IEnumerable<Unit> candidates)
        {
            Unit best = null;
            int bestDist = int.MaxValue;

            foreach (var u in Alive(candidates))
            {
                int d = Dist(caster.GridPos, u.GridPos);
                if (d < skill.minRange || d > skill.maxRange) continue;

                if (d < bestDist)
                {
                    bestDist = d;
                    best = u;
                }
            }
            return best;
        }

        void AddAOEFromCenter(Vector2Int center, IEnumerable<Unit> candidates)
        {
            int r = Mathf.Max(0, skill.aoeRadius);
            foreach (var u in Alive(candidates))
            {
                if (Dist(center, u.GridPos) <= r)
                    targets.Add(u);
            }
        }

        switch (skill.targetMode)
        {
            case SkillTargetMode.AutoNearestSingle:
            {
                var t = NearestInRange(enemies);
                if (t != null) targets.Add(t);
                break;
            }

            case SkillTargetMode.ClickSingle:
            {
                // 확정(클릭)
                if (clickedUnit != null && !clickedUnit.IsDead)
                {
                    if (!allies.Contains(clickedUnit) && InCastRange(caster.GridPos, clickedUnit.GridPos))
                        targets.Add(clickedUnit);
                    break;
                }

                // 프리뷰(후보)
                foreach (var e in Alive(enemies))
                    if (InCastRange(caster.GridPos, e.GridPos))
                        targets.Add(e);
                break;
            }

            case SkillTargetMode.ClickTileAOE:
            {
                if (!clickedTile.HasValue) break;
                var center = clickedTile.Value;

                if (!InCastRange(caster.GridPos, center)) break;

                AddAOEFromCenter(center, enemies);
                break;
            }

            case SkillTargetMode.AllEnemiesInRange:
            {
                foreach (var e in Alive(enemies))
                    if (InCastRange(caster.GridPos, e.GridPos))
                        targets.Add(e);
                break;
            }

            case SkillTargetMode.AllEnemiesAnywhere:
            {
                targets.AddRange(Alive(enemies));
                break;
            }

            case SkillTargetMode.AllAlliesInRange:
            {
                foreach (var a in Alive(allies))
                    if (InCastRange(caster.GridPos, a.GridPos))
                        targets.Add(a);
                break;
            }

            case SkillTargetMode.AllAlliesAnywhere:
            {
                targets.AddRange(Alive(allies));
                break;
            }
        }

        return targets;
    }

    Unit ChooseFirstAlive(List<Unit> list)
    {
        foreach (var u in list)
            if (u != null && !u.IsDead)
                return u;
        return null;
    }

    void UpdateTurnOrderHud()
    {
        for (int i = 0; i < turnOrder.Count; i++)
        {
            var u = turnOrder[i];
            if (u == null || u.IsDead) continue;

            var hud = u.GetComponentInChildren<UnitHud>();
            if (hud != null)
                hud.SetTurnInfo($"#{i+1}  SPD {u.speed}");
        }
    }
    Unit ChooseNearestInRange(Unit caster, List<Unit> candidates, int minR, int maxR)
    {
        Unit best = null;
        int bestDist = int.MaxValue;

        foreach (var u in candidates)
        {
            if (u == null || u.IsDead) continue;

            int d = Mathf.Abs(caster.GridPos.x - u.GridPos.x) + Mathf.Abs(caster.GridPos.y - u.GridPos.y);
            if (d < minR || d > maxR) continue;

            if (d < bestDist)
            {
                bestDist = d;
                best = u;
            }
        }
        return best;
    }

    IEnumerator EnemyTurnRoutine(Unit enemy, System.Action onComplete)
    {
        if (enemy == null || enemy.IsDead)
        {
            onComplete?.Invoke();
            yield break;
        }

        if (!grid) grid = GridManager.I;

        var profile = (enemy.aiProfile != null) ? enemy.aiProfile : enemyAIProfile;

        // ✅ 유닛 전용 스킬풀 우선
        var myPool = GetSkillPoolFor(enemy);

        if (profile == null)
        {
            // 프로파일 없으면: 유닛 전용 풀에서 랜덤 스킬로 폴백
            SkillData fallback = (myPool != null && myPool.Length > 0)
                ? myPool[UnityEngine.Random.Range(0, myPool.Length)]
                : null;

            if (fallback == null)
            {
                onComplete?.Invoke();
                yield break;
            }

            yield return RunSkill(enemy, fallback, onComplete);
            yield break;
        }

        // Planner 입력 구성
        var alliesOfEnemy = GetCasterAllies(enemy);
        var enemiesOfEnemy = GetCasterEnemies(enemy);

        var plan = AIPlanner.Plan(
            enemy,
            alliesOfEnemy,
            enemiesOfEnemy,
            myPool,        // ✅ 여기!
            grid,
            profile,
            playerSkills   // 적 기준 상대 스킬풀(Threat 계산용)
        );
        //평가 디버그
        Debug.Log($"[AI PLAN] {enemy.name} tile={plan.moveTile} skill={(plan.skill ? plan.skill.skillName : "null")} target={(plan.target ? plan.target.name : "null")} score={plan.score:0.00}");
        if (plan.moveTile != enemy.GridPos)
        {
                var path = grid.FindPathWithinRange(enemy, plan.moveTile, enemy.moveRange);
                if (path != null)
                    yield return StartCoroutine(grid.MovePathRoutine(enemy, path));
        }    

        yield return RunSkill(enemy, plan.skill, onComplete);
    }

    bool TryPickBestAttackTile(Unit enemy, SkillData skill, Dictionary<Vector2Int,int> costs, out Vector2Int bestTile)
    {
        bestTile = enemy.GridPos;

        var enemies = GetCasterEnemies(enemy); // enemy 기준 "적" = 플레이어팀(=allies)
        Unit bestTarget = null;
        int bestMoveCost = int.MaxValue;
        int bestTargetDist = int.MaxValue;

        foreach (var kv in costs)
        {
            Vector2Int tile = kv.Key;
            int moveCost = kv.Value;

            // 이 타일에서 공격 가능한 타겟(최소 거리) 찾기
            Unit t = ChooseNearestInRangeFromPos(tile, enemies, skill.minRange, skill.maxRange, out int dist);
            if (t == null) continue;

            // 우선순위:
            // 1) 이동 비용 최소
            // 2) (동률) 타겟까지 거리 최소
            if (moveCost < bestMoveCost || (moveCost == bestMoveCost && dist < bestTargetDist))
            {
                bestMoveCost = moveCost;
                bestTargetDist = dist;
                bestTarget = t;
                bestTile = tile;
            }
        }

        return bestTarget != null;
    }

    bool TryPickBestApproachTile(Unit enemy, Dictionary<Vector2Int,int> costs, out Vector2Int bestTile)
    {
        bestTile = enemy.GridPos;

        var enemies = GetCasterEnemies(enemy);
        int bestNearestDist = int.MaxValue;
        int bestMoveCost = int.MaxValue;

        foreach (var kv in costs)
        {
            Vector2Int tile = kv.Key;
            int moveCost = kv.Value;

            int nearest = NearestDistanceToAny(tile, enemies);
            if (nearest == int.MaxValue) continue;

            // 우선순위:
            // 1) 가장 가까워지는(거리 최소)
            // 2) (동률) 이동 비용 최소
            if (nearest < bestNearestDist || (nearest == bestNearestDist && moveCost < bestMoveCost))
            {
                bestNearestDist = nearest;
                bestMoveCost = moveCost;
                bestTile = tile;
            }
        }

        return true;
    }

    Unit ChooseNearestInRangeFromPos(Vector2Int fromPos, List<Unit> candidates, int minR, int maxR, out int bestDist)
    {
        Unit best = null;
        bestDist = int.MaxValue;

        foreach (var u in candidates)
        {
            if (u == null || u.IsDead) continue;

            int d = Mathf.Abs(fromPos.x - u.GridPos.x) + Mathf.Abs(fromPos.y - u.GridPos.y);
            if (d < minR || d > maxR) continue;

            if (d < bestDist)
            {
                bestDist = d;
                best = u;
            }
        }

        return best;
    }

    int NearestDistanceToAny(Vector2Int fromPos, List<Unit> candidates)
    {
        int best = int.MaxValue;
        foreach (var u in candidates)
        {
            if (u == null || u.IsDead) continue;
            int d = Mathf.Abs(fromPos.x - u.GridPos.x) + Mathf.Abs(fromPos.y - u.GridPos.y);
            if (d < best) best = d;
        }
        return best;
    }

    SkillData[] GetSkillPoolFor(Unit u)
    {
        if (u != null && u.skillPoolOverride != null && u.skillPoolOverride.Length > 0)
            return u.skillPoolOverride;

        // 폴백
        return IsAlly(u) ? playerSkills : enemySkills;
    }

    private List<Vector2Int> BuildManhattanRangeTiles(Vector2Int origin, int minR, int maxR)
    {
        var list = new List<Vector2Int>();
        if (!grid) grid = GridManager.I;
        if (!grid) return list;

        if (minR < 0) minR = 0;
        if (maxR < minR) maxR = minR;

        for (int d = minR; d <= maxR; d++)
        {
            for (int dx = -d; dx <= d; dx++)
            {
                int dy = d - Mathf.Abs(dx);

                var p1 = new Vector2Int(origin.x + dx, origin.y + dy);
                if (grid.InBounds(p1)) list.Add(p1);

                if (dy != 0)
                {
                    var p2 = new Vector2Int(origin.x + dx, origin.y - dy);
                    if (grid.InBounds(p2)) list.Add(p2);
                }
            }
        }

        return list;
    }

    List<Vector2Int> GetPreviewTargetTiles(SkillData skill, Unit attacker)
    {
        // RunSkill과 100% 동일한 입력으로 타겟 계산
        var targets = ResolveTargets(skill, attacker, GetCasterAllies(attacker), GetCasterEnemies(attacker), null, null);

        // 중복 제거 + 죽은 유닛 제외
        var set = new HashSet<Vector2Int>();
        for (int i = 0; i < targets.Count; i++)
        {
            var u = targets[i];
            if (u == null || u.IsDead) continue;
            set.Add(u.GridPos);
        }

        return new List<Vector2Int>(set);
    }

    UnitHud GetHud(Unit u)
    {
        if (u == null) return null;
        var hud = u.GetComponentInChildren<UnitHud>(true); // true: 비활성 오브젝트도 검색
        if (hud == null)
            Debug.LogWarning($"[UX] UnitHud NOT FOUND on {u.name}");
        return hud;
    }

    void ClearPreviewTargetIndicators()
    {
        for (int i = 0; i < previewTargetUnits.Count; i++)
        {
            var u = previewTargetUnits[i];
            var hud = GetHud(u);
            if (hud) hud.SetTargeted(false);
        }
        previewTargetUnits.Clear();
    }

    private IEnumerator PlayerMoveRoutine(Vector2Int to)
    {
        busy = true;
        waitingInput = false;
        SetSkillButtonsInteractable(false);

        // 이동 하이라이트 제거
        if (tileHighlighter) tileHighlighter.ClearAll();

        // // 실제 이동: 최단 경로를 따라 1타일씩 이동 (장애물 관통 방지)
        // var path = grid.FindPathWithinRange(activeUnit, to, activeUnit.moveRange);
        // if (path == null)
        // {
        //     // 도달 불가면 입력 상태로 복귀
        //     busy = false;
        //     waitingInput = true;
        //     SetSkillButtonsInteractable(true);
        //     yield break;
        // }

        // 실제 이동: 캐시된 cameFrom으로 최단 경로 복원 후 경로 따라 이동
        var path = grid.ReconstructPath(activeUnit.GridPos, to, reachableMoveCameFromCache);
        if (path == null)
        {
            // 도달 불가(캐시가 없거나 목표가 범위 밖 등)면 입력 복귀
            busy = false;
            waitingInput = true;
            SetSkillButtonsInteractable(true);
            yield break;
        }
        yield return StartCoroutine(grid.MovePathRoutine(activeUnit, path));

        // 이동 후: "행동 선택 상태"로 복귀
        busy = false;
        waitingInput = true;
        SetSkillButtonsInteractable(true);

        // 다음 UX: 여기서 “이동 후 다시 이동 Blue를 보여줄지” 정책 선택
        // 일반적으로는 이동을 한 번 했으면 이동 표시를 다시 안 띄우고,
        // 스킬/대기 중 하나를 선택하게 두는 게 턴제가 깔끔함.

        // 스킬 선택 모드 초기화
        hasMovedThisTurn = true;
        inputMode = PlayerInputMode.SkillPreview; // 또는 Move가 아닌 상태로(스킬/대기 선택 단계)
        reachableMoveCache = null;
        reachableMoveCameFromCache = null;
    }

    public void OnUnitClicked(Unit clicked)
    {
        if (battleEnded) return;
        if (!waitingInput || busy) return;
        if (activeUnit == null || activeUnit.IsDead) return;
        if (!IsAlly(activeUnit)) return;

        if (inputMode != PlayerInputMode.SkillPreview) return;
        if (selectedSkill == null) return;
        if (clicked == null || clicked.IsDead) return;

        // ClickSingle에서만 유닛 클릭을 사용
        if (selectedSkill.targetMode != SkillTargetMode.ClickSingle)
            return;

        // ✅ ResolveTargets로 클릭 검증 (clickedUnit 넣어서 확정 타겟 계산)
        var resolved = ResolveTargets(selectedSkill, activeUnit, GetCasterAllies(activeUnit), GetCasterEnemies(activeUnit), null, clicked);
        if (resolved.Count == 0) return;

        // UX 정리
        if (tileHighlighter) tileHighlighter.ClearAll();
        ClearPreviewTargetIndicators();
        ClearHoverAOEPreview();
        waitingInput = false;
        busy = true;
        SetSkillButtonsInteractable(false);

        StartCoroutine(RunSkill(activeUnit, selectedSkill, OnActionComplete, null, clicked));
    }

    public void OnTileClicked(Vector2Int gridPos)
    {
        if (battleEnded) return;
        if (!waitingInput || busy) return;
        if (activeUnit == null || activeUnit.IsDead) return;
        if (!IsAlly(activeUnit)) return;

        // ✅ 스킬 AOE 타일 클릭 처리
        if (inputMode == PlayerInputMode.SkillPreview && selectedSkill != null &&
            selectedSkill.targetMode == SkillTargetMode.ClickTileAOE)
        {
            var resolved = ResolveTargets(selectedSkill, activeUnit, GetCasterAllies(activeUnit), GetCasterEnemies(activeUnit), gridPos, null);
            if (resolved.Count == 0) return;

            if (tileHighlighter) tileHighlighter.ClearAll();
            ClearPreviewTargetIndicators();
            ClearHoverAOEPreview();
            waitingInput = false;
            busy = true;
            SetSkillButtonsInteractable(false);

            StartCoroutine(RunSkill(activeUnit, selectedSkill, OnActionComplete, gridPos, null));
            return;
        }

        // ===== 이동 처리(기존 유지) =====
        if (hasMovedThisTurn) return;
        if (inputMode != PlayerInputMode.Move) return;

        if (reachableMoveCache == null || reachableMoveCameFromCache == null)
        {
            var data = grid.GetReachableData(activeUnit, activeUnit.moveRange);
            reachableMoveCache = data.cost;
            reachableMoveCameFromCache = data.cameFrom;
        }

        if (!reachableMoveCache.ContainsKey(gridPos)) return;
        if (gridPos == activeUnit.GridPos) return;

        StartCoroutine(PlayerMoveRoutine(gridPos));
    }
    public void OnHoverTile(Vector2Int gridPos)
    {
        // Hover는 "스킬 미리보기 + ClickTileAOE"에서만 동작
        if (battleEnded) return;
        if (activeUnit == null || activeUnit.IsDead) return;
        if (inputMode != PlayerInputMode.SkillPreview) return;
        if (selectedSkill == null) return;
        if (selectedSkill.targetMode != SkillTargetMode.ClickTileAOE) return;
        if (tileHighlighter == null) return;

        if (hoverTile.HasValue && hoverTile.Value == gridPos) return;
        hoverTile = gridPos;

        // 중심 타일이 사거리 내인지 체크 (사거리 밖이면 표시 제거)
        int d = Mathf.Abs(activeUnit.GridPos.x - gridPos.x) + Mathf.Abs(activeUnit.GridPos.y - gridPos.y);
        if (d < selectedSkill.minRange || d > selectedSkill.maxRange)
        {
            tileHighlighter.ClearTarget();
            return;
        }

        // aoeRadius (Manhattan) 기반 타겟 타일 계산
        var tiles = BuildManhattanDisk(gridPos, Mathf.Max(0, selectedSkill.aoeRadius));
        tileHighlighter.ShowTargetTiles(tiles);
    }

    public void ClearHoverAOEPreview()
    {
        hoverTile = null;
        if (tileHighlighter != null)
            tileHighlighter.ClearTarget();
    }

    List<Vector2Int> BuildManhattanDisk(Vector2Int center, int radius)
    {
        var list = new List<Vector2Int>( (radius * 2 + 1) * (radius * 2 + 1) );
        for (int dx = -radius; dx <= radius; dx++)
        {
            int rem = radius - Mathf.Abs(dx);
            for (int dy = -rem; dy <= rem; dy++)
            {
                var p = new Vector2Int(center.x + dx, center.y + dy);
                list.Add(p);
            }
        }
        return list;
    }

    public void OnHoverMoveTile(Vector2Int gridPos)
    {
        if (battleEnded) return;
        if (activeUnit == null || activeUnit.IsDead) return;
        if (inputMode != PlayerInputMode.Move) return;
        if (tileHighlighter == null) return;

        // 캐시 없으면(정상 흐름이면 거의 없음) 생성
        if (reachableMoveCache == null || reachableMoveCameFromCache == null)
        {
            var data = grid.GetReachableData(activeUnit, activeUnit.moveRange);
            reachableMoveCache = data.cost;
            reachableMoveCameFromCache = data.cameFrom;
        }

        // 도달 가능 타일만 프리뷰
        if (!reachableMoveCache.ContainsKey(gridPos))
        {
            tileHighlighter.ClearTarget();
            hoverMoveTile = null;
            return;
        }

        // 본인 타일이면 프리뷰 끔
        if (gridPos == activeUnit.GridPos)
        {
            tileHighlighter.ClearTarget();
            hoverMoveTile = null;
            return;
        }

        if (hoverMoveTile.HasValue && hoverMoveTile.Value == gridPos) return;
        hoverMoveTile = gridPos;

        // ✅ 최단 경로 복원 (start 제외, goal 포함)
        var path = grid.ReconstructPath(activeUnit.GridPos, gridPos, reachableMoveCameFromCache);
        if (path == null || path.Count == 0)
        {
            tileHighlighter.ClearTarget();
            return;
        }

        // Move 모드에서는 highlight 레이어를 "경로 라인"으로 재활용
        tileHighlighter.ShowMoveTiles(path);
    }

    public void ClearHoverMovePathPreview()
    {
        hoverMoveTile = null;
        if (tileHighlighter != null)
            tileHighlighter.ClearTarget();
    }
}
