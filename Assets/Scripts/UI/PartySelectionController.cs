using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Party-selection screen shown before a run starts (DungeonManager.currentMode is
// GameMode.PartySelection, the scene's starting state). Every candidate is a pre-placed Character
// GameObject - same pattern DungeonManager.party has always used - rather than something
// instantiated from a prefab, since no CharacterData asset has characterPrefab assigned.
public class PartySelectionController : MonoBehaviour
{
    [Header("Candidates")]
    public List<Character> candidates = new List<Character>();

    private readonly HashSet<Character> selected = new HashSet<Character>();
    private readonly Dictionary<Character, Image> cardBackgrounds = new Dictionary<Character, Image>();

    private GameObject canvasGO;
    private Button beginButton;

    private static readonly Color UnselectedColor = new Color(0.15f, 0.15f, 0.15f, 0.9f);
    private static readonly Color SelectedColor = new Color(0.2f, 0.45f, 0.2f, 0.9f);

    private void Awake()
    {
        // Deactivate every candidate before their own Start() can run - Unity calls every active
        // object's Awake() before any Start(), so this reliably keeps Character.Start() (which
        // calls InitializeFromData()) from firing on anyone not chosen, regardless of GameObject
        // ordering in the hierarchy. Reading c.data below still works fine either way since data
        // is a plain field reference, unaffected by active state.
        foreach (Character c in candidates)
        {
            if (c != null) c.gameObject.SetActive(false);
        }
    }

    private void Start()
    {
        BuildUI();
    }

    private void ToggleCandidate(Character c)
    {
        if (selected.Contains(c)) selected.Remove(c);
        else selected.Add(c);

        if (cardBackgrounds.TryGetValue(c, out Image background))
            background.color = selected.Contains(c) ? SelectedColor : UnselectedColor;

        beginButton.interactable = selected.Count > 0;
    }

    private void OnBeginClicked()
    {
        if (selected.Count == 0 || DungeonManager.Instance == null) return;

        List<Character> chosen = new List<Character>(selected);
        foreach (Character c in chosen) c.gameObject.SetActive(true);

        DungeonManager.Instance.BeginRun(chosen);
        canvasGO.SetActive(false);
    }

    private void BuildUI()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<InputSystemUIInputModule>();
        }

        canvasGO = new GameObject("PartySelectionCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        var titleGO = new GameObject("Title");
        titleGO.transform.SetParent(canvasGO.transform, false);
        var titleRect = titleGO.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0, -40);
        titleRect.sizeDelta = new Vector2(600, 60);
        var title = titleGO.AddComponent<Text>();
        title.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        title.fontSize = 28;
        title.alignment = TextAnchor.MiddleCenter;
        title.color = Color.white;
        title.text = "Choose Your Party";

        var rowGO = new GameObject("CandidateRow");
        rowGO.transform.SetParent(canvasGO.transform, false);
        var rowRect = rowGO.AddComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0.5f, 0.5f);
        rowRect.anchorMax = new Vector2(0.5f, 0.5f);
        rowRect.pivot = new Vector2(0.5f, 0.5f);
        rowRect.anchoredPosition = Vector2.zero;
        var layout = rowGO.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 20;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        rowGO.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        foreach (Character c in candidates)
        {
            if (c != null) BuildCandidateCard(rowGO.transform, c);
        }

        beginButton = UIButtonFactory.Build(canvasGO.transform, "Begin",
            new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 40),
            new Vector2(200, 50), new Color(0.2f, 0.6f, 0.2f), OnBeginClicked);
        beginButton.interactable = false;
    }

    private void BuildCandidateCard(Transform parent, Character c)
    {
        var cardGO = new GameObject($"Candidate_{c.data.characterName}");
        cardGO.transform.SetParent(parent, false);
        cardGO.AddComponent<RectTransform>().sizeDelta = new Vector2(160, 200);

        var background = cardGO.AddComponent<Image>();
        background.color = UnselectedColor;
        cardBackgrounds[c] = background;

        var button = cardGO.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(() => ToggleCandidate(c));

        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(cardGO.transform, false);
        var labelRect = labelGO.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        var label = labelGO.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 16;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.text = $"{c.data.characterName}\n({c.data.characterClass})";
    }
}
