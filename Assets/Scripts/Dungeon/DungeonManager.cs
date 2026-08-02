using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

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

    [Header("Generation")]
    public DungeonGenerator dungeonGenerator;

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
    // Generates a fresh floor before dropping the party into it - a new run always means a new
    // layout (roguelike: no cross-run persistence), which a scene reload after Game Over already
    // guarantees happens from a clean slate.
    public void BeginRun(List<Character> chosenParty)
    {
        party = chosenParty;

        if (dungeonGenerator != null)
        {
            dungeonGenerator.Generate();
            WarpPartyToStart(dungeonGenerator.StartPosition);
        }

        currentMode = GameMode.Exploration;
    }

    // Small fixed cluster around the spawn point rather than warping everyone to the identical
    // coordinate - even with a good spawn marker, stacking every character on one exact point is
    // its own source of visual weirdness. Each candidate offset is checked against the NavMesh
    // (same fallback-to-the-anchor-point pattern ExplorationController's formation movement
    // already uses) since an offset can land outside the room if the spawn point sits close to a
    // wall.
    private const float SpawnSpacing = 1.5f;
    private static readonly Vector3[] SpawnOffsets =
    {
        Vector3.zero,
        new Vector3(-1f, 0f, -1f),
        new Vector3(1f, 0f, -1f),
        new Vector3(-1f, 0f, 1f),
        new Vector3(1f, 0f, 1f),
    };

    private void WarpPartyToStart(Vector3 startPosition)
    {
        for (int i = 0; i < party.Count; i++)
        {
            Vector3 offset = SpawnOffsets[Mathf.Min(i, SpawnOffsets.Length - 1)] * SpawnSpacing;
            Vector3 candidate = startPosition + offset;

            Vector3 destination = NavMesh.SamplePosition(candidate, out NavMeshHit hit, SpawnSpacing * 2f, NavMesh.AllAreas)
                ? hit.position
                : startPosition;

            party[i].WarpTo(destination);
        }
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
