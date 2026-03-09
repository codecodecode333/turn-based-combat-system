using UnityEngine;

[CreateAssetMenu(menuName = "Grid/Tile Data")]
public class TileData : ScriptableObject
{
    [Header("Movement")]
    public bool passable = true;

    [Header("Line Of Sight")]
    public bool blocksLOS = false;

    [Header("Height")]
    public int heightLevel = 0;

    [Header("Hazard")]
    public string hazardType;
}