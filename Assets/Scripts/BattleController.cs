using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class BattleController : MonoBehaviour
{
    [Header("Teams")]
    public List<Unit> allies = new List<Unit>();
    public List<Unit> enemies = new List<Unit>();

    [Header("Skills")]
    public SkillData[] playerSkills;   // 플레이어(아군) 공용 슬롯 0~2
    public SkillData[] enemySkills;    // 적 공용(지금 단계)

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
        }
        else
        {
            // 적 AI 턴
            SetSkillButtonsInteractable(false);
            busy = true;

            SkillData skill = ChooseEnemySkill();
            StartCoroutine(RunSkill(activeUnit, skill, OnActionComplete));
        }
    }

    void OnActionComplete()
    {
        // 이번 행동자 처리 완료 → 다음 유닛
        busy = false;
        waitingInput = false;

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

        waitingInput = false;
        busy = true;
        SetSkillButtonsInteractable(false);

        // 플레이어 행동 후 -> OnActionComplete로 다음 유닛
        StartCoroutine(RunSkill(activeUnit, skill, OnActionComplete));
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
}
