using UnityEngine;
using UnityEngine.InputSystem;

// Follows the living party's centroid. Sits on top of that with scroll-wheel zoom, WASD/arrow-key
// pan, and Q/E orbit around the target. Orbit drives the camera's yaw directly (around world up,
// preserving the original authored pitch), and the same yaw rotates the position offset - since
// both use the identical angle, the camera stays aimed at the target throughout the orbit with no
// separate LookAt needed. Pan is deliberately independent of that (a plain position offset, not
// itself a rotation), so panning slides the view without fighting orbit or auto-recentering the
// aim - recenterKey snaps pan back to zero on demand, orbit has no such reset.
public class CameraController : MonoBehaviour
{
    [Header("Follow")]
    public Vector3 offset = new Vector3(0f, 18.5f, -20f);
    public float followSmoothTime = 0.2f;

    [Header("Zoom")]
    public float zoomSpeed = 10f;
    public float minDistance = 10f;
    public float maxDistance = 40f;

    [Header("Pan")]
    public float panSpeed = 20f;
    public float maxPanDistance = 15f;
    public Key recenterKey = Key.Space;

    [Header("Orbit")]
    public float rotateSpeed = 90f; // degrees per second
    public Key rotateLeftKey = Key.Q;
    public Key rotateRightKey = Key.E;

    private float currentDistance;
    private Vector3 panOffset;
    private Vector3 followVelocity;
    private float orbitYaw;
    private Quaternion baseRotation;

    private void Start()
    {
        currentDistance = Mathf.Clamp(offset.magnitude, minDistance, maxDistance);
        baseRotation = transform.rotation;
    }

    private void LateUpdate()
    {
        Vector3? centroid = ComputePartyCentroid();
        if (centroid == null) return;

        HandleOrbit();
        HandleZoom();
        HandlePan();

        Vector3 direction = offset.sqrMagnitude > 0.0001f ? offset.normalized : Vector3.back;
        Vector3 rotatedDirection = Quaternion.Euler(0f, orbitYaw, 0f) * direction;
        Vector3 desiredPosition = centroid.Value + rotatedDirection * currentDistance + panOffset;

        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref followVelocity, followSmoothTime);
    }

    private void HandleOrbit()
    {
        if (Keyboard.current != null)
        {
            float input = 0f;
            if (Keyboard.current[rotateLeftKey].isPressed) input -= 1f;
            if (Keyboard.current[rotateRightKey].isPressed) input += 1f;

            if (!Mathf.Approximately(input, 0f))
                orbitYaw = Mathf.Repeat(orbitYaw + input * rotateSpeed * Time.deltaTime, 360f);
        }

        // Reapplied every frame (not just while orbiting) since SmoothDamp only ever touches
        // position - rotation needs to be set explicitly regardless of whether orbitYaw changed
        // this frame.
        transform.rotation = Quaternion.Euler(0f, orbitYaw, 0f) * baseRotation;
    }

    private Vector3? ComputePartyCentroid()
    {
        if (DungeonManager.Instance == null) return null;

        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (Character member in DungeonManager.Instance.party)
        {
            if (member == null || member.isDead) continue;
            sum += member.transform.position;
            count++;
        }

        return count > 0 ? sum / count : (Vector3?)null;
    }

    private void HandleZoom()
    {
        if (Mouse.current == null) return;

        // Scroll deltas are already discrete per-event impulses, not a continuous rate - unlike
        // the pan input below, this deliberately isn't scaled by Time.deltaTime.
        float scroll = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Approximately(scroll, 0f)) return;

        currentDistance = Mathf.Clamp(currentDistance - scroll * zoomSpeed, minDistance, maxDistance);
    }

    private void HandlePan()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current[recenterKey].wasPressedThisFrame)
        {
            panOffset = Vector3.zero;
            return;
        }

        Vector2 input = Vector2.zero;
        if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) input.y += 1f;
        if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) input.y -= 1f;
        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) input.x -= 1f;
        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) input.x += 1f;

        if (input.sqrMagnitude < 0.0001f) return;

        // Along the camera's own flattened right/forward axes rather than world XZ, so WASD feels
        // screen-relative regardless of the fixed viewing angle.
        Vector3 right = transform.right;
        right.y = 0f;
        right.Normalize();

        Vector3 forward = transform.forward;
        forward.y = 0f;
        forward.Normalize();

        Vector3 delta = (right * input.x + forward * input.y) * panSpeed * Time.deltaTime;
        panOffset = Vector3.ClampMagnitude(panOffset + delta, maxPanDistance);
    }
}
