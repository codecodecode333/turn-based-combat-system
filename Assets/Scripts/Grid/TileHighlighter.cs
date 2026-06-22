using System.Collections.Generic;
using UnityEngine;

public class TileHighlighter : MonoBehaviour
{
    public enum BaseOverlayType
    {
        None,
        Move,
        SkillRange,     // 기본 스킬 사거리
        PreviewArea     // hover 중 AOE 실제 영향 범위
    }

    [System.Flags]
    public enum TileFxFlags
    {
        None = 0,
        Selected = 1 << 0,
        Actionable = 1 << 1,
        TargetHit = 1 << 2,
        FriendlyFire = 1 << 3,
        Hazard = 1 << 4
    }

    [System.Serializable]
    public class OverlayCellView
    {
        public GameObject root;

        public GameObject baseMove;
        public GameObject baseSkillRange;
        public GameObject basePreviewArea;

        public GameObject selectedFx;
        public GameObject actionableFx;
        public GameObject targetFx;
        public GameObject warningFx;
        public GameObject hazardFx;

        public void SetRootActive(bool active)
        {
            if (root != null) root.SetActive(active);
        }

        public void HideAllBase()
        {
            if (baseMove != null) baseMove.SetActive(false);
            if (baseSkillRange != null) baseSkillRange.SetActive(false);
            if (basePreviewArea != null) basePreviewArea.SetActive(false);
        }

        public void HideAllFx()
        {
            if (selectedFx != null) selectedFx.SetActive(false);
            if (actionableFx != null) actionableFx.SetActive(false);
            if (targetFx != null) targetFx.SetActive(false);
            if (warningFx != null) warningFx.SetActive(false);
            if (hazardFx != null) hazardFx.SetActive(false);
        }
    }

    [Header("Refs")]
    public GridManager grid;

    [Header("Base Overlay Prefabs")]
    public GameObject moveTilePrefab;          // 파랑
    public GameObject skillRangeTilePrefab;    // 주황 - 기본 스킬 사거리
    public GameObject previewAreaTilePrefab;   // 주황 - hover AOE 범위

    [Header("FX Prefabs")]
    public GameObject selectedFxPrefab;
    public GameObject actionableFxPrefab;
    public GameObject targetFxPrefab;
    public GameObject warningFxPrefab;         // friendly fire
    public GameObject hazardFxPrefab;

    [Header("Path / Ghost")]
    public GameObject ghostPrefab;
    public GameObject pathArrowPrefab;

    [Header("Placement")]
    public Vector3 overlayOffset = new Vector3(0f, 0f, -0.1f);
    public Vector3 fxOffset = new Vector3(0f, 0f, -0.2f);
    public Vector3 arrowOffset = new Vector3(0f, 0f, -0.25f);
    public Vector3 ghostOffset = new Vector3(0f, 0.05f, -0.3f);

    [Header("Debug")]
    public bool debugLog = false;

    // =========================
    // Base State
    // =========================
    readonly HashSet<Vector2Int> moveTiles = new HashSet<Vector2Int>();
    readonly HashSet<Vector2Int> skillRangeTiles = new HashSet<Vector2Int>();
    readonly HashSet<Vector2Int> previewAreaTiles = new HashSet<Vector2Int>();

    // =========================
    // FX State
    // =========================
    readonly HashSet<Vector2Int> selectedFxTiles = new HashSet<Vector2Int>();
    readonly HashSet<Vector2Int> actionableFxTiles = new HashSet<Vector2Int>();
    readonly HashSet<Vector2Int> targetFxTiles = new HashSet<Vector2Int>();
    readonly HashSet<Vector2Int> warningFxTiles = new HashSet<Vector2Int>();
    readonly HashSet<Vector2Int> hazardFxTiles = new HashSet<Vector2Int>();

    readonly Dictionary<Vector2Int, OverlayCellView> cells = new Dictionary<Vector2Int, OverlayCellView>();

    // =========================
    // Path / Ghost State
    // =========================
    readonly List<GameObject> hoverPathArrows = new List<GameObject>();
    readonly List<GameObject> plannedPathArrows = new List<GameObject>();
    readonly List<GameObject> hoverHazardMarkers = new List<GameObject>();
    readonly List<GameObject> plannedHazardMarkers = new List<GameObject>();

    GameObject ghostInstance;

    void Awake()
    {
        if (!grid) grid = GridManager.I;
    }

    // =========================================================
    // Public API - Base Overlay
    // =========================================================

    public void ShowMoveTiles(IEnumerable<Vector2Int> tiles)
    {
        moveTiles.Clear();
        AddToSet(moveTiles, tiles);
        RebuildVisuals();
    }

    // 기본 스킬 사거리 전용
    public void ShowRangeTiles(IEnumerable<Vector2Int> tiles)
    {
        skillRangeTiles.Clear();
        AddToSet(skillRangeTiles, tiles);
        RebuildVisuals();
    }

    public void ClearRangeTiles()
    {
        skillRangeTiles.Clear();
        RebuildVisuals();
    }

    // hover AOE 실제 영향 범위 전용
    public void ShowPreviewAreaTiles(IEnumerable<Vector2Int> tiles)
    {
        previewAreaTiles.Clear();
        AddToSet(previewAreaTiles, tiles);
        RebuildVisuals();
    }

    public void ClearPreviewAreaTiles()
    {
        previewAreaTiles.Clear();
        RebuildVisuals();
    }

    // =========================================================
    // Public API - Tile FX
    // =========================================================

    public void ShowSelectedTile(Vector2Int tile)
    {
        selectedFxTiles.Clear();
        selectedFxTiles.Add(tile);
        RebuildVisuals();
    }

    public void ClearSelectedTile()
    {
        selectedFxTiles.Clear();
        RebuildVisuals();
    }

    public void ShowTargetTiles(IEnumerable<Vector2Int> tiles)
    {
        targetFxTiles.Clear();
        AddToSet(targetFxTiles, tiles);
        RebuildVisuals();
    }

    public void ClearTarget()
    {
        targetFxTiles.Clear();
        RebuildVisuals();
    }

    public void ShowFriendlyFireTiles(IEnumerable<Vector2Int> tiles)
    {
        warningFxTiles.Clear();
        AddToSet(warningFxTiles, tiles);
        RebuildVisuals();
    }

    public void ClearFriendlyFireTiles()
    {
        warningFxTiles.Clear();
        RebuildVisuals();
    }

    public void ShowHazardTiles(IEnumerable<Vector2Int> tiles)
    {
        hazardFxTiles.Clear();
        AddToSet(hazardFxTiles, tiles);
        RebuildVisuals();
    }

    public void ClearHazardTiles()
    {
        hazardFxTiles.Clear();
        RebuildVisuals();
    }

    public void ClearTargetOverlay()
    {
        targetFxTiles.Clear();
        warningFxTiles.Clear();
        RebuildVisuals();
    }

    public void ShowActionableTiles(IEnumerable<Vector2Int> tiles)
    {
        actionableFxTiles.Clear();
        AddToSet(actionableFxTiles, tiles);
        RebuildVisuals();
    }

    public void ClearActionableTiles()
    {
        actionableFxTiles.Clear();
        RebuildVisuals();
    }

    // =========================================================
    // Public API - Path / Hazard Path / Ghost
    // =========================================================

    public void ShowPathTiles(List<Vector2Int> path)
    {
        RebuildArrowPath(path, hoverPathArrows);
    }

    public void ClearPath()
    {
        ClearSpawnedList(hoverPathArrows);
    }

    public void ShowPlannedPathTiles(List<Vector2Int> path)
    {
        RebuildArrowPath(path, plannedPathArrows);
    }

    public void ClearPlannedPath()
    {
        ClearSpawnedList(plannedPathArrows);
    }

    public void ShowHoverHazardPathTiles(List<Vector2Int> tiles)
    {
        RebuildHazardMarkers(tiles, hoverHazardMarkers);
    }

    public void ClearHoverHazardPath()
    {
        ClearSpawnedList(hoverHazardMarkers);
    }

    public void ShowPlannedHazardPathTiles(List<Vector2Int> tiles)
    {
        RebuildHazardMarkers(tiles, plannedHazardMarkers);
    }

    public void ClearPlannedHazardPath()
    {
        ClearSpawnedList(plannedHazardMarkers);
    }

    public void ShowGhostTile(Vector2Int tile)
    {
        if (ghostPrefab == null || grid == null) return;

        Vector3 world = GetWorld(tile) + ghostOffset;

        if (ghostInstance == null)
        {
            ghostInstance = Instantiate(ghostPrefab, world, Quaternion.identity, transform);
            ghostInstance.name = $"Ghost ({tile.x},{tile.y})";
        }
        else
        {
            ghostInstance.transform.position = world;
            ghostInstance.SetActive(true);
            ghostInstance.name = $"Ghost ({tile.x},{tile.y})";
        }
    }

    public void ClearGhostTile()
    {
        if (ghostInstance != null)
            ghostInstance.SetActive(false);
    }

    // =========================================================
    // Public API - All Clear
    // =========================================================

    public void ClearAll()
    {
        moveTiles.Clear();
        skillRangeTiles.Clear();
        previewAreaTiles.Clear();

        selectedFxTiles.Clear();
        actionableFxTiles.Clear();
        targetFxTiles.Clear();
        warningFxTiles.Clear();
        hazardFxTiles.Clear();

        ClearPath();
        ClearPlannedPath();
        ClearHoverHazardPath();
        ClearPlannedHazardPath();
        ClearGhostTile();

        RebuildVisuals();
    }

    // =========================================================
    // Core Visual Composition
    // =========================================================

    BaseOverlayType ResolveBaseType(Vector2Int tile)
    {
        // 우선순위:
        // PreviewArea > SkillRange > Move
        if (previewAreaTiles.Contains(tile)) return BaseOverlayType.PreviewArea;
        if (skillRangeTiles.Contains(tile)) return BaseOverlayType.SkillRange;
        if (moveTiles.Contains(tile)) return BaseOverlayType.Move;
        return BaseOverlayType.None;
    }

    TileFxFlags ResolveFx(Vector2Int tile)
    {
        TileFxFlags fx = TileFxFlags.None;

        if (selectedFxTiles.Contains(tile)) fx |= TileFxFlags.Selected;
        if (actionableFxTiles.Contains(tile)) fx |= TileFxFlags.Actionable;
        if (targetFxTiles.Contains(tile)) fx |= TileFxFlags.TargetHit;
        if (warningFxTiles.Contains(tile)) fx |= TileFxFlags.FriendlyFire;
        if (hazardFxTiles.Contains(tile)) fx |= TileFxFlags.Hazard;

        return fx;
    }

    void RebuildVisuals()
    {
        var allTiles = new HashSet<Vector2Int>();

        allTiles.UnionWith(moveTiles);
        allTiles.UnionWith(skillRangeTiles);
        allTiles.UnionWith(previewAreaTiles);

        allTiles.UnionWith(selectedFxTiles);
        allTiles.UnionWith(actionableFxTiles);
        allTiles.UnionWith(targetFxTiles);
        allTiles.UnionWith(warningFxTiles);
        allTiles.UnionWith(hazardFxTiles);

        foreach (var kv in cells)
        {
            if (kv.Value == null) continue;
            kv.Value.SetRootActive(false);
            kv.Value.HideAllBase();
            kv.Value.HideAllFx();
        }

        foreach (var tile in allTiles)
        {
            var cell = GetOrCreateCell(tile);
            if (cell == null) continue;

            cell.SetRootActive(true);
            ApplyBase(cell, ResolveBaseType(tile), tile);
            ApplyFx(cell, ResolveFx(tile));
        }
    }

    OverlayCellView GetOrCreateCell(Vector2Int tile)
    {
        if (cells.TryGetValue(tile, out var cached) && cached != null)
            return cached;

        if (grid == null) return null;

        var root = new GameObject($"OverlayCell ({tile.x},{tile.y})");
        root.transform.SetParent(transform, false);
        root.transform.position = GetWorld(tile) + overlayOffset;

        var cell = new OverlayCellView();
        cell.root = root;

        cell.baseMove = CreateChild(moveTilePrefab, root.transform, Vector3.zero, "Base_Move");
        cell.baseSkillRange = CreateChild(skillRangeTilePrefab, root.transform, Vector3.zero, "Base_SkillRange");
        cell.basePreviewArea = CreateChild(previewAreaTilePrefab, root.transform, Vector3.zero, "Base_PreviewArea");

        Vector3 localFxOffset = fxOffset - overlayOffset;
        cell.selectedFx = CreateChild(selectedFxPrefab, root.transform, localFxOffset, "FX_Selected");
        cell.actionableFx = CreateChild(actionableFxPrefab, root.transform, localFxOffset, "FX_Actionable");
        cell.targetFx = CreateChild(targetFxPrefab, root.transform, localFxOffset, "FX_Target");
        cell.warningFx = CreateChild(warningFxPrefab, root.transform, localFxOffset, "FX_FriendlyFire");
        cell.hazardFx = CreateChild(hazardFxPrefab, root.transform, localFxOffset, "FX_Hazard");

        cell.HideAllBase();
        cell.HideAllFx();
        cell.SetRootActive(false);

        cells[tile] = cell;
        return cell;
    }

    void ApplyBase(OverlayCellView cell, BaseOverlayType baseType, Vector2Int tile)
    {
        cell.HideAllBase();

        GameObject activeBase = null;

        switch (baseType)
        {
            case BaseOverlayType.Move:
                activeBase = cell.baseMove;
                break;

            case BaseOverlayType.SkillRange:
                activeBase = cell.baseSkillRange;
                break;

            case BaseOverlayType.PreviewArea:
                activeBase = cell.basePreviewArea;
                break;
        }

        if (activeBase != null)
        {
            activeBase.SetActive(true);

            var pulse = activeBase.GetComponent<TilePulseFx>();
            if (pulse != null)
            {
                bool pulseOn = ShouldPulseBase(tile, baseType);
                pulse.enabled = pulseOn;
            }
        }
    }

    bool ShouldPulseBase(Vector2Int tile, BaseOverlayType baseType)
    {
        // 실제 피해/효과 대상이 최우선
        if (targetFxTiles.Contains(tile)) return true;
        if (warningFxTiles.Contains(tile)) return true;

        // AOE 범위는 PreviewArea 전체 pulse
        if (baseType == BaseOverlayType.PreviewArea &&
            previewAreaTiles.Contains(tile))
            return true;

        // 이동/단일 선택은 selected만 pulse
        if (selectedFxTiles.Contains(tile)) return true;

        return false;
    }

    void ApplyFx(OverlayCellView cell, TileFxFlags fx)
    {
        cell.HideAllFx();

        if ((fx & TileFxFlags.Selected) != 0 && cell.selectedFx != null)
            cell.selectedFx.SetActive(true);

        if ((fx & TileFxFlags.Actionable) != 0 && cell.actionableFx != null)
        cell.actionableFx.SetActive(true);

        if ((fx & TileFxFlags.TargetHit) != 0 && cell.targetFx != null)
            cell.targetFx.SetActive(true);

        if ((fx & TileFxFlags.FriendlyFire) != 0 && cell.warningFx != null)
            cell.warningFx.SetActive(true);

        if ((fx & TileFxFlags.Hazard) != 0 && cell.hazardFx != null)
            cell.hazardFx.SetActive(true);
    }

    // =========================================================
    // Path Rendering
    // =========================================================

    void RebuildArrowPath(List<Vector2Int> path, List<GameObject> storage)
    {
        ClearSpawnedList(storage);

        if (pathArrowPrefab == null || grid == null || path == null || path.Count == 0)
            return;

        for (int i = 0; i < path.Count; i++)
        {
            Vector2Int tile = path[i];
            Vector2 dir = Vector2.zero;

            if (path.Count == 1)
            {
                dir = Vector2.right;
            }
            else if (i < path.Count - 1)
            {
                dir = (Vector2)(path[i + 1] - path[i]);
            }
            else
            {
                dir = (Vector2)(path[i] - path[i - 1]);
            }

            float angle = DirectionToZAngle(dir);
            Vector3 world = GetWorld(tile) + arrowOffset;

            var go = Instantiate(pathArrowPrefab, world, Quaternion.Euler(0f, 0f, angle), transform);
            go.name = $"PathArrow ({tile.x},{tile.y})";
            storage.Add(go);
        }
    }

    void RebuildHazardMarkers(List<Vector2Int> tiles, List<GameObject> storage)
    {
        ClearSpawnedList(storage);

        if (hazardFxPrefab == null || grid == null || tiles == null || tiles.Count == 0)
            return;

        for (int i = 0; i < tiles.Count; i++)
        {
            Vector2Int tile = tiles[i];
            Vector3 world = GetWorld(tile) + fxOffset;
            var go = Instantiate(hazardFxPrefab, world, Quaternion.identity, transform);
            go.name = $"HazardPathFx ({tile.x},{tile.y})";
            storage.Add(go);
        }
    }

    // =========================================================
    // Helpers
    // =========================================================

    GameObject CreateChild(GameObject prefab, Transform parent, Vector3 localPos, string name)
    {
        if (prefab == null) return null;

        var go = Instantiate(prefab, parent);
        go.name = name;
        go.transform.localPosition = localPos;
        go.SetActive(false);
        return go;
    }

    void ClearSpawnedList(List<GameObject> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null)
                Destroy(list[i]);
        }
        list.Clear();
    }

    void AddToSet(HashSet<Vector2Int> set, IEnumerable<Vector2Int> tiles)
    {
        if (tiles == null) return;
        foreach (var t in tiles)
            set.Add(t);
    }

    Vector3 GetWorld(Vector2Int tile)
    {
        return grid != null ? grid.GridToWorldWithHeight(tile) : Vector3.zero;
    }

    float DirectionToZAngle(Vector2 dir)
    {
        if (dir == Vector2.right) return 0f;
        if (dir == Vector2.up) return 90f;
        if (dir == Vector2.left) return 180f;
        if (dir == Vector2.down) return 270f;
        return 0f;
    }
}