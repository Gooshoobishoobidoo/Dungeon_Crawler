using System.Collections.Generic;
using UnityEngine;

public enum GameMode
{
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
    public GameMode currentMode = GameMode.Exploration;

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
        CombatManager.Instance.BeginEncounter(LivingParty(), engaging);
    }

    public void ReturnToExploration()
    {
        currentMode = GameMode.Exploration;
    }

    public void GameOver()
    {
        Debug.Log("Party wiped - run over.");
        // TODO: return-to-party-select flow (future phase)
    }

    private List<Character> LivingParty()
    {
        return party.FindAll(c => !c.isDead);
    }
}
