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

    //ghost(previewPosition)
    private Vector2Int? plannedMoveTile = null;      // ghost 위치
    private SkillData plannedSkill = null;           // 확정 예정 스킬
    private int plannedSkillIndex = -1;
    private Vector2Int? plannedClickedTile = null;   // AOE 중심 고정
    private Unit plannedClickedUnit = null;          // 단일타겟 고정

    public bool HasLockedAOETarget =>
    plannedSkill != null &&
    plannedSkill.targetMode == SkillTargetMode.ClickTileAOE &&
    plannedClickedTile.HasValue;

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
        reachableMoveCache = null;
        reachableMoveCameFromCache = null;
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
                reachableMoveCache = null;
                reachableMoveCameFromCache = null;                
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
            plannedMoveTile = null;
            plannedSkill = null;
            plannedSkillIndex = -1;
            plannedClickedTile = null;
            plannedClickedUnit = null;
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
        //AP부족시
        if (activeUnit == null) return;
        if (!activeUnit.CanPayAP(skill.costAP))
        {
            // TODO: AP 부족 피드백
            return;
        }

        // 같은 스킬 다시 누르면 "즉시 실행"이 아니라 "선택 해제"
        if (plannedSkill == skill && plannedSkillIndex == skillIndex)
        {
            ClearPlannedSkill();
            RefreshPlanningVisuals();
            return;
        }

        // 스킬 계획 저장
        plannedSkill = skill;
        plannedSkillIndex = skillIndex;

        // 기존 호환용 상태도 같이 맞춰둠
        selectedSkill = skill;
        selectedSkillIndex = skillIndex;

        inputMode = PlayerInputMode.SkillPreview;

        // 이동 hover 잔상 제거
        ClearHoverMovePathPreview();

        // 이전 타겟 고정 해제
        ClearPlannedTarget();

        // 화면 갱신
        RefreshPlanningVisuals();

        EventSystem.current?.SetSelectedGameObject(null);
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

        if (!attacker.SpendAP(skill.costAP))
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
        return ResolveTargetsFromPosition(
            skill, caster, caster.GridPos, allies, enemies, clickedTile, clickedUnit);
    }

    List<Unit> ResolveTargetsFromPosition(
    SkillData skill,
    Unit caster,
    Vector2Int casterPos,
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

        bool HasLOS(Vector2Int from, Vector2Int to)
        {
            if (!skill.requiresLineOfSight)
                return true;

            if (grid == null) return true;

            return grid.HasLineOfSight(from, to);
        }

        bool CanAffectPoint(Vector2Int from, Vector2Int to)
        {
            return InCastRange(from, to) && HasLOS(from, to);
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
                if (!CanAffectPoint(casterPos, u.GridPos))
                    continue;

                int d = Dist(casterPos, u.GridPos);
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
                if (clickedUnit != null && !clickedUnit.IsDead)
                {
                    if (!allies.Contains(clickedUnit) &&
                        CanAffectPoint(casterPos, clickedUnit.GridPos))
                    {
                        targets.Add(clickedUnit);
                    }
                    break;
                }

                foreach (var e in Alive(enemies))
                {
                    if (CanAffectPoint(casterPos, e.GridPos))
                        targets.Add(e);
                }
                break;
            }

            case SkillTargetMode.ClickTileAOE:
            {
                if (!clickedTile.HasValue) break;

                var center = clickedTile.Value;
                if (!CanAffectPoint(casterPos, center)) break;

                AddAOEFromCenter(center, enemies);
                break;
            }

            case SkillTargetMode.AllEnemiesInRange:
            {
                foreach (var e in Alive(enemies))
                {
                    if (CanAffectPoint(casterPos, e.GridPos))
                        targets.Add(e);
                }
                break;
            }

            case SkillTargetMode.AllEnemiesAnywhere:
            {
                foreach (var e in Alive(enemies))
                {
                    if (HasLOS(casterPos, e.GridPos))
                        targets.Add(e);
                }
                break;
            }

            case SkillTargetMode.AllAlliesInRange:
            {
                foreach (var a in Alive(allies))
                {
                    if (CanAffectPoint(casterPos, a.GridPos))
                        targets.Add(a);
                }
                break;
            }

            case SkillTargetMode.AllAlliesAnywhere:
            {
                foreach (var a in Alive(allies))
                {
                    if (HasLOS(casterPos, a.GridPos))
                        targets.Add(a);
                }
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
        
        if (plan.moveTile != enemy.GridPos)
        {
            var path = grid.FindPathWithinRange(enemy, plan.moveTile, enemy.moveRange);
            if (path != null)
                yield return StartCoroutine(grid.MovePathRoutine(enemy, path));
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
        if (plannedSkill == null) return;
        if (clicked == null || clicked.IsDead) return;
        if (plannedSkill.targetMode != SkillTargetMode.ClickSingle) return;

        var resolved = ResolveTargetsFromPosition(
            plannedSkill,
            activeUnit,
            PreviewPosition,
            GetCasterAllies(activeUnit),
            GetCasterEnemies(activeUnit),
            null,
            clicked
        );

        if (resolved.Count == 0) return;

        plannedClickedUnit = clicked;
        plannedClickedTile = null;
        RefreshPlanningVisuals();
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
            var resolved = ResolveTargetsFromPosition(
                plannedSkill,
                activeUnit,
                PreviewPosition,
                GetCasterAllies(activeUnit),
                GetCasterEnemies(activeUnit),
                gridPos,
                null
            );

            if (resolved.Count == 0) return;

            plannedClickedTile = gridPos;
            plannedClickedUnit = null;
            RefreshPlanningVisuals();
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
        if (gridPos == activeUnit.GridPos)
        {
            ClearPlannedMove();
            RefreshPlanningVisuals();
            return;
        }

        PlanMove(gridPos);
        return;
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

        bool castable = IsPointCastable(plannedSkill, PreviewPosition, gridPos);
        if (!castable)
        {
            tileHighlighter.ClearTargetOverlay();
            tileHighlighter.ClearTarget();
            tileHighlighter.ShowInvalidTile(gridPos);
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
            tileHighlighter.ClearPath();
            hoverMoveTile = null;
            return;
        }

        // 본인 타일이면 프리뷰 끔
        if (gridPos == activeUnit.GridPos)
        {
            tileHighlighter.ClearPath();
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
            var data = grid.GetReachableData(activeUnit, activeUnit.moveRange);
            reachableMoveCache = data.cost;
            reachableMoveCameFromCache = data.cameFrom;

            if (!reachableMoveCache.ContainsKey(gridPos))
            {
                tileHighlighter.ClearPath();
                hoverMoveTile = null;
                return;
            }

            path = grid.ReconstructPath(activeUnit.GridPos, gridPos, reachableMoveCameFromCache);
        }

        if (path == null || path.Count == 0)
        {
            tileHighlighter.ClearPath();
            hoverMoveTile = null;
            return;
        }

        tileHighlighter.ShowPathTiles(path);
    }

    public void ClearHoverMovePathPreview()
    {
        hoverMoveTile = null;
        if (tileHighlighter != null)
        {
            // A안: range 타일 승격(overlay) 원복 + 별도 target 타일 제거
            tileHighlighter.ClearTargetOverlay();
            tileHighlighter.ClearPath();
        }
    }

    private Vector2Int PreviewPosition
    {
        get
        {
            if (plannedMoveTile.HasValue) return plannedMoveTile.Value;
            return activeUnit != null ? activeUnit.GridPos : Vector2Int.zero;
        }
    }

    private bool HasPlannedTarget =>
        plannedClickedTile.HasValue || plannedClickedUnit != null;

    private void ClearPlannedTarget()
    {
        plannedClickedTile = null;
        plannedClickedUnit = null;
        ClearPreviewTargetIndicators();
        ClearHoverAOEPreview();
    }

    private void ClearPlannedSkill()
    {
        plannedSkill = null;
        plannedSkillIndex = -1;
        selectedSkill = null;
        selectedSkillIndex = -1;
        inputMode = PlayerInputMode.Move;
        ClearPlannedTarget();
    }

    private void ClearPlannedMove()
    {
        plannedMoveTile = null;
        hoverMoveTile = null;
        ClearHoverMovePathPreview();

        // ghost 표시 제거용 메서드가 TileHighlighter에 필요
        if (tileHighlighter) tileHighlighter.ClearGhost();
    }

    private void ClearAllPlannedActions()
    {
        ClearPlannedSkill();
        ClearPlannedMove();
    }

    private void PlanMove(Vector2Int gridPos)
    {
        if (reachableMoveCache == null || reachableMoveCameFromCache == null)
        {
            var data = grid.GetReachableData(activeUnit, activeUnit.moveRange);
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

        plannedMoveTile = gridPos;

        RefreshPlanningVisuals();
    }

    private void RefreshPlanningVisuals()
    {
        if (tileHighlighter) tileHighlighter.ClearAll();
        ClearPreviewTargetIndicators();
        ClearHoverAOEPreview();

        if (activeUnit == null || activeUnit.IsDead) return;

        if (!hasMovedThisTurn && inputMode == PlayerInputMode.Move)
        {
            if (reachableMoveCache == null || reachableMoveCameFromCache == null)
            {
                var data = grid.GetReachableData(activeUnit, activeUnit.moveRange);
                reachableMoveCache = data.cost;
                reachableMoveCameFromCache = data.cameFrom;
            }

            if (tileHighlighter) tileHighlighter.ShowMoveTiles(reachableMoveCache.Keys);

            if (plannedMoveTile.HasValue)
            {
                var path = grid.ReconstructPath(activeUnit.GridPos, plannedMoveTile.Value, reachableMoveCameFromCache);

                if (tileHighlighter)
                {
                    if (path != null && path.Count > 0)
                    {
                        var body = new List<Vector2Int>(path);

                        // 마지막 칸은 ghost가 담당
                        body.RemoveAt(body.Count - 1);

                        if (body.Count > 0)
                            tileHighlighter.ShowPathTiles(body);
                    }

                    tileHighlighter.ShowGhostTile(plannedMoveTile.Value);
                }
            }
        }

        if (plannedSkill != null)
        {
            Vector2Int previewPos = PreviewPosition;

            var rangeTiles = BuildManhattanRangeTiles(previewPos, plannedSkill.minRange, plannedSkill.maxRange);
            if (tileHighlighter) tileHighlighter.ShowRangeTiles(rangeTiles);

            // 스킬 프리뷰 중에도 예정 경로 + ghost 유지
            if (plannedMoveTile.HasValue && tileHighlighter)
            {
                var path = grid.ReconstructPath(activeUnit.GridPos, plannedMoveTile.Value, reachableMoveCameFromCache);

                if (path != null && path.Count > 0)
                {
                    var body = new List<Vector2Int>(path);
                    body.RemoveAt(body.Count - 1);

                    if (body.Count > 0)
                        tileHighlighter.ShowPathTiles(body);
                }

                tileHighlighter.ShowGhostTile(plannedMoveTile.Value);
            }

            var targets = ResolveTargetsFromPosition(
                plannedSkill,
                activeUnit,
                previewPos,
                GetCasterAllies(activeUnit),
                GetCasterEnemies(activeUnit),
                plannedClickedTile,
                plannedClickedUnit
            );

            foreach (var t in targets)
            {
                if (t == null || t.IsDead) continue;
                var hud = GetHud(t);
                if (hud) hud.SetTargeted(true);
                previewTargetUnits.Add(t);
            }

            if (plannedSkill.targetMode == SkillTargetMode.ClickTileAOE && plannedClickedTile.HasValue)
            {
                var tiles = BuildManhattanDisk(plannedClickedTile.Value, Mathf.Max(0, plannedSkill.aoeRadius));
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

        Vector2Int executeFrom = activeUnit.GridPos;

        bool NeedsExplicitTarget(SkillData skill)
        {
            if (skill == null) return false;

            return skill.targetMode == SkillTargetMode.ClickSingle ||
                skill.targetMode == SkillTargetMode.ClickTileAOE;
        }

        if (plannedMoveTile.HasValue && plannedMoveTile.Value != activeUnit.GridPos)
        {
            var path = grid.ReconstructPath(activeUnit.GridPos, plannedMoveTile.Value, reachableMoveCameFromCache);
            if (path == null)
            {
                busy = false;
                waitingInput = true;
                SetSkillButtonsInteractable(true);
                yield break;
            }

            if (tileHighlighter) tileHighlighter.ClearAll();
            yield return StartCoroutine(grid.MovePathRoutine(activeUnit, path));
            hasMovedThisTurn = true;
            reachableMoveCache = null;
            reachableMoveCameFromCache = null;
            executeFrom = activeUnit.GridPos;
        }

        if (plannedSkill != null)
        {
            if (RequiresExplicitTarget(plannedSkill) &&
                !HasExplicitPlannedTarget(plannedSkill))
            {
                busy = false;
                waitingInput = true;
                SetSkillButtonsInteractable(true);
                yield break;
            }

            if (!activeUnit.CanPayAP(plannedSkill.costAP))
            {
                busy = false;
                waitingInput = true;
                SetSkillButtonsInteractable(true);
                RefreshPlanningVisuals();
                yield break;
            }

            var finalTargets = ResolveTargets(
                plannedSkill,
                activeUnit,
                GetCasterAllies(activeUnit),
                GetCasterEnemies(activeUnit),
                plannedClickedTile,
                plannedClickedUnit
            );

            if (finalTargets.Count > 0)
            {
                yield return RunSkill(
                    activeUnit,
                    plannedSkill,
                    null,
                    plannedClickedTile,
                    plannedClickedUnit
                );
            }
        }

        busy = false;
        waitingInput = false;
        OnActionComplete();
    }

    public void CancelPlanningStep()
    {
        if (battleEnded) return;
        if (!waitingInput || busy) return;

        // 1) 타겟 고정 해제
        if (HasPlannedTarget)
        {
            ClearPlannedTarget();
            RefreshPlanningVisuals();
            return;
        }

        // 2) 스킬 선택 해제
        if (plannedSkill != null)
        {
            ClearPlannedSkill();
            RefreshPlanningVisuals();
            return;
        }

        // 3) 이동 ghost 해제
        if (plannedMoveTile.HasValue)
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
                return plannedClickedUnit != null;

            case SkillTargetMode.ClickTileAOE:
                return plannedClickedTile.HasValue;

            default:
                return true; // All* / AutoNearestSingle 은 클릭 타겟 불필요
        }
    }

    bool HasLOSForSkill(SkillData skill, Vector2Int from, Vector2Int to)
    {
        if (skill == null) return false;
        if (!skill.requiresLineOfSight) return true;

        if (!grid) grid = GridManager.I;
        if (!grid) return false;

        return grid.HasLineOfSight(from, to);
    }

    bool IsPointCastable(SkillData skill, Vector2Int from, Vector2Int to)
    {
        if (skill == null) return false;

        int d = Mathf.Abs(from.x - to.x) + Mathf.Abs(from.y - to.y);
        if (d < skill.minRange || d > skill.maxRange)
            return false;

        return HasLOSForSkill(skill, from, to);
    }
}
