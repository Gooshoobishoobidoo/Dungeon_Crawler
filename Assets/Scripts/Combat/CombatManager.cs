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

        // Reset all characters for the new turn
        foreach (Character c in playerCharacters)
            c.hasActedThisTurn = false;

        foreach (Character c in enemyCharacters)
            c.hasActedThisTurn = false;

        // TODO: trigger Planning UI here
        // TODO: trigger Enemy AI planning here
    }

    // Called by UI when all player characters have been assigned actions
    public void OnPlanningComplete()
    {
        Debug.Log("Planning complete, starting execution...");
        StartCoroutine(ExecutionPhase());
    }

    // -------------------------
    // EXECUTION PHASE
    // -------------------------

    private IEnumerator ExecutionPhase()
    {
        currentPhase = CombatPhase.Execution;
        Debug.Log("--- Execution Phase ---");

        List<Character> allCharacters = new List<Character>();
        allCharacters.AddRange(playerCharacters);
        allCharacters.AddRange(enemyCharacters);
        allCharacters.Sort((a, b) => b.data.speed.CompareTo(a.data.speed));

        // Start all characters moving simultaneously
        foreach (Character c in allCharacters)
        {
            if (c.isDead || c.plannedAction == null) continue;
            c.MoveTo(c.plannedAction.moveDestination);
        }

        // Wait until all characters have finished moving
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
            yield return null;
        }

        Debug.Log("All characters finished moving.");

        // TODO: trigger abilities after movement
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
        Debug.Log($"{c.data.characterName} uses {(c.plannedAction.ability != null ? c.plannedAction.ability.abilityName : "no ability")}.");
        c.hasActedThisTurn = true;
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