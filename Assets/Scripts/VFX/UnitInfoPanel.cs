using TMPro;
using UnityEngine;

public class UnitInfoPanel : MonoBehaviour
{
    public TMP_Text nameText;
    public TMP_Text hpText;
    public TMP_Text apText;
    public TMP_Text statText;
    public TMP_Text statusText;

    public void Show(Unit unit)
    {
        if (unit == null)
        {
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);

        nameText.text = unit.name;
        hpText.text = $"HP {unit.currentHP}/{unit.maxHP}";
        apText.text = $"AP {unit.currentAP}/{unit.maxAP}";
        statText.text = $"SPD {unit.speed}";
        statusText.text = "None";
    }
    public void Hide()
    {
        gameObject.SetActive(false);
    }
}