using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// The one required exit from a generated floor - lives inside DungeonGenerator's
// staircaseRoomPrefab, placed at whichever room the generator determined is farthest (by path
// distance) from the start. Modeled on RestRoomTransition's trigger pattern, but simpler: there
// are no doors to seal/open here, since confirming replaces the whole floor rather than gating
// between two fixed zones.
[RequireComponent(typeof(BoxCollider))]
public class StaircaseDown : MonoBehaviour
{
    private bool hasTriggered;
    private GameObject promptCanvasGO;

    private void Awake()
    {
        GetComponent<BoxCollider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (hasTriggered) return;
        if (DungeonManager.Instance == null || DungeonManager.Instance.currentMode != GameMode.Exploration) return;

        Character character = other.GetComponentInParent<Character>();
        if (character == null || !DungeonManager.Instance.party.Contains(character)) return;

        hasTriggered = true;
        ShowDescendPrompt();
    }

    // Dismisses the prompt if the party leaves without confirming, rather than leaving it stuck
    // on screen forever - re-entering afterward shows it again (hasTriggered reset here too).
    private void OnTriggerExit(Collider other)
    {
        if (!hasTriggered) return;

        Character character = other.GetComponentInParent<Character>();
        if (character == null || DungeonManager.Instance == null || !DungeonManager.Instance.party.Contains(character)) return;

        hasTriggered = false;
        if (promptCanvasGO != null) Destroy(promptCanvasGO);
    }

    private void ShowDescendPrompt()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<InputSystemUIInputModule>();
        }

        promptCanvasGO = new GameObject("StaircaseCanvas");
        Canvas canvas = promptCanvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        promptCanvasGO.AddComponent<CanvasScaler>();
        promptCanvasGO.AddComponent<GraphicRaycaster>();

        Vector2 center = new Vector2(0.5f, 0.5f);
        UIButtonFactory.Build(promptCanvasGO.transform, "Descend?", center, center, center,
            new Vector2(0, -150), new Vector2(200, 50), new Color(0.2f, 0.4f, 0.6f), OnDescendClicked);
    }

    private void OnDescendClicked()
    {
        DungeonManager.Instance?.DescendToNextFloor();

        if (promptCanvasGO != null) Destroy(promptCanvasGO);
    }
}
