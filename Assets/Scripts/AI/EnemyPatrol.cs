using UnityEngine;

// Idle/patrol behavior for an enemy Character outside of combat. Only acts while
// DungeonManager is in Exploration mode; once alerted it stops patrolling/re-checking until
// DungeonManager.ReturnToExploration calls ResetAlert (on flee or after a fight elsewhere ends) -
// otherwise this enemy could never trigger a second encounter after its first one.
[RequireComponent(typeof(Character))]
public class EnemyPatrol : MonoBehaviour
{
    [Header("Patrol")]
    public Transform[] waypoints; // empty = stays idle, just watches

    [Header("Detection")]
    public float detectionRadius = 6f;
    public float detectionAngle = 360f; // full cone angle in degrees - 360 = old omnidirectional behavior

    public Character Character { get; private set; }
    public bool IsAlerted { get; private set; }

    private int waypointIndex;

    private const float EyeHeight = 1f; // keeps the line-of-sight raycast from grazing the floor collider

    private void Awake()
    {
        Character = GetComponent<Character>();
    }

    // Called by DungeonManager once combat is over (flee or victory elsewhere), so this enemy
    // can detect the party again rather than being permanently inert after its first alert.
    public void ResetAlert()
    {
        IsAlerted = false;
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
            if (!CanSee(member)) continue;

            IsAlerted = true;
            Character.StopMoving();
            DungeonManager.Instance.OnEnemyAlerted(this);
            return true;
        }

        return false;
    }

    // Range, then cone, then line-of-sight - cheapest checks first, so a raycast only happens for
    // members that already passed the first two. Cone/LoS naturally reduce to "always true" when
    // detectionAngle is left at 360 and nothing blocks the line, preserving today's plain-radius
    // behavior for any enemy that hasn't been tuned.
    private bool CanSee(Character member)
    {
        Vector3 eyePosition = transform.position + Vector3.up * EyeHeight;
        Vector3 targetPosition = member.transform.position + Vector3.up * EyeHeight;
        Vector3 toTarget = targetPosition - eyePosition;
        float distance = toTarget.magnitude;

        if (distance > detectionRadius) return false;

        Vector3 flatDirection = toTarget;
        flatDirection.y = 0;
        if (flatDirection.sqrMagnitude > 0.0001f)
        {
            float angle = Vector3.Angle(transform.forward, flatDirection.normalized);
            if (angle > detectionAngle * 0.5f) return false;
        }

        if (distance > 0.01f &&
            Physics.Raycast(eyePosition, toTarget.normalized, out RaycastHit hit, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            Character hitCharacter = hit.collider.GetComponentInParent<Character>();
            if (hitCharacter != member) return false; // something else - a wall, another character - is in the way
        }

        return true;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        if (detectionAngle < 360f)
        {
            Quaternion leftRotation = Quaternion.AngleAxis(-detectionAngle * 0.5f, Vector3.up);
            Quaternion rightRotation = Quaternion.AngleAxis(detectionAngle * 0.5f, Vector3.up);
            Gizmos.DrawLine(transform.position, transform.position + leftRotation * transform.forward * detectionRadius);
            Gizmos.DrawLine(transform.position, transform.position + rightRotation * transform.forward * detectionRadius);
        }
    }

    private void Patrol()
    {
        if (waypoints == null || waypoints.Length == 0) return;
        if (Character.isMoving) return;

        Character.MoveTo(waypoints[waypointIndex].position);
        waypointIndex = (waypointIndex + 1) % waypoints.Length;
    }
}
