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
    public float currentCooldown;

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

        if (currentCooldown > 0 && (exploring || executingTurn))
        {
            currentCooldown = Mathf.Max(0, currentCooldown - Time.deltaTime);
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
        currentCooldown = 0f;
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

    // Used by rest points between dungeon areas. Doesn't revive the dead - callers are
    // expected to only invoke this on living party members, same as everywhere else in the
    // codebase that operates on the whole party.
    public void FullyRestore()
    {
        currentHealth = data.maxHealth;
        currentMana = data.maxMana;
        currentStamina = data.maxStamina;
        currentCooldown = 0f;
    }

    private void Die()
    {
        isDead = true;
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

[System.Serializable]
public class PlannedAction
{
    public Vector3 moveDestination;
    public Ability ability;
    public ItemData itemToUse; // mutually exclusive with ability - one action per turn, for now
    public Vector3 abilityTarget;
    public Character targetCharacter;
}