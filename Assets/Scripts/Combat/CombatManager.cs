using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum CombatPhase
{
    Planning,
    Execution,
    Resolution
}

public class CombatManager : MonoBehaviour
{
    public static CombatManager Instance { get; private set; }

    [Header("Characters")]
    public List<Character> playerCharacters = new List<Character>();
    public List<Character> enemyCharacters = new List<Character>();

    [Header("State")]
    public CombatPhase currentPhase = CombatPhase.Planning;
    public int turnNumber = 0;

    [Header("Placeholder Enemy AI")]
    public float enemyMoveRange = 3f; // random move radius until real enemy AI exists

    private void Awake()
    {
        // Singleton pattern - only one CombatManager can exist
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        StartPlanningPhase();
    }

    // -------------------------
    // PLANNING PHASE
    // -------------------------

    public void StartPlanningPhase()
    {
        currentPhase = CombatPhase.Planning;
        turnNumber++;
        Debug.Log($"--- Turn {turnNumber}: Planning Phase ---");

        // Reset all characters for the new turn. Cooldowns are real-time (see Character.Update)
        // and only tick down during Execution, so they're left untouched here.
        foreach (Character c in playerCharacters)
        {
            c.hasActedThisTurn = false;
            c.plannedAction = null;
        }

        foreach (Character c in enemyCharacters)
        {
            c.hasActedThisTurn = false;
            c.plannedAction = null;
        }

        // Real player orders come from PlanningController (Assets/Scripts/UI); it calls
        // OnPlanningComplete() once every living player character has a plannedAction.
        AssignPlaceholderEnemyActions();
    }

    // Called by the planning UI when all player characters have been assigned actions
    public void OnPlanningComplete()
    {
        Debug.Log("Planning complete, starting execution...");
        StartCoroutine(ExecutionPhase());
    }

    public bool AllPlayersReady()
    {
        return playerCharacters.TrueForAll(c => c.isDead || c.plannedAction != null);
    }

    // Placeholder until real enemy AI exists: gives every living enemy a random move so
    // combat stays playable end-to-end. Remove once enemy decision-making is implemented.
    private void AssignPlaceholderEnemyActions()
    {
        foreach (Character c in enemyCharacters)
        {
            if (c.isDead) continue;

            Vector3 randomOffset = new Vector3(
                Random.Range(-enemyMoveRange, enemyMoveRange),
                0f,
                Random.Range(-enemyMoveRange, enemyMoveRange));

            c.plannedAction = new PlannedAction
            {
                moveDestination = c.transform.position + randomOffset,
                ability = null,
                abilityTarget = Vector3.zero,
                targetCharacter = null
            };
        }
    }

    // -------------------------
    // EXECUTION PHASE
    // -------------------------

    private List<Character> AllCharacters()
    {
        List<Character> all = new List<Character>();
        all.AddRange(playerCharacters);
        all.AddRange(enemyCharacters);
        return all;
    }

    private IEnumerator ExecutionPhase()
    {
        currentPhase = CombatPhase.Execution;
        Debug.Log("--- Execution Phase ---");

        List<Character> allCharacters = AllCharacters();
        allCharacters.Sort((a, b) => b.data.speed.CompareTo(a.data.speed));

        // Start all characters moving simultaneously
        foreach (Character c in allCharacters)
        {
            if (c.isDead || c.plannedAction == null) continue;
            c.MoveTo(c.plannedAction.moveDestination);
        }

        // Wait until all characters have finished moving. Agents converging on the same or
        // overlapping destinations can jostle each other indefinitely without ever settling
        // inside their stopping distance, so this is time-boxed rather than unconditional.
        const float moveTimeoutSeconds = 8f;
        float moveTimer = 0f;
        bool allDoneMoving = false;
        while (!allDoneMoving)
        {
            allDoneMoving = true;
            foreach (Character c in allCharacters)
            {
                if (!c.isDead && c.isMoving)
                {
                    allDoneMoving = false;
                    break;
                }
            }

            if (!allDoneMoving)
            {
                moveTimer += Time.deltaTime;
                if (moveTimer >= moveTimeoutSeconds)
                {
                    Debug.LogWarning("Movement timed out - forcing remaining characters to stop.");
                    foreach (Character c in allCharacters)
                    {
                        if (!c.isDead && c.isMoving) c.StopMoving();
                    }
                    break;
                }
            }

            yield return null;
        }

        Debug.Log("All characters finished moving.");

        foreach (Character c in allCharacters)
        {
            if (c.isDead || c.plannedAction == null) continue;
            ExecuteCharacterAction(c);
            yield return new WaitForSeconds(0.5f);
        }

        StartResolutionPhase();
    }

    private void ExecuteCharacterAction(Character c)
    {
        if (c.plannedAction == null) return;

        Ability ability = c.plannedAction.ability;
        if (ability == null)
        {
            Debug.Log($"{c.data.characterName} uses no ability.");
            c.hasActedThisTurn = true;
            return;
        }

        c.SpendMana(ability.manaCost);
        c.SpendStamina(ability.staminaCost);
        c.currentCooldown = ability.cooldown;

        switch (ability.abilityType)
        {
            case AbilityType.UnitTarget:
                ResolveUnitTarget(c, ability);
                break;

            case AbilityType.AreaOfEffect:
            case AbilityType.Skillshot:
                ResolveAreaTarget(c, ability);
                break;

            case AbilityType.Self:
                Debug.Log($"{c.data.characterName} uses {ability.abilityName} on self.");
                c.TakeDamage(ability.damage);
                break;
        }

        c.hasActedThisTurn = true;
    }

    private void ResolveUnitTarget(Character c, Ability ability)
    {
        Character target = c.plannedAction.targetCharacter;
        if (target == null || target.isDead ||
            Vector3.Distance(c.transform.position, target.transform.position) > ability.range)
        {
            Debug.Log($"{c.data.characterName}'s {ability.abilityName} fizzled - target out of range.");
            return;
        }

        Debug.Log($"{c.data.characterName} uses {ability.abilityName} on {target.data.characterName}.");
        target.TakeDamage(ability.damage);
    }

    private void ResolveAreaTarget(Character c, Ability ability)
    {
        Vector3 point = c.plannedAction.abilityTarget;
        if (Vector3.Distance(c.transform.position, point) > ability.range)
        {
            Debug.Log($"{c.data.characterName}'s {ability.abilityName} fizzled - target point out of range.");
            return;
        }

        Debug.Log($"{c.data.characterName} uses {ability.abilityName} at {point}.");
        foreach (Character other in AllCharacters())
        {
            if (other.isDead) continue;
            if (Vector3.Distance(other.transform.position, point) <= ability.aoeRadius)
                other.TakeDamage(ability.damage);
        }
    }

    // -------------------------
    // RESOLUTION PHASE
    // -------------------------

    private void StartResolutionPhase()
    {
        currentPhase = CombatPhase.Resolution;
        Debug.Log("--- Resolution Phase ---");

        // Check for deaths, end of combat, etc.
        CheckCombatEnd();
    }

    private void CheckCombatEnd()
    {
        bool allPlayersDead = playerCharacters.TrueForAll(c => c.isDead);
        bool allEnemiesDead = enemyCharacters.TrueForAll(c => c.isDead);

        if (allPlayersDead)
        {
            Debug.Log("All players dead - Game Over!");
            // TODO: trigger game over screen
            return;
        }

        if (allEnemiesDead)
        {
            Debug.Log("All enemies dead - Combat Won!");
            // TODO: trigger victory screen
            return;
        }

        // Combat continues, start next planning phase
        StartPlanningPhase();
    }
}