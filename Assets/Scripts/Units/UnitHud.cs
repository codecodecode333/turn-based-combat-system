using System.Text;
using TMPro;
using UnityEngine;

public class UnitHud : MonoBehaviour
{
    [Header("Refs")]
    public Unit unit;                 // 자동 연결 가능
    public TMP_Text hpText;
    public TMP_Text turnText;
    public TMP_Text statusText;       // 추가: 상태 표시용

    [Header("Follow")]
    public Vector3 offset = new Vector3(0f, 0.6f, 0f);
    public bool faceCamera = true;

    Camera cam;

    [Header("Target UX")]
    public GameObject targetIndicator; // 아이콘(예: !, 검표시 등) 오브젝트

    void Awake()
    {
        if (!unit) unit = GetComponentInParent<Unit>();
        cam = Camera.main;

        if (!targetIndicator)
        {
            // 네 구조: UnitHud/Canvas/Target_indicator
            var t = transform.Find("Canvas/Target_indicator");
            if (t) targetIndicator = t.gameObject;
        }

        if (!statusText)
        {
            // 네 구조에서 statusText 자동 탐색
            var t = transform.Find("Canvas/StatusText");
            if (t) statusText = t.GetComponent<TMP_Text>();
        }

        // 기본은 무조건 OFF
        if (targetIndicator) targetIndicator.SetActive(false);
    }

    void LateUpdate()
    {
        if (unit)
            transform.position = unit.transform.position + offset;

        if (faceCamera && cam)
            transform.forward = cam.transform.forward;

        Refresh();
    }

    public void Refresh()
    {
        if (!unit) return;

        if (hpText)
            hpText.text = $"HP {unit.currentHP}/{unit.maxHP}";

        if (turnText && string.IsNullOrEmpty(turnText.text))
            turnText.text = $"SPD {unit.speed}";

        RefreshStatus();
    }

    void RefreshStatus()
    {
        if (!statusText)
            return;

        if (unit == null || unit.StatusEffects == null || unit.StatusEffects.Count == 0)
        {
            statusText.text = "";
            return;
        }

        var sb = new StringBuilder(64);

        for (int i = 0; i < unit.StatusEffects.Count; i++)
        {
            var s = unit.StatusEffects[i];
            if (s == null || s.IsExpired) continue;

            if (sb.Length > 0)
                sb.Append('\n');

            sb.Append(GetStatusLabel(s));

            if (s.remainingTurns > 0)
                sb.Append(" (").Append(s.remainingTurns).Append(')');
        }

        statusText.text = sb.ToString();
    }

    string GetStatusLabel(StatusEffect s)
    {
        switch (s.Id)
        {
            case StatusId.Burn:
                return $"Burn {s.power}";
            case StatusId.Poison:
                return $"Poison {s.power}";
            case StatusId.Stun:
                return "Stun";
            case StatusId.Freeze:
                return "Freeze";
            case StatusId.Slow:
                return $"Slow {s.power}";
            case StatusId.Counter:
                return "Counter";
            case StatusId.Invincible:
                return "Invincible";
            case StatusId.Shield:
                return $"Shield {s.power}";
            default:
                return s.Id.ToString();
        }
    }

    public void SetTurnInfo(string info)
    {
        if (turnText) turnText.text = info;
    }

    public void ClearTurnInfo()
    {
        if (turnText) turnText.text = "";
    }

    public void SetTargeted(bool on)
    {
        if (targetIndicator)
            targetIndicator.SetActive(on);
    }
}