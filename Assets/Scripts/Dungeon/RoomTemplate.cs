using UnityEngine;

// Marker component on the root of a hand-built room prefab. Every room prefab is authored to the
// same grid-cell footprint (DungeonGenerator.cellSize) with a wall segment on all four sides -
// DungeonGenerator disables whichever side(s) connect to a placed neighbor at generation time,
// leaving the rest sealed (a dead end on any side with no neighbor, with zero extra authoring).
public class RoomTemplate : MonoBehaviour
{
    [Header("Cardinal Walls (disabled by DungeonGenerator when that side connects to a neighbor)")]
    public GameObject northWall;
    public GameObject eastWall;
    public GameObject southWall;
    public GameObject westWall;
}
