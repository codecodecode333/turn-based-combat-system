using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SkillTooltip : MonoBehaviour
{
    public Image iconImage;
    public TMP_Text nameText;
    public TMP_Text costText;
    public TMP_Text descText;

    public void Show(SkillData skill)
    {
        if (skill == null) return;

        gameObject.SetActive(true);

        if (iconImage != null)
        {
            iconImage.sprite = skill.icon;
            iconImage.enabled = skill.icon != null;
        }

        if (nameText != null)
            nameText.text = skill.skillName;

        if (costText != null)
            costText.text = $"AP {skill.costAP}";

        if (descText != null)
            descText.text = skill.description;
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }
}