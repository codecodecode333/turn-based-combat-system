using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UnitInfoPanel : MonoBehaviour
{
    [Header("Texts")]
    public TMP_Text nameText;
    public TMP_Text hpText;
    public TMP_Text apText;
    public TMP_Text statText;
    public TMP_Text statusText;

    [Header("Portrait")]
    public Image portraitImage;

    [Header("HP Bar")]
    public RectTransform hpFill;
    public Image hpFillImage;
    public Color allyHpColor = new Color32(70, 140, 90, 255);
    public Color enemyHpColor = new Color32(140, 70, 70, 255);

    [Header("AP Pips")]
    public Image[] apPips;
    public Color apOnColor = new Color32(255, 215, 60, 255);
    public Color apOffColor = new Color32(75, 65, 75, 255);

    Unit currentUnit;
    float hpFillMaxWidth;

    void Update()
    {
        if (currentUnit == null || currentUnit.IsDead)
            return;

        RefreshDynamicInfo(currentUnit);
    }

    public void Show(Unit unit)
    {
        if (unit == null)
        {
            Hide();
            return;
        }

        currentUnit = unit;
        gameObject.SetActive(true);

        if (portraitImage != null)
        {
            portraitImage.sprite = unit.portrait;
            portraitImage.enabled = unit.portrait != null;
        }

        if (nameText != null)
            nameText.text = unit.name;

        statText.text =
            $"SPD     {unit.speed}\n" +
            $"MOVE   {unit.GetEffectiveMoveRange()}";

        RefreshDynamicInfo(unit);
    }

    void RefreshDynamicInfo(Unit unit)
    {
        RefreshHp(unit);
        RefreshAp(unit);

        if (statusText != null)
            statusText.text = BuildStatusText(unit);
    }

    void RefreshHp(Unit unit)
    {
        if (hpText != null)
            hpText.text = $"HP {unit.currentHP}/{unit.maxHP}";

        float hp01 = unit.maxHP <= 0 ? 0f : unit.currentHP / (float)unit.maxHP;
        hp01 = Mathf.Clamp01(hp01);

        if (hpFill != null)
        {
            hpFillImage.type = Image.Type.Filled;
            hpFillImage.fillMethod = Image.FillMethod.Horizontal;
            hpFillImage.fillOrigin = 0; // Left
            hpFillImage.fillAmount = hp01;
            hpFillImage.color = unit.isAlly ? allyHpColor : enemyHpColor;
        }

        if (hpFillImage != null)
            hpFillImage.color = unit.isAlly ? allyHpColor : enemyHpColor;
    }

    void RefreshAp(Unit unit)
    {
        if (apText != null)
            apText.text = $"AP {unit.currentAP}/{unit.maxAP}";

        if (apPips == null) return;

        for (int i = 0; i < apPips.Length; i++)
        {
            if (apPips[i] == null) continue;

            bool exists = i < unit.maxAP;
            apPips[i].gameObject.SetActive(exists);

            if (exists)
                apPips[i].color = i < unit.currentAP ? apOnColor : apOffColor;
        }
    }

    string BuildStatusText(Unit unit)
    {
        if (unit == null || unit.StatusEffects == null || unit.StatusEffects.Count == 0)
            return "None";

        string result = "";

        foreach (var status in unit.StatusEffects)
        {
            if (status == null || status.IsExpired)
                continue;

            result += $"{status.Id} ({status.remainingTurns})\n";
        }

        return string.IsNullOrEmpty(result) ? "None" : result.TrimEnd();
    }

    public void Hide()
    {
        currentUnit = null;
        gameObject.SetActive(false);
    }
}