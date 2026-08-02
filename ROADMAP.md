# Dungeon Crawler Roadmap

Target game loop: pick a party → enter the dungeon → move the party around in real time while
looting/scavenging → enemies patrol and initiate combat when they notice you → fight (or
flee/sneak past) → repeat until the area's clear or you push on → next area.

## Foundations (done)

- [x] Turn-based combat core: Planning → Execution → Resolution phases (`CombatManager`)
- [x] Point-and-click planning UI: select character, move, target abilities (`PlanningController`, `PartyBarUI`, `AbilityBarUI`)
- [x] Real ability execution: damage, mana/stamina cost, cooldowns, per-`AbilityType` targeting/resolution
- [x] Basic enemy AI: nearest-target selection, best-available-ability attack, approach-if-out-of-range

## Phase 1 — Exploration ↔ Combat loop (done)

- [x] `DungeonManager`: Exploration/Combat mode switching, persistent party roster
- [x] `EnemyPatrol`: idle/waypoint patrol, radius-based detection, triggers encounters
- [x] `ExplorationController`: click-to-move for the whole party outside combat
- [x] `CombatManager.BeginEncounter`/`Flee`: encounters are resumable, resources carry over between fights
- [x] Return to Exploration on victory or flee; sneaking past undetected enemies works via distance-based detection

## Phase 2 — Multiple areas & progression (done)

- [x] `RestRoomTransition`: rest room between areas — heals the party once, "Continue?" prompt,
      seals the entrance / opens the exit via `NavMeshObstacle` on confirm
- [x] `Character.FullyRestore()`: full HP/mana/stamina/cooldown reset for a living character
- [x] `UIButtonFactory`: shared button-builder extracted from `PlanningController`, reused by the rest room's Continue prompt
- [x] Second area with its own `EnemyPatrol` enemy, connected via the rest room (manual scene work)
- [x] Playtest fixes: kinematic `Rigidbody` on `Character` so trigger volumes actually fire events;
      click raycasts ignore triggers (a tall trigger volume was intercepting ground clicks);
      cooldowns now tick during Exploration too, not just mid-fight; `Flee` fails if still within
      an enemy's detection radius; `EnemyPatrol` re-arms after combat ends instead of going permanently inert

## Phase 3 — Looting & scavenging (done)

- [x] `ItemData`: Consumable/Currency item definitions (`Assets/Scripts/Items/`)
- [x] `ItemPickup`: click-to-target world pickups, collected once the party walks into range —
      currency goes straight to `DungeonManager.currency`, everything else to the nearest
      living party member's `Character.inventory`
- [x] Per-character inventory (`Character.inventory`) + shared party currency pool
- [x] Sample content: `HealingPotion` (Consumable), `GoldPouch` (Currency)

## Phase 3.5 — Anytime item use (done)

- [x] `ItemData.useTime`/`ApplyTo`: items cost no mana/stamina — balanced by a delay before the
      effect lands instead
- [x] `Character.UseItem`: exploration-mode use, self-contained (removes from inventory
      immediately, `isBusy` blocks new moves for that character until `useTime` elapses)
- [x] `PlannedAction.itemToUse` + `CombatManager.ExecuteCharacterAction` (now a coroutine): combat
      use, delays that character's resolution by the item's `useTime` instead of the flat 0.5s
      stagger — mutually exclusive with using an ability, still one action per turn
- [x] `InventoryBarUI`: shared item-button row, shown by both `PlanningController` (combat) and
      `ExplorationController` (click a party member to open their inventory)

## Phase 4 — Multi-action turn economy

Bigger combat rework: spend as many actions as your stamina/mana allow in a turn instead of
exactly one, with resource regen and the risk of running dry mid-fight. Deliberately split into
two steps rather than one big rework — see the plan discussion for why the second step is a
genuinely different execution architecture (a live timeline, not a fixed sequence).

- [x] **Step 1**: `PlannedAction.queue` (`List<QueuedAction>`) replaces the old single
      ability/item fields; `PlanningController` appends to the queue (validated: an ability can't
      be queued twice in one turn, items are limited to unqueued copies actually held, both
      checked against resources left over after everything already queued) instead of overwriting
      a single selection; `QueueDisplayUI` shows the order with per-entry removal;
      `CombatManager.ExecuteCharacterAction` resolves the queue in order, stopping early if the
      character dies mid-turn. Enemy AI still only ever queues one ability (multi-action AI is a
      separate future upgrade), and there's no live button-graying for "already queued"/"can't
      afford anymore" yet — rejected with a log warning instead when you try, same pattern the
      codebase already used for e.g. out-of-range targeting.
- [x] Playtest fixes on Step 1: every queue-mutating entry point (`TryQueueAbility`/`TryQueueItem`/
      `RemoveQueuedAction`/`HandleWorldClick`) now checks `CombatManager.currentPhase ==
      Planning` — the canvas-visibility check alone didn't stop queue edits during Execution,
      and mutating the same `List` `CombatManager` was actively `foreach`-ing threw; `QueueDisplayUI`
      now treats a `null` `plannedAction` (true at the start of every new turn) as "0 queued"
      instead of skipping its rebuild check entirely, so it actually clears instead of showing
      last turn's stale entries until something else happened to call `Show()` again.
- [x] **Step 2, moved up**: turned out to be needed immediately rather than a later refinement -
      ability/item resolution had actually been sequential (one character's whole queue to
      completion before the next started) since the very first execution pass, which
      contradicted "everyone acts at once" the moment movement needed to interleave with actions.
      `ExecutionPhase` now launches every character's queue as its own concurrent coroutine
      (started the same frame, waited on together) instead of a sequential per-character loop,
      and `Move` joins `Ability`/`Item` as a third queueable `QueuedActionType` - `PlannedAction`'s
      old single `moveDestination` field is gone, replaced by explicit queued Move entries (enemy
      AI included). Speed no longer drives execution order (nothing is ordered anymore) but still
      drives `NavMeshAgent` speed as it always did.
- [x] **Fast-follow**: passive mana/stamina regen over time (`CharacterData.manaRegenPerSecond`/
      `staminaRegenPerSecond`, ticking under the same real-time gating cooldowns already use),
      plus two separate queueable actions — Rest (stamina) and Focus (mana) — picked with a +/-
      duration stepper showing a live "will restore ~X" preview. Restoration lands gradually over
      the channel (`CombatManager.ExecuteRegenChannel`) rather than all at the end, so taking
      damage partway through banks whatever had already accrued instead of losing it; only damage
      interrupts a channel, not other events. Enemies never queue either - player-only for now,
      same gap as the rest of enemy AI only ever queuing one action.
- [x] **Playtest round 2 fixes**: ability cooldowns are now tracked per-`Ability`
      (`Character.abilityCooldowns`) instead of one shared value per character - using one ability
      no longer put every other ability on the same cooldown. Rest/Focus moved out of their own
      panel into the ability bar itself (`AbilityBarUI`): pressing either collapses the row into
      just the duration stepper, Confirm queues and collapses it back. A new **Pass** pseudo-action
      (`QueuedActionType.Pass`, "Do Nothing" button) explicitly marks a character ready with zero
      actions - mutually exclusive with queuing anything else this turn. Combat's queued Move now
      spends stamina proportional to distance travelled (`CharacterData.moveStaminaCostPerUnit`).
      Party portraits/stats (`PartyBarUI`) are now shared between combat and Exploration instead
      of combat-only, so resources are visible between fights too.
- [x] **Playtest round 3 fixes**: Move's stamina cost now drains gradually per-frame based on
      ground actually covered (`CombatManager.ExecuteMove`) instead of deducting the whole cost up
      front, and stops the character the moment stamina runs dry instead of letting them coast to
      the destination for free. Passive stamina regen (not mana) now pauses while `isMoving`, in
      both Exploration and combat. Also fixed a `QueueDisplayUI` bug where a newly-started turn's
      first queued action could silently fail to display until something else forced a rebuild
      (`Rebuild()` wasn't resetting `lastKnownCount` on a null `plannedAction`).

## Phase 5 — Party selection & run structure

- [x] Party-selection screen before entering the dungeon (`PartySelectionController`) - candidates
      are pre-placed, initially-deactivated `Character` GameObjects (no prefab instantiation, since
      no `CharacterData` has `characterPrefab` assigned); toggle-select who to bring, Begin
      activates the chosen ones and hands them to `DungeonManager.BeginRun`. `GameMode` gained a
      `PartySelection` value (placed first so the scene's already-serialized `currentMode: 0`
      starts every run there for free) that `ExplorationController`/`PlanningController` already
      correctly hide themselves during, with no changes needed to either.
- [x] Real game-over / return-to-town flow (`GameOverUI`) on a full party wipe - `DungeonManager.
      GameOver()` shows a Game Over overlay instead of just logging; Return to Town reloads the
      scene outright (`SceneManager.LoadScene`) rather than hand-resetting every subsystem, which
      needed `TestScene` added to Build Settings (previously only `SampleScene.unity` was listed).

## Phase 6 — Polish (done)

- [x] Smarter detection - `EnemyPatrol` gained `detectionAngle` (full cone angle, default 360°
      preserves the old omnidirectional behavior) and a `Physics.Raycast` line-of-sight check, both
      gating `CheckForDetection` alongside the existing radius. A stationary guard's cone faces
      whatever direction it's placed facing; a patrolling one sweeps naturally since `NavMeshAgent`
      already rotates the transform to face movement. `OnDrawGizmosSelected` draws the radius/cone
      for tuning. `CombatManager.Flee()` deliberately still uses plain radius - an already-engaged
      enemy shouldn't "lose track" of you from a facing/cover technicality.
- [x] **Formation-based group movement** - `ExplorationController.MovePartyInFormation` replaces
      the old "everyone targets the exact same point" click-to-move with a 4-slot diamond (point at
      the click, two flanks behind-and-to-the-side, one anchor straight behind), oriented toward the
      party's direction of travel rather than fixed world axes. Each slot is passed through
      `NavMesh.SamplePosition` before use (falls back to the raw destination if a slot would land
      off-mesh, e.g. against a wall), and characters are matched to slots by greedy nearest-pair
      assignment rather than fixed roster order, so nobody criss-crosses the party to reach a far
      slot when a nearer one is free. Pattern is written to extend gracefully (straight back) past
      4 slots even though today's 4-hero roster cap never exercises that path.
- [x] **Risk/skill on fleeing** - `CombatManager.Flee()` still fails outright under the same
      `EnemyPatrol.detectionRadius` check as before, but a failed attempt now costs something: every
      living enemy whose own best usable ability (`ChooseBestAbility`, the same pick the normal
      enemy-turn builder uses) can currently reach a player gets one free opportunity attack
      (`OpportunityAttacks`) as the party tries to disengage, run through the real ability pipeline
      (`ExecuteAbilityUse` - VFX, cooldowns, damage) rather than a lightweight stand-in. A clean
      escape (nobody within detection range at all) still costs nothing. A `fleeResolving` guard
      stops a second Flee click from double-triggering while the parting shots are still animating,
      and `CheckCombatEnd()` runs afterward so a parting shot that wipes the party still correctly
      triggers Game Over instead of dropping back into a fresh Planning phase with a dead party.
- [x] Smarter enemy AI - `AssignEnemyActions`/`BuildEnemyTurn` now builds a real multi-entry queue
      per enemy: `SelectTarget` goes after lowest current HP (tie-broken by distance) instead of
      purely nearest, and `ChooseBestAbility` tracks a running mana/stamina budget across the whole
      turn so an enemy can chain several of its own abilities (capped at 4 total, a safety bound
      not a tuned limit) instead of ever doing just one thing. Still commits to one target and at
      most one Move per turn, and picks greedily by damage rather than exhaustively searching for
      an in-range alternative - deliberate scope cuts, not bugs.
- [x] **Adaptive chase**: enemies can now track a dodging target within
      `CharacterData.chaseLeashDistance` (0 = off, opt-in per enemy) - `ExecuteMove` periodically
      recomputes its destination from the caster's live position (not a rigid shift, which used to
      pin enemies to a stale offset and made them sidestep away from an approaching target), and
      `AreaOfEffect`/`Skillshot` re-aim at the target's current position right before firing
      (`UnitTarget` already re-checks live position on its own, so it only needed the Move fixed).
      Enemies also now commit to `ApproachDestination`'s `PenetrationFactor` (70% of an ability's
      range, not just inside the boundary) - otherwise an approaching target could leave an enemy
      with almost no travel left, making its attack feel like it fires instantly. Real fix (cast
      times, a dodge mechanic) is future work; this is a cheap mitigation until then.

## Tech debt / backlog

- [ ] Shared UI helper for the "labeled box with a Text child" pattern every procedural UI class
      (`AbilityBarUI`, `InventoryBarUI`, `QueueDisplayUI`, `PartyBarUI`) currently hand-rolls - not
      urgent at 3-4 panels, worth doing if another one shows up.
- [ ] `QueueDisplayUI`'s single horizontal row will get unwieldy once a character queues 4-5+
      actions in a turn - consider a scroll view or wrap once that's actually happening in play.
