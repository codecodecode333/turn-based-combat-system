using UnityEngine;
using UnityEngine.InputSystem;

public class BattleInput : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Camera cam;
    [SerializeField] private BattleController battle;

    [Header("Input Actions (Optional)")]
    // 프로젝트에 Input Actions 에셋을 쓰는 경우 여기에 연결해도 됨
    [SerializeField] private InputActionReference clickActionRef;     // 예: <Pointer>/press
    [SerializeField] private InputActionReference pointerPosActionRef; // 예: <Pointer>/position

    [Header("Raycast")]
    [SerializeField] private LayerMask raycastMask = ~0; // 기본: 전부
    [SerializeField] private bool debugLog = false;

    private InputAction clickAction;
    private InputAction pointerPosAction;
    private Vector2Int? lastHoverTile;

    void Awake()
    {
        if (!cam) cam = Camera.main;
        if (!battle) battle = FindObjectOfType<BattleController>();

        // InputActionReference가 연결되어 있으면 사용, 아니면 코드로 생성
        clickAction = clickActionRef != null ? clickActionRef.action : null;
        pointerPosAction = pointerPosActionRef != null ? pointerPosActionRef.action : null;

        if (clickAction == null)
        {
            // 마우스/터치 공통: Pointer press
            clickAction = new InputAction("Click", InputActionType.Button, "<Pointer>/press");
        }

        if (pointerPosAction == null)
        {
            pointerPosAction = new InputAction("PointerPos", InputActionType.Value, "<Pointer>/position");
        }
    }
    void Update()
    {
        if (cam == null || battle == null) return;

        var mode = battle.InputMode;
        var skill = battle.SelectedSkill;

        bool hoverAOE =
            (battle.IsWaitingInput && !battle.IsBusy &&
            mode == BattleController.PlayerInputMode.SkillPreview &&
            skill != null &&
            skill.targetMode == SkillTargetMode.ClickTileAOE);

        if (!hoverAOE)
        {
            if (lastHoverTile.HasValue)
            {
                lastHoverTile = null;
                battle.ClearHoverAOEPreview();
            }
            return;
        }

        // 포인터 위치 -> 월드 -> RaycastAll
        Vector2 screen = pointerPosAction.ReadValue<Vector2>();
        Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));

        var hits = Physics2D.RaycastAll(world, Vector2.zero, 0f, raycastMask);

        TileView hitTile = null;
        if (hits != null)
        {
            for (int i = 0; i < hits.Length; i++)
            {
                var col = hits[i].collider;
                if (!col) continue;

                hitTile = col.GetComponentInParent<TileView>();
                if (hitTile != null) break;
            }
        }

        if (hitTile == null)
        {
            if (lastHoverTile.HasValue)
            {
                lastHoverTile = null;
                battle.ClearHoverAOEPreview();
            }
            return;
        }

        var gp = hitTile.GridPos;
        if (!lastHoverTile.HasValue || lastHoverTile.Value != gp)
        {
            lastHoverTile = gp;
            battle.OnHoverTile(gp);
        }
    }
    void OnEnable()
    {
        if (clickAction != null)
        {
            clickAction.Enable();
            clickAction.performed += OnClickPerformed;
        }

        if (pointerPosAction != null)
            pointerPosAction.Enable();
    }

    void OnDisable()
    {
        if (clickAction != null)
        {
            clickAction.performed -= OnClickPerformed;
            clickAction.Disable();
        }

        if (pointerPosAction != null)
            pointerPosAction.Disable();
    }

    private void OnClickPerformed(InputAction.CallbackContext ctx)
    {
        if (cam == null || battle == null) return;

        Vector2 screen = pointerPosAction.ReadValue<Vector2>();
        Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));

        // ✅ 핵심: RaycastAll로 “유닛/타일 둘 다” 찾는다
        var hits = Physics2D.RaycastAll(world, Vector2.zero, 0f, raycastMask);
        if (hits == null || hits.Length == 0) return;

        Unit hitUnit = null;
        TileView hitTile = null;

        // hits 순서는 보장되지 않으니, 그냥 둘 다 확보
        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i].collider;
            if (!col) continue;

            if (hitUnit == null)
                hitUnit = col.GetComponentInParent<Unit>();

            if (hitTile == null)
                hitTile = col.GetComponentInParent<TileView>();

            if (hitUnit != null && hitTile != null)
                break;
        }

        // ===== 우선순위 결정 =====
        // Move 모드: 타일 우선
        // SkillPreview + ClickTileAOE: 타일 우선
        // SkillPreview + ClickSingle: 유닛 우선
        var mode = battle.InputMode;
        var skill = battle.SelectedSkill;
        bool isAOE = (mode == BattleController.PlayerInputMode.SkillPreview &&
                      skill != null &&
                      skill.targetMode == SkillTargetMode.ClickTileAOE);

        bool unitFirst = (mode == BattleController.PlayerInputMode.SkillPreview &&
                          skill != null &&
                          skill.targetMode == SkillTargetMode.ClickSingle);

        if (debugLog)
        {
            Debug.Log($"[BattleInput] mode={mode}, skill={(skill ? skill.skillName : "null")}, " +
                      $"unit={(hitUnit ? hitUnit.name : "null")}, tile={(hitTile ? hitTile.name : "null")}, " +
                      $"AOE={isAOE}, unitFirst={unitFirst}");
        }

        if (unitFirst)
        {
            if (hitUnit != null) battle.OnUnitClicked(hitUnit);
            else if (hitTile != null) battle.OnTileClicked(hitTile.GridPos);
            return;
        }

        // 타일 우선(이동 + AOE)
        if (hitTile != null)
        {
            battle.OnTileClicked(hitTile.GridPos);
            // ✅ AOE에서는 유닛 클릭을 “동시에” 처리하면 오작동할 수 있어서 여기서 종료
            if (isAOE) return;
        }

        if (hitUnit != null)
            battle.OnUnitClicked(hitUnit);
    }
}