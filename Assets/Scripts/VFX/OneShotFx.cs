using UnityEngine;

public class OneShotFx : MonoBehaviour
{
    [SerializeField] private float autoDestroyDelay = 0.6f;

    private void OnEnable()
    {
        CancelInvoke();
        Invoke(nameof(DestroySelf), autoDestroyDelay);
    }

    private void DestroySelf()
    {
        Destroy(gameObject);
    }
}