using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class TileView : MonoBehaviour
{
    public Vector2Int GridPos { get; private set; }

    public void Init(Vector2Int gridPos)
    {
        GridPos = gridPos;
        gameObject.name = $"Tile ({gridPos.x},{gridPos.y})";
    }
}