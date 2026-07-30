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

## Phase 3 — Looting & scavenging

- [ ] Item data model
- [ ] World pickups
- [ ] Inventory

## Phase 4 — Party selection & run structure

- [ ] Party-selection screen before entering the dungeon
- [ ] Real game-over / return-to-town flow on a full party wipe (currently just a log/TODO in `DungeonManager.GameOver`)

## Phase 5 — Polish

- [ ] Smarter detection (vision cones / line-of-sight instead of a plain radius)
- [ ] Real formation-based group movement (today the whole party converges on one clicked point)
- [ ] Risk/skill on fleeing (distance checks, opportunity attacks) instead of an unconditional button
