using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Exploration-mode click-to-move: reuses the same raycast/EventSystem-check pattern as
// PlanningController.HandleWorldClick, but simpler - no turns, no abilities, no plannedAction.
// Only acts while DungeonManager is in Exploration mode.
public class ExplorationController : MonoBehaviour
{
    private const float InteractRange = 2f;

    private Camera cam;
    private ItemPickup pendingPickup;

    private void Start()
    {
        cam = Camera.main;
    }

    private void Update()
    {
        if (DungeonManager.Instance == null || DungeonManager.Instance.currentMode != GameMode.Exploration) return;

        CheckPendingPickup();
        HandleWorldClick();
    }

    // Runs every frame, independent of new clicks, so a pickup targeted a few clicks ago still
    // resolves once the party actually reaches it.
    private void CheckPendingPickup()
    {
        if (pendingPickup == null) return;

        if (IsAnyPartyMemberWithin(pendingPickup.transform.position, InteractRange))
        {
            pendingPickup.Collect();
            pendingPickup = null;
        }
    }

    private void HandleWorldClick()
    {
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        if (cam == null) return;

        // Ignore triggers - Physics.Raycast hits them by default, and a tall trigger volume
        // like RestRoomTransition's detection box would otherwise intercept clicks meant for
        // the floor beneath it (its near face, not the floor, becoming the hit point).
        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return;

        // A plain ground click cancels any in-progress walk-to-pickup; clicking a pickup
        // (re)targets it. Either way the party still walks toward the clicked point.
        pendingPickup = hit.collider.GetComponentInParent<ItemPickup>();

        foreach (Character member in DungeonManager.Instance.party)
        {
            if (!member.isDead) member.MoveTo(hit.point);
        }
    }

    private bool IsAnyPartyMemberWithin(Vector3 point, float range)
    {
        foreach (Character member in DungeonManager.Instance.party)
        {
            if (member.isDead) continue;
            if (Vector3.Distance(member.transform.position, point) <= range) return true;
        }

        return false;
    }
}
