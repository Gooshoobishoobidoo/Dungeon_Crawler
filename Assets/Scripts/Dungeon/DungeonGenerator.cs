using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

// Assembles one floor from hand-built room prefabs (RoomTemplate) arranged on an integer grid,
// then bakes a NavMesh over the result. Step 1 of "making the dungeon work": layout + walkable
// NavMesh only - no enemy/item population and no real staircase-down trigger yet (both future
// steps; see ROADMAP.md).
public class DungeonGenerator : MonoBehaviour
{
    private enum Direction { North, East, South, West }

    private static readonly Direction[] AllDirections =
        { Direction.North, Direction.East, Direction.South, Direction.West };

    private static readonly Dictionary<Direction, Vector2Int> DirectionOffsets = new Dictionary<Direction, Vector2Int>
    {
        { Direction.North, new Vector2Int(0, 1) },
        { Direction.East, new Vector2Int(1, 0) },
        { Direction.South, new Vector2Int(0, -1) },
        { Direction.West, new Vector2Int(-1, 0) },
    };

    private static readonly Dictionary<Direction, Direction> Opposite = new Dictionary<Direction, Direction>
    {
        { Direction.North, Direction.South },
        { Direction.East, Direction.West },
        { Direction.South, Direction.North },
        { Direction.West, Direction.East },
    };

    [Header("Room Prefabs")]
    public GameObject startRoomPrefab;
    public List<GameObject> roomPrefabs = new List<GameObject>();

    [Header("Layout")]
    public float cellSize = 20f;
    public int minRooms = 8;
    public int maxRooms = 14;

    // World-space position of the start room, for DungeonManager to place the party after
    // generation completes.
    public Vector3 StartPosition { get; private set; }

    private Transform currentFloor;
    private NavMeshSurface navMeshSurface;

    private void Awake()
    {
        navMeshSurface = GetComponent<NavMeshSurface>();
    }

    public void Generate()
    {
        if (startRoomPrefab == null || roomPrefabs.Count == 0)
        {
            Debug.LogError("DungeonGenerator needs a startRoomPrefab and at least one entry in roomPrefabs before it can generate.");
            return;
        }

        // Immediate, not deferred Destroy: BuildNavMesh() runs later in this same call and must
        // only see the new floor's geometry, not last floor's still-pending-destruction leftovers.
        if (currentFloor != null) DestroyImmediate(currentFloor.gameObject);

        currentFloor = new GameObject("CurrentFloor").transform;
        // Parented under this object so the NavMeshSurface (also on this object, Children collect
        // mode) actually finds the instantiated rooms - they'd otherwise sit in a sibling
        // hierarchy the surface never scans, silently baking an empty NavMesh.
        currentFloor.SetParent(transform, false);

        Vector2Int start = Vector2Int.zero;
        Dictionary<Vector2Int, HashSet<Direction>> openSides = BuildGraph(start);
        Vector2Int staircaseCell = FindFarthestCell(start, openSides);

        Dictionary<Vector2Int, RoomTemplate> instantiatedRooms = InstantiateRooms(start, openSides);
        OpenConnectedWalls(openSides, instantiatedRooms);

        StartPosition = CellToWorld(start);

        if (staircaseCell != start)
        {
            Debug.Log($"Staircase location: cell {staircaseCell} (world {CellToWorld(staircaseCell)}) " +
                      "- no trigger placed yet, that's a future step.");
        }

        if (navMeshSurface != null)
            navMeshSurface.BuildNavMesh();
        else
            Debug.LogWarning("DungeonGenerator has no NavMeshSurface component - the generated floor won't be walkable.");
    }

    // Randomized growing-tree walk on an integer grid: repeatedly expands from a *random* already-
    // placed cell (not always the most recently placed one) rather than a plain DFS, which is what
    // produces branching side paths instead of one long corridor. A cell stays eligible to expand
    // again after being used once, so junctions with 3+ connections happen naturally.
    private Dictionary<Vector2Int, HashSet<Direction>> BuildGraph(Vector2Int start)
    {
        var openSides = new Dictionary<Vector2Int, HashSet<Direction>> { { start, new HashSet<Direction>() } };
        var frontier = new List<Vector2Int> { start };

        // Clamped defensively - Random.Range(min, max) misbehaves if min ends up greater than
        // max (e.g. the two Inspector fields set in the wrong order).
        int targetCount = Random.Range(Mathf.Max(1, minRooms), Mathf.Max(minRooms, maxRooms) + 1);

        while (openSides.Count < targetCount && frontier.Count > 0)
        {
            Vector2Int current = frontier[Random.Range(0, frontier.Count)];
            List<Direction> openDirections = UnoccupiedDirections(current, openSides);
            if (openDirections.Count == 0)
            {
                frontier.Remove(current);
                continue;
            }

            Direction chosen = openDirections[Random.Range(0, openDirections.Count)];
            Vector2Int next = current + DirectionOffsets[chosen];

            openSides[current].Add(chosen);
            openSides[next] = new HashSet<Direction> { Opposite[chosen] };
            frontier.Add(next);
        }

        return openSides;
    }

    private List<Direction> UnoccupiedDirections(Vector2Int cell, Dictionary<Vector2Int, HashSet<Direction>> openSides)
    {
        List<Direction> result = new List<Direction>();
        foreach (Direction dir in AllDirections)
        {
            if (!openSides.ContainsKey(cell + DirectionOffsets[dir])) result.Add(dir);
        }
        return result;
    }

    // BFS over the connection graph - the room with the maximum path distance from the start
    // becomes the staircase, guaranteeing it sits at the end of the critical path while every
    // other branch remains an optional detour.
    private Vector2Int FindFarthestCell(Vector2Int start, Dictionary<Vector2Int, HashSet<Direction>> openSides)
    {
        var distances = new Dictionary<Vector2Int, int> { { start, 0 } };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(start);

        Vector2Int farthest = start;
        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            foreach (Direction dir in openSides[cell])
            {
                Vector2Int neighbor = cell + DirectionOffsets[dir];
                if (distances.ContainsKey(neighbor)) continue;

                distances[neighbor] = distances[cell] + 1;
                queue.Enqueue(neighbor);
                if (distances[neighbor] > distances[farthest]) farthest = neighbor;
            }
        }

        return farthest;
    }

    private Dictionary<Vector2Int, RoomTemplate> InstantiateRooms(Vector2Int start, Dictionary<Vector2Int, HashSet<Direction>> openSides)
    {
        var instantiatedRooms = new Dictionary<Vector2Int, RoomTemplate>();

        foreach (Vector2Int cell in openSides.Keys)
        {
            GameObject prefab = cell == start ? startRoomPrefab : roomPrefabs[Random.Range(0, roomPrefabs.Count)];
            GameObject instance = Instantiate(prefab, CellToWorld(cell), Quaternion.identity, currentFloor);

            RoomTemplate template = instance.GetComponent<RoomTemplate>();
            if (template == null)
                Debug.LogWarning($"Room prefab '{prefab.name}' has no RoomTemplate component - its walls won't open.");
            else
                instantiatedRooms[cell] = template;
        }

        return instantiatedRooms;
    }

    private void OpenConnectedWalls(Dictionary<Vector2Int, HashSet<Direction>> openSides, Dictionary<Vector2Int, RoomTemplate> instantiatedRooms)
    {
        foreach (var entry in openSides)
        {
            if (!instantiatedRooms.TryGetValue(entry.Key, out RoomTemplate template)) continue;

            foreach (Direction dir in entry.Value)
            {
                GameObject wall = WallFor(template, dir);
                if (wall != null) wall.SetActive(false);
            }
        }
    }

    private static GameObject WallFor(RoomTemplate template, Direction dir)
    {
        switch (dir)
        {
            case Direction.North: return template.northWall;
            case Direction.East: return template.eastWall;
            case Direction.South: return template.southWall;
            case Direction.West: return template.westWall;
            default: return null;
        }
    }

    private Vector3 CellToWorld(Vector2Int cell) => new Vector3(cell.x * cellSize, 0f, cell.y * cellSize);
}
