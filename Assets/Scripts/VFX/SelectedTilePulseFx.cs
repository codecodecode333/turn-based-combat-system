using UnityEngine;

public class SelectedTilePulseFx : MonoBehaviour
{
    [Header("Pulse")]
    public float speed = 4.5f;
    public float minAlpha = 0.8f;
    public float maxAlpha = 1f;
    public float scaleAmount = 0.05f;

    SpriteRenderer[] renderers;
    Vector3 baseScale;

    void Awake()
    {
        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        baseScale = transform.localScale;
    }

    void OnEnable()
    {
        baseScale = transform.localScale;
    }

    void Update()
    {
        float t = (Mathf.Sin(Time.time * speed) + 1f) * 0.5f;

        float alpha = Mathf.Lerp(minAlpha, maxAlpha, t);
        float scale = 1f + Mathf.Lerp(0f, scaleAmount, t);

        transform.localScale = baseScale * scale;

        for (int i = 0; i < renderers.Length; i++)
        {
            var sr = renderers[i];
            if (sr == null) continue;

            var c = sr.color;
            c.a = alpha;
            sr.color = c;
        }
    }
}