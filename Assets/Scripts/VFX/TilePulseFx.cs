using UnityEngine;

public class TilePulseFx : MonoBehaviour
{
    public float speed = 4f;
    public float minAlpha = 0.35f;
    public float maxAlpha = 0.75f;

    SpriteRenderer[] renderers;
    Color[] baseColors;

    void Awake()
    {
        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        baseColors = new Color[renderers.Length];

        for (int i = 0; i < renderers.Length; i++)
            baseColors[i] = renderers[i].color;
    }

    void Update()
    {
        float t = (Mathf.Sin(Time.time * speed) + 1f) * 0.5f;
        float alpha = Mathf.Lerp(minAlpha, maxAlpha, t);

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;

            Color c = baseColors[i];
            c.a = alpha;
            renderers[i].color = c;
        }
    }

    void OnDisable()
    {
        if (renderers == null || baseColors == null) return;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            renderers[i].color = baseColors[i];
        }
    }
}