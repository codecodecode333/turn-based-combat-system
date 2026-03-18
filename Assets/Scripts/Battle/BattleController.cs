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

    [Header("Preview Actor")]
    public bool usePreviewActorGhost = true;
    [Range(0f, 1f)] public float sourceActorPreviewAlpha = 0.4f;
    public Vector3 previewActorOffset = new Vector3(0f, 0f, -0.35f);

    GameObject previewActorRoot;
    readonly List<SpriteRenderer> activeUnitSpriteCache = new List<SpriteRenderer>(8);
    readonly List<Color> activeUnitOriginalColors = new List<Color>(8);
    readonly List<SpriteRenderer> previewActorSpriteCache = new List<SpriteRenderer>(8);

    Unit previewActorSourceUnit;
    bool previewActorInitialized = false;

    [Header("Preview Actor Visual Root")]
    public string previewVisualRootName = "UnitRoot"; // 네 프로젝트 시각 루트 이름에 맞게 수정

    Transform previewActorSourceVisualRoot;
    Animator previewActorAnimator;
    readonly List<Behaviour> previewDisabledBehaviours = new List<Behaviour>();
    readonly List<Collider2D> previewDisabledColliders = new List<Collider2D>();
    readonly List<Canvas> previewDisabledCanvas = new List<Canvas>();

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

    private CombatResolver.Context BuildCombatContext(Unit attacker)
    {
        return new CombatResolver.Context
        {
            grid = grid,
            allies = GetCasterAllies(attacker),
            enemies = GetCasterEnemies(attacker),
            getSkillPoolFor = GetSkillPoolFor,
            isOffensiveSkill = IsOffensiveSkill,
            runCounterAttack = TryRunCounterAttack,
            isCounterAttackInProgress = isCounterAttackInProgress
        };
    }

    public GridManager grid;
    private readonly List<Unit> previewTargetUnits = new List<Unit>(16);
    private readonly List<Vector2Int> previewEnemyTiles = new List<Vector2Int>(16);
    private readonly List<Vector2Int> previewFriendlyFireTiles = new List<Vector2Int>(16);
    private readonly List<Vector2Int> previewHazardTiles = new List<Vector2Int>(16);
    public bool IsBusy => busy;
    public bool IsWaitingInput => waitingInput;

    private int cachedMoveRange = -1;
    private int cachedStatusRevision = -1;

    private bool isCounterAttackInProgress = false;

    public bool HasLockedAOETarget =>
        PlannedSkill != null &&
        PlannedSkill.targetMode == SkillTargetMode.ClickTileAOE &&
        PlannedClickedTile.HasValue;

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
            // 차후 tooltip/hud 설명용
            // string summary = s != null ? SkillEffectFormatter.BuildSkillSummary(s) : "";
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
                ClearPreviewGhostPresentation();
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
        ClearPreviewGhostPresentation();
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
        HazardSystem.Resolve(unit, unit.GridPos, HazardTriggerType.OnTurnStart, grid);
    }

    private void ApplyTileHazardOnTurnEnd(Unit unit)
    {
        if (unit == null || unit.IsDead) return;
        HazardSystem.Resolve(unit, unit.GridPos, HazardTriggerType.OnTurnEnd, grid);
    }

    private void ApplyTileHazardOnStepEntered(Unit unit, Vector2Int step)
    {
        if (unit == null || unit.IsDead) return;
        HazardSystem.Resolve(unit, step, HazardTriggerType.OnEnter, grid);
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

        var ctx = BuildCombatContext(attacker);

        var resolved = CombatResolver.ResolveForExecution(
            skill,
            attacker,
            ctx,
            clickedTile,
            clickedUnit,
            spendAP
        );

        if (!resolved.success)
        {
            onComplete?.Invoke();
            yield break;
        }

        bool hitDone = false;
        bool endDone = false;
        bool effectsApplied = false;

        IEnumerator ApplyOnce()
        {
            if (effectsApplied)
                yield break;

            effectsApplied = true;
            yield return CombatResolver.ApplyResolvedEffectsAndCounters(
                skill,
                attacker,
                resolved,
                BuildCombatContext(attacker)
            );
        }

        void OnHit()
        {
            if (hitDone) return;
            hitDone = true;
            StartCoroutine(ApplyOnce());
        }

        void OnEnd()
        {
            endDone = true;
        }

        attacker.AttackHitEvent += OnHit;
        attacker.AttackEndEvent += OnEnd;

        attacker.PlayAttack(skill.animationTrigger);

        float timeout = 1.5f;
        float t = 0f;

        if (skill.timing == SkillTiming.Immediate)
        {
            yield return ApplyOnce();
            hitDone = true;
        }
        else if (skill.timing == SkillTiming.OnAttackHit)
        {
            while (!hitDone && t < timeout)
            {
                t += Time.deltaTime;
                yield return null;
            }

            if (!effectsApplied)
                yield return ApplyOnce();
        }
        else if (skill.timing == SkillTiming.OnAttackEnd)
        {
            while (!endDone && t < timeout)
            {
                t += Time.deltaTime;
                yield return null;
            }

            if (!effectsApplied)
            {
                yield return ApplyOnce();
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
        for (int i = 0; i < allies.Count; i++)
        {
            var u = allies[i];
            if (u == null) continue;

            var hud = u.GetComponentInChildren<UnitHud>();
            if (hud != null)
            {
                hud.SetTargeted(false); // 임시
                // 추후: hud.SetTargetGlow(TargetGlowType.None);
            }
        }

        for (int i = 0; i < enemies.Count; i++)
        {
            var u = enemies[i];
            if (u == null) continue;

            var hud = u.GetComponentInChildren<UnitHud>();
            if (hud != null)
            {
                hud.SetTargeted(false); // 임시
                // 추후: hud.SetTargetGlow(TargetGlowType.None);
            }
        }
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

            hoverTile = null; // 추가
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

        tileHighlighter.ClearPreviewAreaTiles();
        tileHighlighter.ClearInvalid();
        tileHighlighter.ClearTargetOverlay();
        tileHighlighter.ClearSelectedTile();
        ClearPreviewTargetIndicators();

        bool castable = CombatTargetResolver.IsPointCastable(
            PlannedSkill,
            PreviewPosition,
            gridPos,
            grid
        );

        if (!castable)
        {
            tileHighlighter.ShowInvalidTile(gridPos);
            return;
        }

        // AOE 전체 영역 = 주황 베이스
        var aoeTiles = BuildManhattanDisk(gridPos, Mathf.Max(0, PlannedSkill.aoeRadius));
        tileHighlighter.ShowPreviewAreaTiles(aoeTiles);
        tileHighlighter.ShowSelectedTile(gridPos);

        // 실제 적중 대상 분리
        previewTargetUnits.Clear();
        previewTargetUnits.AddRange(
            CombatTargetResolver.ResolveTargetsFromPosition(
                PlannedSkill,
                activeUnit,
                PreviewPosition,
                GetCasterAllies(activeUnit),
                GetCasterEnemies(activeUnit),
                grid,
                gridPos,
                null
            )
        );

        SplitPreviewTargetTiles(previewTargetUnits, activeUnit, PreviewPosition);

        if (previewEnemyTiles.Count > 0)
            tileHighlighter.ShowTargetTiles(previewEnemyTiles);

        if (previewFriendlyFireTiles.Count > 0)
            tileHighlighter.ShowFriendlyFireTiles(previewFriendlyFireTiles);

        ApplyPreviewTargetIndicators(previewTargetUnits, activeUnit, PreviewPosition);
    }

    public void ClearHoverAOEPreview()
    {
        hoverTile = null;

        if (tileHighlighter != null)
        {
            tileHighlighter.ClearPreviewAreaTiles();
            tileHighlighter.ClearTargetOverlay();
            tileHighlighter.ClearInvalid();
            tileHighlighter.ClearSelectedTile();
        }

        ClearPreviewTargetIndicators();

        // locked preview / move preview 다시 복원
        RefreshPlanningVisuals();
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

        if (reachableMoveCache == null || reachableMoveCameFromCache == null)
        {
            var data = grid.GetReachableData(activeUnit, GetCurrentMoveRange(activeUnit));
            reachableMoveCache = data.cost;
            reachableMoveCameFromCache = data.cameFrom;
        }

        if (!reachableMoveCache.ContainsKey(gridPos))
        {
            tileHighlighter.ClearPath();
            tileHighlighter.ClearHoverHazardPath();
            tileHighlighter.ClearSelectedTile();
            hoverMoveTile = null;
            RefreshPlanningVisuals();
            return;
        }

        if (gridPos == activeUnit.GridPos)
        {
            tileHighlighter.ClearPath();
            tileHighlighter.ClearHoverHazardPath();
            tileHighlighter.ClearSelectedTile();
            hoverMoveTile = null;
            RefreshPlanningVisuals();
            return;
        }

        if (hoverMoveTile.HasValue && hoverMoveTile.Value == gridPos) return;
        hoverMoveTile = gridPos;

        var path = grid.ReconstructPath(activeUnit.GridPos, gridPos, reachableMoveCameFromCache);

        tileHighlighter.ClearPath();
        tileHighlighter.ClearHoverHazardPath();
        tileHighlighter.ClearSelectedTile();

        if (path != null && path.Count > 0)
        {
            tileHighlighter.ShowPathTiles(path);
            tileHighlighter.ShowSelectedTile(gridPos);

            BuildPreviewHazardTiles(path);
            if (previewHazardTiles.Count > 0)
                tileHighlighter.ShowHoverHazardPathTiles(previewHazardTiles);
        }
    }

    public void ClearHoverMovePathPreview()
    {
        hoverMoveTile = null;

        if (tileHighlighter != null)
        {
            tileHighlighter.ClearPath();
            tileHighlighter.ClearHoverHazardPath();
            tileHighlighter.ClearSelectedTile();
        }

        // hover 프리뷰만 지우고 끝내면
        // planned move / planned skill 시각 정보가 같이 사라질 수 있으므로
        // 현재 계획 상태를 다시 복원
        if (busy || !waitingInput || battleEnded || activeUnit == null || activeUnit.IsDead)
            return;

        RefreshPlanningVisuals();
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

    void RefreshPlanningVisuals()
    {
        if (tileHighlighter == null || activeUnit == null || activeUnit.IsDead || grid == null)
            return;

        tileHighlighter.ClearAll();
        ClearPreviewTargetIndicators();

        // -------------------------
        // Move base overlay
        // -------------------------
        if (!hasMovedThisTurn && inputMode == PlayerInputMode.Move && activeUnit.CanMove())
        {
            EnsureReachableMoveCacheCurrent();

            if (reachableMoveCache == null || reachableMoveCameFromCache == null)
            {
                var data = grid.GetReachableData(activeUnit, GetCurrentMoveRange(activeUnit));
                reachableMoveCache = data.cost;
                reachableMoveCameFromCache = data.cameFrom;
            }

            if (reachableMoveCache != null)
                tileHighlighter.ShowMoveTiles(reachableMoveCache.Keys);
        }

        // -------------------------
        // Planned move: ghost + selected + planned path
        // -------------------------
        if (PlannedMoveTile.HasValue)
        {
            var moveTile = PlannedMoveTile.Value;

            tileHighlighter.ClearGhostTile(); // 안전하게 정리
            tileHighlighter.ShowSelectedTile(moveTile);

            if (reachableMoveCameFromCache != null &&
                reachableMoveCameFromCache.ContainsKey(moveTile))
            {
                var plannedPath = grid.ReconstructPath(activeUnit.GridPos, moveTile, reachableMoveCameFromCache);
                if (plannedPath != null && plannedPath.Count > 0)
                {
                    tileHighlighter.ShowPlannedPathTiles(plannedPath);

                    BuildPreviewHazardTiles(plannedPath);
                    if (previewHazardTiles.Count > 0)
                        tileHighlighter.ShowPlannedHazardPathTiles(previewHazardTiles);
                }
            }
        }

        // -------------------------
        // Skill preview base overlay
        // -------------------------
        if (inputMode == PlayerInputMode.SkillPreview && PlannedSkill != null)
        {
            var castableTiles = GetCastableTilesForPreview(PlannedSkill, PreviewPosition);
            tileHighlighter.ShowRangeTiles(castableTiles);

            // ClickSingle locked preview
            if (PlannedSkill.targetMode == SkillTargetMode.ClickSingle && PlannedClickedUnit != null)
            {
                previewTargetUnits.Clear();
                previewTargetUnits.AddRange(
                    CombatTargetResolver.ResolveTargetsFromPosition(
                        PlannedSkill,
                        activeUnit,
                        PreviewPosition,
                        GetCasterAllies(activeUnit),
                        GetCasterEnemies(activeUnit),
                        grid,
                        null,
                        PlannedClickedUnit
                    )
                );

                SplitPreviewTargetTiles(previewTargetUnits, activeUnit, PreviewPosition);

                if (previewEnemyTiles.Count > 0)
                    tileHighlighter.ShowTargetTiles(previewEnemyTiles);

                if (previewFriendlyFireTiles.Count > 0)
                    tileHighlighter.ShowFriendlyFireTiles(previewFriendlyFireTiles);

                ApplyPreviewTargetIndicators(previewTargetUnits, activeUnit, PreviewPosition);
            }

            // ClickTileAOE locked preview
            if (PlannedSkill.targetMode == SkillTargetMode.ClickTileAOE && PlannedClickedTile.HasValue)
            {
                Vector2Int center = PlannedClickedTile.Value;

                var aoeTiles = BuildManhattanDisk(center, Mathf.Max(0, PlannedSkill.aoeRadius));
                tileHighlighter.ShowRangeTiles(aoeTiles);
                tileHighlighter.ShowSelectedTile(center);

                previewTargetUnits.Clear();
                previewTargetUnits.AddRange(
                    CombatTargetResolver.ResolveTargetsFromPosition(
                        PlannedSkill,
                        activeUnit,
                        PreviewPosition,
                        GetCasterAllies(activeUnit),
                        GetCasterEnemies(activeUnit),
                        grid,
                        center,
                        null
                    )
                );

                SplitPreviewTargetTiles(previewTargetUnits, activeUnit, PreviewPosition);

                if (previewEnemyTiles.Count > 0)
                    tileHighlighter.ShowTargetTiles(previewEnemyTiles);

                if (previewFriendlyFireTiles.Count > 0)
                    tileHighlighter.ShowFriendlyFireTiles(previewFriendlyFireTiles);

                ApplyPreviewTargetIndicators(previewTargetUnits, activeUnit, PreviewPosition);
            }
        }

        // -------------------------
        // Move hover path preview
        // -------------------------
        if (hoverMoveTile.HasValue &&
            inputMode == PlayerInputMode.Move &&
            !hasMovedThisTurn &&
            reachableMoveCameFromCache != null &&
            reachableMoveCameFromCache.ContainsKey(hoverMoveTile.Value))
        {
            var hoverPath = grid.ReconstructPath(activeUnit.GridPos, hoverMoveTile.Value, reachableMoveCameFromCache);
            if (hoverPath != null && hoverPath.Count > 0)
            {
                tileHighlighter.ShowPathTiles(hoverPath);

                BuildPreviewHazardTiles(hoverPath);
                if (previewHazardTiles.Count > 0)
                    tileHighlighter.ShowHoverHazardPathTiles(previewHazardTiles);
            }
        }
        UpdatePreviewGhostPresentation();
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

        ClearHoverMovePathPreview();
        ClearPreviewGhostPresentation();
        if (tileHighlighter != null)
            tileHighlighter.ClearGhostTile();

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

        if (tileHighlighter) tileHighlighter.ClearGhostTile();
        ClearPreviewGhostPresentation();
    }

    private void ClearAllPlannedActions()
    {
        actionQueue.Clear();
        selectedSkill = null;
        selectedSkillIndex = -1;
        hoverMoveTile = null;
        hoverTile = null;

        if (tileHighlighter) tileHighlighter.ClearGhostTile();

        ClearPreviewTargetIndicators();
        ClearHoverAOEPreview();
        ClearHoverMovePathPreview();
        ClearPreviewGhostPresentation();
    }
    
    void ApplyPreviewTargetIndicators(List<Unit> targets, Unit caster, Vector2Int casterPreviewPos)
    {
        ClearPreviewTargetIndicators();

        if (targets == null) return;

        bool casterIsAlly = IsAlly(caster);

        for (int i = 0; i < targets.Count; i++)
        {
            var u = targets[i];
            if (u == null || u.IsDead) continue;

            var hud = u.GetComponentInChildren<UnitHud>();
            if (hud == null) continue;

            bool sameSide = IsAlly(u) == casterIsAlly;

            // 현재는 bool 기반 임시
            hud.SetTargeted(true);

            // 추후 glow 패치 후 교체:
            // hud.SetTargetGlow(sameSide ? TargetGlowType.FriendlyFire : TargetGlowType.EnemyTarget);
        }
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
        return SkillMetaUtility.ContainsOffensiveEffect(skill);
    }

    private void ShowHoverPathWithHazardPreview(List<Vector2Int> path)
    {
        if (tileHighlighter == null) return;

        if (path == null || path.Count == 0)
        {
            tileHighlighter.ClearPath();
            tileHighlighter.ClearHoverHazardPath();
            tileHighlighter.ClearSelectedTile();
            hoverMoveTile = null;
            RefreshPlanningVisuals();
            return;
        }

        tileHighlighter.ShowPathTiles(path);

        var hazardTiles = HazardUtility.ExtractHazardPathTiles(path, grid, HazardTriggerType.OnEnter);
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

        var hazardTiles = HazardUtility.ExtractHazardPathTiles(pathBody, grid, HazardTriggerType.OnEnter);
        if (hazardTiles.Count > 0)
            tileHighlighter.ShowPlannedHazardPathTiles(hazardTiles);
        else
            tileHighlighter.ClearPlannedHazardPath();
    }
    void SplitPreviewTargetTiles(List<Unit> resolvedTargets, Unit caster, Vector2Int casterPreviewPos)
    {
        previewEnemyTiles.Clear();
        previewFriendlyFireTiles.Clear();

        if (resolvedTargets == null) return;

        bool casterIsAlly = IsAlly(caster);

        for (int i = 0; i < resolvedTargets.Count; i++)
        {
            var u = resolvedTargets[i];
            if (u == null || u.IsDead) continue;

            Vector2Int pos = (u == caster) ? casterPreviewPos : u.GridPos;
            bool sameSide = IsAlly(u) == casterIsAlly;

            if (sameSide)
                previewFriendlyFireTiles.Add(pos);
            else
                previewEnemyTiles.Add(pos);
        }
    }

    void BuildPreviewHazardTiles(List<Vector2Int> pathOrTiles)
    {
        previewHazardTiles.Clear();
        if (pathOrTiles == null || grid == null) return;

        for (int i = 0; i < pathOrTiles.Count; i++)
        {
            var p = pathOrTiles[i];

            if (HazardUtility.HasHazard(grid, p))
                previewHazardTiles.Add(p);
        }
    }

    List<Vector2Int> GetCastableTilesForPreview(SkillData skill, Vector2Int fromPos)
    {
        var result = new List<Vector2Int>();
        if (skill == null || grid == null) return result;

        for (int x = 0; x < grid.width; x++)
        {
            for (int y = 0; y < grid.height; y++)
            {
                var p = new Vector2Int(x, y);
                if (CombatTargetResolver.IsPointCastable(skill, fromPos, p, grid))
                    result.Add(p);
            }
        }

        return result;
    }

    void CacheActiveUnitSpriteRenderers(Unit unit)
    {
        activeUnitSpriteCache.Clear();
        activeUnitOriginalColors.Clear();

        if (unit == null) return;

        var renderers = unit.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var sr = renderers[i];
            if (sr == null) continue;

            activeUnitSpriteCache.Add(sr);
            activeUnitOriginalColors.Add(sr.color);
        }
    }

    void SetActiveUnitPreviewAlpha(Unit unit, float alpha)
    {
        if (unit == null) return;

        if (!previewActorInitialized || previewActorSourceUnit != unit || activeUnitSpriteCache.Count == 0)
        {
            previewActorSourceUnit = unit;
            CacheActiveUnitSpriteRenderers(unit);
            previewActorInitialized = true;
        }

        for (int i = 0; i < activeUnitSpriteCache.Count; i++)
        {
            var sr = activeUnitSpriteCache[i];
            if (sr == null) continue;

            Color c = activeUnitOriginalColors[i];
            c.a *= alpha;
            sr.color = c;
        }
    }

    void RestoreActiveUnitVisual(Unit unit)
    {
        if (unit == null) return;

        if (!previewActorInitialized || previewActorSourceUnit != unit || activeUnitSpriteCache.Count == 0)
        {
            previewActorSourceUnit = unit;
            CacheActiveUnitSpriteRenderers(unit);
            previewActorInitialized = true;
        }

        for (int i = 0; i < activeUnitSpriteCache.Count; i++)
        {
            var sr = activeUnitSpriteCache[i];
            if (sr == null) continue;

            sr.color = activeUnitOriginalColors[i];
        }
    }

    void UpdatePreviewActorTransform(Unit sourceUnit, Vector2Int previewGridPos)
    {
        if (!usePreviewActorGhost || sourceUnit == null || grid == null) return;

        EnsurePreviewActor(sourceUnit);
        if (previewActorRoot == null || previewActorSourceVisualRoot == null) return;

        Vector3 sourceWorld = sourceUnit.transform.position;
        Vector3 targetWorld = grid.GridToWorld(previewGridPos) + previewActorOffset;
        Vector3 delta = targetWorld - sourceWorld;

        // visual root 전체를 source visual root 위치 기준으로 이동
        previewActorRoot.transform.position = previewActorSourceVisualRoot.position + delta;
        previewActorRoot.transform.rotation = previewActorSourceVisualRoot.rotation;
        previewActorRoot.transform.localScale = previewActorSourceVisualRoot.lossyScale;

        // flip/정렬/알파 동기화
        var srcRenderers = previewActorSourceVisualRoot.GetComponentsInChildren<SpriteRenderer>(true);
        var dstRenderers = previewActorRoot.GetComponentsInChildren<SpriteRenderer>(true);

        int count = Mathf.Min(srcRenderers.Length, dstRenderers.Length);
        for (int i = 0; i < count; i++)
        {
            var src = srcRenderers[i];
            var dst = dstRenderers[i];
            if (src == null || dst == null) continue;

            dst.flipX = src.flipX;
            dst.flipY = src.flipY;
            dst.sortingLayerID = src.sortingLayerID;
            dst.sortingOrder = src.sortingOrder + 1;
            dst.color = ForceOpaque(src.color);
        }

        // Animator가 있으면 바로 재생 유지
        if (previewActorAnimator != null)
        {
            previewActorAnimator.speed = 1f;
            previewActorAnimator.enabled = true;
        }
    }

    void ClearPreviewActorVisual()
    {
        if (previewActorRoot != null)
        {
            Destroy(previewActorRoot);
            previewActorRoot = null;
        }

        previewActorAnimator = null;
        previewActorSourceVisualRoot = null;
        previewDisabledBehaviours.Clear();
        previewDisabledColliders.Clear();
        previewDisabledCanvas.Clear();
        previewActorSpriteCache.Clear(); // 남아 있어도 무방하지만 정리
    }

    void ClearPreviewGhostPresentation()
    {
        RestoreActiveUnitVisual(activeUnit);
        ClearPreviewActorVisual();
    }

    void UpdatePreviewGhostPresentation()
    {
        if (!usePreviewActorGhost || activeUnit == null || activeUnit.IsDead || grid == null)
        {
            ClearPreviewGhostPresentation();
            return;
        }

        if (!PlannedMoveTile.HasValue)
        {
            ClearPreviewGhostPresentation();
            return;
        }

        // 기존 타일 ghost는 이제 안 씀
        if (tileHighlighter != null)
            tileHighlighter.ClearGhostTile();

        SetActiveUnitPreviewAlpha(activeUnit, sourceActorPreviewAlpha);
        UpdatePreviewActorTransform(activeUnit, PreviewPosition);
    }

    Transform FindPreviewVisualRoot(Unit unit)
    {
        if (unit == null) return null;

        if (!string.IsNullOrEmpty(previewVisualRootName))
        {
            var t = unit.transform.Find(previewVisualRootName);
            if (t != null) return t;
        }

        // fallback: Animator가 있는 첫 child
        var anim = unit.GetComponentInChildren<Animator>(true);
        if (anim != null) return anim.transform;

        // 최후 fallback
        return unit.transform;
    }

    void EnsurePreviewActor(Unit sourceUnit)
    {
        if (!usePreviewActorGhost || sourceUnit == null) return;

        Transform visualRoot = FindPreviewVisualRoot(sourceUnit);
        if (visualRoot == null) return;

        // 같은 source + 같은 visual root면 재사용
        if (previewActorRoot != null &&
            previewActorSourceUnit == sourceUnit &&
            previewActorSourceVisualRoot == visualRoot)
            return;

        ClearPreviewActorVisual();

        previewActorSourceUnit = sourceUnit;
        previewActorSourceVisualRoot = visualRoot;

        previewActorRoot = Instantiate(visualRoot.gameObject, transform);
        previewActorRoot.name = $"PreviewActor_{sourceUnit.name}";

        // preview actor 내부에서 로직/충돌/UI 비활성
        previewDisabledBehaviours.Clear();
        previewDisabledColliders.Clear();
        previewDisabledCanvas.Clear();

        var behaviours = previewActorRoot.GetComponentsInChildren<Behaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            var b = behaviours[i];
            if (b == null) continue;

            // Animator는 유지
            if (b is Animator) continue;

            // SpriteRenderer는 Behaviour가 아님, 여긴 영향 없음
            // 전투 로직/입력/HUD 관련 스크립트 비활성
            b.enabled = false;
            previewDisabledBehaviours.Add(b);
        }

        var colliders = previewActorRoot.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            var c = colliders[i];
            if (c == null) continue;
            c.enabled = false;
            previewDisabledColliders.Add(c);
        }

        var canvases = previewActorRoot.GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            var cv = canvases[i];
            if (cv == null) continue;
            cv.enabled = false;
            previewDisabledCanvas.Add(cv);
        }

        previewActorAnimator = previewActorRoot.GetComponentInChildren<Animator>(true);

        // preview actor는 항상 원본 알파 유지
        var previewRenderers = previewActorRoot.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < previewRenderers.Length; i++)
        {
            var sr = previewRenderers[i];
            if (sr == null) continue;

            sr.color = ForceOpaque(sr.color);
            sr.sortingOrder += 1;
        }
    }

    Color ForceOpaque(Color c)
    {
        c.a = 1f;
        return c;
    }
}   
