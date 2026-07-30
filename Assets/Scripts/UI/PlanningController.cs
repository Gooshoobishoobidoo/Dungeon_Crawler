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

    private GameObject canvasGO;
    private PartyBarUI partyBar;
    private AbilityBarUI abilityBar;
    private InventoryBarUI inventoryBar;
    private Button endPlanningButton;
    private Button fleeButton;
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
        // Standalone combat-test scenes (no DungeonManager) always show this UI, matching the
        // original single-encounter workflow. Everywhere else, this whole panel - including the
        // shared InventoryBarUI and the End Planning/Flee buttons - must be fully hidden outside
        // Combat mode, not just have its logic gated: a merely-inactive-but-still-visible combat
        // UI sits at the same screen position as ExplorationController's own panels and can
        // silently intercept clicks meant for them, and a stale reachable End Planning button
        // can restart an old encounter's ExecutionPhase using whatever enemies it last had.
        bool inCombat = DungeonManager.Instance == null || DungeonManager.Instance.currentMode == GameMode.Combat;
        if (canvasGO != null) canvasGO.SetActive(inCombat);
        if (!inCombat) return;

        if (CombatManager.Instance != null && CombatManager.Instance.CombatEnded)
        {
            if (endPlanningButton != null) endPlanningButton.interactable = false;
            if (fleeButton != null) fleeButton.interactable = false;
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

        // Ignore triggers - see ExplorationController for why (a tall trigger volume like
        // RestRoomTransition's would otherwise intercept clicks meant for the ground/a character).
        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return;

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
        planned.itemToUse = null;
        Debug.Log($"{ActiveCharacter.data.characterName} will use {pendingAbility.abilityName}.");

        mode = TargetMode.AwaitingMove;
        pendingAbility = null;
    }

    private PlannedAction EnsurePlannedAction(Character c)
    {
        // A fresh PlannedAction's moveDestination defaults to Vector3.zero (world origin) -
        // targeting an ability without first clicking a move would otherwise walk the
        // character to (0,0,0). Default to holding position instead.
        if (c.plannedAction == null)
            c.plannedAction = new PlannedAction { moveDestination = c.transform.position };
        return c.plannedAction;
    }

    public void SelectCharacter(Character c)
    {
        ActiveCharacter = c;
        mode = TargetMode.AwaitingMove;
        pendingAbility = null;
        abilityBar?.Show(c);
        inventoryBar?.Show(c, OnUseItemSelected);
    }

    public void SelectAbility(Ability ability)
    {
        if (ActiveCharacter == null || ability == null) return;

        if (ability.abilityType == AbilityType.Self)
        {
            PlannedAction planned = EnsurePlannedAction(ActiveCharacter);
            planned.ability = ability;
            planned.itemToUse = null;
            planned.abilityTarget = ActiveCharacter.transform.position;
            planned.targetCharacter = null;
            Debug.Log($"{ActiveCharacter.data.characterName} will use {ability.abilityName} on self.");
            return;
        }

        mode = TargetMode.AwaitingAbilityTarget;
        pendingAbility = ability;
        Debug.Log($"Select a target for {ability.abilityName}.");
    }

    // No targeting step - items are self-only for now. Mutually exclusive with an ability,
    // same one-action-per-turn constraint everything else in Planning follows.
    private void OnUseItemSelected(Character character, ItemData item)
    {
        PlannedAction planned = EnsurePlannedAction(character);
        planned.itemToUse = item;
        planned.ability = null;
        mode = TargetMode.AwaitingMove;
        pendingAbility = null;
        Debug.Log($"{character.data.characterName} will use {item.itemName}.");
    }

    private void OnEndPlanningClicked()
    {
        if (CombatManager.Instance != null && CombatManager.Instance.AllPlayersReady())
            CombatManager.Instance.OnPlanningComplete();
    }

    private void OnFleeClicked()
    {
        CombatManager.Instance?.Flee();
    }

    private void BuildUI()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<InputSystemUIInputModule>();
        }

        canvasGO = new GameObject("PlanningCanvas");
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

        var inventoryBarGO = new GameObject("InventoryBar");
        inventoryBarGO.transform.SetParent(canvasGO.transform, false);
        inventoryBar = inventoryBarGO.AddComponent<InventoryBarUI>();
        inventoryBar.Build();

        Vector2 bottomRight = new Vector2(1, 0);
        Vector2 buttonSize = new Vector2(160, 40);

        endPlanningButton = UIButtonFactory.Build(canvasGO.transform, "End Planning",
            bottomRight, bottomRight, bottomRight, new Vector2(-20, 20), buttonSize,
            new Color(0.2f, 0.6f, 0.2f), OnEndPlanningClicked);
        fleeButton = UIButtonFactory.Build(canvasGO.transform, "Flee",
            bottomRight, bottomRight, bottomRight, new Vector2(-190, 20), buttonSize,
            new Color(0.6f, 0.2f, 0.2f), OnFleeClicked);
    }
}
