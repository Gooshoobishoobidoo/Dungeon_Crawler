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

    // Guards against a second Flee() re-entering while OpportunityAttacks() is still animating a
    // punished failed attempt (the Flee button has no phase-based interactable gating today, so
    // it stays clickable through that ~0.5s+ window).
    private bool fleeResolving;

    // Fails if any living enemy still has a living player within its own detection radius -
    // reuses EnemyPatrol.detectionRadius rather than a separate flee-specific range, so it's
    // the exact same "has this enemy noticed you" rule as triggering the fight in the first place.
    // A failed attempt now costs something: any enemy whose best ability can actually reach a
    // player gets one free opportunity attack as the party tries to disengage (OpportunityAttacks
    // below) - a clean escape (nobody within detection range at all) never triggers one.
    public bool Flee()
    {
        if (fleeResolving) return false;

        bool blocked = false;
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
                    blocked = true;
                }
            }
        }

        if (blocked)
        {
            fleeResolving = true;
            StartCoroutine(OpportunityAttacks());
            return false;
        }

        Debug.Log("Party flees the encounter.");
        CombatEnded = true;
        DungeonManager.Instance?.ReturnToExploration();
        return true;
    }

    // One free parting shot per enemy that can actually reach a player right now, using the same
    // "best usable ability" pick the normal enemy turn-builder uses (ChooseBestAbility) and the
    // real ability execution pipeline (ExecuteAbilityUse) rather than a lightweight stand-in, so
    // VFX/cooldowns/damage all resolve exactly like a normal attack would.
    private IEnumerator OpportunityAttacks()
    {
        List<Coroutine> running = new List<Coroutine>();
        foreach (Character enemy in enemyCharacters)
        {
            if (enemy.isDead) continue;

            Ability ability = ChooseBestAbility(enemy, enemy.currentMana, enemy.currentStamina, new HashSet<Ability>());
            if (ability == null) continue;

            Character target = FindOpportunityTarget(enemy, ability);
            if (target == null) continue;

            QueuedAction attack = BuildAbilityQueueEntry(ability, target, enemy.transform.position, target.transform.position);
            running.Add(StartCoroutine(ExecuteAbilityUse(enemy, attack)));
        }

        foreach (Coroutine r in running) yield return r;

        fleeResolving = false;

        // A parting shot can wipe the party - reuse the same end-of-turn check a normal
        // Execution pass runs, so Game Over still triggers correctly instead of silently
        // dropping back into Planning with a dead party. Its own fallback (StartPlanningPhase)
        // is exactly what should happen if nobody died: back to Planning after a punished attempt.
        CheckCombatEnd();
    }

    // Nearest living player within the given ability's range - mirrors the distance-check style
    // Flee() itself already uses above, just per-ability instead of per-detection-radius.
    private Character FindOpportunityTarget(Character enemy, Ability ability)
    {
        Character best = null;
        float bestDistance = float.MaxValue;

        foreach (Character player in playerCharacters)
        {
            if (player.isDead) continue;
            float distance = Vector3.Distance(enemy.transform.position, player.transform.position);
            if (distance <= ability.range && distance < bestDistance)
            {
                bestDistance = distance;
                best = player;
            }
        }

        return best;
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

    // Every living enemy: pick the weakest living opponent, then greedily chain as many of its
    // own abilities as its current mana/stamina cover this turn - closing the distance with a
    // single Move first if the best available ability isn't already in range. Simpler than the
    // player's fully general planner: commits to one target and at most one Move for the whole
    // turn, never switches targets or moves again mid-chain.
    private void AssignEnemyActions()
    {
        foreach (Character c in enemyCharacters)
        {
            if (c.isDead) continue;

            Character target = SelectTarget(c, playerCharacters);
            if (target == null) continue;

            c.plannedAction = BuildEnemyTurn(c, target);
        }
    }

    private PlannedAction BuildEnemyTurn(Character c, Character target)
    {
        var planned = new PlannedAction();

        // Captured once - the whole turn commits to this one target, so every entry (the Move and
        // every ability) shares the same anchor for ChaseShift to measure from.
        Vector3 chaseAnchor = target.transform.position;

        int remainingMana = c.currentMana;
        int remainingStamina = c.currentStamina;
        var used = new HashSet<Ability>();

        Ability primary = ChooseBestAbility(c, remainingMana, remainingStamina, used);
        if (primary == null)
        {
            // Nothing usable at all - just close the distance, same fallback as before. No
            // chase here: there's no ability being set up to land, so nothing to adapt for.
            planned.queue.Add(new QueuedAction
            {
                type = QueuedActionType.Move,
                target = ApproachDestination(c.transform.position, target.transform.position, 0f)
            });
            return planned;
        }

        Vector3 casterPosition = c.transform.position;
        if (Vector3.Distance(casterPosition, target.transform.position) > primary.range)
        {
            casterPosition = ApproachDestination(casterPosition, target.transform.position, primary.range);
            planned.queue.Add(new QueuedAction
            {
                type = QueuedActionType.Move,
                target = casterPosition,
                chaseTarget = target,
                chaseAnchor = chaseAnchor,
                chaseRange = primary.range
            });
        }

        planned.queue.Add(BuildAbilityQueueEntry(primary, target, casterPosition, chaseAnchor));
        remainingMana -= primary.manaCost;
        remainingStamina -= primary.staminaCost;
        used.Add(primary);

        // Try to chain a few more from the same final position - no further movement. This is a
        // safety bound on the loop, not a tuned gameplay limit.
        const int maxAdditional = 3;
        for (int i = 0; i < maxAdditional; i++)
        {
            Ability next = ChooseBestAbility(c, remainingMana, remainingStamina, used);
            if (next == null) break;

            // Greedy: stops the moment the single best-by-damage pick doesn't fit in range,
            // rather than searching for a worse-but-in-range alternative instead.
            if (Vector3.Distance(casterPosition, target.transform.position) > next.range) break;

            planned.queue.Add(BuildAbilityQueueEntry(next, target, casterPosition, chaseAnchor));
            remainingMana -= next.manaCost;
            remainingStamina -= next.staminaCost;
            used.Add(next);
        }

        return planned;
    }

    // Isolated so different enemy types can plug in other priorities later without touching the
    // rest of the decision flow. Lowest current HP first ("focus down the weakest target"),
    // distance as the tiebreaker.
    private Character SelectTarget(Character c, List<Character> candidates)
    {
        Character best = null;
        int bestHealth = int.MaxValue;
        float bestDistance = float.MaxValue;

        foreach (Character candidate in candidates)
        {
            if (candidate.isDead) continue;
            float distance = Vector3.Distance(c.transform.position, candidate.transform.position);

            if (best == null || candidate.currentHealth < bestHealth ||
                (candidate.currentHealth == bestHealth && distance < bestDistance))
            {
                best = candidate;
                bestHealth = candidate.currentHealth;
                bestDistance = distance;
            }
        }

        return best;
    }

    // Highest-damage ability the character can afford out of whatever mana/stamina it has left
    // this turn, isn't on cooldown, and hasn't already been picked this turn. Self abilities are
    // excluded - there's no self-buff/heal AI logic yet.
    private Ability ChooseBestAbility(Character c, int availableMana, int availableStamina, HashSet<Ability> excluding)
    {
        if (c.data.abilities == null) return null;

        Ability best = null;
        foreach (Ability ability in c.data.abilities)
        {
            if (ability == null || ability.abilityType == AbilityType.Self) continue;
            if (excluding.Contains(ability)) continue;
            if (c.IsAbilityOnCooldown(ability)) continue;
            if (availableMana < ability.manaCost || availableStamina < ability.staminaCost) continue;
            if (best == null || ability.damage > best.damage) best = ability;
        }

        return best;
    }

    // Branches by AbilityType the same way ExecuteAbilityUse's resolution already does -
    // UnitTarget needs targetCharacter, AreaOfEffect targets the ground point at the target's
    // position, Skillshot needs a direction rather than a point. Every entry also carries
    // chaseTarget/chaseAnchor so ExecuteAbilityUse can re-aim AoE/Skillshot within a leash right
    // before it resolves (UnitTarget ignores these - it already re-checks the target's live
    // position on its own).
    private QueuedAction BuildAbilityQueueEntry(Ability ability, Character target, Vector3 casterPosition, Vector3 chaseAnchor)
    {
        switch (ability.abilityType)
        {
            case AbilityType.UnitTarget:
                return new QueuedAction { type = QueuedActionType.Ability, ability = ability, target = target.transform.position, targetCharacter = target, chaseTarget = target, chaseAnchor = chaseAnchor };

            case AbilityType.AreaOfEffect:
                return new QueuedAction { type = QueuedActionType.Ability, ability = ability, target = target.transform.position, chaseTarget = target, chaseAnchor = chaseAnchor };

            case AbilityType.Skillshot:
                Vector3 toTarget = target.transform.position - casterPosition;
                Vector3 direction = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector3.forward;
                return new QueuedAction { type = QueuedActionType.Ability, ability = ability, direction = direction, chaseTarget = target, chaseAnchor = chaseAnchor };

            default:
                return new QueuedAction { type = QueuedActionType.Ability, ability = ability, target = target.transform.position, chaseTarget = target, chaseAnchor = chaseAnchor };
        }
    }

    // Moves toward target, stopping at keepRange * PenetrationFactor rather than right at the
    // range boundary (0 = walk all the way there). Committing well within range instead of just
    // barely inside it means an enemy always has real ground left to cover even if its target
    // walks toward it mid-chase - without this, an approaching target could leave the enemy with
    // almost no travel left, making its follow-up attack feel like it fires instantly instead of
    // "running in and swinging." A real fix (cast times, a dodge mechanic) is future work; this
    // is a cheap, effective mitigation until then. Shared by BuildEnemyTurn's initial plan and
    // ExecuteMove's live chase recompute, so both stay consistent with each other for free.
    private const float PenetrationFactor = 0.7f;

    private Vector3 ApproachDestination(Vector3 from, Vector3 to, float keepRange)
    {
        Vector3 toTarget = to - from;
        float distance = toTarget.magnitude;
        float travel = Mathf.Max(0, distance - keepRange * PenetrationFactor);
        return from + toTarget.normalized * travel;
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
                    yield return ExecuteMove(c, queuedAction);
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
    private IEnumerator ExecuteMove(Character c, QueuedAction queuedAction)
    {
        Vector3 originalDestination = queuedAction.target;
        Vector3 currentDestination = originalDestination;
        c.MoveTo(currentDestination);

        const float timeoutSeconds = 8f;
        const float chaseCheckInterval = 0.25f; // re-pathing every frame would make the agent visibly jitter
        float timer = 0f;
        float chaseCheckTimer = 0f;
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

            // Enemy AI only (chaseTarget is never set by player-facing queueing) - periodically
            // recompute the ideal approach destination from the caster's own *current* position
            // toward the target's *current* position, then clamp how far that ideal point is
            // allowed to differ from the originally-planned destination to chaseLeashDistance.
            // Using the caster's live position (not a fixed anchor/rigid offset) matters: a rigid
            // "shift the destination by however far the target moved" approach keeps the enemy
            // pinned to its old relative offset even when the target walks *toward* it, sending
            // it wandering off to maintain a stale offset instead of recognizing it's already
            // close enough. Recomputing from scratch each check avoids that.
            if (queuedAction.chaseTarget != null)
            {
                chaseCheckTimer += Time.deltaTime;
                if (chaseCheckTimer >= chaseCheckInterval)
                {
                    chaseCheckTimer = 0f;
                    if (!queuedAction.chaseTarget.isDead && c.data.chaseLeashDistance > 0f)
                    {
                        Vector3 idealDestination = ApproachDestination(c.transform.position, queuedAction.chaseTarget.transform.position, queuedAction.chaseRange);
                        Vector3 clampedOffset = Vector3.ClampMagnitude(idealDestination - originalDestination, c.data.chaseLeashDistance);
                        Vector3 chased = originalDestination + clampedOffset;

                        if (Vector3.Distance(chased, currentDestination) > 0.1f)
                        {
                            currentDestination = chased;
                            c.MoveTo(currentDestination);
                        }
                    }
                }
            }
        }
    }

    // Shifts by however far chaseTarget has moved since chaseAnchor was captured at planning
    // time, capped at leash units - "chase within a leash": small dodges get tracked, large ones
    // don't. Returns zero (a no-op) for every player-queued action, since those never set
    // chaseTarget - this is purely additive for enemy AI.
    private Vector3 ChaseShift(Character chaseTarget, Vector3 chaseAnchor, float leash)
    {
        if (chaseTarget == null || chaseTarget.isDead || leash <= 0f) return Vector3.zero;

        Vector3 shift = chaseTarget.transform.position - chaseAnchor;
        return Vector3.ClampMagnitude(shift, leash);
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
                Vector3 aoePoint = queuedAction.target + ChaseShift(queuedAction.chaseTarget, queuedAction.chaseAnchor, c.data.chaseLeashDistance);
                ResolveAreaTarget(c, ability, aoePoint);
                break;

            case AbilityType.Skillshot:
                // Re-aim from the caster's actual position (after any chase-move) toward the
                // chase-adjusted aim point, rather than the direction baked in at planning time.
                // Falls back to queuedAction.direction unchanged for player-queued Skillshots,
                // since ChaseShift is a no-op when chaseTarget is null.
                Vector3 aimPoint = queuedAction.chaseAnchor + ChaseShift(queuedAction.chaseTarget, queuedAction.chaseAnchor, c.data.chaseLeashDistance);
                Vector3 aimDirection = queuedAction.chaseTarget != null
                    ? (aimPoint - c.transform.position).normalized
                    : queuedAction.direction;
                ResolveSkillshot(c, ability, aimDirection);
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