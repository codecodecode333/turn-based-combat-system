using UnityEngine;
using UnityEngine.InputSystem;

public class BattleInput : MonoBehaviour
{
    public Camera cam;
    public BattleController battle;

    void Awake()
    {
        if (!cam) cam = Camera.main;
        if (!battle) battle = FindObjectOfType<BattleController>();
    }

    void Update()
    {   
        if (Mouse.current == null || cam == null || battle == null) return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Vector2 screen = Mouse.current.position.ReadValue();
            Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));

            // 2D 레이캐스트 (타일 콜라이더 hit)
            RaycastHit2D hit = Physics2D.Raycast(world, Vector2.zero);
            if (hit.collider == null) return;

            // ✅ 1) 유닛 클릭 우선
            var unit = hit.collider.GetComponentInParent<Unit>();
            if (unit != null)
            {
                battle.OnUnitClicked(unit);
                return;
            }

            // ✅ 2) 그 다음 타일 클릭
            var tile = hit.collider.GetComponentInParent<TileView>();
            if (tile == null) return;

            battle.OnTileClicked(tile.GridPos); // BattleController에 “요청”만 던짐
        }
    }
}