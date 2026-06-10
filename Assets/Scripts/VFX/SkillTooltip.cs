using TMPro;
using UnityEngine;

public class SkillTooltip : MonoBehaviour
{
    public TMP_Text nameText;
    public TMP_Text costText;
    public TMP_Text descText;

    public void Show(SkillData skill)
    {
        if (skill == null) return;

        gameObject.SetActive(true);

        nameText.text = skill.skillName;
        costText.text = $"AP {skill.costAP}";
        descText.text = skill.description;
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }
}