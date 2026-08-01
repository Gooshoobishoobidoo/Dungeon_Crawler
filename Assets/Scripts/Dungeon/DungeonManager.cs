using System.Collections.Generic;
using UnityEngine;

// PartySelection is deliberately first: TestScene.unity already has currentMode explicitly
// serialized as 0, so making PartySelection index 0 starts every run there for free without
// needing any scene-file edit for this field.
public enum GameMode
{
    PartySelection,
    Exploration,
    Combat
}

// Top-level coordinator switching between free-roam exploration and CombatManager encounters.
// Owns the persistent party roster so HP/mana/stamina/cooldowns survive across encounters -
// CombatManager only ever sees whichever subset is passed to BeginEncounter.
public class DungeonManager : MonoBehaviour
{
    public static DungeonManager Instance { get; private set; }

    [Header("Party")]
    public List<Character> party = new List<Character>();

    [Header("State")]
    public GameMode currentMode = GameMode.PartySelection;

    // Shared party-wide pool - currency isn't really an "item" any one character carries,
    // unlike everything else in inventory which is per-character.
    public int currency;

    private readonly List<EnemyPatrol> allEnemies = new List<EnemyPatrol>();

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        allEnemies.AddRange(FindObjectsByType<EnemyPatrol>());
    }

    public void OnEnemyAlerted(EnemyPatrol source)
    {
        if (currentMode != GameMode.Exploration) return;

        List<Character> engaging = new List<Character>();
        foreach (EnemyPatrol enemy in allEnemies)
        {
            if (enemy.IsAlerted && !enemy.Character.isDead)
                engaging.Add(enemy.Character);
        }

        if (engaging.Count == 0) return;

        currentMode = GameMode.Combat;
        FreezeExploration();
        CombatManager.Instance.BeginEncounter(LivingParty(), engaging);
    }

    // Stops any movement already in progress the instant combat starts - EnemyPatrol/
    // ExplorationController only stop issuing *new* moves once currentMode != Exploration,
    // they don't cancel one already underway (e.g. a patrolling enemy elsewhere mid-waypoint-walk).
    private void FreezeExploration()
    {
        foreach (Character member in party)
        {
            if (!member.isDead) member.StopMoving();
        }

        foreach (EnemyPatrol enemy in allEnemies)
        {
            if (!enemy.Character.isDead) enemy.Character.StopMoving();
        }
    }

    public void ReturnToExploration()
    {
        currentMode = GameMode.Exploration;

        // Only enemies that were part of the ending encounter can be alerted right now (see
        // OnEnemyAlerted - it gathers every currently-alerted enemy into the same fight), so
        // this is safe to apply broadly rather than tracking "this encounter's enemies" separately.
        foreach (EnemyPatrol enemy in allEnemies)
        {
            if (!enemy.Character.isDead) enemy.ResetAlert();
        }
    }

    // Called by PartySelectionController once the player confirms who they're bringing.
    public void BeginRun(List<Character> chosenParty)
    {
        party = chosenParty;
        currentMode = GameMode.Exploration;
    }

    public void GameOver()
    {
        Debug.Log("Party wiped - run over.");
        GameOverUI.Instance?.Show();
    }

    private List<Character> LivingParty()
    {
        return party.FindAll(c => !c.isDead);
    }
}
