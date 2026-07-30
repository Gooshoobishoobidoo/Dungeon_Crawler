using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public enum TargetMode
{
    AwaitingMove,
    AwaitingAbilityTarget
}

// Root of the planning-phase UI. Builds its own Canvas/EventSystem/party bar/ability bar
// at runtime, then translates mouse clicks into orders on Character.plannedAction.
public class PlanningController : MonoBehaviour
{
    public static PlanningController Instance { get; private set; }

    public Character ActiveCharacter { get; private set; }

    private PartyBarUI partyBar;
    private AbilityBarUI abilityBar;
    private Button endPlanningButton;
    private Camera cam;

    private TargetMode mode = TargetMode.AwaitingMove;
    private Ability pendingAbility;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        cam = Camera.main;
        BuildUI();
    }

    private void Update()
    {
        if (CombatManager.Instance != null && CombatManager.Instance.CombatEnded)
        {
            if (endPlanningButton != null) endPlanningButton.interactable = false;
            return;
        }

        if (endPlanningButton != null)
            endPlanningButton.interactable = CombatManager.Instance != null && CombatManager.Instance.AllPlayersReady();

        partyBar?.Refresh();
        HandleWorldClick();
    }

    private void HandleWorldClick()
    {
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        if (ActiveCharacter == null || cam == null) return;

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f)) return;

        if (mode == TargetMode.AwaitingAbilityTarget && pendingAbility != null)
        {
            ResolveAbilityTarget(hit);
        }
        else
        {
            EnsurePlannedAction(ActiveCharacter).moveDestination = hit.point;
            Debug.Log($"{ActiveCharacter.data.characterName} will move to {hit.point}.");
        }
    }

    private void ResolveAbilityTarget(RaycastHit hit)
    {
        float distance = Vector3.Distance(ActiveCharacter.transform.position, hit.point);
        if (distance > pendingAbility.range)
        {
            Debug.LogWarning($"{pendingAbility.abilityName} target is out of range ({distance:F1} > {pendingAbility.range}).");
            return;
        }

        PlannedAction planned = EnsurePlannedAction(ActiveCharacter);

        if (pendingAbility.abilityType == AbilityType.UnitTarget)
        {
            Character hitCharacter = hit.collider.GetComponentInParent<Character>();
            if (hitCharacter == null)
            {
                Debug.LogWarning($"{pendingAbility.abilityName} requires a character target.");
                return;
            }

            if (hitCharacter == ActiveCharacter)
            {
                Debug.LogWarning($"{pendingAbility.abilityName} can't target yourself - use a Self ability for that.");
                return;
            }

            if (hitCharacter.isDead)
            {
                Debug.LogWarning($"{pendingAbility.abilityName} can't target a defeated character.");
                return;
            }

            planned.targetCharacter = hitCharacter;
            planned.abilityTarget = hitCharacter.transform.position;
        }
        else
        {
            planned.abilityTarget = hit.point;
            planned.targetCharacter = null;
        }

        planned.ability = pendingAbility;
        Debug.Log($"{ActiveCharacter.data.characterName} will use {pendingAbility.abilityName}.");

        mode = TargetMode.AwaitingMove;
        pendingAbility = null;
    }

    private PlannedAction EnsurePlannedAction(Character c)
    {
        if (c.plannedAction == null) c.plannedAction = new PlannedAction();
        return c.plannedAction;
    }

    public void SelectCharacter(Character c)
    {
        ActiveCharacter = c;
        mode = TargetMode.AwaitingMove;
        pendingAbility = null;
        abilityBar?.Show(c);
    }

    public void SelectAbility(Ability ability)
    {
        if (ActiveCharacter == null || ability == null) return;

        if (ability.abilityType == AbilityType.Self)
        {
            PlannedAction planned = EnsurePlannedAction(ActiveCharacter);
            planned.ability = ability;
            planned.abilityTarget = ActiveCharacter.transform.position;
            planned.targetCharacter = null;
            Debug.Log($"{ActiveCharacter.data.characterName} will use {ability.abilityName} on self.");
            return;
        }

        mode = TargetMode.AwaitingAbilityTarget;
        pendingAbility = ability;
        Debug.Log($"Select a target for {ability.abilityName}.");
    }

    private void OnEndPlanningClicked()
    {
        if (CombatManager.Instance != null && CombatManager.Instance.AllPlayersReady())
            CombatManager.Instance.OnPlanningComplete();
    }

    private void BuildUI()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<InputSystemUIInputModule>();
        }

        var canvasGO = new GameObject("PlanningCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        var partyBarGO = new GameObject("PartyBar");
        partyBarGO.transform.SetParent(canvasGO.transform, false);
        partyBar = partyBarGO.AddComponent<PartyBarUI>();
        partyBar.Build();

        var abilityBarGO = new GameObject("AbilityBar");
        abilityBarGO.transform.SetParent(canvasGO.transform, false);
        abilityBar = abilityBarGO.AddComponent<AbilityBarUI>();
        abilityBar.Build();

        BuildEndPlanningButton(canvasGO.transform);
    }

    private void BuildEndPlanningButton(Transform parent)
    {
        var buttonGO = new GameObject("EndPlanningButton");
        buttonGO.transform.SetParent(parent, false);

        var rect = buttonGO.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(1, 0);
        rect.anchorMax = new Vector2(1, 0);
        rect.pivot = new Vector2(1, 0);
        rect.anchoredPosition = new Vector2(-20, 20);
        rect.sizeDelta = new Vector2(160, 40);

        var image = buttonGO.AddComponent<Image>();
        image.color = new Color(0.2f, 0.6f, 0.2f);

        endPlanningButton = buttonGO.AddComponent<Button>();
        endPlanningButton.targetGraphic = image;
        endPlanningButton.onClick.AddListener(OnEndPlanningClicked);

        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(buttonGO.transform, false);
        var labelRect = labelGO.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        var label = labelGO.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.text = "End Planning";
    }
}
