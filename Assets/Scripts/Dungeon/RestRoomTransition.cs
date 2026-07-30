using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// A rest room between dungeon areas: walking in heals the party once and shows a "Continue?"
// prompt. Confirming seals the entrance and opens the exit via NavMeshObstacle, so the party
// physically can't path back into the area they just left - not just a soft UI restriction.
[RequireComponent(typeof(BoxCollider))]
public class RestRoomTransition : MonoBehaviour
{
    [Header("Doors")]
    // The door the party just walked through to get here (e.g. "Zone1Exit") - starts disabled
    // (open); seals shut on Continue.
    public NavMeshObstacle doorToPreviousArea;
    // The door leading onward (e.g. "Zone2Entrance") - starts enabled (closed); opens on Continue.
    public NavMeshObstacle doorToNextArea;

    private bool hasTriggered;
    private GameObject promptCanvasGO;

    private void Awake()
    {
        GetComponent<BoxCollider>().isTrigger = true;

        // NavMeshObstacle only cuts a hole in the NavMesh if `carving` is true, which isn't
        // set just by adding the component - enforced here so a door actually blocks/opens
        // whenever its `enabled` is toggled, regardless of Inspector setup.
        if (doorToPreviousArea != null) doorToPreviousArea.carving = true;
        if (doorToNextArea != null) doorToNextArea.carving = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (hasTriggered) return;

        Character character = other.GetComponentInParent<Character>();
        if (character == null || DungeonManager.Instance == null) return;
        if (!DungeonManager.Instance.party.Contains(character)) return;

        hasTriggered = true;
        RestParty();
        ShowContinuePrompt();
    }

    private void RestParty()
    {
        foreach (Character member in DungeonManager.Instance.party)
        {
            if (!member.isDead) member.FullyRestore();
        }
    }

    private void ShowContinuePrompt()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<InputSystemUIInputModule>();
        }

        promptCanvasGO = new GameObject("RestRoomCanvas");
        Canvas canvas = promptCanvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        promptCanvasGO.AddComponent<CanvasScaler>();
        promptCanvasGO.AddComponent<GraphicRaycaster>();

        Vector2 center = new Vector2(0.5f, 0.5f);
        UIButtonFactory.Build(promptCanvasGO.transform, "Continue?", center, center, center,
            new Vector2(0, -150), new Vector2(200, 50), new Color(0.2f, 0.4f, 0.6f), OnContinueClicked);
    }

    private void OnContinueClicked()
    {
        if (doorToPreviousArea != null) doorToPreviousArea.enabled = true;
        if (doorToNextArea != null) doorToNextArea.enabled = false;

        if (promptCanvasGO != null) Destroy(promptCanvasGO);
    }
}
