using System;
using System.Collections;
using UnityEngine;

public class Unit : MonoBehaviour
{
    [Header("Visual")]
    [SerializeField] private Transform visual;   // 자식 Visual 
    [SerializeField] private float footOffsetPixels = -18f; // 발이 중앙에서 얼마나 아래인지(픽셀)
    [SerializeField] private float pixelsPerUnit = 64f;    // 프로젝트 PPU에 맞추기
    [SerializeField] bool defaultFacesLeft = true;

    [Header("Stats")]
    public int maxHP = 30;
    public int currentHP;
    public int speed = 10;
    public int moveRange = 2;

    [Header("Skills")]
    public SkillData[] skillPoolOverride;

    [Header("AI")]
    public AIProfile aiProfile;

    public bool IsDead => currentHP <= 0;

    [Header("Animator")]
    public Animator anim;
    public string attackTrigger = "triggerAttack";
    public string hitTrigger = "triggerHit";
    public string movingBool = "isMoving";

    // (선택) 피격 피드백
    public SpriteRenderer sr;
    public float flashDuration = 0.08f;
    public float knockbackPx = 3f;
    public float knockbackDuration = 0.08f;

    private Vector3 baseVisualLocalPos;
    private Coroutine hitCo;

    // === BattleController가 구독할 이벤트 ===
    public event Action AttackHitEvent;
    public event Action AttackEndEvent;

    public event System.Action HitEndEvent;

    //실행중인 스킬
    public SkillData currentSkill;

    private readonly System.Collections.Generic.List<StatusEffect> statusEffects
    = new System.Collections.Generic.List<StatusEffect>();

    public Vector2Int GridPos { get; private set; }
    public bool isAlly;
    float baseVisualScaleX = 1f;


    void LateUpdate()
    {
        if (sr != null)
            sr.sortingOrder = -Mathf.RoundToInt(transform.position.y * 100f);
    }

    void Awake()
    {
        // 1) Visual 자동 찾기
        if (visual == null)
        {
            var t = transform.Find("Visual");
            visual = t != null ? t : transform; // 임시 fallback
        }
        baseVisualScaleX = Mathf.Abs(visual.localScale.x);
        if (baseVisualScaleX < 0.0001f) baseVisualScaleX = 1f;

        // 2) Animator/SR를 Visual 기준으로
        if (anim == null) anim = visual.GetComponentInChildren<Animator>();
        if (sr == null) sr = visual.GetComponentInChildren<SpriteRenderer>();

        currentHP = maxHP;
        if (anim != null) anim.SetBool(movingBool, false);

        baseVisualLocalPos = visual.localPosition;
        ApplyFootOffset();
        baseVisualLocalPos = visual.localPosition;
    }
    void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(transform.position, 0.03f);
    }
    void ApplyFootOffset()
    {
        float oy = -footOffsetPixels / pixelsPerUnit;
        visual.localPosition = baseVisualLocalPos + new Vector3(0f, oy, 0f);
    }

    public void PlayAttack(string triggerName)
    {
        if (!anim) return;

        // hit 중 공격 씹힘 방지
        anim.ResetTrigger(hitTrigger);

        // 같은 트리거 연속 호출 방지
        anim.ResetTrigger(triggerName);
        anim.SetTrigger(triggerName);
    }

    public void PlayHit()
    {
        if (!anim) return;
        anim.ResetTrigger(attackTrigger);
        anim.ResetTrigger(hitTrigger);
        anim.SetTrigger(hitTrigger);
    }
    public void TakeDamage(int damage)
    {
        TakeDamage(damage, transform.position);
    }

    // 공격자 위치를 받으면 넉백 방향 계산 가능
    public void TakeDamage(int dmg, Vector3 attackerWorldPos)
    {
        currentHP -= dmg;
        if (currentHP < 0) currentHP = 0;

        PlayHit();

        // ❌ StopAllCoroutines() 지우기
        if (hitCo != null) StopCoroutine(hitCo);
        hitCo = StartCoroutine(HitFeedback(attackerWorldPos));

        if (IsDead)
        {
            if (anim) anim.enabled = false;
            gameObject.SetActive(false);
        }
    }



    IEnumerator HitFeedback(Vector3 attackerWorldPos)
    {
        // flash
        if (sr != null)
        {
            var orig = sr.color;
            sr.color = Color.white;
            yield return new WaitForSeconds(flashDuration);
            sr.color = orig;
        }

        // knockback 방향은 Root(world) 기준 OK
        Vector3 dir = (transform.position - attackerWorldPos);
        dir.z = 0f;
        dir = dir.sqrMagnitude < 0.0001f ? Vector3.right : dir.normalized;

        // ✅ Visual만 밀기
        Vector3 target = baseVisualLocalPos + dir * (knockbackPx / 100f);

        float t = 0f;
        while (t < knockbackDuration)
        {
            t += Time.deltaTime;
            float a = t / knockbackDuration;
            visual.localPosition = Vector3.Lerp(baseVisualLocalPos, target, a);
            yield return null;
        }
        visual.localPosition = baseVisualLocalPos;
        hitCo = null;
    }


    public void Heal(int amount)
    {
        if (IsDead) return;

        currentHP += amount;
        if (currentHP > maxHP)
            currentHP = maxHP;

        // TODO: 힐 이펙트, 힐 숫자 표시
    }


    // ==========================
    // Animation Event에서 호출될 함수 (이름 정확히)
    // ==========================
    public void OnAttackHit()
    {
        AttackHitEvent?.Invoke();
    }

    public void OnAttackEnd()
    {
        if (visual) visual.localPosition = baseVisualLocalPos;
        AttackEndEvent?.Invoke();
    }

    public void OnHitEnd()
    {
        if (visual) visual.localPosition = baseVisualLocalPos;
        HitEndEvent?.Invoke();
    }

    public void OnTurnStart()
    {
        if (IsDead) return;

        // 뒤에서 앞으로 순회(제거 안전)
        for (int i = statusEffects.Count - 1; i >= 0; i--)
        {
            var s = statusEffects[i];
            if (s == null) { statusEffects.RemoveAt(i); continue; }

            s.OnTurnStart(this);

            if (s.IsExpired)
                statusEffects.RemoveAt(i);
        }
    }

    public void OnTurnEnd()
    {
        if (IsDead) return;

        for (int i = statusEffects.Count - 1; i >= 0; i--)
        {
            var s = statusEffects[i];
            if (s == null) { statusEffects.RemoveAt(i); continue; }

            s.OnTurnEnd(this);

            if (s.IsExpired)
                statusEffects.RemoveAt(i);
        }
    }
    public void AddOrRefreshStatus(StatusEffect incoming)
    {
        if (incoming == null || IsDead) return;

        // 같은 Id면 갱신(여기서는 Burn에 최적화된 간단 룰)
        for (int i = 0; i < statusEffects.Count; i++)
        {
            if (statusEffects[i] != null && statusEffects[i].Id == incoming.Id)
            {
                // Burn 갱신 규칙: 턴은 더 큰 값, 데미지는 더 큰 값
                if (statusEffects[i] is BurnStatus cur && incoming is BurnStatus inc)
                {
                    cur.remainingTurns = System.Math.Max(cur.remainingTurns, inc.remainingTurns);
                    cur.damagePerTurn = System.Math.Max(cur.damagePerTurn, inc.damagePerTurn);
                }
                else
                {
                    // 다른 상태 일반 갱신(턴만)
                    statusEffects[i].remainingTurns = System.Math.Max(statusEffects[i].remainingTurns, incoming.remainingTurns);
                }
                return;
            }
        }

        statusEffects.Add(incoming);
    }

    public void SetGridPosAndWarp(Vector2Int p, GridManager grid)
    {
        GridPos = p;
        transform.position = grid.GridToWorld(p);
    }
    public void SetGridPosOnly(Vector2Int p)
    {
        GridPos = p;
    }
    public void SetMoving(bool v)
    {
        if (anim != null) anim.SetBool("isMoving", v);
    }

    public void SetFacingX(int dir)
    {
        if (visual == null) return;
        dir = dir < 0 ? -1 : 1;

        // ✅ 기본(+X)이 "왼쪽"을 보는 리소스라면,
        // 오른쪽을 보려면 X를 음수로 뒤집어야 함.
        int flip = (dir == 1) ? -1 : 1;

        var s = visual.localScale;
        s.x = baseVisualScaleX * flip;
        visual.localScale = s;
    }
}
