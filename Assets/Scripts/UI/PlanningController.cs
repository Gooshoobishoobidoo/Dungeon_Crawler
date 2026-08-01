using System.Collections.Generic;
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
    private QueueDisplayUI queueDisplay;
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
        if (!IsPlanning()) return;
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
            QueueMove(ActiveCharacter, hit.point);
        }
    }

    // Clicking the ground repeatedly just updates where you're headed next (same feel as
    // before the queue existed) - only queuing something else in between produces a second,
    // deliberate Move entry later in the queue.
    private void QueueMove(Character c, Vector3 destination)
    {
        if (!IsPlanning()) return;

        if (HasPassQueued(c))
        {
            Debug.LogWarning($"{c.data.characterName} is passing this turn - remove Pass before queuing a move.");
            return;
        }

        PlannedAction planned = EnsurePlannedAction(c);

        if (planned.queue.Count > 0 && planned.queue[planned.queue.Count - 1].type == QueuedActionType.Move)
        {
            planned.queue[planned.queue.Count - 1].target = destination;
        }
        else
        {
            planned.queue.Add(new QueuedAction { type = QueuedActionType.Move, target = destination });
        }

        Debug.Log($"{c.data.characterName} will move to {destination}.");
    }

    // Where a character will actually be by the time a newly-queued action would resolve -
    // the destination of the last Move already in their queue, or their current position if
    // they haven't queued one. Since abilities are always appended at the end, this is exactly
    // the position range should be checked against (otherwise queuing Move-then-Slash on a
    // target only in range *after* the move would be incorrectly rejected using where the
    // character stands right now, before Planning even ends).
    private Vector3 GetEffectivePosition(Character c)
    {
        if (c.plannedAction != null)
        {
            for (int i = c.plannedAction.queue.Count - 1; i >= 0; i--)
            {
                if (c.plannedAction.queue[i].type == QueuedActionType.Move)
                    return c.plannedAction.queue[i].target;
            }
        }

        return c.transform.position;
    }

    private void ResolveAbilityTarget(RaycastHit hit)
    {
        Vector3 effectivePosition = GetEffectivePosition(ActiveCharacter);

        // Skillshot's click only ever defines a direction, not an impact location - the beam
        // always travels exactly `range` units that way regardless of how far you clicked, so
        // it skips the "is the clicked point within range" rejection entirely.
        if (pendingAbility.abilityType == AbilityType.Skillshot)
        {
            Vector3 aim = hit.point - effectivePosition;
            if (aim.sqrMagnitude < 0.25f)
            {
                Debug.LogWarning($"{pendingAbility.abilityName} needs a clear direction - click further away.");
                return;
            }

            TryQueueAbility(ActiveCharacter, pendingAbility, effectivePosition, null, aim.normalized);
            mode = TargetMode.AwaitingMove;
            pendingAbility = null;
            return;
        }

        float distance = Vector3.Distance(effectivePosition, hit.point);
        if (distance > pendingAbility.range)
        {
            Debug.LogWarning($"{pendingAbility.abilityName} target is out of range from where {ActiveCharacter.data.characterName} will be ({distance:F1} > {pendingAbility.range}).");
            return;
        }

        Vector3 target;
        Character targetCharacter = null;

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

            targetCharacter = hitCharacter;
            target = hitCharacter.transform.position;
        }
        else
        {
            target = hit.point;
        }

        // Whether this succeeds or gets rejected (already queued / can't afford), a different
        // target click wouldn't change that outcome, so either way we're done targeting.
        TryQueueAbility(ActiveCharacter, pendingAbility, target, targetCharacter);
        mode = TargetMode.AwaitingMove;
        pendingAbility = null;
    }

    // Combat stays in Combat mode for its whole duration (Planning + Execution + Resolution),
    // so the canvas-visibility check in Update() isn't enough to stop queue edits mid-execution -
    // CombatManager is actively foreach-ing this same queue then, and mutating a List while it's
    // being enumerated throws. Every place that mutates plannedAction/queue needs this guard,
    // not just Update()'s world-click handling, since UI button clicks fire independently of it.
    private bool IsPlanning()
    {
        return CombatManager.Instance == null || CombatManager.Instance.currentPhase == CombatPhase.Planning;
    }

    // Rejects (log warning, no change) if this ability is already queued this turn, or if the
    // resources left over after everything already queued can't cover it. Cooldown itself
    // doesn't tick during Planning, so "already queued" is what stops double-queuing around that.
    private bool TryQueueAbility(Character c, Ability ability, Vector3 target, Character targetCharacter, Vector3 direction = default)
    {
        if (!IsPlanning())
        {
            Debug.LogWarning("Can't change actions once execution has started.");
            return false;
        }

        if (HasPassQueued(c))
        {
            Debug.LogWarning($"{c.data.characterName} is passing this turn - remove Pass before queuing anything else.");
            return false;
        }

        PlannedAction planned = EnsurePlannedAction(c);

        if (planned.queue.Exists(q => q.ability == ability))
        {
            Debug.LogWarning($"{ability.abilityName} is already queued this turn.");
            return false;
        }

        int queuedMana = 0;
        int queuedStamina = 0;
        foreach (QueuedAction q in planned.queue)
        {
            if (q.ability == null) continue;
            queuedMana += q.ability.manaCost;
            queuedStamina += q.ability.staminaCost;
        }

        if (c.currentMana - queuedMana < ability.manaCost || c.currentStamina - queuedStamina < ability.staminaCost)
        {
            Debug.LogWarning($"Not enough resources left this turn for {ability.abilityName}.");
            return false;
        }

        planned.queue.Add(new QueuedAction { type = QueuedActionType.Ability, ability = ability, target = target, targetCharacter = targetCharacter, direction = direction });
        Debug.Log($"{c.data.characterName} will use {ability.abilityName}.");
        return true;
    }

    // Rejects if there's no unqueued copy left - "available" is the inventory with already-
    // queued item references removed one-for-one, so duplicate ItemData entries (how stacking
    // already works) naturally support queuing e.g. 2 held potions.
    private bool TryQueueItem(Character c, ItemData item)
    {
        if (!IsPlanning())
        {
            Debug.LogWarning("Can't change actions once execution has started.");
            return false;
        }

        if (HasPassQueued(c))
        {
            Debug.LogWarning($"{c.data.characterName} is passing this turn - remove Pass before queuing anything else.");
            return false;
        }

        PlannedAction planned = EnsurePlannedAction(c);

        List<ItemData> available = new List<ItemData>(c.inventory);
        foreach (QueuedAction q in planned.queue)
        {
            if (q.item != null) available.Remove(q.item);
        }

        if (!available.Contains(item))
        {
            Debug.LogWarning($"No more {item.itemName} available to queue this turn.");
            return false;
        }

        planned.queue.Add(new QueuedAction { type = QueuedActionType.Item, item = item });
        Debug.Log($"{c.data.characterName} will use {item.itemName}.");
        return true;
    }

    // Rest/Focus cost nothing to queue (no mana/stamina/cooldown to check against) - just the
    // phase guard every other queue-mutating entry point uses.
    public bool TryQueueRest(Character c, float duration)
    {
        if (!IsPlanning())
        {
            Debug.LogWarning("Can't change actions once execution has started.");
            return false;
        }

        if (HasPassQueued(c))
        {
            Debug.LogWarning($"{c.data.characterName} is passing this turn - remove Pass before queuing anything else.");
            return false;
        }

        EnsurePlannedAction(c).queue.Add(new QueuedAction { type = QueuedActionType.Rest, duration = duration });
        Debug.Log($"{c.data.characterName} will rest for {duration:F1}s.");
        return true;
    }

    public bool TryQueueFocus(Character c, float duration)
    {
        if (!IsPlanning())
        {
            Debug.LogWarning("Can't change actions once execution has started.");
            return false;
        }

        if (HasPassQueued(c))
        {
            Debug.LogWarning($"{c.data.characterName} is passing this turn - remove Pass before queuing anything else.");
            return false;
        }

        EnsurePlannedAction(c).queue.Add(new QueuedAction { type = QueuedActionType.Focus, duration = duration });
        Debug.Log($"{c.data.characterName} will focus for {duration:F1}s.");
        return true;
    }

    // Pass only makes sense as a character's sole action for the turn - rejected if anything
    // else is already queued, mirroring the reverse guard every other TryQueue* method has
    // against queuing on top of an existing Pass.
    public bool TryQueuePass(Character c)
    {
        if (!IsPlanning())
        {
            Debug.LogWarning("Can't change actions once execution has started.");
            return false;
        }

        PlannedAction planned = EnsurePlannedAction(c);
        if (planned.queue.Count > 0)
        {
            Debug.LogWarning($"{c.data.characterName} already has actions queued this turn - remove them before passing.");
            return false;
        }

        planned.queue.Add(new QueuedAction { type = QueuedActionType.Pass });
        Debug.Log($"{c.data.characterName} will pass this turn.");
        return true;
    }

    private bool HasPassQueued(Character c)
    {
        return c.plannedAction != null && c.plannedAction.queue.Exists(q => q.type == QueuedActionType.Pass);
    }

    public void RemoveQueuedAction(Character c, QueuedAction queuedAction)
    {
        if (!IsPlanning())
        {
            Debug.LogWarning("Can't change actions once execution has started.");
            return;
        }

        c.plannedAction?.queue.Remove(queuedAction);
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
        inventoryBar?.Show(c, OnUseItemSelected);
        queueDisplay?.Show(c);
    }

    public void SelectAbility(Ability ability)
    {
        if (ActiveCharacter == null || ability == null) return;

        if (ability.abilityType == AbilityType.Self)
        {
            TryQueueAbility(ActiveCharacter, ability, GetEffectivePosition(ActiveCharacter), null);
            return;
        }

        mode = TargetMode.AwaitingAbilityTarget;
        pendingAbility = ability;
        Debug.Log($"Select a target for {ability.abilityName}.");
    }

    // No targeting step - items are self-only for now.
    private void OnUseItemSelected(Character character, ItemData item)
    {
        TryQueueItem(character, item);
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
        partyBar.getCharacters = () => CombatManager.Instance?.playerCharacters;
        partyBar.onPortraitClicked = SelectCharacter;

        var abilityBarGO = new GameObject("AbilityBar");
        abilityBarGO.transform.SetParent(canvasGO.transform, false);
        abilityBar = abilityBarGO.AddComponent<AbilityBarUI>();
        abilityBar.Build();

        var inventoryBarGO = new GameObject("InventoryBar");
        inventoryBarGO.transform.SetParent(canvasGO.transform, false);
        inventoryBar = inventoryBarGO.AddComponent<InventoryBarUI>();
        inventoryBar.Build();

        var queueDisplayGO = new GameObject("QueueDisplay");
        queueDisplayGO.transform.SetParent(canvasGO.transform, false);
        queueDisplay = queueDisplayGO.AddComponent<QueueDisplayUI>();
        queueDisplay.Build();

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
