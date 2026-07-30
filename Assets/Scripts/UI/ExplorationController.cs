using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Exploration-mode click-to-move: reuses the same raycast/EventSystem-check pattern as
// PlanningController.HandleWorldClick, but simpler - no turns, no abilities, no plannedAction.
// Only acts while DungeonManager is in Exploration mode.
public class ExplorationController : MonoBehaviour
{
    private Camera cam;

    private void Start()
    {
        cam = Camera.main;
    }

    private void Update()
    {
        if (DungeonManager.Instance == null || DungeonManager.Instance.currentMode != GameMode.Exploration) return;
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f)) return;

        foreach (Character member in DungeonManager.Instance.party)
        {
            if (!member.isDead) member.MoveTo(hit.point);
        }
    }
}
