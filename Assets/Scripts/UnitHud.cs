using TMPro;
using UnityEngine;

public class UnitHud : MonoBehaviour
{
    [Header("Refs")]
    public Unit unit;                 // 자동 연결 가능
    public TMP_Text hpText;
    public TMP_Text turnText;

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

        // 기본은 무조건 OFF (항상 떠있는 버그 방지)
        if (targetIndicator) targetIndicator.SetActive(false);
    }

    void LateUpdate()
    {
        // 유닛 머리 위로 위치 고정
        if (unit)
            transform.position = unit.transform.position + offset;

        // 카메라 바라보기(월드 스페이스 텍스트가 눕는 것 방지)
        if (faceCamera && cam)
            transform.forward = cam.transform.forward;

        Refresh();
    }

    public void Refresh()
    {
        if (!unit) return;

        if (hpText)
            hpText.text = $"HP {unit.currentHP}/{unit.maxHP}";

        // 기본: 턴 텍스트는 BattleController가 SetTurnInfo로 넣어줄 예정
        // 없으면 SPD만 표시
        if (turnText && string.IsNullOrEmpty(turnText.text))
            turnText.text = $"SPD {unit.speed}";
    }

    public void SetTurnInfo(string info)
    {
        if (turnText) turnText.text = info;
    }

    public void SetTargeted(bool on)
    {
        if (targetIndicator)
            targetIndicator.SetActive(on);
    }
}
