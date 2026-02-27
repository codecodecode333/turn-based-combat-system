using UnityEngine;

public class TilemapPresenter : MonoBehaviour
{
    public GridManager grid;
    public TileView tilePrefab;

    void Awake()
    {
        if (!grid) grid = GridManager.I;
        Build();
    }

    public void Build()
    {
        if (!grid || !tilePrefab) return;

        // grid의 가로/세로 크기 접근 방식은 네 GridManager에 맞춰 바꿔야 함
        // 예: grid.width, grid.height 또는 grid.SizeX/SizeY 등
        int w = grid.width;
        int h = grid.height;

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var p = new Vector2Int(x, y);
            var world = grid.GridToWorld(p);

            var tv = Instantiate(tilePrefab, world, Quaternion.identity, transform);
            tv.Init(p);
        }
    }
}