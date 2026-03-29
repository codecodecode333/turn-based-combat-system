using System.Collections;
using TMPro;
using UnityEngine;

public class UnitHud : MonoBehaviour
{
    [Header("Refs")]
    public Unit unit;                 // 자동 연결 가능
    public TMP_Text hpText;
    public TMP_Text apText;

    [Header("Follow")]
    public Vector3 offset = new Vector3(0f, 1.35f, 0f);
    public bool faceCamera = true;

    [Header("Target UX")]
    public GameObject targetIndicator; // 아이콘(예: !, 검표시 등)

    [Header("AP Feedback")]
    public Color normalApColor = Color.white;
    public Color lowApColor = new Color(1f, 0.4f, 0.4f, 1f);
    public float apFlashDuration = 0.18f;
    public float apPunchScale = 1.15f;

    Coroutine apFlashCo;
    Vector3 apTextBaseScale = Vector3.one;

    Camera cam;

    void Awake()
    {
        if (!unit) unit = GetComponentInParent<Unit>();
        cam = Camera.main;

        if (!hpText)
        {
            var t = transform.Find("Canvas/HPText");
            if (t) hpText = t.GetComponent<TMP_Text>();
        }

        if (!apText)
        {
            var t = transform.Find("Canvas/APText");
            if (t) apText = t.GetComponent<TMP_Text>();
        }

        if (apText != null)
            apTextBaseScale = apText.transform.localScale;

        if (!targetIndicator)
        {
            var t = transform.Find("Canvas/Target_indicator");
            if (t) targetIndicator = t.gameObject;
        }

        if (targetIndicator)
            targetIndicator.SetActive(false);

        Refresh();
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

        RefreshHp();
        RefreshAp();
    }

    void RefreshHp()
    {
        if (!hpText) return;

        hpText.text = $"HP {unit.currentHP}/{unit.maxHP}";
    }

    void RefreshAp()
    {
        if (!apText) return;

        apText.text = $"AP {unit.currentAP}/{unit.maxAP}";

        if (unit.currentAP <= 0)
            apText.color = new Color(1f, 0.4f, 0.4f);
        else if (unit.currentAP < unit.maxAP)
            apText.color = new Color(1f, 0.85f, 0.3f);
        else
            apText.color = Color.white;
    }

    public void SetTargeted(bool on)
    {
        if (targetIndicator != null)
            targetIndicator.SetActive(on);
    }

    public void PulseAPInsufficient()
    {
        if (apText == null) return;

        if (apFlashCo != null)
            StopCoroutine(apFlashCo);

        apFlashCo = StartCoroutine(CoPulseAPInsufficient());
    }

    IEnumerator CoPulseAPInsufficient()
    {
        if (apText == null) yield break;

        apText.color = lowApColor;
        apText.transform.localScale = apTextBaseScale * apPunchScale;

        yield return new WaitForSeconds(apFlashDuration);

        apText.color = normalApColor;
        apText.transform.localScale = apTextBaseScale;
        apFlashCo = null;
    }
}