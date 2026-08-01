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

    // Fails if any living enemy still has a living player within its own detection radius -
    // reuses EnemyPatrol.detectionRadius rather than a separate flee-specific range, so it's
    // the exact same "has this enemy noticed you" rule as triggering the fight in the first place.
    public bool Flee()
    {
        foreach (Character enemy in enemyCharacters)
        {
            if (enemy.isDead) continue;

            EnemyPatrol patrol = enemy.GetComponent<EnemyPatrol>();
            float detectionRadius = patrol != null ? patrol.detectionRadius : 0f;

            foreach (Character player in playerCharacters)
            {
                if (player.isDead) continue;
                if (Vector3.Distance(enemy.transform.position, player.transform.position) <= detectionRadius)
                {
                    Debug.Log($"Flee failed - still within {enemy.data.characterName}'s detection range.");
                    return false;
                }
            }
        }

        Debug.Log("Party flees the encounter.");
        CombatEnded = true;
        DungeonManager.Instance?.ReturnToExploration();
        return true;
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
        if (c.data.abilities == null) return null;

        Ability best = null;
        foreach (Ability ability in c.data.abilities)
        {
            if (ability == null || ability.abilityType == AbilityType.Self) continue;
            if (c.IsAbilityOnCooldown(ability)) continue;
            if (c.currentMana < ability.manaCost || c.currentStamina < ability.staminaCost) continue;
            if (best == null || ability.damage > best.damage) best = ability;
        }

        return best;
    }

    private PlannedAction BuildAttackAction(Character c, Ability ability, Character target)
    {
        var queuedAction = new QueuedAction
        {
            type = QueuedActionType.Ability,
            ability = ability,
            target = target.transform.position,
            targetCharacter = ability.abilityType == AbilityType.UnitTarget ? target : null
        };

        return new PlannedAction { queue = new List<QueuedAction> { queuedAction } };
    }

    // Moves toward target, stopping ~90% of the way to keepRange (0 = walk all the way there).
    private PlannedAction BuildApproachAction(Character c, Character target, float keepRange)
    {
        Vector3 toTarget = target.transform.position - c.transform.position;
        float distance = toTarget.magnitude;
        float travel = Mathf.Max(0, distance - keepRange * 0.9f);
        Vector3 destination = c.transform.position + toTarget.normalized * travel;

        var queuedAction = new QueuedAction { type = QueuedActionType.Move, target = destination };
        return new PlannedAction { queue = new List<QueuedAction> { queuedAction } };
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

        // Every character's queue runs as its own coroutine, all launched this same frame, so
        // everyone genuinely acts at once instead of taking turns - each character's own queue
        // still resolves strictly in the order it was queued, but different characters' queues
        // run concurrently rather than waiting for one another.
        List<Coroutine> running = new List<Coroutine>();
        foreach (Character c in AllCharacters())
        {
            if (c.isDead || c.plannedAction == null) continue;
            running.Add(StartCoroutine(ExecuteCharacterAction(c)));
        }

        foreach (Coroutine r in running) yield return r;

        Debug.Log("All characters finished their turn.");
        StartResolutionPhase();
    }

    // Resolves the character's whole queued action list in order, stopping early if they die
    // partway through (e.g. from another character's concurrently-resolving AoE) rather than
    // continuing to resolve a dead character's remaining actions.
    private IEnumerator ExecuteCharacterAction(Character c)
    {
        if (c.plannedAction == null) yield break;

        if (c.plannedAction.queue.Count == 0)
        {
            Debug.Log($"{c.data.characterName} does nothing this turn.");
            c.hasActedThisTurn = true;
            yield break;
        }

        foreach (QueuedAction queuedAction in c.plannedAction.queue)
        {
            if (c.isDead) break;

            switch (queuedAction.type)
            {
                case QueuedActionType.Move:
                    yield return ExecuteMove(c, queuedAction.target);
                    break;

                case QueuedActionType.Item:
                    yield return ExecuteItemUse(c, queuedAction.item);
                    break;

                case QueuedActionType.Ability:
                    yield return ExecuteAbilityUse(c, queuedAction);
                    break;

                case QueuedActionType.Rest:
                    yield return ExecuteRegenChannel(c, queuedAction.duration, c.data.restRegenPerSecond, c.RestoreStamina);
                    break;

                case QueuedActionType.Focus:
                    yield return ExecuteRegenChannel(c, queuedAction.duration, c.data.focusRegenPerSecond, c.RestoreMana);
                    break;

                case QueuedActionType.Pass:
                    Debug.Log($"{c.data.characterName} passes the turn.");
                    break;
            }
        }

        c.hasActedThisTurn = true;
    }

    // Per-character movement wait, replacing the old party-wide one now that movement is just
    // another queued entry. Same 8-second timeout safeguard as before - agents converging on
    // overlapping destinations can still jostle indefinitely without ever settling into their
    // stopping distance - just scoped to one character instead of blocking everyone.
    private IEnumerator ExecuteMove(Character c, Vector3 destination)
    {
        c.MoveTo(destination);

        const float timeoutSeconds = 8f;
        float timer = 0f;
        float costAccumulator = 0f;
        Vector3 lastPosition = c.transform.position;

        while (c.isMoving && !c.isDead)
        {
            timer += Time.deltaTime;
            if (timer >= timeoutSeconds)
            {
                Debug.LogWarning($"{c.data.characterName}'s movement timed out - stopping.");
                c.StopMoving();
                break;
            }

            yield return null;

            // Drain proportional to ground actually covered this frame (not the straight-line
            // distance to the destination - handles path curves/deceleration correctly), then
            // stop the moment stamina actually runs dry instead of letting them coast the rest
            // of the way for free. Gated on moveStaminaCostPerUnit > 0 so a character who's
            // already at 0 stamina from other spending isn't blocked from moving on an
            // asset/character that hasn't set a movement cost at all.
            float stepDistance = Vector3.Distance(c.transform.position, lastPosition);
            lastPosition = c.transform.position;

            costAccumulator += stepDistance * c.data.moveStaminaCostPerUnit;
            while (costAccumulator >= 1f)
            {
                c.SpendStamina(1);
                costAccumulator -= 1f;
            }

            if (c.data.moveStaminaCostPerUnit > 0f && c.currentStamina <= 0)
            {
                c.StopMoving();
                break;
            }
        }
    }

    private IEnumerator ExecuteAbilityUse(Character c, QueuedAction queuedAction)
    {
        Ability ability = queuedAction.ability;

        c.SpendMana(ability.manaCost);
        c.SpendStamina(ability.staminaCost);
        c.SetAbilityCooldown(ability, ability.cooldown);

        switch (ability.abilityType)
        {
            case AbilityType.UnitTarget:
                ResolveUnitTarget(c, ability, queuedAction.targetCharacter);
                break;

            case AbilityType.AreaOfEffect:
                ResolveAreaTarget(c, ability, queuedAction.target);
                break;

            case AbilityType.Skillshot:
                ResolveSkillshot(c, ability, queuedAction.direction);
                break;

            case AbilityType.Self:
                Debug.Log($"{c.data.characterName} uses {ability.abilityName} on self.");
                c.TakeDamage(ability.damage);
                break;
        }

        yield return new WaitForSeconds(0.5f);
    }

    // Item use delays resolution by its own useTime instead of the flat 0.5s stagger abilities
    // get - the same idea (give the action a moment to read/land), just item-specific.
    private IEnumerator ExecuteItemUse(Character c, ItemData item)
    {
        Debug.Log($"{c.data.characterName} uses {item.itemName}.");
        c.inventory.Remove(item);
        yield return new WaitForSeconds(item.useTime);
        if (!c.isDead) item.ApplyTo(c);
    }

    // Shared by Rest (stamina) and Focus (mana) - restores gradually over `duration` rather than
    // all at once at the end, so getting hit partway through still banks whatever had already
    // accrued instead of losing it all. Interrupted by death or by taking any damage this frame
    // (compared against health at the start of the frame, since another character's concurrently-
    // resolving action can land damage mid-channel); ends cleanly if neither happens by `duration`.
    private IEnumerator ExecuteRegenChannel(Character c, float duration, float ratePerSecond, System.Action<int> restore)
    {
        float elapsed = 0f;
        float accumulator = 0f;

        while (elapsed < duration && !c.isDead)
        {
            int healthBeforeFrame = c.currentHealth;
            yield return null;

            if (c.isDead) yield break;
            if (c.currentHealth < healthBeforeFrame)
            {
                Debug.Log($"{c.data.characterName}'s channel was interrupted by taking damage.");
                yield break;
            }

            elapsed += Time.deltaTime;
            accumulator += ratePerSecond * Time.deltaTime;

            while (accumulator >= 1f)
            {
                restore(1);
                accumulator -= 1f;
            }
        }
    }

    private void ResolveUnitTarget(Character c, Ability ability, Character target)
    {
        if (target == null || target.isDead ||
            Vector3.Distance(c.transform.position, target.transform.position) > ability.range)
        {
            Debug.Log($"{c.data.characterName}'s {ability.abilityName} fizzled - target out of range.");
            return;
        }

        Debug.Log($"{c.data.characterName} uses {ability.abilityName} on {target.data.characterName}.");
        target.TakeDamage(ability.damage);
    }

    private void ResolveAreaTarget(Character c, Ability ability, Vector3 point)
    {
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

    // A beam from the caster's current (live) position out to ability.range, aoeRadius wide.
    // Unlike ResolveAreaTarget, the caster is excluded - a beam leaving you shouldn't hit you
    // at the point you fired it, whereas standing in your own AoE blast plausibly should.
    private void ResolveSkillshot(Character c, Ability ability, Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.0001f)
        {
            Debug.Log($"{c.data.characterName}'s {ability.abilityName} fizzled - no direction.");
            return;
        }

        direction.Normalize();
        Vector3 origin = c.transform.position;

        Debug.Log($"{c.data.characterName} uses {ability.abilityName} toward {direction}.");

        foreach (Character other in AllCharacters())
        {
            if (other == c || other.isDead) continue;

            Vector3 toOther = other.transform.position - origin;
            float projection = Vector3.Dot(toOther, direction);
            if (projection < 0 || projection > ability.range) continue;

            Vector3 closestPointOnBeam = origin + direction * projection;
            if (Vector3.Distance(other.transform.position, closestPointOnBeam) <= ability.aoeRadius)
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