using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class Character : MonoBehaviour
{
    [Header("Data")]
    public CharacterData data;

    [Header("Runtime Stats")]
    public int currentHealth;
    public int currentMana;
    public int currentStamina;

    [Header("Inventory")]
    public List<ItemData> inventory = new List<ItemData>();

    [Header("State")]
    public bool isPlayerControlled;
    public bool isDead;
    public bool hasActedThisTurn;
    public bool isMoving;
    public bool isBusy; // channeling an item's use time - ExplorationController won't move this character

    // NonSerialized: Unity can't represent null for an embedded [Serializable] class field -
    // it silently replaces null with a default instance the moment the Inspector touches this
    // component, which broke plannedAction-based readiness checks. This must stay pure runtime state.
    [System.NonSerialized]
    public PlannedAction plannedAction;

    private NavMeshAgent agent;

    // Per-ability, not per-character - each ability ticks down independently using its own
    // cooldown value, rather than one shared value that used to make every ability on a
    // character reflect whichever one was used last.
    private readonly Dictionary<Ability, float> abilityCooldowns = new Dictionary<Ability, float>();

    // Fractional regen banked between frames - mana/stamina are ints but regen rates are floats,
    // so a whole point is only actually granted once enough partial frames add up to one.
    private float manaRegenAccumulator;
    private float staminaRegenAccumulator;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();

        // NavMeshAgent moves the transform directly, not through physics, so without a
        // Rigidbody here Unity treats this character as a static collider and never fires
        // OnTrigger events for it (e.g. RestRoomTransition never noticing the party walk in) -
        // even though it's visibly moving. Kinematic keeps it out of the physics simulation
        // entirely; this only exists to make trigger detection work.
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    private void Start()
    {
        InitializeFromData();
    }

    private void Update()
    {
        if (isMoving && agent != null)
        {
            if (!agent.pathPending &&
                agent.remainingDistance <= agent.stoppingDistance + 0.1f &&
                (!agent.hasPath || agent.velocity.sqrMagnitude < 0.01f))
            {
                isMoving = false;
                agent.ResetPath();
            }
        }

        // Cooldowns run in real time whenever real time is actually passing: during free-roam
        // exploration, or while a turn is executing. They freeze during Planning (the player
        // thinking, not gameplay time passing) - and, since CombatManager.currentPhase just
        // stays wherever it last was once a fight ends, they'd otherwise stay frozen forever
        // after every encounter instead of resuming once back in Exploration.
        bool exploring = DungeonManager.Instance != null && DungeonManager.Instance.currentMode == GameMode.Exploration;
        bool executingTurn = CombatManager.Instance != null && CombatManager.Instance.currentPhase == CombatPhase.Execution;

        if (exploring || executingTurn)
        {
            // Copy the keys first - ticking an entry down doesn't remove it, but this mirrors
            // the same "don't mutate a collection you're iterating" care taken elsewhere in this
            // codebase (see PlanningController's phase-gating around plannedAction.queue).
            List<Ability> onCooldown = new List<Ability>(abilityCooldowns.Keys);
            foreach (Ability ability in onCooldown)
            {
                if (abilityCooldowns[ability] > 0)
                    abilityCooldowns[ability] = Mathf.Max(0, abilityCooldowns[ability] - Time.deltaTime);
            }
        }

        // Passive regen ticks under the exact same condition as cooldowns above - real gameplay
        // time passing, not the player thinking during Planning.
        if (!isDead && data != null && (exploring || executingTurn))
        {
            manaRegenAccumulator += data.manaRegenPerSecond * Time.deltaTime;
            while (manaRegenAccumulator >= 1f)
            {
                RestoreMana(1);
                manaRegenAccumulator -= 1f;
            }

            // Stamina specifically pauses while actively moving (mana doesn't) - isMoving is
            // shared by both ExplorationController's free-roam movement and combat's queued
            // Move, so this covers both without touching either controller. Regen simply resumes
            // where it left off once movement stops - the accumulator itself isn't reset here.
            if (!isMoving)
            {
                staminaRegenAccumulator += data.staminaRegenPerSecond * Time.deltaTime;
                while (staminaRegenAccumulator >= 1f)
                {
                    RestoreStamina(1);
                    staminaRegenAccumulator -= 1f;
                }
            }
        }
    }

    public void InitializeFromData()
    {
        if (data == null)
        {
            Debug.LogWarning($"{name} has no CharacterData assigned!");
            return;
        }

        currentHealth = data.maxHealth;
        currentMana = data.maxMana;
        currentStamina = data.maxStamina;
        abilityCooldowns.Clear();
        isDead = false;
        hasActedThisTurn = false;

        // Sync NavMesh Agent speed with character data
        if (agent != null)
            agent.speed = data.speed;
    }

    public void MoveTo(Vector3 destination)
    {
        if (agent == null) return;
        agent.SetDestination(destination);
        isMoving = true;
    }

    public void StopMoving()
    {
        if (agent != null) agent.ResetPath();
        isMoving = false;
    }

    public void TakeDamage(int amount)
    {
        int mitigated = Mathf.Max(0, amount - data.defense);
        currentHealth -= mitigated;
        Debug.Log($"{data.characterName} took {mitigated} damage. HP: {currentHealth}/{data.maxHealth}");

        if (currentHealth <= 0)
        {
            currentHealth = 0;
            Die();
        }
    }

    public void SpendMana(int amount)
    {
        currentMana = Mathf.Max(0, currentMana - amount);
    }

    public void RestoreMana(int amount)
    {
        currentMana = Mathf.Min(data.maxMana, currentMana + amount);
    }

    public void SpendStamina(int amount)
    {
        currentStamina = Mathf.Max(0, currentStamina - amount);
    }

    public void RestoreStamina(int amount)
    {
        currentStamina = Mathf.Min(data.maxStamina, currentStamina + amount);
    }

    public void Heal(int amount)
    {
        currentHealth = Mathf.Min(data.maxHealth, currentHealth + amount);
    }

    public float GetAbilityCooldown(Ability ability)
    {
        return abilityCooldowns.TryGetValue(ability, out float remaining) ? remaining : 0f;
    }

    public bool IsAbilityOnCooldown(Ability ability)
    {
        return GetAbilityCooldown(ability) > 0f;
    }

    public void SetAbilityCooldown(Ability ability, float seconds)
    {
        abilityCooldowns[ability] = seconds;
    }

    // Used by rest points between dungeon areas. Doesn't revive the dead - callers are
    // expected to only invoke this on living party members, same as everywhere else in the
    // codebase that operates on the whole party.
    public void FullyRestore()
    {
        currentHealth = data.maxHealth;
        currentMana = data.maxMana;
        currentStamina = data.maxStamina;
        abilityCooldowns.Clear();
    }

    private void Die()
    {
        isDead = true;

        // Has to happen before disabling the agent below - Update()'s arrival check reads
        // agent.remainingDistance whenever isMoving is true, which throws once the agent is no
        // longer active on a NavMesh. A character dying mid-move (the common case - dying is
        // usually the result of getting hit while dodging) would otherwise spam that error every
        // frame for the rest of the scene's life, since nothing else ever clears isMoving again.
        isMoving = false;
        if (agent != null) agent.enabled = false;

        Debug.Log($"{data.characterName} has died.");
    }

    // Exploration-mode item use: self-contained and fire-and-forget from the caller's side.
    // The item is removed the instant use starts - you're committed, so dying or getting
    // interrupted mid-channel wastes it, which is the point of useTime as a risk/cost.
    public void UseItem(ItemData item)
    {
        if (item == null || !inventory.Contains(item)) return;
        inventory.Remove(item);
        StartCoroutine(UseItemRoutine(item));
    }

    private IEnumerator UseItemRoutine(ItemData item)
    {
        isBusy = true;
        yield return new WaitForSeconds(item.useTime);
        if (!isDead) item.ApplyTo(this);
        isBusy = false;
    }
}

public enum QueuedActionType
{
    Move,
    Ability,
    Item,
    Rest,
    Focus,
    Pass
}

// One entry in a turn's action queue - a move, an ability use, or an item use - carrying
// whatever target info that entry needs, since different queued entries can aim at different
// things (or nothing, for Move).
[System.Serializable]
public class QueuedAction
{
    public QueuedActionType type;
    public Ability ability;
    public ItemData item;
    public Vector3 target; // move destination, ground point (AreaOfEffect), or unit position
    public Character targetCharacter; // UnitTarget abilities only
    public Vector3 direction; // normalized aim direction - Skillshot abilities only
    public float duration; // channel length - Rest/Focus only

    // Enemy AI only, never set by player-facing queueing - lets this Move/ability track a
    // dodging target within CharacterData.chaseLeashDistance. chaseAnchor is the target's
    // position at the moment this entry was planned; the shift is always measured from here.
    public Character chaseTarget;
    public Vector3 chaseAnchor;
    public float chaseRange; // Move only - the ability range this Move was closing distance for

    public string DisplayName => type switch
    {
        QueuedActionType.Move => "Move",
        QueuedActionType.Ability => ability != null ? ability.abilityName : "?",
        QueuedActionType.Item => item != null ? item.itemName : "?",
        QueuedActionType.Rest => $"Rest ({duration:F1}s)",
        QueuedActionType.Focus => $"Focus ({duration:F1}s)",
        QueuedActionType.Pass => "Pass",
        _ => "?"
    };
}

[System.Serializable]
public class PlannedAction
{
    public List<QueuedAction> queue = new List<QueuedAction>();
}