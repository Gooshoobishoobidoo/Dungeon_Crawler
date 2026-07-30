using UnityEngine;

// Idle/patrol behavior for an enemy Character outside of combat. Only acts while
// DungeonManager is in Exploration mode; once alerted it stops patrolling/re-checking -
// it's either now in the triggered fight, or (if the party fled and stayed nearby) will
// naturally re-trigger since detection is just distance-based and continuous.
[RequireComponent(typeof(Character))]
public class EnemyPatrol : MonoBehaviour
{
    [Header("Patrol")]
    public Transform[] waypoints; // empty = stays idle, just watches

    [Header("Detection")]
    public float detectionRadius = 6f;

    public Character Character { get; private set; }
    public bool IsAlerted { get; private set; }

    private int waypointIndex;

    private void Awake()
    {
        Character = GetComponent<Character>();
    }

    private void Update()
    {
        if (IsAlerted || Character.isDead) return;
        if (DungeonManager.Instance == null || DungeonManager.Instance.currentMode != GameMode.Exploration) return;

        if (CheckForDetection()) return;
        Patrol();
    }

    private bool CheckForDetection()
    {
        foreach (Character member in DungeonManager.Instance.party)
        {
            if (member.isDead) continue;
            if (Vector3.Distance(transform.position, member.transform.position) > detectionRadius) continue;

            IsAlerted = true;
            Character.StopMoving();
            DungeonManager.Instance.OnEnemyAlerted(this);
            return true;
        }

        return false;
    }

    private void Patrol()
    {
        if (waypoints == null || waypoints.Length == 0) return;
        if (Character.isMoving) return;

        Character.MoveTo(waypoints[waypointIndex].position);
        waypointIndex = (waypointIndex + 1) % waypoints.Length;
    }
}
