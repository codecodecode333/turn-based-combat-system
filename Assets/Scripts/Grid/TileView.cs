using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class TileView : MonoBehaviour
{
    public Vector2Int GridPos { get; private set; }

    [Header("Tile Data")]
    public TileData tileData;

    public TileType TileType => tileData ? tileData.tileType : TileType.Normal;
    public bool Passable => tileData == null || tileData.passable;
    public bool BlocksLOS => tileData != null && tileData.blocksLOS;
    public int HeightLevel => tileData ? tileData.heightLevel : 0;

    public HazardType HazardType => tileData ? tileData.hazardType : HazardType.None;
    public HazardTriggerType HazardTrigger => tileData ? tileData.hazardTrigger : HazardTriggerType.None;
    public int HazardPower => tileData ? Mathf.Max(0, tileData.hazardPower) : 0;
    public int HazardDuration => tileData ? Mathf.Max(0, tileData.hazardDuration) : 0;

    public bool HasHazard => HazardType != HazardType.None && HazardTrigger != HazardTriggerType.None;

    public void Init(Vector2Int gridPos)
    {
        GridPos = gridPos;
        gameObject.name = $"Tile ({gridPos.x},{gridPos.y})";
    }
}