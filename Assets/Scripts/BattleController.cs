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
            if (tileHighlighter)
                tileHighlighter.ShowReachableMoveTiles(activeUnit);
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
            SetSkillButtonsInteractable(true);
            // ✅ 입력 대기 상태에서만 표시
            if (tileHighlighter) tileHighlighter.ShowReachableMoveTiles(activeUnit);
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

        // (필드명이 다르면 여기만 바꿔)
        int minR = skill.minRange;
        int maxR = skill.maxRange;

        // 하이라이트 초기화
        if (tileHighlighter) tileHighlighter.ClearAll();
        // 1클릭: 사거리 표시(RED)
        var tiles = BuildManhattanRangeTiles(activeUnit.GridPos, minR, maxR);
        if (tileHighlighter) tileHighlighter.ShowRangeTiles(tiles);
        ClearPreviewTargetIndicators();

        // ✅ RunSkill과 동일 로직으로 타겟 계산
        var targets = ResolveTargets(skill, activeUnit, GetCasterAllies(activeUnit), GetCasterEnemies(activeUnit));

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
        if (attacker == null || skill == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        List<Unit> targets = ResolveTargets(skill, attacker, GetCasterAllies(attacker), GetCasterEnemies(attacker));
        if (targets.Count == 0)
        {
            // 사거리 밖이면 그냥 공격 애니도 안 하고 턴 종료(원하면 애니만 하고 종료로 바꿔도 됨)
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
            {
                if (t != null && !t.IsDead)
                    ApplySkillEffects(skill, attacker, t);
            }
        }

        void OnEnd()
        {
            endDone = true;
        }

        attacker.AttackHitEvent += OnHit;
        attacker.AttackEndEvent += OnEnd;

        // Unit.cs에 PlayAttack(string) 있어야 함
        attacker.PlayAttack(skill.animationTrigger);

        float timeout = 1.5f;
        float t = 0f;

        // 1) 효과 타이밍
        if (skill.timing == SkillTiming.Immediate)
        {
            foreach (var tt in targets)
            {
                if (tt != null && !tt.IsDead)
                    ApplySkillEffects(skill, attacker, tt);
            }
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
                foreach (var tt in targets)
                {
                    if (tt != null && !tt.IsDead)
                        ApplySkillEffects(skill, attacker, tt);
                }
                hitDone = true;
            }
        }

        // 2) 턴 종료는 항상 공격 애니 종료까지 기다림
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

    // =========================
    // Target resolver
    // =========================
    List<Unit> ResolveTargets(SkillData skill, Unit caster, List<Unit> allies, List<Unit> enemies)
    {
        List<Unit> targets = new List<Unit>();

        switch (skill.targetType)
        {
            case SkillTargetType.Self:
                targets.Add(caster);
                break;

            case SkillTargetType.SingleEnemy:
            {
                Unit t = ChooseNearestInRange(caster, enemies, skill.minRange, skill.maxRange);
                if (t != null) targets.Add(t);
                break;
            }

            case SkillTargetType.SingleAlly:
            {
                Unit t = ChooseNearestInRange(caster, allies, skill.minRange, skill.maxRange);
                if (t != null) targets.Add(t);
                break;
            }

            case SkillTargetType.AllEnemies:
                foreach (var e in enemies)
                    if (e != null && !e.IsDead)
                        targets.Add(e);
                break;

            case SkillTargetType.AllAllies:
                foreach (var a in allies)
                    if (a != null && !a.IsDead)
                        targets.Add(a);
                break;
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
            yield return grid.MoveRoutine(enemy, plan.moveTile);

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
        var targets = ResolveTargets(skill, attacker, GetCasterAllies(attacker), GetCasterEnemies(attacker));

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
}
