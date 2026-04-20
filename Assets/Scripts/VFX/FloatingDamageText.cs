using TMPro;
using UnityEngine;

public class FloatingDamageText : MonoBehaviour
{
    [SerializeField] private TMP_Text text;
    [SerializeField] private float lifetime = 0.7f;
    [SerializeField] private float riseSpeed = 0.6f;

    private float timer;

    public void SetValue(int value)
    {
        Debug.Log($"SetValue {value}, text={(text != null)}");

        if (text != null)
            text.text = value.ToString();
    }

    private void Update()
    {
        transform.position += Vector3.up * (riseSpeed * Time.deltaTime);

        timer += Time.deltaTime;
        if (timer >= lifetime)
            Destroy(gameObject);
    }
}