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

    [Header("State")]
    public bool isPlayerControlled;
    public bool isDead;
    public bool hasActedThisTurn;
    public bool isMoving;

    // NonSerialized: Unity can't represent null for an embedded [Serializable] class field -
    // it silently replaces null with a default instance the moment the Inspector touches this
    // component, which broke plannedAction-based readiness checks. This must stay pure runtime state.
    [System.NonSerialized]
    public PlannedAction plannedAction;

    private NavMeshAgent agent;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
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

        // Cooldowns run in real time, but only while a turn is actually executing - Planning
        // is the player thinking, not gameplay time passing.
        if (currentCooldown > 0 && CombatManager.Instance != null &&
            CombatManager.Instance.currentPhase == CombatPhase.Execution)
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

    private void Die()
    {
        isDead = true;
        if (agent != null) agent.enabled = false;
        Debug.Log($"{data.characterName} has died.");
    }
}

[System.Serializable]
public class PlannedAction
{
    public Vector3 moveDestination;
    public Ability ability;
    public Vector3 abilityTarget;
    public Character targetCharacter;
}