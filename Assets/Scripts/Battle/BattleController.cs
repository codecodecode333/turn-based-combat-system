using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

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

    private int cachedMoveRange = -1;
    private int cachedStatusRevision = -1;

    private bool isCounterAttackInProgress = false;

    public bool HasLockedAOETarget =>
        PlannedSkill != null &&
        PlannedSkill.targetMode == SkillTargetMode.ClickTileAOE &&
        PlannedClickedTile.HasValue;

    private struct HazardResolveResult
    {
        public bool triggered;
        public bool stopMovement;
        public bool consumedTile;
    }
    // =========================
    // Action Queue
    // =========================

    public enum PlannedActionType
    {
        Move,
        Skill
    }

    [System.Serializable]
    public class PlannedAction
    {
        public PlannedActionType type;

        // Move
        public Vector2Int moveTile;

        // Skill
        public SkillData skill;
        public int skillIndex = -1;
        public Vector2Int? clickedTile;
        public Unit clickedUnit;

        public static PlannedAction CreateMove(Vector2Int tile)
        {
            return new PlannedAction
            {
                type = PlannedActionType.Move,
                moveTile = tile
            };
        }

        public static PlannedAction CreateSkill(
            SkillData skill,
            int skillIndex,
            Vector2Int? clickedTile,
            Unit clickedUnit)
        {
            return new PlannedAction
            {
                type = PlannedActionType.Skill,
                skill = skill,
                skillIndex = skillIndex,
                clickedTile = clickedTile,
                clickedUnit = clickedUnit
            };
        }
    }

    private readonly List<PlannedAction> actionQueue = new List<PlannedAction>(2);

    private PlannedAction MoveAction
    {
        get
        {
            for (int i = 0; i < actionQueue.Count; i++)
            {
                if (actionQueue[i].type == PlannedActionType.Move)
                    return actionQueue[i];
            }
            return null;
        }
    }

    private PlannedAction SkillAction
    {
        get
        {
            for (int i = 0; i < actionQueue.Count; i++)
            {
                if (actionQueue[i].type == PlannedActionType.Skill)
                    return actionQueue[i];
            }
            return null;
        }
    }

    private Vector2Int? PlannedMoveTile => MoveAction != null ? MoveAction.moveTile : (Vector2Int?)null;
    private SkillData PlannedSkill => SkillAction != null ? SkillAction.skill : null;
    private int PlannedSkillIndex => SkillAction != null ? SkillAction.skillIndex : -1;
    private Vector2Int? PlannedClickedTile => SkillAction != null ? SkillAction.clickedTile : (Vector2Int?)null;
    private Unit PlannedClickedUnit => SkillAction != null ? SkillAction.clickedUnit : null;

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

    void SetupSkillButtons()
    {
        if (skillButtons == null) return;

        var pool = GetSkillPoolFor(activeUnit);

        for (int i = 0; i < skillButtons.Length; i++)
        {
            int idx = i;
            if (skillButtons[i] == null) continue;

            skillButtons[i].onClick.RemoveAllListeners();
            skillButtons[i].onClick.AddListener(() => UseSkill(idx));

            var label = skillButtons[i].GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                var s = (pool != null && idx < pool.Length) ? pool[idx] : null;
                label.text = s != null ? s.skillName : "-";
            }

            skillButtons[i].interactable = (pool != null && idx < pool.Length && pool[idx] != null);
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
        reachableMoveCache = null;
        reachableMoveCameFromCache = null;
        if (battleEnded) return;

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

        if (turnOrder.Count == 0 || turnIndex >= turnOrder.Count)
        {
            BuildTurnOrder();
            turnIndex = 0;
        }

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

        if (activeUnit == null)
        {
            BuildTurnOrder();
            turnIndex = 0;
            StartNextTurn();
            return;
        }

        // 핵심: tick 전에 이번 턴 행동 가능 여부를 캡처
        bool canActAtTurnStart = activeUnit.CanAct();
        bool canMoveAtTurnStart = activeUnit.CanMove();

        activeUnit.OnTurnStart();
        ApplyTileHazardOnTurnStart(activeUnit);

        if (activeUnit == null || activeUnit.IsDead)
        {
            turnIndex++;
            StartNextTurn();
            return;
        }

        busy = false;
        waitingInput = false;

        if (turnText)
            turnText.text = $"{activeUnit.name} TURN (SPD {activeUnit.speed})";

        // 핵심: tick 후 제거됐더라도, 턴 시작 시 행동불가였으면 이번 턴은 스킵
        if (!canActAtTurnStart)
        {
            SetSkillButtonsInteractable(false);

            if (turnText)
                turnText.text = $"{activeUnit.name} TURN (SKIPPED)";

            OnActionComplete();
            return;
        }

        if (IsAlly(activeUnit))
        {
            waitingInput = true;
            hasMovedThisTurn = false;
            ClearAllPlannedActions();

            SetupSkillButtons();
            SetSkillButtonsInteractable(true);

            RebuildReachableMoveCache();
            inputMode = PlayerInputMode.Move;

            if (!canMoveAtTurnStart)
            {
                reachableMoveCache = null;
                reachableMoveCameFromCache = null;
                if (tileHighlighter) tileHighlighter.ClearAll();
            }
            else
            {
                if (tileHighlighter && reachableMoveCache != null)
                    tileHighlighter.ShowMoveTiles(reachableMoveCache.Keys);
            }
        }
        else
        {
            SetSkillButtonsInteractable(false);
            busy = true;
            StartCoroutine(EnemyTurnRoutine(activeUnit, OnActionComplete));
        }
    }

    void OnActionComplete()
    {   
        ClearHoverAOEPreview();
        ClearAllPlannedActions();
        selectedSkill = null;
        selectedSkillIndex = -1;
        reachableMoveCache = null;
        reachableMoveCameFromCache = null;
        if (tileHighlighter) tileHighlighter.ClearAll();
        ClearPreviewTargetIndicators();
        // 이번 행동자 처리 완료 → 다음 유닛
        busy = false;
        waitingInput = false;
        if (tileHighlighter) tileHighlighter.ClearAll();

        if (activeUnit != null && !activeUnit.IsDead)
        {
            activeUnit.OnTurnEnd();
            ApplyTileHazardOnTurnEnd(activeUnit);
        }
        turnIndex++;
        StartNextTurn();
    }

    private void ApplyTileHazardOnTurnStart(Unit unit)
    {
        if (unit == null || unit.IsDead) return;
        ResolveHazard(unit, unit.GridPos, HazardTriggerType.OnTurnStart);
    }

    private void ApplyTileHazardOnTurnEnd(Unit unit)
    {
        if (unit == null || unit.IsDead) return;
        ResolveHazard(unit, unit.GridPos, HazardTriggerType.OnTurnEnd);
    }

    private void ApplyTileHazardOnStepEntered(Unit unit, Vector2Int step)
    {
        if (unit == null || unit.IsDead) return;
        ResolveHazard(unit, step, HazardTriggerType.OnEnter);
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
        if (activeUnit == null || activeUnit.IsDead) return;
        if (!activeUnit.CanAct()) return;

        var pool = GetSkillPoolFor(activeUnit);
        if (pool == null) return;
        if (skillIndex < 0 || skillIndex >= pool.Length) return;

        SkillData skill = pool[skillIndex];
        if (skill == null) return;

        if (!activeUnit.CanPayAP(skill.costAP))
            return;

        if (PlannedSkill == skill && PlannedSkillIndex == skillIndex)
        {
            ClearPlannedSkill();
            RefreshPlanningVisuals();
            return;
        }

        SetPlannedSkill(skill, skillIndex);

        selectedSkill = skill;
        selectedSkillIndex = skillIndex;

        inputMode = PlayerInputMode.SkillPreview;

        ClearHoverMovePathPreview();
        ClearPlannedTarget();
        RefreshPlanningVisuals();

        EventSystem.current?.SetSelectedGameObject(null);
    }

    // =========================
    // Core Skill Execution (Target system included)
    // =========================
    IEnumerator RunSkill(Unit attacker, SkillData skill, System.Action onComplete)
    {
        yield return RunSkill(attacker, skill, onComplete, null, null);
    }

    IEnumerator RunSkill(
        Unit attacker,
        SkillData skill,
        System.Action onComplete,
        Vector2Int? clickedTile,
        Unit clickedUnit,
        bool spendAP = true)
    {
        if (attacker == null || skill == null || attacker.IsDead)
        {
            onComplete?.Invoke();
            yield break;
        }

        if (!attacker.CanAct())
        {
            onComplete?.Invoke();
            yield break;
        }

        var targets = CombatTargetResolver.ResolveTargets(
            skill,
            attacker,
            GetCasterAllies(attacker),
            GetCasterEnemies(attacker),
            grid,
            clickedTile,
            clickedUnit
        );
        if (targets.Count == 0)
        {
            onComplete?.Invoke();
            yield break;
        }

        if (spendAP)
        {
            if (!attacker.SpendAP(skill.costAP))
            {
                onComplete?.Invoke();
                yield break;
            }
        }

        bool hitDone = false;
        bool endDone = false;

        IEnumerator ApplyEffectsAndCounters()
        {
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null || t.IsDead)
                    continue;

                ApplySkillEffects(skill, attacker, t);

                // 단일 공격 Counter 체크
                if (IsSingleTargetCounterable(skill, targets, t))
                {
                    yield return TryRunCounterAttack(t, attacker);
                }
            }
        }

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
            yield return ApplyEffectsAndCounters();
            hitDone = true;
        }
        else if (skill.timing == SkillTiming.OnAttackHit)
        {
            while (!hitDone && t < timeout)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }
        else if (skill.timing == SkillTiming.OnAttackEnd)
        {
            while (!endDone && t < timeout)
            {
                t += Time.deltaTime;
                yield return null;
            }

            if (!hitDone)
            {
                yield return ApplyEffectsAndCounters();
                hitDone = true;
            }
        }

        t = 0f;
        while (!endDone && t < timeout)
        {
            t += Time.deltaTime;
            yield return null;
        }

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
        
        if (!enemy.CanAct())
        {
            onComplete?.Invoke();
            yield break;
        }

        if (plan.moveTile != enemy.GridPos && enemy.CanMove())
        {
            var path = grid.FindPathWithinRange(enemy, plan.moveTile, enemy.GetEffectiveMoveRange());
            if (path != null)
            {
                    yield return StartCoroutine(
                    grid.MovePathRoutine(
                        enemy,
                        path,
                        (unit, step) => ApplyTileHazardOnStepEntered(unit, step)
                    )
                );
            }
        }

        if (enemy.GridPos != plan.moveTile)
        {
            Debug.LogWarning($"[AI EXEC] move mismatch planned={plan.moveTile} actual={enemy.GridPos}");
            onComplete?.Invoke();
            yield break;
        }
        // ✅ 스킬이 없으면 이동만 하고 종료
        if (plan.skill == null)
        {
            onComplete?.Invoke();
            yield break;
        }
        
        // ✅ 스킬 실행 전에 "AI가 만든 클릭 입력"을 반드시 전달
        yield return RunSkill(enemy, plan.skill, onComplete, plan.clickedTile, plan.clickedUnit);
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
        var targets = CombatTargetResolver.ResolveTargets(
            skill,
            attacker,
            GetCasterAllies(attacker),
            GetCasterEnemies(attacker),
            grid,
            null,
            null
        );

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
        yield return StartCoroutine(
            grid.MovePathRoutine(
                activeUnit,
                path,
                (unit, step) => ApplyTileHazardOnStepEntered(unit, step)
            )
        );

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
        if (PlannedSkill == null) return;
        if (clicked == null || clicked.IsDead) return;
        if (PlannedSkill.targetMode != SkillTargetMode.ClickSingle) return;

        var resolved = CombatTargetResolver.ResolveTargetsFromPosition(
            PlannedSkill,
            activeUnit,
            PreviewPosition,
            GetCasterAllies(activeUnit),
            GetCasterEnemies(activeUnit),
            grid,
            null,
            clicked
        );

        if (resolved.Count == 0) return;

        SetPlannedSkillTarget(null, clicked);
        RefreshPlanningVisuals();
    }

    public void OnTileClicked(Vector2Int gridPos)
    {
        if (battleEnded) return;
        if (!waitingInput || busy) return;
        if (activeUnit == null || activeUnit.IsDead) return;
        if (!IsAlly(activeUnit)) return;

        // AOE 타일 클릭
        if (inputMode == PlayerInputMode.SkillPreview &&
            selectedSkill != null &&
            selectedSkill.targetMode == SkillTargetMode.ClickTileAOE)
        {
            var resolved = CombatTargetResolver.ResolveTargetsFromPosition(
                PlannedSkill,
                activeUnit,
                PreviewPosition,
                GetCasterAllies(activeUnit),
                GetCasterEnemies(activeUnit),
                grid,
                gridPos,
                null
            );

            if (resolved.Count == 0) return;

            SetPlannedSkillTarget(gridPos, null);
            RefreshPlanningVisuals();
            return;
        }

        // 이동 클릭
        if (hasMovedThisTurn) return;
        if (inputMode != PlayerInputMode.Move) return;
        if (!activeUnit.CanMove()) return;

        EnsureReachableMoveCacheCurrent();

        if (reachableMoveCache == null || reachableMoveCameFromCache == null)
        {
            var data = grid.GetReachableData(activeUnit, GetCurrentMoveRange(activeUnit));
            reachableMoveCache = data.cost;
            reachableMoveCameFromCache = data.cameFrom;
        }

        if (!reachableMoveCache.ContainsKey(gridPos)) return;

        if (gridPos == activeUnit.GridPos)
        {
            ClearPlannedMove();
            RefreshPlanningVisuals();
            return;
        }

        PlanMove(gridPos);
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

        tileHighlighter.ClearInvalid();
        tileHighlighter.ClearTargetOverlay();

        bool castable = CombatTargetResolver.IsPointCastable(
            PlannedSkill,
            PreviewPosition,
            gridPos,
            grid
        );
        if (!castable)
        {
            tileHighlighter.ClearTargetOverlay();
            tileHighlighter.ClearTarget();
            tileHighlighter.ShowInvalidTile(gridPos);
            return;
        }

        var aoeTiles = BuildManhattanDisk(gridPos, Mathf.Max(0, PlannedSkill.aoeRadius));
        tileHighlighter.ShowTargetTiles(aoeTiles);
    }

    public void ClearHoverAOEPreview()
    {
        hoverTile = null;
        if (tileHighlighter != null)
        {
            tileHighlighter.ClearTargetOverlay();
            tileHighlighter.ClearInvalid();
        }
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
        if (!activeUnit.CanMove()) return;
        if (tileHighlighter == null) return;

        EnsureReachableMoveCacheCurrent();

        // 캐시 없으면(정상 흐름이면 거의 없음) 생성
        if (reachableMoveCache == null || reachableMoveCameFromCache == null)
        {
            var data = grid.GetReachableData(activeUnit, GetCurrentMoveRange(activeUnit));
            reachableMoveCache = data.cost;
            reachableMoveCameFromCache = data.cameFrom;
        }

        // 도달 가능 타일만 프리뷰
        if (!reachableMoveCache.ContainsKey(gridPos))
        {
            tileHighlighter.ClearPath();
            tileHighlighter.ClearHoverHazardPath();
            hoverMoveTile = null;
            return;
        }

        if (gridPos == activeUnit.GridPos)
        {
            tileHighlighter.ClearPath();
            tileHighlighter.ClearHoverHazardPath();
            hoverMoveTile = null;
            return;
        }
        if (hoverMoveTile.HasValue && hoverMoveTile.Value == gridPos) return;
        hoverMoveTile = gridPos;

        // ✅ 최단 경로 복원 (start 제외, goal 포함)
        var path = grid.ReconstructPath(activeUnit.GridPos, gridPos, reachableMoveCameFromCache);

        if (path == null)
        {
            // 캐시가 꼬였을 가능성 있으니 1회 재생성
            var data = grid.GetReachableData(activeUnit, GetCurrentMoveRange(activeUnit));
            reachableMoveCache = data.cost;
            reachableMoveCameFromCache = data.cameFrom;

            if (!reachableMoveCache.ContainsKey(gridPos))
            {
                tileHighlighter.ClearPath();
                tileHighlighter.ClearHoverHazardPath();
                hoverMoveTile = null;
                return;
            }

            path = grid.ReconstructPath(activeUnit.GridPos, gridPos, reachableMoveCameFromCache);
        }

        if (path == null || path.Count == 0)
        {
            tileHighlighter.ClearPath();
            tileHighlighter.ClearHoverHazardPath();
            hoverMoveTile = null;
            return;
        }

        ShowHoverPathWithHazardPreview(path);
    }

    public void ClearHoverMovePathPreview()
    {
        hoverMoveTile = null;
        if (tileHighlighter != null)
        {
            tileHighlighter.ClearTargetOverlay();
            tileHighlighter.ClearPath();
            tileHighlighter.ClearHoverHazardPath();
        }
    }

    private Vector2Int PreviewPosition
    {
        get
        {
            if (PlannedMoveTile.HasValue) return PlannedMoveTile.Value;
            return activeUnit != null ? activeUnit.GridPos : Vector2Int.zero;
        }
    }

    private bool HasPlannedTarget =>
        PlannedClickedTile.HasValue || PlannedClickedUnit != null;

    private void RefreshPlanningVisuals()
    {
        if (tileHighlighter) tileHighlighter.ClearAll();
        ClearPreviewTargetIndicators();
        ClearHoverAOEPreview();

        if (activeUnit == null || activeUnit.IsDead) return;

        if (!hasMovedThisTurn && inputMode == PlayerInputMode.Move && activeUnit.CanMove())
        {
            EnsureReachableMoveCacheCurrent();

            if (tileHighlighter && reachableMoveCache != null)
                tileHighlighter.ShowMoveTiles(reachableMoveCache.Keys);

            if (PlannedMoveTile.HasValue && reachableMoveCameFromCache != null)
            {
                var path = grid.ReconstructPath(activeUnit.GridPos, PlannedMoveTile.Value, reachableMoveCameFromCache);

                if (tileHighlighter)
                {
                    if (path != null && path.Count > 0)
                    {
                        var body = new List<Vector2Int>(path);
                        body.RemoveAt(body.Count - 1);

                        ShowPlannedPathWithHazardPreview(body);
                    }
                    else
                    {
                        tileHighlighter.ClearPlannedPath();
                        tileHighlighter.ClearPlannedHazardPath();
                    }

                    tileHighlighter.ShowGhostTile(PlannedMoveTile.Value);
                }
            }
        }

        if (PlannedSkill != null)
        {
            Vector2Int previewPos = PreviewPosition;

            var rangeTiles = BuildManhattanRangeTiles(previewPos, PlannedSkill.minRange, PlannedSkill.maxRange);
            if (tileHighlighter) tileHighlighter.ShowRangeTiles(rangeTiles);

            if (PlannedMoveTile.HasValue && tileHighlighter)
            {
                var path = grid.ReconstructPath(activeUnit.GridPos, PlannedMoveTile.Value, reachableMoveCameFromCache);

                if (path != null && path.Count > 0)
                {
                    var body = new List<Vector2Int>(path);
                    body.RemoveAt(body.Count - 1);

                    ShowPlannedPathWithHazardPreview(body);
                }
                else
                {
                    tileHighlighter.ClearPlannedPath();
                    tileHighlighter.ClearPlannedHazardPath();
                }

                tileHighlighter.ShowGhostTile(PlannedMoveTile.Value);
            }

            var targets = CombatTargetResolver.ResolveTargetsFromPosition(
                PlannedSkill,
                activeUnit,
                previewPos,
                GetCasterAllies(activeUnit),
                GetCasterEnemies(activeUnit),
                grid,
                PlannedClickedTile,
                PlannedClickedUnit
            );

            foreach (var t in targets)
            {
                if (t == null || t.IsDead) continue;
                var hud = GetHud(t);
                if (hud) hud.SetTargeted(true);
                previewTargetUnits.Add(t);
            }

            if (PlannedSkill.targetMode == SkillTargetMode.ClickTileAOE && PlannedClickedTile.HasValue)
            {
                var tiles = BuildManhattanDisk(PlannedClickedTile.Value, Mathf.Max(0, PlannedSkill.aoeRadius));
                if (tileHighlighter) tileHighlighter.ShowTargetTiles(tiles);
            }
            else
            {
                var targetTiles = new HashSet<Vector2Int>();
                foreach (var t in targets)
                    if (t != null && !t.IsDead)
                        targetTiles.Add(t.GridPos);

                if (tileHighlighter) tileHighlighter.ShowTargetTiles(new List<Vector2Int>(targetTiles));
            }
        }
    }
    
    public void ConfirmPlannedAction()
    {
        if (battleEnded) return;
        if (!waitingInput || busy) return;
        if (activeUnit == null || activeUnit.IsDead) return;
        if (!IsAlly(activeUnit)) return;

        StartCoroutine(ConfirmPlannedActionRoutine());
    }

    private IEnumerator ConfirmPlannedActionRoutine()
    {
        busy = true;
        waitingInput = false;
        SetSkillButtonsInteractable(false);

        var executeQueue = new List<PlannedAction>(actionQueue);
        executeQueue.Sort((a, b) =>
        {
            if (a.type == b.type) return 0;
            return a.type == PlannedActionType.Move ? -1 : 1;
        });

        bool executedAnyAction = false;
        bool executedMove = false;
        bool executedSkill = false;

        for (int i = 0; i < executeQueue.Count; i++)
        {   
            if (activeUnit == null || activeUnit.IsDead)
            {
                busy = false;
                waitingInput = false;
                OnActionComplete();
                yield break;
            }

            var action = executeQueue[i];
            if (action == null) continue;

            if (action.type == PlannedActionType.Move)
            {
                if (action.moveTile == activeUnit.GridPos)
                    continue;

                var path = grid.ReconstructPath(activeUnit.GridPos, action.moveTile, reachableMoveCameFromCache);
                if (path == null)
                {
                    busy = false;
                    waitingInput = true;
                    SetSkillButtonsInteractable(true);
                    RefreshPlanningVisuals();
                    yield break;
                }

                if (tileHighlighter) tileHighlighter.ClearAll();
                yield return StartCoroutine(
                    grid.MovePathRoutine(
                        activeUnit,
                        path,
                        (unit, step) => ApplyTileHazardOnStepEntered(unit, step)
                    )
                );

                reachableMoveCache = null;
                reachableMoveCameFromCache = null;

                // 이동 중 hazard/explosion 등으로 죽었으면 즉시 턴 정리
                if (activeUnit == null || activeUnit.IsDead)
                {
                    busy = false;
                    waitingInput = false;
                    OnActionComplete();
                    yield break;
                }

                executedAnyAction = true;
                executedMove = true;
                hasMovedThisTurn = true;
            }
            else if (action.type == PlannedActionType.Skill)
            {
                if (action.skill == null)
                    continue;
                if (activeUnit == null || activeUnit.IsDead)
                {
                    busy = false;
                    waitingInput = false;
                    OnActionComplete();
                    yield break;
                }

                if (!activeUnit.CanAct())
                {
                    busy = false;
                    waitingInput = true;
                    SetSkillButtonsInteractable(true);
                    RefreshPlanningVisuals();
                    yield break;
                }

                if (RequiresExplicitTarget(action.skill) && !HasExplicitTarget(action))
                {
                    busy = false;
                    waitingInput = true;
                    SetSkillButtonsInteractable(true);
                    RefreshPlanningVisuals();
                    yield break;
                }

                if (!activeUnit.CanPayAP(action.skill.costAP))
                {
                    busy = false;
                    waitingInput = true;
                    SetSkillButtonsInteractable(true);
                    RefreshPlanningVisuals();
                    yield break;
                }

                var finalTargets = CombatTargetResolver.ResolveTargets(
                    action.skill,
                    activeUnit,
                    GetCasterAllies(activeUnit),
                    GetCasterEnemies(activeUnit),
                    grid,
                    action.clickedTile,
                    action.clickedUnit
                );

                if (finalTargets.Count > 0)
                {
                    yield return RunSkill(
                        activeUnit,
                        action.skill,
                        null,
                        action.clickedTile,
                        action.clickedUnit
                    );

                    executedAnyAction = true;
                    executedSkill = true;
                }
            }
        }

        // =========================
        // 턴 종료 규칙
        // =========================

        // 1) 아무 행동도 계획하지 않고 Confirm -> 턴 종료
        if (!executedAnyAction)
        {
            busy = false;
            waitingInput = false;
            OnActionComplete();
            yield break;
        }

        // 2) Skill을 사용했으면 턴 종료
        if (executedSkill)
        {
            busy = false;
            waitingInput = false;
            OnActionComplete();
            yield break;
        }

        // 3) Move만 했으면 턴 유지, 스킬 입력 단계로 복귀
        if (executedMove && !executedSkill)
        {
            actionQueue.Clear();
            ClearPlannedTarget();

            selectedSkill = null;
            selectedSkillIndex = -1;
            inputMode = PlayerInputMode.SkillPreview;

            busy = false;
            waitingInput = true;
            SetSkillButtonsInteractable(true);

            RefreshPlanningVisuals();
            yield break;
        }
    }

    public void CancelPlanningStep()
    {
        if (battleEnded) return;
        if (!waitingInput || busy) return;

        if (HasPlannedTarget)
        {
            ClearPlannedTarget();
            RefreshPlanningVisuals();
            return;
        }

        if (SkillAction != null)
        {
            ClearPlannedSkill();
            RefreshPlanningVisuals();
            return;
        }

        if (MoveAction != null)
        {
            ClearPlannedMove();
            RefreshPlanningVisuals();
            return;
        }
    }

    private bool RequiresExplicitTarget(SkillData skill)
    {
        if (skill == null) return false;

        return skill.targetMode == SkillTargetMode.ClickSingle ||
            skill.targetMode == SkillTargetMode.ClickTileAOE;
    }

    private bool HasExplicitPlannedTarget(SkillData skill)
    {
        if (skill == null) return false;

        switch (skill.targetMode)
        {
            case SkillTargetMode.ClickSingle:
                return PlannedClickedUnit != null;

            case SkillTargetMode.ClickTileAOE:
                return PlannedClickedTile.HasValue;

            default:
                return true;
        }
    }

    private void PlanMove(Vector2Int gridPos)
    {
        EnsureReachableMoveCacheCurrent();

        if (reachableMoveCache == null || !reachableMoveCache.ContainsKey(gridPos))
            return;

        if (gridPos == activeUnit.GridPos)
        {
            ClearPlannedMove();
            RefreshPlanningVisuals();
            return;
        }

        SetPlannedMove(gridPos);
        RefreshPlanningVisuals();
    }

    private void TrimQueue()
    {
        actionQueue.Sort((a, b) =>
        {
            if (a.type == b.type) return 0;
            return a.type == PlannedActionType.Move ? -1 : 1;
        });

        while (actionQueue.Count > 2)
            actionQueue.RemoveAt(actionQueue.Count - 1);
    }

    private void SetPlannedMove(Vector2Int tile)
    {
        var move = MoveAction;
        if (move != null)
        {
            move.moveTile = tile;
        }
        else
        {
            actionQueue.Add(PlannedAction.CreateMove(tile));
        }

        TrimQueue();
    }

    private void SetPlannedSkill(SkillData skill, int skillIndex)
    {
        var skillAction = SkillAction;
        if (skillAction != null)
        {
            skillAction.skill = skill;
            skillAction.skillIndex = skillIndex;
            skillAction.clickedTile = null;
            skillAction.clickedUnit = null;
        }
        else
        {
            actionQueue.Add(PlannedAction.CreateSkill(skill, skillIndex, null, null));
        }

        TrimQueue();
    }

    private void SetPlannedSkillTarget(Vector2Int? clickedTile, Unit clickedUnit)
    {
        var skillAction = SkillAction;
        if (skillAction == null) return;

        skillAction.clickedTile = clickedTile;
        skillAction.clickedUnit = clickedUnit;
    }

    private void ClearPlannedTarget()
    {
        var skillAction = SkillAction;
        if (skillAction != null)
        {
            skillAction.clickedTile = null;
            skillAction.clickedUnit = null;
        }

        ClearPreviewTargetIndicators();
        ClearHoverAOEPreview();
    }

    private void ClearPlannedSkill()
    {
        var skillAction = SkillAction;
        if (skillAction != null)
            actionQueue.Remove(skillAction);

        selectedSkill = null;
        selectedSkillIndex = -1;
        inputMode = PlayerInputMode.Move;
        ClearPlannedTarget();
    }

    private void ClearPlannedMove()
    {
        var move = MoveAction;
        if (move != null)
            actionQueue.Remove(move);

        hoverMoveTile = null;
        ClearHoverMovePathPreview();

        if (tileHighlighter) tileHighlighter.ClearGhost();
    }

    private void ClearAllPlannedActions()
    {
        actionQueue.Clear();
        selectedSkill = null;
        selectedSkillIndex = -1;
        hoverMoveTile = null;
        hoverTile = null;

        if (tileHighlighter) tileHighlighter.ClearGhost();

        ClearPreviewTargetIndicators();
        ClearHoverAOEPreview();
        ClearHoverMovePathPreview();
    }

    private bool HasExplicitTarget(PlannedAction action)
    {
        if (action == null || action.skill == null) return false;

        switch (action.skill.targetMode)
        {
            case SkillTargetMode.ClickSingle:
                return action.clickedUnit != null;

            case SkillTargetMode.ClickTileAOE:
                return action.clickedTile.HasValue;

            default:
                return true;
        }
    }

    private int GetCurrentMoveRange(Unit unit)
    {
        if (unit == null) return 0;
        return Mathf.Max(0, unit.GetEffectiveMoveRange());
    }

    private void RebuildReachableMoveCache()
    {
        reachableMoveCache = null;
        reachableMoveCameFromCache = null;
        cachedMoveRange = -1;
        cachedStatusRevision = -1;

        if (grid == null) grid = GridManager.I;
        if (grid == null || activeUnit == null || activeUnit.IsDead) return;
        if (!activeUnit.CanMove()) return;

        int moveRange = GetCurrentMoveRange(activeUnit);
        var data = grid.GetReachableData(activeUnit, moveRange);

        reachableMoveCache = data.cost;
        reachableMoveCameFromCache = data.cameFrom;
        cachedMoveRange = moveRange;
        cachedStatusRevision = activeUnit.StatusRevision;
    }

    private void EnsureReachableMoveCacheCurrent()
    {
        if (activeUnit == null || activeUnit.IsDead)
        {
            reachableMoveCache = null;
            reachableMoveCameFromCache = null;
            cachedMoveRange = -1;
            cachedStatusRevision = -1;
            return;
        }

        int nowRange = GetCurrentMoveRange(activeUnit);
        int nowRevision = activeUnit.StatusRevision;

        bool dirty =
            reachableMoveCache == null ||
            reachableMoveCameFromCache == null ||
            cachedMoveRange != nowRange ||
            cachedStatusRevision != nowRevision;

        if (dirty)
            RebuildReachableMoveCache();
    }

    private bool IsSingleTargetCounterable(SkillData skill, List<Unit> resolvedTargets, Unit defender)
    {
        if (skill == null || defender == null || resolvedTargets == null)
            return false;

        if (isCounterAttackInProgress)
            return false;

        if (defender.IsDead)
            return false;

        if (!defender.CanAct())
            return false;

        if (!defender.HasCounterReady())
            return false;

        if (!IsOffensiveSkill(skill))
            return false;

        // AOE는 반격 불가
        if (skill.targetMode == SkillTargetMode.ClickTileAOE)
            return false;

        // 단일 공격만 허용
        // ResolveTargets 결과가 정확히 1명이고 그 1명이 defender여야 함
        if (resolvedTargets.Count != 1)
            return false;

        if (resolvedTargets[0] != defender)
            return false;

        return true;
    }

    private SkillData ChooseCounterSkill(Unit counterUnit, Unit attacker)
    {
        if (counterUnit == null || attacker == null) return null;
        if (counterUnit.IsDead || attacker.IsDead) return null;

        var pool = GetSkillPoolFor(counterUnit);
        if (pool == null || pool.Length == 0) return null;

        for (int i = 0; i < pool.Length; i++)
        {
            var skill = pool[i];
            if (skill == null) continue;

            // 반격은 단일 공격만 허용
            if (skill.targetMode != SkillTargetMode.ClickSingle &&
                skill.targetMode != SkillTargetMode.AutoNearestSingle)
                continue;

            int dist = Mathf.Abs(counterUnit.GridPos.x - attacker.GridPos.x)
                    + Mathf.Abs(counterUnit.GridPos.y - attacker.GridPos.y);

            if (dist < skill.minRange || dist > skill.maxRange)
                continue;

            if (skill.requiresLineOfSight && !grid.HasLineOfSight(counterUnit.GridPos, attacker.GridPos))
                continue;

            return skill;
        }

        return null;
    }

    private IEnumerator TryRunCounterAttack(Unit defender, Unit originalAttacker)
    {
        if (defender == null || originalAttacker == null)
            yield break;

        if (defender.IsDead || originalAttacker.IsDead)
            yield break;

        if (isCounterAttackInProgress)
            yield break;

        var counterSkill = ChooseCounterSkill(defender, originalAttacker);
        if (counterSkill == null)
            yield break;

        isCounterAttackInProgress = true;

        yield return RunSkill(
            defender,
            counterSkill,
            null,
            null,
            originalAttacker,
            false
        );
        defender.RemoveStatus(StatusId.Counter);
        isCounterAttackInProgress = false;
    }

    private bool IsOffensiveSkill(SkillData skill)
    {
        if (skill == null || skill.effects == null)
            return false;

        for (int i = 0; i < skill.effects.Length; i++)
        {
            var e = skill.effects[i];
            if (e == null) continue;

            if (e is DealDamageEffect) return true;
            if (e is BurnApplyEffect) return true;
            if (e is PoisonApplyEffect) return true;
            if (e is StunApplyEffect) return true;
            if (e is FreezeApplyEffect) return true;
            if (e is SlowApplyEffect) return true;
        }

        return false;
    }

    private HazardResolveResult ResolveHazard(Unit unit, Vector2Int tilePos, HazardTriggerType trigger)
    {
        var result = new HazardResolveResult();

        if (unit == null || unit.IsDead || grid == null)
            return result;

        var tile = grid.GetTileView(tilePos);
        if (tile == null || tile.tileData == null)
            return result;

        if (tile.HazardType == HazardType.None)
            return result;

        if (tile.HazardTrigger != trigger)
            return result;

        int power = Mathf.Max(1, tile.HazardPower);
        int duration = Mathf.Max(1, tile.HazardDuration);

        switch (tile.HazardType)
        {
            case HazardType.Burn:
                unit.AddOrRefreshStatus(new BurnStatus(power, duration, unit));
                result.triggered = true;
                break;

            case HazardType.Poison:
                unit.AddOrRefreshStatus(new PoisonStatus(power, duration, unit));
                result.triggered = true;
                break;

            case HazardType.Explosion:
                unit.TakeDamage(power);
                result.triggered = true;
                break;
        }

        return result;
    }

    private List<Vector2Int> ExtractHazardPathTiles(List<Vector2Int> path)
    {
        var result = new List<Vector2Int>();
        if (path == null || grid == null) return result;

        for (int i = 0; i < path.Count; i++)
        {
            var step = path[i];
            var td = grid.GetTileData(step);
            if (td == null) continue;

            if (td.hazardType == HazardType.None) continue;
            if (td.hazardTrigger != HazardTriggerType.OnEnter) continue;

            result.Add(step);
        }

        return result;
    }

    private void ShowHoverPathWithHazardPreview(List<Vector2Int> path)
    {
        if (tileHighlighter == null) return;

        if (path == null || path.Count == 0)
        {
            tileHighlighter.ClearPath();
            tileHighlighter.ClearHoverHazardPath();
            return;
        }

        tileHighlighter.ShowPathTiles(path);

        var hazardTiles = ExtractHazardPathTiles(path);
        if (hazardTiles.Count > 0)
            tileHighlighter.ShowHoverHazardPathTiles(hazardTiles);
        else
            tileHighlighter.ClearHoverHazardPath();
    }

    private void ShowPlannedPathWithHazardPreview(List<Vector2Int> pathBody)
    {
        if (tileHighlighter == null) return;

        if (pathBody == null || pathBody.Count == 0)
        {
            tileHighlighter.ClearPlannedPath();
            tileHighlighter.ClearPlannedHazardPath();
            return;
        }

        tileHighlighter.ShowPlannedPathTiles(pathBody);

        var hazardTiles = ExtractHazardPathTiles(pathBody);
        if (hazardTiles.Count > 0)
            tileHighlighter.ShowPlannedHazardPathTiles(hazardTiles);
        else
            tileHighlighter.ClearPlannedHazardPath();
    }
}
