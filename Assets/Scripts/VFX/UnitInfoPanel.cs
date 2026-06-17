using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UnitInfoPanel : MonoBehaviour
{
    public TMP_Text nameText;
    public Image portraitImage;
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

        portraitImage.sprite = unit.portrait;
        portraitImage.enabled = unit.portrait != null;
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