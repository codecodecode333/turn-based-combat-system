using UnityEngine;
using UnityEngine.Tilemaps;

public class TilemapPresenter : MonoBehaviour
{
    [Header("Refs")]
    public GridManager grid;
    public TileView tilePrefab;

    [Header("Ground Tilemaps")]
    public Tilemap groundH0;
    public Tilemap groundH1;
    public Tilemap groundH2;

    [Header("Obstacle Tilemaps")]
    public Tilemap obstacleH0;
    public Tilemap obstacleH1;
    public Tilemap obstacleH2;

    [Header("Ground TileData")]
    public TileData grassH0;
    public TileData grassH1;
    public TileData grassH2;

    [Header("Rock TileData")]
    public TileData rockH0;
    public TileData rockH1;
    public TileData rockH2;

    [Header("Tree TileData")]
    public TileData treeH0;
    public TileData treeH1;
    public TileData treeH2;

    [Header("Obstacle Tiles")]
    public TileBase rockTile;
    public TileBase treeTile;

    [Header("Debug")]
    public bool debugLog = false;

    void Awake()
    {
        if (!grid) grid = GridManager.I;
        Build();
    }

    public void Build()
    {
        if (!grid || !tilePrefab) return;

        var bounds = GetCombinedBounds();

        foreach (var cellPos in bounds.allPositionsWithin)
        {
            var gridPos = new Vector2Int(cellPos.x, cellPos.y);

            if (!grid.InBounds(gridPos))
                continue;

            TileData data = ResolveTileData(cellPos);
            if (data == null)
                continue;

            var tv = Instantiate(tilePrefab, Vector3.zero, Quaternion.identity, transform);

            tv.tileData = data;
            tv.Init(gridPos);
            tv.transform.position = grid.GridToWorldWithHeight(gridPos, data.heightLevel);

            grid.RegisterTile(tv);

            if (debugLog)
            {
                Debug.Log(
                    $"[TilemapPresenter] Created {gridPos} / " +
                    $"data={data.name}, passable={data.passable}, " +
                    $"blocksLOS={data.blocksLOS}, height={data.heightLevel}"
                );
            }
        }
    }

    TileData ResolveTileData(Vector3Int cellPos)
    {
        // Obstacle 우선. 같은 좌표에 ground + obstacle이 있으면 obstacle 로직 적용.
        if (obstacleH2 != null && obstacleH2.HasTile(cellPos))
            return ResolveObstacleData(obstacleH2.GetTile(cellPos), 2);

        if (obstacleH1 != null && obstacleH1.HasTile(cellPos))
            return ResolveObstacleData(obstacleH1.GetTile(cellPos), 1);

        if (obstacleH0 != null && obstacleH0.HasTile(cellPos))
            return ResolveObstacleData(obstacleH0.GetTile(cellPos), 0);

        if (groundH2 != null && groundH2.HasTile(cellPos)) return grassH2;
        if (groundH1 != null && groundH1.HasTile(cellPos)) return grassH1;
        if (groundH0 != null && groundH0.HasTile(cellPos)) return grassH0;

        return null;
    }

    TileData ResolveObstacleData(TileBase tile, int height)
    {
        bool isTree = tile == treeTile;

        if (isTree)
        {
            if (height == 2) return treeH2;
            if (height == 1) return treeH1;
            return treeH0;
        }

        // 기본은 rock 처리
        if (height == 2) return rockH2;
        if (height == 1) return rockH1;
        return rockH0;
    }

    BoundsInt GetCombinedBounds()
    {
        BoundsInt bounds = new BoundsInt(Vector3Int.zero, Vector3Int.zero);
        bool initialized = false;

        void Encapsulate(Tilemap map)
        {
            if (map == null) return;

            map.CompressBounds();

            if (!initialized)
            {
                bounds = map.cellBounds;
                initialized = true;
                return;
            }

            bounds.xMin = Mathf.Min(bounds.xMin, map.cellBounds.xMin);
            bounds.yMin = Mathf.Min(bounds.yMin, map.cellBounds.yMin);
            bounds.xMax = Mathf.Max(bounds.xMax, map.cellBounds.xMax);
            bounds.yMax = Mathf.Max(bounds.yMax, map.cellBounds.yMax);
        }

        Encapsulate(groundH0);
        Encapsulate(groundH1);
        Encapsulate(groundH2);

        Encapsulate(obstacleH0);
        Encapsulate(obstacleH1);
        Encapsulate(obstacleH2);

        return bounds;
    }
}