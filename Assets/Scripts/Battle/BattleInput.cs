using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class BattleInput : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Camera cam;
    [SerializeField] private BattleController battle;

    [Header("Input Actions (Optional)")]
    // 프로젝트에 Input Actions 에셋을 쓰는 경우 여기에 연결해도 됨
    [SerializeField] private InputActionReference clickActionRef;      // 예: <Pointer>/press
    [SerializeField] private InputActionReference pointerPosActionRef; // 예: <Pointer>/position

    [Header("Raycast")]
    [SerializeField] private LayerMask raycastMask = ~0; // 기본: 전부
    [SerializeField] private bool debugLog = false;

    private InputAction clickAction;
    private InputAction pointerPosAction;

    // Hover state
    // skill hover tile:
    // - ClickTileAOE 중심 타일 hover
    // - ClickSingle / All* / AutoNearestSingle에서도 "현재 커서 아래 타일" 전달용
    private Vector2Int? lastHoverTile;

    // Move hover tile
    private Vector2Int? lastHoverMoveTile;

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

        // ===== Confirm / Cancel 입력 =====
        if (Keyboard.current != null)
        {
            if (Keyboard.current.enterKey.wasPressedThisFrame ||
                Keyboard.current.numpadEnterKey.wasPressedThisFrame)
            {
                EventSystem.current?.SetSelectedGameObject(null);
                battle.ConfirmPlannedAction();
                return;
            }

            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                ClearSkillHoverIfNeeded();
                ClearMoveHoverIfNeeded();
                battle.CancelPlanningStep();
                return;
            }
        }

        if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
        {
            ClearSkillHoverIfNeeded();
            ClearMoveHoverIfNeeded();
            battle.CancelPlanningStep();
            return;
        }

        var mode = battle.InputMode;
        var skill = battle.SelectedSkill;

        bool hoverMove =
            battle.IsWaitingInput && !battle.IsBusy &&
            mode == BattleController.PlayerInputMode.Move;

        bool hoverSkillTile =
            battle.IsWaitingInput && !battle.IsBusy &&
            mode == BattleController.PlayerInputMode.SkillPreview &&
            skill != null;

        // 어떤 hover도 안 쓰는 상태면 남은 preview 정리
        if (!hoverMove && !hoverSkillTile)
        {
            ClearSkillHoverIfNeeded();
            ClearMoveHoverIfNeeded();
            return;
        }

        // 포인터 위치 -> 월드 -> RaycastAll
        Vector2 screen = pointerPosAction.ReadValue<Vector2>();
        Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));

        var hits = Physics2D.RaycastAll(world, Vector2.zero, 0f, raycastMask);

        Unit hitUnit = null;
        TileView hitTile = null;

        if (hits != null)
        {
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
        }

        // 타일을 못 맞추면: skill/move hover 정리
        if (hitTile == null)
        {
            if (hoverSkillTile) ClearSkillHoverIfNeeded();
            if (hoverMove) ClearMoveHoverIfNeeded();
            return;
        }

        var gp = hitTile.GridPos;

        // ---- Skill hover (AOE / Single / All* / AutoNearestSingle 공통) ----
        if (hoverSkillTile)
        {
            if (!lastHoverTile.HasValue || lastHoverTile.Value != gp)
            {
                lastHoverTile = gp;
                battle.OnHoverTile(gp);
            }
        }
        else
        {
            ClearSkillHoverIfNeeded();
        }

        // ---- Move hover ----
        if (hoverMove)
        {
            if (!lastHoverMoveTile.HasValue || lastHoverMoveTile.Value != gp)
            {
                lastHoverMoveTile = gp;
                battle.OnHoverMoveTile(gp);
            }
        }
        else
        {
            ClearMoveHoverIfNeeded();
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

        // 비활성화/씬전환에서도 잔상 확실히 정리
        ClearSkillHoverIfNeeded(force: true);
        ClearMoveHoverIfNeeded(force: true);
    }

    private void ClearSkillHoverIfNeeded(bool force = false)
    {
        if (!force && battle != null && battle.HasLockedAOETarget)
            return;

        if (force || lastHoverTile.HasValue)
        {
            lastHoverTile = null;
            if (battle != null) battle.ClearHoverAOEPreview();
        }
    }

    private void ClearMoveHoverIfNeeded(bool force = false)
    {
        if (force || lastHoverMoveTile.HasValue)
        {
            lastHoverMoveTile = null;
            if (battle != null) battle.ClearHoverMovePathPreview();
        }
    }

    private void OnClickPerformed(InputAction.CallbackContext ctx)
    {
        if (cam == null || battle == null) return;

        Vector2 screen = pointerPosAction.ReadValue<Vector2>();
        Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));

        // 핵심: RaycastAll로 “유닛/타일 둘 다” 찾는다
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

        var mode = battle.InputMode;
        var skill = battle.SelectedSkill;

        bool tileFirst =
            mode == BattleController.PlayerInputMode.Move ||
            (mode == BattleController.PlayerInputMode.SkillPreview &&
             skill != null &&
             (skill.targetMode == SkillTargetMode.ClickTileAOE ||
              skill.targetMode == SkillTargetMode.ClickSingle));

        if (debugLog)
        {
            Debug.Log(
                $"[BattleInput] mode={mode}, " +
                $"skill={(skill ? skill.skillName : "null")}, " +
                $"unit={(hitUnit ? hitUnit.name : "null")}, " +
                $"tile={(hitTile ? hitTile.name : "null")}, " +
                $"tileFirst={tileFirst}"
            );
        }

        if (tileFirst)
        {
            if (hitTile != null)
                battle.OnTileClicked(hitTile.GridPos);
            else if (hitUnit != null)
                battle.OnUnitClicked(hitUnit); // 폴백
        }
        else
        {
            if (hitUnit != null)
                battle.OnUnitClicked(hitUnit);
            else if (hitTile != null)
                battle.OnTileClicked(hitTile.GridPos);
        }
    }
}