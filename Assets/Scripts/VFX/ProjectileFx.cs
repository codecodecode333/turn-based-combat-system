using System.Collections;
using UnityEngine;

public class ProjectileFx : MonoBehaviour
{
    public float speed = 8f;

    public IEnumerator Play(Vector3 from, Vector3 to, GameObject impactFxPrefab)
    {
        transform.position = from;

        Vector3 dir = to - from;
        dir.z = 0f;

        if (dir.sqrMagnitude > 0.001f)
        {
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        while (Vector3.Distance(transform.position, to) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                to,
                speed * Time.deltaTime
            );

            yield return null;
        }

        transform.position = to;

        if (impactFxPrefab != null)
            Instantiate(impactFxPrefab, to, Quaternion.identity);

        Destroy(gameObject);
    }
}