using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Hidden until DungeonManager.GameOver() calls Show() on a full party wipe. Return to Town
// reloads the active scene outright rather than hand-resetting every subsystem (loot, killed
// enemies, opened doors) - trivially correct since all of that is just scene state.
public class GameOverUI : MonoBehaviour
{
    public static GameOverUI Instance { get; private set; }

    private GameObject canvasGO;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        BuildUI();
    }

    public void Show()
    {
        canvasGO?.SetActive(true);
    }

    private void OnReturnToTownClicked()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void BuildUI()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<InputSystemUIInputModule>();
        }

        canvasGO = new GameObject("GameOverCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        var dimGO = new GameObject("Dim");
        dimGO.transform.SetParent(canvasGO.transform, false);
        var dimRect = dimGO.AddComponent<RectTransform>();
        dimRect.anchorMin = Vector2.zero;
        dimRect.anchorMax = Vector2.one;
        dimRect.offsetMin = Vector2.zero;
        dimRect.offsetMax = Vector2.zero;
        var dimImage = dimGO.AddComponent<Image>();
        dimImage.color = new Color(0, 0, 0, 0.75f);

        var messageGO = new GameObject("Message");
        messageGO.transform.SetParent(canvasGO.transform, false);
        var messageRect = messageGO.AddComponent<RectTransform>();
        messageRect.anchorMin = new Vector2(0.5f, 0.5f);
        messageRect.anchorMax = new Vector2(0.5f, 0.5f);
        messageRect.pivot = new Vector2(0.5f, 0.5f);
        messageRect.anchoredPosition = new Vector2(0, 40);
        messageRect.sizeDelta = new Vector2(600, 80);
        var message = messageGO.AddComponent<Text>();
        message.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        message.fontSize = 32;
        message.alignment = TextAnchor.MiddleCenter;
        message.color = Color.white;
        message.text = "Game Over";

        UIButtonFactory.Build(canvasGO.transform, "Return to Town",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -40),
            new Vector2(220, 50), new Color(0.5f, 0.2f, 0.2f), OnReturnToTownClicked);

        canvasGO.SetActive(false);
    }
}
