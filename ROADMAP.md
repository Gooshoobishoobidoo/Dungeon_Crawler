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

- [ ] **Step 1**: `PlannedAction` holds a queue of actions instead of one; Planning UI lets you
      add multiple abilities per turn gated by current resources, shows the queued order; still
      resolved with today's simple sequential execution model (no mid-execution recharge-waiting)
- [ ] Resource regen: mana/stamina recover some amount each turn (rate/formula TBD, likely a new
      tunable `CharacterData` field)
- [ ] **Step 2** (separate follow-up once Step 1's feel is known): simultaneous execution where a
      character short on resources for their next queued action waits mid-execution until they
      regen enough, rather than everything resolving in a fixed speed-sorted sequence

## Phase 5 — Party selection & run structure

- [ ] Party-selection screen before entering the dungeon
- [ ] Real game-over / return-to-town flow on a full party wipe (currently just a log/TODO in `DungeonManager.GameOver`)

## Phase 6 — Polish

- [ ] Smarter detection (vision cones / line-of-sight instead of a plain radius)
- [ ] Real formation-based group movement (today the whole party converges on one clicked point)
- [ ] Risk/skill on fleeing (distance checks, opportunity attacks) instead of an unconditional button
