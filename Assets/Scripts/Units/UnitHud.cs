using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

    [Header("Visual UI")]
    public Image hpBarFill;
    public Image[] apDots;

    public Color apOnColor = new Color(1f, 0.85f, 0.2f, 1f);
    public Color apOffColor = new Color(0.2f, 0.2f, 0.2f, 0.6f);

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

        if (!hpBarFill)
        {
            var t = transform.Find("Canvas/HpBarBg/HpBarFill");
            if (t) hpBarFill = t.GetComponent<Image>();
        }

        if (apDots == null || apDots.Length == 0)
        {
            apDots = new Image[3];

            for (int i = 0; i < apDots.Length; i++)
            {
                var t = transform.Find($"Canvas/ApRoot/AP_{i}");
                if (t) apDots[i] = t.GetComponent<Image>();
            }
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
        if (hpText)
            hpText.text = $"HP {unit.currentHP}/{unit.maxHP}";

        if (hpBarFill)
            hpBarFill.fillAmount = unit.maxHP > 0
                ? unit.currentHP / (float)unit.maxHP
                : 0f;
    }

    void RefreshAp()
    {
        if (apText)
            apText.text = $"AP {unit.currentAP}/{unit.maxAP}";

        if (apDots != null)
        {
            for (int i = 0; i < apDots.Length; i++)
            {
                if (!apDots[i]) continue;

                bool on = i < unit.currentAP;
                bool exists = i < unit.maxAP;

                apDots[i].gameObject.SetActive(exists);
                apDots[i].color = on ? apOnColor : apOffColor;
            }
        }
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