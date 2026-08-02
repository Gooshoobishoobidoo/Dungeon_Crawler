using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
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
    private const float FormationSpacing = 1.5f;

    private Camera cam;
    private ItemPickup pendingPickup;
    private GameObject canvasGO;
    private InventoryBarUI inventoryBar;
    private PartyBarUI partyBar;

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

        partyBar?.Refresh();
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

        MovePartyInFormation(hit.point);
    }

    // Spreads the party across distinct points around the destination instead of sending every
    // member to the exact same spot - NavMeshAgent avoidance alone just left them jostling for it.
    private void MovePartyInFormation(Vector3 destination)
    {
        List<Character> movable = new List<Character>();
        foreach (Character member in DungeonManager.Instance.party)
        {
            // Busy (channeling an item) characters ignore new move orders - the rest of the
            // party can still move independently.
            if (!member.isDead && !member.isBusy) movable.Add(member);
        }
        if (movable.Count == 0) return;

        Vector3 centroid = Vector3.zero;
        foreach (Character member in movable) centroid += member.transform.position;
        centroid /= movable.Count;

        // Flattened onto XZ so a party on uneven ground doesn't tilt the formation. Falls back to
        // world-forward if the click lands right on the party (near-zero direction can't be
        // normalized into a sensible "back" axis for the formation).
        Vector3 travel = destination - centroid;
        travel.y = 0f;
        Vector3 travelDir = travel.sqrMagnitude > 0.01f ? travel.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, travelDir);

        List<Vector3> slots = BuildFormationSlots(movable.Count, destination, travelDir, right);
        AssignSlotsByProximity(movable, slots);
    }

    // Diamond: point at the clicked destination, two flanks behind-and-to-the-side, one anchor
    // straight behind. (side, rows-back) in formation-local space, scaled by FormationSpacing.
    private static readonly Vector2[] DiamondOffsets =
    {
        new Vector2(0f, 0f),  // point (front)
        new Vector2(-1f, 1f), // left flank
        new Vector2(1f, 1f),  // right flank
        new Vector2(0f, 2f),  // back anchor
    };

    private List<Vector3> BuildFormationSlots(int count, Vector3 destination, Vector3 back, Vector3 right)
    {
        List<Vector3> slots = new List<Vector3>();
        for (int i = 0; i < count; i++)
        {
            // Beyond the 4-slot diamond (not reachable at today's 4-hero roster cap): keep
            // extending straight back so this doesn't silently stack characters on top of slot 3.
            Vector2 pattern = i < DiamondOffsets.Length
                ? DiamondOffsets[i]
                : new Vector2(0f, 2f + (i - DiamondOffsets.Length + 1));

            Vector3 offset = right * (pattern.x * FormationSpacing) + -back * (pattern.y * FormationSpacing);
            Vector3 target = destination + offset;
            if (NavMesh.SamplePosition(target, out NavMeshHit navHit, FormationSpacing * 2f, NavMesh.AllAreas))
                slots.Add(navHit.position);
            else
                slots.Add(destination);
        }
        return slots;
    }

    // Greedily pairs whichever character/slot are currently closest, repeatedly, rather than a
    // fixed index order - keeps characters from criss-crossing to reach a far slot when a nearer
    // one is available to them.
    private void AssignSlotsByProximity(List<Character> members, List<Vector3> slots)
    {
        List<Character> remainingMembers = new List<Character>(members);
        List<Vector3> remainingSlots = new List<Vector3>(slots);

        while (remainingMembers.Count > 0)
        {
            float bestDist = float.MaxValue;
            int bestMemberIndex = 0, bestSlotIndex = 0;

            for (int m = 0; m < remainingMembers.Count; m++)
            {
                for (int s = 0; s < remainingSlots.Count; s++)
                {
                    float dist = Vector3.SqrMagnitude(remainingMembers[m].transform.position - remainingSlots[s]);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestMemberIndex = m;
                        bestSlotIndex = s;
                    }
                }
            }

            remainingMembers[bestMemberIndex].MoveTo(remainingSlots[bestSlotIndex]);
            remainingMembers.RemoveAt(bestMemberIndex);
            remainingSlots.RemoveAt(bestSlotIndex);
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

        var partyBarGO = new GameObject("PartyBar");
        partyBarGO.transform.SetParent(canvasGO.transform, false);
        partyBar = partyBarGO.AddComponent<PartyBarUI>();
        partyBar.Build();
        partyBar.getCharacters = () => DungeonManager.Instance?.party;
        partyBar.onPortraitClicked = c => inventoryBar?.Show(c, OnUseItemSelected);
    }
}
