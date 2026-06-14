using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

public class BattleController : MonoBehaviour
{
    [SerializeField] private CameraShake cameraShake;

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

    [Header("Skill Tooltip")]
    public SkillTooltip skillTooltip;

    [Header("Skill Cost UI")]
    public Color costOnColor = Color.white;
    public Color costOffColor = new Color(0.25f, 0.25f, 0.25f, 0.55f);

    [Header("Enemy AI")]
    public AIProfile enemyAIProfile;

    [Header("UI")]
    public Button[] skillButtons;      // size 3
    public TMP_Text turnText;
    [SerializeField] private UnitInfoPanel unitInfoPanel;

    [Header("Battle Tempo")]
    [SerializeField] private float turnTransitionDelay = 0.35f;
    [SerializeField] private float enemyThinkDelay = 0.45f;
    [SerializeField] private float afterMoveDelay = 0.15f;
    [SerializeField] private float afterSkillDelay = 0.25f;
    [SerializeField] private float skippedTurnDelay = 0.35f;

    [Header("AP UX")]
    public Color skillButtonNormalTextColor = Color.white;
    public Color skillButtonNoApTextColor = new Color(1f, 0.45f, 0.45f, 1f);
    public Color skillButtonDisabledTextColor = new Color(0.65f, 0.65f, 0.65f, 1f);

    public Color skillButtonNormalBgColor = Color.white;
    public Color skillButtonNoApBgColor = new Color(0.45f, 0.35f, 0.35f, 1f);
    public Color skillButtonDisabledBgColor = new Color(0.45f, 0.45f, 0.45f, 1f);

    public float apButtonShakeDuration = 0.14f;
    public float apButtonShakeMagnitude = 10f;
    public string apInsufficientMessage = "AP 부족";

    bool skillButtonsInputEnabled = false;
    Coroutine apInsufficientTextCo;
    readonly Dictionary<Button, Coroutine> buttonShakeCos = new Dictionary<Button, Coroutine>();

    [Header("Fail Feedback Text")]
    public string msgNotEnoughAP = "AP 부족";
    public string msgOutOfRange = "사거리 밖";
    public string msgBlockedByLOS = "LOS 차단";
    public string msgNoValidTarget = "유효한 대상 없음";
    public string msgHeightRestricted = "높이 차로 이동 불가";
    public string msgInvalidMove = "이동 불가";
    public string msgCannotAct = "행동 불가";

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

    public enum ActionFailReason
    {
        None,
        NotEnoughAP,
        OutOfRange,
        BlockedByLOS,
        NoValidTarget,
        HeightRestricted,
        InvalidMove,
        CannotAct
    }
    private PlayerInputMode inputMode = PlayerInputMode.Move;

    private bool hasMovedThisTurn = false;

    public PlayerInputMode InputMode => inputMode;
    public SkillData SelectedSkill => selectedSkill;
    public Unit ActiveUnit => activeUnit;

    // hoverTile:
    // - ClickTileAOE에서는 중심 타일 hover
    // - ClickSingle / All* / AutoNearestSingle에서도 "현재 커서 아래 타일" 공통 hover
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
                var iconImage =
                    skillButtons[i]
                    .transform
                    .Find("Frame/Icon")
                    ?.GetComponent<Image>();

                if (s != null)
                {

                    if (iconImage != null)
                    {
                        if (s != null && s.icon != null)
                        {
                            iconImage.enabled = true;
                            iconImage.sprite = s.icon;
                        }
                        else
                        {
                            iconImage.enabled = false;
                        }
                    }
                }
                else
                {
                    if (iconImage != null)
                        iconImage.sprite = null;
                }

                var hover = skillButtons[i].GetComponent<SkillButtonHover>();
                if (hover != null)
                {
                    hover.Setup(s, skillTooltip);
                }

                RefreshSkillCostIcons(skillButtons[i], s);
            }
        }

        RefreshSkillButtonStates();
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
        if (!tileHighlighter) tileHighlighter = FindFirstObjectByType<TileHighlighter>();

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

            StartCoroutine(SkippedTurnRoutine());
            return;
        }

        if (IsAlly(activeUnit))
        {
            waitingInput = true;
            hasMovedThisTurn = false;
            ClearAllPlannedActions();

            SetupSkillButtons();
            SetSkillButtonsInteractable(true);
            RefreshSkillButtonStates();

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

    private IEnumerator SkippedTurnRoutine()
    {
        busy = true;
        yield return new WaitForSeconds(skippedTurnDelay);
        busy = false;

        OnActionComplete();
    }

    void OnActionComplete()
    {   
        HideUnitInfo();
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
        StartCoroutine(StartNextTurnDelayedRoutine());
        RefreshSkillButtonStates();
    }

    private IEnumerator StartNextTurnDelayedRoutine()
    {
        busy = true;
        yield return new WaitForSeconds(turnTransitionDelay);
        busy = false;

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
        {
            TriggerAPInsufficientFeedback(skillIndex);
            RefreshSkillButtonStates();
            return;
        }

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

        RefreshSkillButtonStates();
        RefreshActiveUnitHud();

        bool hitDone = false;
        bool endDone = false;
        bool effectsApplied = false;

        IEnumerator ApplyOnce()
        {
            if (effectsApplied)
                yield break;

            effectsApplied = true;

            yield return PlaySkillFxIfNeeded(attacker, skill, resolved);

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

        yield return new WaitForSeconds(afterSkillDelay);

        onComplete?.Invoke();
    }

    void SetSkillButtonsInteractable(bool v)
    {
        skillButtonsInputEnabled = v;
        RefreshSkillButtonStates();
    } 

    Unit ChooseFirstAlive(List<Unit> list)
    {
        foreach (var u in list)
            if (u != null && !u.IsDead)
                return u;
        return null;
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
        yield return new WaitForSeconds(enemyThinkDelay);

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
                    yield return new WaitForSeconds(afterMoveDelay);
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

    private bool HasAnyUsableSkill(Unit unit)
    {
        if (unit == null || unit.IsDead) return false;
        if (!unit.CanAct()) return false;

        var pool = GetSkillPoolFor(unit);
        if (pool == null || pool.Length == 0) return false;

        for (int i = 0; i < pool.Length; i++)
        {
            var skill = pool[i];
            if (skill == null) continue;

            if (unit.CanPayAP(skill.costAP))
                return true;
        }

        return false;
    }

    private List<Vector2Int> BuildManhattanRangeTiles(Vector2Int origin, int minR, int maxR)
    {
        var list = new List<Vector2Int>();
        if (!grid) grid = GridManager.I;
        if (!grid) return list;

        SkillData skill = PlannedSkill != null ? PlannedSkill : selectedSkill;
        if (skill == null) return list;

        if (minR < 0) minR = 0;
        if (maxR < minR) maxR = minR;

        for (int d = minR; d <= maxR; d++)
        {
            for (int dx = -d; dx <= d; dx++)
            {
                int dy = d - Mathf.Abs(dx);

                var p1 = new Vector2Int(origin.x + dx, origin.y + dy);
                if (IsValidSkillRangeTile(skill, origin, p1))
                    list.Add(p1);

                if (dy != 0)
                {
                    var p2 = new Vector2Int(origin.x + dx, origin.y - dy);
                    if (IsValidSkillRangeTile(skill, origin, p2))
                        list.Add(p2);
                }
            }
        }

        return list;
    }

    private bool IsValidSkillRangeTile(SkillData skill, Vector2Int from, Vector2Int tile)
    {
        if (grid == null || skill == null)
            return false;

        if (!grid.InBounds(tile))
            return false;

        // 타일이 없으면 표시하지 않음
        var tileView = grid.GetTile(tile);
        if (tileView == null)
            return false;

        // 벽/바위/나무 등 이동불가 obstacle 위에는 표시하지 않음
        if (!tileView.Passable)
            return false;

        // LOS 필요 스킬이면 LOS 차단 시 표시하지 않음
        if (skill.requiresLineOfSight && !grid.HasLineOfSight(from, tile))
            return false;

        return true;
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

    public void OnHoverUnit(Unit hovered)
    {
        // ClickSingle을 tile 기반으로 바꿨으므로 사용 안 함.
    }

    public void ClearHoverSinglePreview()
    {
        // ClickSingle hover도 hoverTile 하나로 처리하므로 별도 정리 불필요.
    }

    public void OnUnitClicked(Unit clicked)
    {
        // ClickSingle을 tile 기반으로 바꿨으므로 사용 안 함.
    }

    public void OnTileClicked(Vector2Int gridPos)
    {   

        Unit unitOnTile = null;

        if (grid != null)
            unitOnTile = grid.GetUnitAt(gridPos);

        if (unitOnTile != null)
        {
            ShowUnitInfo(unitOnTile);

            // Move 상태에서 유닛이 있는 칸을 클릭한 경우:
            // 정보만 보고 이동 계획은 하지 않음
            if (inputMode == PlayerInputMode.Move)
                return;
        }
        else
        {
            HideUnitInfo();
        }

        if (battleEnded) return;
        if (!waitingInput || busy) return;
        if (activeUnit == null || activeUnit.IsDead) return;
        if (!IsAlly(activeUnit)) return;

        // ClickSingle 타일 클릭
        if (inputMode == PlayerInputMode.SkillPreview &&
            selectedSkill != null &&
            selectedSkill.targetMode == SkillTargetMode.ClickSingle)
        {
            Unit clickedTarget = GetUnitFromClickedTile(gridPos);

            // self-target preview tile special case
            if (clickedTarget == null &&
                activeUnit != null &&
                !activeUnit.IsDead &&
                gridPos == PreviewPosition)
            {
                clickedTarget = activeUnit;
            }

            var fail = EvaluateTileTargetFailure(PlannedSkill, gridPos, PreviewPosition);
            if (fail != ActionFailReason.None)
            {
                TriggerActionFailFeedback(fail, PlannedSkillIndex, pulseHudAP: false);
                return;
            }

            if (clickedTarget == null || clickedTarget.IsDead)
            {
                TriggerActionFailFeedback(ActionFailReason.NoValidTarget, PlannedSkillIndex, false);
                return;
            }

            hoverTile = null;
            SetPlannedSkillTarget(gridPos, clickedTarget);
            RefreshPlanningVisuals();
            return;
        }

        // AOE 타일 클릭
        if (inputMode == PlayerInputMode.SkillPreview &&
            selectedSkill != null &&
            selectedSkill.targetMode == SkillTargetMode.ClickTileAOE)
        {
            var fail = EvaluateTileTargetFailure(PlannedSkill, gridPos, PreviewPosition);
            if (fail != ActionFailReason.None)
            {
                TriggerActionFailFeedback(fail, PlannedSkillIndex, pulseHudAP: false);
                return;
            }

            hoverTile = null;
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

        if (gridPos == activeUnit.GridPos)
        {
            ClearPlannedMove();
            RefreshPlanningVisuals();
            return;
        }

        hoverMoveTile = null;
        PlanMove(gridPos);
    }
    public void OnHoverTile(Vector2Int gridPos)
    {
        if (battleEnded) return;
        if (activeUnit == null || activeUnit.IsDead) return;
        if (inputMode != PlayerInputMode.SkillPreview) return;
        if (selectedSkill == null) return;

        if (hoverTile.HasValue && hoverTile.Value == gridPos) return;

        hoverTile = gridPos;
        RefreshPlanningVisuals();
    }

    public void ClearHoverAOEPreview()
    {
        if (!hoverTile.HasValue)
            return;

        hoverTile = null;

        if (busy || !waitingInput || battleEnded || activeUnit == null || activeUnit.IsDead)
            return;

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

        // move가 이미 계획된 뒤에는 hover preview를 더 이상 갱신하지 않음
        if (PlannedMoveTile.HasValue) return;

        if (hoverMoveTile.HasValue && hoverMoveTile.Value == gridPos) return;

        // 핵심:
        // 이제는 유효/무효와 상관없이 hover 위치 자체는 저장한다.
        // selected는 모든 hover 타일에 뜨고,
        // actionable/path는 RefreshPlanningVisuals()에서 유효할 때만 뜬다.
        hoverMoveTile = gridPos;
        RefreshPlanningVisuals();
    }               

    public void ClearHoverMovePathPreview()
    {
        if (!hoverMoveTile.HasValue)
            return;

        hoverMoveTile = null;

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
        tileHighlighter.ClearActionableTiles();

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
        // Planned move: selected + planned path
        // -------------------------
        if (PlannedMoveTile.HasValue)
        {
            var moveTile = PlannedMoveTile.Value;

            tileHighlighter.ClearGhostTile();
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
            tileHighlighter.ClearPreviewAreaTiles();

            // 모든 스킬 공통: 빈 타일 hover면 selected
            // 단, explicit target lock이 걸린 상태에서는 locked preview를 우선
            if (hoverTile.HasValue && !HasPlannedTarget)
            {
                tileHighlighter.ShowSelectedTile(hoverTile.Value);
            }

            switch (PlannedSkill.targetMode)
            {
                // =====================================
                // ClickSingle
                // =====================================
                case SkillTargetMode.ClickSingle:
                {
                    // locked target preview
                    if (PlannedClickedTile.HasValue)
                    {
                        Vector2Int targetTile = PlannedClickedTile.Value;
                        tileHighlighter.ShowSelectedTile(targetTile);

                        Unit clickedTarget = GetUnitFromClickedTile(targetTile);

                        var resolved = CombatTargetResolver.ResolveTargetsFromPosition(
                            PlannedSkill,
                            activeUnit,
                            PreviewPosition,
                            GetCasterAllies(activeUnit),
                            GetCasterEnemies(activeUnit),
                            grid,
                            targetTile,
                            clickedTarget
                        );

                        ShowResolvedSkillPreview(
                            resolved,
                            activeUnit,
                            PreviewPosition,
                            showPreviewArea: true,   // ← 복구 포인트
                            showActionable: true
                        );
                    }
                    // hover preview
                    else if (hoverTile.HasValue)
                    {
                        Vector2Int targetTile = hoverTile.Value;
                        tileHighlighter.ShowSelectedTile(targetTile);

                        bool castable = CombatTargetResolver.IsPointCastable(
                            PlannedSkill,
                            PreviewPosition,
                            targetTile,
                            grid
                        );

                        if (castable)
                        {
                            Unit hoveredTarget = GetUnitFromClickedTile(targetTile);

                            var resolved = CombatTargetResolver.ResolveTargetsFromPosition(
                                PlannedSkill,
                                activeUnit,
                                PreviewPosition,
                                GetCasterAllies(activeUnit),
                                GetCasterEnemies(activeUnit),
                                grid,
                                targetTile,
                                hoveredTarget
                            );

                            ShowResolvedSkillPreview(
                                resolved,
                                activeUnit,
                                PreviewPosition,
                                showPreviewArea: true,   // ← 복구 포인트
                                showActionable: true
                            );
                        }
                    }

                    break;
                }

                // =====================================
                // ClickTileAOE
                // =====================================
                case SkillTargetMode.ClickTileAOE:
                {
                    // locked target preview
                    if (PlannedClickedTile.HasValue)
                    {
                        Vector2Int center = PlannedClickedTile.Value;

                        tileHighlighter.ShowSelectedTile(center);
                        tileHighlighter.ShowActionableTiles(new[] { center });

                        var aoeTiles = BuildManhattanDisk(center, Mathf.Max(0, PlannedSkill.aoeRadius));
                        tileHighlighter.ShowPreviewAreaTiles(aoeTiles);

                        var resolved = CombatTargetResolver.ResolveTargetsFromPosition(
                            PlannedSkill,
                            activeUnit,
                            PreviewPosition,
                            GetCasterAllies(activeUnit),
                            GetCasterEnemies(activeUnit),
                            grid,
                            center,
                            null
                        );

                        previewTargetUnits.Clear();
                        previewTargetUnits.AddRange(resolved);

                        SplitPreviewTargetTiles(previewTargetUnits, activeUnit, PreviewPosition);

                        if (previewEnemyTiles.Count > 0)
                            tileHighlighter.ShowTargetTiles(previewEnemyTiles);

                        if (previewFriendlyFireTiles.Count > 0)
                            tileHighlighter.ShowFriendlyFireTiles(previewFriendlyFireTiles);

                        ApplyPreviewTargetIndicators(previewTargetUnits, activeUnit, PreviewPosition);
                    }
                    // hover preview
                    else if (hoverTile.HasValue)
                    {
                        Vector2Int center = hoverTile.Value;

                        bool castable = CombatTargetResolver.IsPointCastable(
                            PlannedSkill,
                            PreviewPosition,
                            center,
                            grid
                        );

                        if (castable)
                        {
                            tileHighlighter.ShowActionableTiles(new[] { center });

                            var aoeTiles = BuildManhattanDisk(center, Mathf.Max(0, PlannedSkill.aoeRadius));
                            tileHighlighter.ShowPreviewAreaTiles(aoeTiles);

                            var resolved = CombatTargetResolver.ResolveTargetsFromPosition(
                                PlannedSkill,
                                activeUnit,
                                PreviewPosition,
                                GetCasterAllies(activeUnit),
                                GetCasterEnemies(activeUnit),
                                grid,
                                center,
                                null
                            );

                            previewTargetUnits.Clear();
                            previewTargetUnits.AddRange(resolved);

                            SplitPreviewTargetTiles(previewTargetUnits, activeUnit, PreviewPosition);

                            if (previewEnemyTiles.Count > 0)
                                tileHighlighter.ShowTargetTiles(previewEnemyTiles);

                            if (previewFriendlyFireTiles.Count > 0)
                                tileHighlighter.ShowFriendlyFireTiles(previewFriendlyFireTiles);

                            ApplyPreviewTargetIndicators(previewTargetUnits, activeUnit, PreviewPosition);
                        }
                    }

                    break;
                }

                // =====================================
                // Auto / All* 계열
                // =====================================
                case SkillTargetMode.AutoNearestSingle:
                case SkillTargetMode.AllEnemiesInRange:
                case SkillTargetMode.AllEnemiesAnywhere:
                case SkillTargetMode.AllAlliesInRange:
                case SkillTargetMode.AllAlliesAnywhere:
                {
                    var resolved = CombatTargetResolver.ResolveTargetsFromPosition(
                        PlannedSkill,
                        activeUnit,
                        PreviewPosition,
                        GetCasterAllies(activeUnit),
                        GetCasterEnemies(activeUnit),
                        grid,
                        null,
                        null
                    );

                    ShowResolvedSkillPreview(
                        resolved,
                        activeUnit,
                        PreviewPosition,
                        showPreviewArea: true,
                        showActionable: true
                    );

                    break;
                }
            }
        }

        // -------------------------
        // Move hover path preview
        // -------------------------
        if (inputMode == PlayerInputMode.Move && hoverMoveTile.HasValue && !PlannedMoveTile.HasValue)
        {
            Vector2Int hover = hoverMoveTile.Value;

            tileHighlighter.ShowSelectedTile(hover);

            bool isValidMoveHover =
                !hasMovedThisTurn &&
                activeUnit.CanMove() &&
                reachableMoveCache != null &&
                reachableMoveCameFromCache != null &&
                reachableMoveCache.ContainsKey(hover) &&
                hover != activeUnit.GridPos;

            if (isValidMoveHover)
            {
                tileHighlighter.ShowActionableTiles(new[] { hover });

                var hoverPath = grid.ReconstructPath(activeUnit.GridPos, hover, reachableMoveCameFromCache);
                if (hoverPath != null && hoverPath.Count > 0)
                {
                    tileHighlighter.ShowPathTiles(hoverPath);

                    BuildPreviewHazardTiles(hoverPath);
                    if (previewHazardTiles.Count > 0)
                        tileHighlighter.ShowHoverHazardPathTiles(previewHazardTiles);
                }
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
                    TriggerActionFailFeedback(ActionFailReason.NoValidTarget, action.skillIndex, false);

                    busy = false;
                    waitingInput = true;
                    SetSkillButtonsInteractable(true);
                    RefreshPlanningVisuals();
                    yield break;
                }

                if (!activeUnit.CanPayAP(action.skill.costAP))
                {
                    TriggerActionFailFeedback(ActionFailReason.NotEnoughAP, action.skillIndex, true);

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

                bool allowEmptyAOE =
                    action.skill.targetMode == SkillTargetMode.ClickTileAOE &&
                    action.clickedTile.HasValue &&
                    CombatTargetResolver.IsPointCastable(
                        action.skill,
                        activeUnit.GridPos,
                        action.clickedTile.Value,
                        grid
                    );

                if (finalTargets.Count > 0 || allowEmptyAOE)
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
                else
                {
                    TriggerActionFailFeedback(ActionFailReason.NoValidTarget, action.skillIndex, false);

                    busy = false;
                    waitingInput = true;
                    SetSkillButtonsInteractable(true);
                    RefreshPlanningVisuals();
                    yield break;
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

        // 2) Skill을 사용했으면:
        //    - AP가 남고 사용 가능한 스킬이 있으면 턴 유지
        //    - 아니면 턴 종료
        if (executedSkill)
        {
            actionQueue.Clear();
            ClearPlannedTarget();

            selectedSkill = null;
            selectedSkillIndex = -1;

            inputMode = PlayerInputMode.SkillPreview;

            RefreshSkillButtonStates();

            if (activeUnit != null &&
                !activeUnit.IsDead &&
                activeUnit.CanAct() &&
                HasAnyUsableSkill(activeUnit))
            {
                busy = false;
                waitingInput = true;
                SetSkillButtonsInteractable(true);
                RefreshPlanningVisuals();
                yield break;
            }

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
            RefreshSkillButtonStates();

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

        HideUnitInfo();
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
        {
            bool inBounds = grid != null && grid.InBounds(gridPos);
            bool climbBlocked = false;

            if (grid != null && activeUnit != null && inBounds)
                climbBlocked = !grid.CanClimb(activeUnit, activeUnit.GridPos, gridPos);

            TriggerActionFailFeedback(
                climbBlocked ? ActionFailReason.HeightRestricted : ActionFailReason.InvalidMove
            );
            return;
        }

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
            {
                if (action.clickedUnit != null)
                    return true;

                if (action.clickedTile.HasValue &&
                    activeUnit != null &&
                    !activeUnit.IsDead &&
                    action.clickedTile.Value == PreviewPosition)
                {
                    return true;
                }

                return false;
            }

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

            bool sameSide = IsAlly(u) == casterIsAlly;
            var occupied = GetUnitOccupiedTiles(u, casterPreviewPos);

            for (int j = 0; j < occupied.Count; j++)
            {
                if (sameSide)
                    previewFriendlyFireTiles.Add(occupied[j]);
                else
                    previewEnemyTiles.Add(occupied[j]);
            }
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

    HashSet<Vector2Int> BuildResolvedTargetTileSet(List<Unit> resolvedTargets, Unit caster, Vector2Int casterPreviewPos)
    {
        var set = new HashSet<Vector2Int>();

        if (resolvedTargets == null)
            return set;

        for (int i = 0; i < resolvedTargets.Count; i++)
        {
            var u = resolvedTargets[i];
            if (u == null || u.IsDead) continue;

            var occupied = GetUnitOccupiedTiles(u, casterPreviewPos);
            for (int j = 0; j < occupied.Count; j++)
                set.Add(occupied[j]);
        }

        return set;
    }

    void ShowResolvedSkillPreview(List<Unit> resolvedTargets, Unit caster, Vector2Int casterPreviewPos, bool showPreviewArea, bool showActionable)
    {
        previewTargetUnits.Clear();

        if (resolvedTargets != null)
            previewTargetUnits.AddRange(resolvedTargets);

        SplitPreviewTargetTiles(previewTargetUnits, caster, casterPreviewPos);

        var actualTiles = BuildResolvedTargetTileSet(previewTargetUnits, caster, casterPreviewPos);

        if (showPreviewArea && actualTiles.Count > 0)
            tileHighlighter.ShowPreviewAreaTiles(actualTiles);

        if (previewEnemyTiles.Count > 0)
            tileHighlighter.ShowTargetTiles(previewEnemyTiles);

        if (previewFriendlyFireTiles.Count > 0)
            tileHighlighter.ShowFriendlyFireTiles(previewFriendlyFireTiles);

        ApplyPreviewTargetIndicators(previewTargetUnits, caster, casterPreviewPos);

        if (showActionable && actualTiles.Count > 0)
            tileHighlighter.ShowActionableTiles(actualTiles);
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

                // 1. 맵에 실제 로직 타일이 없는 곳은 표시하지 않음
                var tile = grid.GetTile(p);
                if (tile == null)
                    continue;

                // 2. obstacle / wall / tree 등 passable=false 타일은 표시하지 않음
                if (!tile.Passable)
                    continue;

                // 3. 기존 스킬 사거리 + LOS 규칙 유지
                if (!CombatTargetResolver.IsPointCastable(skill, fromPos, p, grid))
                    continue;

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
        Vector3 targetWorld = grid.GridToWorldWithHeight(previewGridPos) + previewActorOffset;
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

    void RefreshSkillButtonStates()
    {
        if (skillButtons == null) return;

        var pool = GetSkillPoolFor(activeUnit);

        for (int i = 0; i < skillButtons.Length; i++)
        {
            var button = skillButtons[i];
            if (button == null) continue;

            var label = button.GetComponentInChildren<TMP_Text>();
            var image = button.GetComponent<Image>();

            SkillData skill = (pool != null && i < pool.Length) ? pool[i] : null;

            bool hasSkill = skill != null;
            bool hasAP = hasSkill && activeUnit != null && activeUnit.CanPayAP(skill.costAP);

            // 클릭 자체는 가능하게 두고, 전역 잠금일 때만 막는다.
            button.interactable = skillButtonsInputEnabled && hasSkill;

            if (!hasSkill)
            {
                if (label != null) label.color = skillButtonDisabledTextColor;
                if (image != null) image.color = skillButtonDisabledBgColor;
                continue;
            }

            if (!skillButtonsInputEnabled)
            {
                if (label != null) label.color = skillButtonDisabledTextColor;
                if (image != null) image.color = skillButtonDisabledBgColor;
                continue;
            }

            if (hasAP)
            {
                if (label != null) label.color = skillButtonNormalTextColor;
                if (image != null) image.color = skillButtonNormalBgColor;
            }
            else
            {
                if (label != null) label.color = skillButtonNoApTextColor;
                if (image != null) image.color = skillButtonNoApBgColor;
            }
        }

        RefreshActiveUnitHud();
    }

    void RefreshActiveUnitHud()
    {
        if (activeUnit == null) return;

        var hud = activeUnit.GetComponentInChildren<UnitHud>(true);
        if (hud != null)
            hud.Refresh();
    }

    void TriggerAPInsufficientFeedback(int skillIndex)
    {
        TriggerActionFailFeedback(
            ActionFailReason.NotEnoughAP,
            skillIndex,
            pulseHudAP: true
        );
    }

    string GetFailMessage(ActionFailReason reason)
    {
        switch (reason)
        {
            case ActionFailReason.NotEnoughAP:   return msgNotEnoughAP;
            case ActionFailReason.OutOfRange:    return msgOutOfRange;
            case ActionFailReason.BlockedByLOS:  return msgBlockedByLOS;
            case ActionFailReason.NoValidTarget: return msgNoValidTarget;
            case ActionFailReason.HeightRestricted: return msgHeightRestricted;
            case ActionFailReason.InvalidMove:   return msgInvalidMove;
            case ActionFailReason.CannotAct:     return msgCannotAct;
            default:                             return "";
        }
    }

    void TriggerActionFailFeedback(ActionFailReason reason, int skillIndex = -1, bool pulseHudAP = false)
    {
        string msg = GetFailMessage(reason);
        if (!string.IsNullOrEmpty(msg))
            ShowTemporaryTurnText(msg);

        if (skillIndex >= 0 &&
            skillButtons != null &&
            skillIndex < skillButtons.Length &&
            skillButtons[skillIndex] != null)
        {
            ShakeButton(skillButtons[skillIndex]);
        }

        if (pulseHudAP && activeUnit != null)
        {
            var hud = activeUnit.GetComponentInChildren<UnitHud>(true);
            if (hud != null)
                hud.PulseAPInsufficient();
        }
    }

    void ShowTemporaryTurnText(string msg)
    {
        if (turnText == null) return;

        if (apInsufficientTextCo != null)
            StopCoroutine(apInsufficientTextCo);

        apInsufficientTextCo = StartCoroutine(CoShowTemporaryTurnText(msg, 0.6f));
    }

    IEnumerator CoShowTemporaryTurnText(string msg, float duration)
    {
        if (turnText == null) yield break;

        string prev = turnText.text;
        turnText.text = msg;

        yield return new WaitForSeconds(duration);

        if (activeUnit != null && !battleEnded)
            turnText.text = $"{activeUnit.name} TURN (SPD {activeUnit.speed})";
        else
            turnText.text = prev;

        apInsufficientTextCo = null;
    }

    void ShakeButton(Button button)
    {
        if (button == null) return;

        if (buttonShakeCos.TryGetValue(button, out var oldCo) && oldCo != null)
            StopCoroutine(oldCo);

        var co = StartCoroutine(CoShakeButton(button));
        buttonShakeCos[button] = co;
    }

    IEnumerator CoShakeButton(Button button)
    {
        if (button == null) yield break;

        RectTransform rt = button.transform as RectTransform;
        if (rt == null) yield break;

        Vector2 basePos = rt.anchoredPosition;
        float t = 0f;

        while (t < apButtonShakeDuration)
        {
            t += Time.deltaTime;
            float damper = 1f - Mathf.Clamp01(t / apButtonShakeDuration);

            float x = Random.Range(-1f, 1f) * apButtonShakeMagnitude * damper;
            float y = Random.Range(-1f, 1f) * (apButtonShakeMagnitude * 0.35f) * damper;

            rt.anchoredPosition = basePos + new Vector2(x, y);
            yield return null;
        }

        rt.anchoredPosition = basePos;
        buttonShakeCos[button] = null;
    }

    bool IsInSkillRangeFromPreview(SkillData skill, Vector2Int from, Vector2Int to)
    {
        if (skill == null) return false;

        int d = Mathf.Abs(from.x - to.x) + Mathf.Abs(from.y - to.y);
        return d >= skill.minRange && d <= skill.maxRange;
    }

    ActionFailReason EvaluateCastPointFailure(SkillData skill, Vector2Int from, Vector2Int to)
    {
        if (skill == null) return ActionFailReason.NoValidTarget;

        if (!IsInSkillRangeFromPreview(skill, from, to))
            return ActionFailReason.OutOfRange;

        if (skill.requiresLineOfSight && (grid == null || !grid.HasLineOfSight(from, to)))
            return ActionFailReason.BlockedByLOS;

        return ActionFailReason.None;
    }

    ActionFailReason EvaluateUnitTargetFailure(SkillData skill, Unit target, Vector2Int from)
    {
        if (skill == null || target == null || target.IsDead)
            return ActionFailReason.NoValidTarget;

        var pointFail = EvaluateCastPointFailure(skill, from, target.GridPos);
        if (pointFail != ActionFailReason.None)
            return pointFail;

        var resolved = CombatTargetResolver.ResolveTargetsFromPosition(
            skill,
            activeUnit,
            from,
            GetCasterAllies(activeUnit),
            GetCasterEnemies(activeUnit),
            grid,
            null,
            target
        );

        return (resolved != null && resolved.Count > 0)
            ? ActionFailReason.None
            : ActionFailReason.NoValidTarget;
    }

    ActionFailReason EvaluateTileTargetFailure(SkillData skill, Vector2Int tile, Vector2Int from)
    {
        if (skill == null)
            return ActionFailReason.NoValidTarget;

        var pointFail = EvaluateCastPointFailure(skill, from, tile);
        if (pointFail != ActionFailReason.None)
            return pointFail;

        if (skill.targetMode == SkillTargetMode.ClickSingle)
        {
            Unit target = GetUnitFromClickedTile(tile);

            if (target == null &&
                activeUnit != null &&
                !activeUnit.IsDead &&
                tile == from)
            {
                target = activeUnit;
            }

            if (target == null || target.IsDead)
                return ActionFailReason.NoValidTarget;

            if (!CanSkillAffectUnit(skill, target))
                return ActionFailReason.NoValidTarget;

            var resolved = CombatTargetResolver.ResolveTargetsFromPosition(
                skill,
                activeUnit,
                from,
                GetCasterAllies(activeUnit),
                GetCasterEnemies(activeUnit),
                grid,
                tile,
                target
            );

            return (resolved != null && resolved.Count > 0)
                ? ActionFailReason.None
                : ActionFailReason.NoValidTarget;
        }

        if (skill.targetMode == SkillTargetMode.ClickTileAOE)
        {
            var resolved = CombatTargetResolver.ResolveTargetsFromPosition(
                skill,
                activeUnit,
                from,
                GetCasterAllies(activeUnit),
                GetCasterEnemies(activeUnit),
                grid,
                tile,
                null
            );

            bool allowEmptyAOE = true;
            if (resolved != null && resolved.Count > 0)
                return ActionFailReason.None;

            return allowEmptyAOE ? ActionFailReason.None : ActionFailReason.NoValidTarget;
        }

        return ActionFailReason.NoValidTarget;
    }

    Unit GetUnitFromClickedTile(Vector2Int tile)
    {
        if (activeUnit != null &&
            !activeUnit.IsDead &&
            tile == PreviewPosition)
        {
            return activeUnit;
        }

        if (grid == null) return null;
        return grid.GetUnitAt(tile);
    }

    // 지금은 1x1 기준.
    // 나중에 2x2 유닛 넣을 때 이 함수만 확장하면 됨.
    List<Vector2Int> GetUnitOccupiedTiles(Unit unit, Vector2Int casterPreviewPos)
    {
        var result = new List<Vector2Int>();
        if (unit == null || unit.IsDead) return result;

        if (unit == activeUnit)
            result.Add(casterPreviewPos);
        else
            result.Add(unit.GridPos);

        return result;
    }

    bool CanSkillAffectUnit(SkillData skill, Unit target)
    {
        if (skill == null || target == null || target.IsDead || activeUnit == null)
            return false;

        bool canClickEnemy = GetCasterEnemies(activeUnit).Contains(target);
        bool canClickAlly = GetCasterAllies(activeUnit).Contains(target) && SkillMetaUtility.IsMostlyHelpfulSkill(skill);

        return canClickEnemy || canClickAlly;
    }

    IEnumerator PlaySkillFxIfNeeded(
    Unit attacker,
    SkillData skill,
    CombatResolver.ResolveResult resolved)
    {
        if (attacker == null || skill == null || resolved.targets == null)
            yield break;

        if (skill.projectileFxPrefab == null)
            yield break;

        Unit target = null;

        for (int i = 0; i < resolved.targets.Count; i++)
        {
            var t = resolved.targets[i];

            if (t != null && !t.IsDead)
            {
                target = t;
                break;
            }
        }

        if (target == null)
            yield break;

        GameObject obj = Instantiate(skill.projectileFxPrefab);
        ProjectileFx projectile = obj.GetComponent<ProjectileFx>();

        if (projectile == null)
        {
            Destroy(obj);
            yield break;
        }

        Vector3 from = attacker.transform.position + skill.projectileStartOffset;
        Vector3 to = target.transform.position + skill.projectileHitOffset;

        yield return projectile.Play(from, to, skill.impactFxPrefab);
        cameraShake?.Shake(0.05f, 0.06f);
    }

    void RefreshSkillCostIcons(Button button, SkillData skill)
    {
        if (button == null) return;

        var root = button.transform.Find("ApCostRoot");
        if (root == null) return;

        int cost = skill != null ? Mathf.Clamp(skill.costAP, 0, 3) : 0;

        for (int i = 0; i < 3; i++)
        {
            var t = root.Find($"Cost_{i}");
            if (t == null) continue;

            var img = t.GetComponent<Image>();
            if (img == null) continue;

            img.enabled = skill != null;
            img.color = i < cost ? costOnColor : costOffColor;
        }
    }

    public void ShowUnitInfo(Unit unit)
    {
        if (unitInfoPanel == null)
        {
            return;
        }

        unitInfoPanel.Show(unit);
    }

    public void HideUnitInfo()
    {
        if (unitInfoPanel != null)
            unitInfoPanel.Hide();
    }
}   
