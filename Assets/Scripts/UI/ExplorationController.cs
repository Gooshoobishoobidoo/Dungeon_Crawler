using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Exploration-mode click-to-move: reuses the same raycast/EventSystem-check pattern as
// PlanningController.HandleWorldClick, but simpler - no turns, no abilities, no plannedAction.
// Only acts while DungeonManager is in Exploration mode.
public class ExplorationController : MonoBehaviour
{
    private const float InteractRange = 2f;

    private Camera cam;
    private ItemPickup pendingPickup;
    private GameObject canvasGO;
    private InventoryBarUI inventoryBar;

    private void Start()
    {
        cam = Camera.main;
        BuildUI();
    }

    private void Update()
    {
        // Hide this panel entirely outside Exploration mode, not just gate its logic - left
        // merely-inert-but-visible, it would sit at the same screen position as
        // PlanningController's own panels and could intercept clicks meant for them.
        bool exploring = DungeonManager.Instance != null && DungeonManager.Instance.currentMode == GameMode.Exploration;
        if (canvasGO != null) canvasGO.SetActive(exploring);
        if (!exploring) return;

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

        // Clicking a party member opens their inventory instead of moving - doesn't touch
        // pendingPickup or issue any moves.
        Character character = hit.collider.GetComponentInParent<Character>();
        if (character != null && DungeonManager.Instance.party.Contains(character))
        {
            inventoryBar?.Show(character, OnUseItemSelected);
            return;
        }

        inventoryBar?.Hide();

        // A plain ground click cancels any in-progress walk-to-pickup; clicking a pickup
        // (re)targets it. Either way the party still walks toward the clicked point.
        pendingPickup = hit.collider.GetComponentInParent<ItemPickup>();

        foreach (Character member in DungeonManager.Instance.party)
        {
            // Busy (channeling an item) characters ignore new move orders - the rest of the
            // party can still move independently, same as any other per-character MoveTo call.
            if (!member.isDead && !member.isBusy) member.MoveTo(hit.point);
        }
    }

    // Uses the item immediately (no targeting step, items are self-only) and re-shows the panel
    // so the now-consumed item disappears from the list right away.
    private void OnUseItemSelected(Character character, ItemData item)
    {
        character.UseItem(item);
        inventoryBar?.Show(character, OnUseItemSelected);
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

    private void BuildUI()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<InputSystemUIInputModule>();
        }

        canvasGO = new GameObject("ExplorationCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        var inventoryBarGO = new GameObject("InventoryBar");
        inventoryBarGO.transform.SetParent(canvasGO.transform, false);
        inventoryBar = inventoryBarGO.AddComponent<InventoryBarUI>();
        inventoryBar.Build();
    }
}
