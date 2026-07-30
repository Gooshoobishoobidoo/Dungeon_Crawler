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
        // Standalone combat-test scenes (no DungeonManager) still auto-start on load, same as
        // before. Scenes driven by DungeonManager wait for BeginEncounter() instead.
        if (DungeonManager.Instance == null)
            StartPlanningPhase();
    }

    // Called by DungeonManager when an enemy notices the party (or the party walks up to an
    // already-alerted one). Resets CombatEnded/turnNumber since one CombatManager now runs
    // multiple encounters back to back instead of just one for the whole scene.
    public void BeginEncounter(List<Character> players, List<Character> enemies)
    {
        playerCharacters = players;
        enemyCharacters = enemies;
        CombatEnded = false;
        turnNumber = 0;
        StartPlanningPhase();
    }

    public void Flee()
    {
        Debug.Log("Party flees the encounter.");
        CombatEnded = true;
        DungeonManager.Instance?.ReturnToExploration();
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
        AssignEnemyActions();
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

    // Set once CheckCombatEnd finds a winner. Nothing clears plannedAction/cooldowns after
    // combat ends (StartPlanningPhase, the only place that does, never runs again), so the
    // planning UI needs this to know to stop accepting input rather than replaying stale orders.
    public bool CombatEnded { get; private set; }

    // Every living enemy: pick the nearest living opponent, use the best ability currently
    // usable against them if already in range, otherwise close the distance.
    private void AssignEnemyActions()
    {
        foreach (Character c in enemyCharacters)
        {
            if (c.isDead) continue;

            Character target = SelectNearestTarget(c, playerCharacters);
            if (target == null) continue;

            Ability ability = ChooseBestAbility(c, target);
            float distanceToTarget = Vector3.Distance(c.transform.position, target.transform.position);

            if (ability != null && distanceToTarget <= ability.range)
            {
                c.plannedAction = BuildAttackAction(c, ability, target);
            }
            else if (ability != null)
            {
                c.plannedAction = BuildApproachAction(c, target, ability.range);
            }
            else
            {
                c.plannedAction = BuildApproachAction(c, target, 0f);
            }
        }
    }

    // Isolated so different enemy types can plug in other priorities later (e.g. lowest HP)
    // without touching the rest of the decision flow.
    private Character SelectNearestTarget(Character c, List<Character> candidates)
    {
        Character nearest = null;
        float nearestDistance = float.MaxValue;

        foreach (Character candidate in candidates)
        {
            if (candidate.isDead) continue;
            float distance = Vector3.Distance(c.transform.position, candidate.transform.position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = candidate;
            }
        }

        return nearest;
    }

    // Highest-damage ability the character can currently afford and isn't on cooldown.
    // Self abilities are excluded - there's no self-buff/heal AI logic yet.
    private Ability ChooseBestAbility(Character c, Character target)
    {
        if (c.data.abilities == null || c.currentCooldown > 0) return null;

        Ability best = null;
        foreach (Ability ability in c.data.abilities)
        {
            if (ability == null || ability.abilityType == AbilityType.Self) continue;
            if (c.currentMana < ability.manaCost || c.currentStamina < ability.staminaCost) continue;
            if (best == null || ability.damage > best.damage) best = ability;
        }

        return best;
    }

    private PlannedAction BuildAttackAction(Character c, Ability ability, Character target)
    {
        return new PlannedAction
        {
            moveDestination = c.transform.position,
            ability = ability,
            abilityTarget = target.transform.position,
            targetCharacter = ability.abilityType == AbilityType.UnitTarget ? target : null
        };
    }

    // Moves toward target, stopping ~90% of the way to keepRange (0 = walk all the way there).
    private PlannedAction BuildApproachAction(Character c, Character target, float keepRange)
    {
        Vector3 toTarget = target.transform.position - c.transform.position;
        float distance = toTarget.magnitude;
        float travel = Mathf.Max(0, distance - keepRange * 0.9f);
        Vector3 destination = c.transform.position + toTarget.normalized * travel;

        return new PlannedAction
        {
            moveDestination = destination,
            ability = null,
            abilityTarget = Vector3.zero,
            targetCharacter = null
        };
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
            CombatEnded = true;
            // TODO: trigger game over screen
            DungeonManager.Instance?.GameOver();
            return;
        }

        if (allEnemiesDead)
        {
            Debug.Log("All enemies dead - Combat Won!");
            CombatEnded = true;
            // TODO: trigger victory screen
            DungeonManager.Instance?.ReturnToExploration();
            return;
        }

        // Combat continues, start next planning phase
        StartPlanningPhase();
    }
}