using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class TileView : MonoBehaviour
{
    public Vector2Int GridPos { get; private set; }

    [Header("Tile Data")]        
    public TileData tileData;

    public bool BlocksLOS
    {
        get
        {
            if (tileData == null) return false;
            return tileData.blocksLOS;
        }
    }

    public void Init(Vector2Int gridPos)
    {
        GridPos = gridPos;
        gameObject.name = $"Tile ({gridPos.x},{gridPos.y})";
    }
}