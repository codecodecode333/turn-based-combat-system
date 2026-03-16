using UnityEngine;

public enum TileType
{
    Normal,
    Wall,
    Obstacle,
    Forest,
    Water,
    Hazard_Burn,
    Hazard_Poison,
    Hazard_Explosion
}

public enum HazardType
{
    None,
    Burn,
    Poison,
    Explosion
}

public enum HazardTriggerType
{
    None,
    OnEnter,
    OnTurnStart,
    OnTurnEnd
}

[CreateAssetMenu(menuName = "Grid/Tile Data")]
public class TileData : ScriptableObject
{
    [Header("Type")]
    public TileType tileType = TileType.Normal;

    [Header("Movement")]
    public bool passable = true;

    [Header("Line Of Sight")]
    public bool blocksLOS = false;

    [Header("Height")]
    public int heightLevel = 0;

    [Header("Hazard")]
    public HazardType hazardType = HazardType.None;
    public HazardTriggerType hazardTrigger = HazardTriggerType.None;
    [Min(0)] public int hazardPower = 1;
    [Min(1)] public int hazardDuration = 1;
}