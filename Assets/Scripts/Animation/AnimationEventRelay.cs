using UnityEngine;

public class AnimationEventRelay : MonoBehaviour
{
    Unit unit;

    void Awake()
    {
        unit = GetComponentInParent<Unit>();
    }

    public void OnAttackEnd()
    {
        if (unit != null)
            unit.OnAttackEnd();
    }

    public void OnAttackHit()
    {
        if (unit != null)
            unit.OnAttackHit();
    }

    public void OnHitEnd()
    {
        if (unit != null)
            unit.OnHitEnd();
    }
}