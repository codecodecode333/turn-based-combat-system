using System;
using System.Collections;
using System.Collections.Generic;
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

    [Header("Movement")]
    [Min(0)] public int maxClimbDelta = 1;

    [Header("AP")]
    public int maxAP = 2;
    public int regenAP = 2;
    public int currentAP = 0;

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

    private UnitFxPlayer fxPlayer;

    // === BattleController가 구독할 이벤트 ===
    public event Action AttackHitEvent;
    public event Action AttackEndEvent;

    public event System.Action HitEndEvent;

    //실행중인 스킬
    public SkillData currentSkill;

    private readonly System.Collections.Generic.List<StatusEffect> statusEffects
    = new System.Collections.Generic.List<StatusEffect>();
    public IReadOnlyList<StatusEffect> StatusEffects => statusEffects;

    private int statusRevision = 0;
    public int StatusRevision => statusRevision;

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

        //AP
        currentAP = regenAP;

        if (fxPlayer == null)
            fxPlayer = GetComponent<UnitFxPlayer>();
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
        if (IsDead) return;
        if (dmg <= 0) return;

        // 1) Invincible: 모든 피해 무효
        if (HasInvincible())
        {
            // 필요하면 여기서 무효화 이펙트/로그 추가 가능
            return;
        }

        // 2) Shield: 먼저 피해 흡수
        int finalDamage = TryAbsorbWithShield(dmg);

        // Shield가 전부 막았으면 피격 애니메이션도 생략 가능
        if (finalDamage <= 0)
            return;

        // 3) 남은 피해만 HP에 반영
        currentHP -= finalDamage;
        if (currentHP < 0) currentHP = 0;

        if (fxPlayer != null)
        {
            fxPlayer.PlayHitFx();
            fxPlayer.PlayDamageText(finalDamage);
        }

        PlayHit();

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

    public bool HasInvincible()
    {
        for (int i = 0; i < statusEffects.Count; i++)
        {
            var s = statusEffects[i];
            if (s == null || s.IsExpired) continue;
            if (s.IsInvincible) return true;
        }
        return false;
    }

    public ShieldStatus GetShieldStatus()
    {
        for (int i = 0; i < statusEffects.Count; i++)
        {
            if (statusEffects[i] is ShieldStatus shield && !shield.IsExpired)
                return shield;
        }
        return null;
    }

    public int TryAbsorbWithShield(int incomingDamage)
    {
        if (incomingDamage <= 0) return 0;

        var shield = GetShieldStatus();
        if (shield == null) return incomingDamage;

        int remain = shield.Absorb(incomingDamage);

        // shield가 0 이하가 되면 즉시 제거
        if (shield.ShieldAmount <= 0)
            statusEffects.Remove(shield);

        return remain;
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

        RestoreAPOnTurnStart();

        bool changed = false;

        for (int i = statusEffects.Count - 1; i >= 0; i--)
        {
            var s = statusEffects[i];
            if (s == null)
            {
                statusEffects.RemoveAt(i);
                changed = true;
                continue;
            }

            s.OnTurnStart(this);

            if (s.IsExpired)
            {
                s.OnRemove(this);
                OnStatusRemovedFx(s);
                statusEffects.RemoveAt(i);
                changed = true;
            }
        }

        if (changed)
            statusRevision++;
    }

    public void OnTurnEnd()
    {
        if (IsDead) return;

        bool changed = false;

        for (int i = statusEffects.Count - 1; i >= 0; i--)
        {
            var s = statusEffects[i];
            if (s == null)
            {
                statusEffects.RemoveAt(i);
                changed = true;
                continue;
            }

            s.OnTurnEnd(this);

            if (s.IsExpired)
            {
                s.OnRemove(this);
                OnStatusRemovedFx(s);
                statusEffects.RemoveAt(i);
                changed = true;
            }
        }

        if (changed)
            statusRevision++;
    }
    public void AddOrRefreshStatus(StatusEffect incoming)
    {
        if (incoming == null) return;

        // 이미 있는지 확인
        var existing = statusEffects.Find(s => s.Id == incoming.Id);

        if (existing != null)
        {
            // 갱신(refresh) - FX 재생하지 않음
            existing.MergeFrom(incoming);
            statusRevision++;
            return;
        }

        // 새로 추가
        statusEffects.Add(incoming);
        incoming.OnApply(this);

        // 👉 Apply FX (신규일 때만)
        if (fxPlayer != null)
        {
            switch (incoming.Id)
            {
                case StatusId.Burn:
                    fxPlayer.PlayBurnApplyFx();
                    break;

                case StatusId.Stun:
                    fxPlayer.ShowStunLoopFx();
                    break;
            }
        }

        statusRevision++;
    }

    public T GetStatus<T>() where T : StatusEffect
    {
        for (int i = 0; i < statusEffects.Count; i++)
        {
            if (statusEffects[i] is T t && !t.IsExpired)
                return t;
        }
        return null;
    }

    public StatusEffect GetStatus(StatusId id)
    {
        for (int i = 0; i < statusEffects.Count; i++)
        {
            var s = statusEffects[i];
            if (s != null && s.Id == id && !s.IsExpired)
                return s;
        }
        return null;
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

    public void RestoreAPOnTurnStart()
    {
        currentAP = Mathf.Min(maxAP, currentAP + regenAP);
    }

    public bool CanPayAP(int amount)
    {
        return currentAP >= amount;
    }

    public bool SpendAP(int amount)
    {
        if (amount < 0) amount = 0;
        if (currentAP < amount) return false;
        currentAP -= amount;
        return true;
    }

    public bool HasStatus(StatusId id)
    {
        for (int i = 0; i < statusEffects.Count; i++)
        {
            var s = statusEffects[i];
            if (s != null && s.Id == id && !s.IsExpired)
                return true;
        }
        return false;
    }

    public bool CanAct()
    {
        if (IsDead) return false;

        for (int i = 0; i < statusEffects.Count; i++)
        {
            var s = statusEffects[i];
            if (s != null && !s.IsExpired && s.BlocksAction)
                return false;
        }

        return true;
    }

    public bool CanMove()
    {
        if (IsDead) return false;

        for (int i = 0; i < statusEffects.Count; i++)
        {
            var s = statusEffects[i];
            if (s != null && !s.IsExpired && s.BlocksMove)
                return false;
        }

        return true;
    }

    public int GetEffectiveMoveRange()
    {
        int value = moveRange;

        for (int i = 0; i < statusEffects.Count; i++)
        {
            var s = statusEffects[i];
            if (s != null && !s.IsExpired)
                value += s.MoveRangeDelta;
        }

        if (!CanMove())
            return 0;

        return Mathf.Max(0, value);
    }

    public bool HasCounterReady()
    {
        for (int i = 0; i < statusEffects.Count; i++)
        {
            var s = statusEffects[i];
            if (s == null || s.IsExpired) continue;
            if (s.HasCounter) return true;
        }
        return false;
    }

    public void RemoveStatus(StatusId id)
    {
        for (int i = statusEffects.Count - 1; i >= 0; i--)
        {
            var s = statusEffects[i];
            if (s == null)
            {
                statusEffects.RemoveAt(i);
                continue;
            }

            if (s.Id == id)
            {
                s.OnRemove(this);
                statusEffects.RemoveAt(i);
            }
        }
    }

    void OnStatusRemovedFx(StatusEffect status)
    {
        if (fxPlayer == null || status == null)
            return;

        switch (status.Id)
        {
            case StatusId.Stun:
                fxPlayer.HideStunLoopFx();
                break;
        }
    }
}
