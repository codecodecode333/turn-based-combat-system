using System.Collections;
using TMPro;
using UnityEngine;

public class TurnPopup : MonoBehaviour
{
    public TMP_Text popupText;
    public CanvasGroup canvasGroup;

    public float fadeInTime = 0.12f;
    public float holdTime = 0.55f;
    public float fadeOutTime = 0.25f;
    public float punchScale = 1.12f;

    Coroutine co;
    Vector3 baseScale;

    void Awake()
    {
        baseScale = transform.localScale;

        if (!canvasGroup)
            canvasGroup = GetComponent<CanvasGroup>();

    }

    public void Show(string text)
    {
        if (co != null)
            StopCoroutine(co);

        co = StartCoroutine(CoShow(text));
    }

    IEnumerator CoShow(string text)
    {
        if (popupText)
            popupText.text = text;

        float t = 0f;
        transform.localScale = baseScale * punchScale;

        while (t < fadeInTime)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / fadeInTime);

            if (canvasGroup)
                canvasGroup.alpha = a;

            transform.localScale = Vector3.Lerp(baseScale * punchScale, baseScale, a);
            yield return null;
        }

        if (canvasGroup)
            canvasGroup.alpha = 1f;

        transform.localScale = baseScale;

        yield return new WaitForSeconds(holdTime);

        t = 0f;
        while (t < fadeOutTime)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / fadeOutTime);

            if (canvasGroup)
                canvasGroup.alpha = 1f - a;

            yield return null;
        }

        if (canvasGroup)
            canvasGroup.alpha = 0f;
        co = null;
    }
}