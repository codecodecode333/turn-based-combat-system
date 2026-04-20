using UnityEngine;

public class UnitFxPlayer : MonoBehaviour
{
    [Header("Sockets")]
    [SerializeField] private Transform fxBodySocket;
    [SerializeField] private Transform fxHeadSocket;

    [Header("One-shot Prefabs")]
    [SerializeField] private GameObject burnBodyPrefab;
    [SerializeField] private GameObject burnHeadPrefab;
    [SerializeField] private GameObject burnTickBodyPrefab;
    [SerializeField] private GameObject hitPrefab;

    [Header("Loop Prefabs")]
    [SerializeField] private GameObject stunLoopPrefab;

    [Header("Unit Size")]
    [SerializeField] private bool useHeadFx = true;

    [SerializeField] private GameObject damageTextPrefab;
    [SerializeField] private Transform damageTextSocket;

    GameObject stunLoopInstance;

    private void Awake()
    {
        if (fxBodySocket == null)
        {
            var t = transform.Find("FXRoot/FX_BodySocket");
            if (t) fxBodySocket = t;
        }

        if (fxHeadSocket == null)
        {
            var t = transform.Find("FXRoot/FX_HeadSocket");
            if (t) fxHeadSocket = t;
        }

        if (damageTextSocket == null)
        {
            var t = transform.Find("FXRoot/FX_HeadSocket");
            if (t) damageTextSocket = t;
        }
    }

    public void PlayBurnApplyFx()
    {
        SpawnOneShotAtSocket(burnBodyPrefab, fxBodySocket);

        if (useHeadFx)
            SpawnOneShotAtSocket(burnHeadPrefab, fxHeadSocket);
    }

    public void PlayBurnTickFx()
    {
        var prefab = burnTickBodyPrefab != null ? burnTickBodyPrefab : burnBodyPrefab;
        SpawnOneShotAtSocket(prefab, fxBodySocket);
    }

    public void PlayHitFx()
    {
        SpawnOneShotAtSocket(hitPrefab, fxBodySocket);
    }

    public void PlayDamageText(int damage)
    {
        Debug.Log($"PlayDamageText damage={damage}, prefab={(damageTextPrefab ? damageTextPrefab.name : "null")}, socket={(damageTextSocket ? damageTextSocket.name : "null")}");

        if (damageTextPrefab == null || damageTextSocket == null)
            return;

        var go = Instantiate(damageTextPrefab, damageTextSocket.position, Quaternion.identity);
        var floating = go.GetComponent<FloatingDamageText>();

        Debug.Log($"DamageText spawned: {go.name}, floating={(floating != null)}");

        if (floating != null)
            floating.SetValue(damage);
    }

    public void ShowStunLoopFx()
    {
        if (stunLoopInstance != null)
            return;

        Transform socket = useHeadFx && fxHeadSocket != null ? fxHeadSocket : fxBodySocket;
        if (socket == null || stunLoopPrefab == null)
            return;

        stunLoopInstance = Instantiate(stunLoopPrefab, socket);
        stunLoopInstance.transform.localPosition = Vector3.zero;
        stunLoopInstance.transform.localRotation = Quaternion.identity;
        stunLoopInstance.transform.localScale = Vector3.one;
    }

    public void HideStunLoopFx()
    {
        if (stunLoopInstance == null)
            return;

        Destroy(stunLoopInstance);
        stunLoopInstance = null;
    }

    void SpawnOneShotAtSocket(GameObject prefab, Transform socket)
    {
        if (prefab == null || socket == null) return;

        var fx = Instantiate(prefab, socket);
        fx.transform.localPosition = Vector3.zero;
        fx.transform.localRotation = Quaternion.identity;
        fx.transform.localScale = Vector3.one;
    }
}