using UnityEngine;
using UnityEngine.EventSystems;

public class SkillButtonHover : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    private SkillData skill;
    private SkillTooltip tooltip;

    public void Setup(SkillData newSkill, SkillTooltip newTooltip)
    {
        skill = newSkill;
        tooltip = newTooltip;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (skill == null || tooltip == null) return;
        tooltip.Show(skill);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (tooltip == null) return;
        tooltip.Hide();
    }
}