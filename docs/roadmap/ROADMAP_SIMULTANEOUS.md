# Simultaneous Combat Roadmap

> **Context:** This roadmap replaces `updated_roadmap.md`. The game design has shifted away from D&D 5E rules toward a custom, tactics-first system built for digital play. The three pillars are: simultaneous turn resolution, per-player hidden information, and adaptive encounters. See `GAME_DESIGN.md` for the full design philosophy.
>
> **Current state:** Phase 2 is complete. The app has auth, campaigns, sessions, character CRUD, a sequential encounter loop (initiative → move → attack → end turn), a 20×20 grid, and a combat feed. All of that works end-to-end across multiple browser tabs. The sequential turn system will be replaced, but the infrastructure beneath it — Redis state, SignalR broadcasting, NGXS slices, the service layer pattern — carries forward.

Status: ✅ Done · 🔄 In Progress · ⬜ Not Started

---

## Phase 3A — Simultaneous Turn Foundation

*Goal: Replace sequential initiative with the planning/resolution cycle. This is the single largest architectural change in the project and everything else builds on it.*

The existing `EncounterService`, `CombatService`, `EncounterState`, and `GridComponent` will be heavily reworked in this phase. Expect to gut and rebuild rather than incrementally modify — the mental model is fundamentally different.

### ⬜ Slice 3A.1 — Planning Phase & Lock-In

Delivers: When an encounter starts, all players enter a planning phase simultaneously. Each player can plan one action (move or attack). Players lock in when ready. A countdown timer enforces a deadline. The GM can force-start resolution. No actions resolve yet — this slice is purely about the planning flow.

**Postgres:** No new tables. Modify `Encounter` entity: remove `Initiative`-related fields from `EncounterCombatant`. Add `RoundNumber` to `Encounter`.

**Redis:**
- Rework `session:{sessionId}:active_encounter` — remove `activeTurnCombatantId` and `initiativeOrder`. Add `roundPhase` (`planning` | `resolving` | `between_rounds`), `roundNumber`, `planningTimerExpiresAt`.
- New key `encounter:{encounterId}:planned_actions` (Hash) — one field per combatant ID, value is JSON of their planned action. Written during planning, consumed during resolution, cleared between rounds.
- New key `encounter:{encounterId}:locked_in` (Set) — combatant IDs that have committed their plan. Resolution triggers when this set's size equals the number of living combatants (or timer expires).

**Server:**
- New `IPlanningService` / `PlanningService` — handles `PlanAction` (validate action is legal, write to Redis planned_actions hash), `LockIn` (add to locked_in set, check if all locked in → trigger resolution), `ForceResolve` (GM-only, starts resolution regardless of lock-in state).
- Rework `EncounterService.StartEncounter` — instead of rolling initiative and assigning turn order, it seeds the encounter in `planning` phase and broadcasts `PlanningPhaseStarted`.
- New server-side timer: a background task or delayed job that fires when `planningTimerExpiresAt` is reached. Any player not locked in gets a default action (defend in place). Then resolution triggers.

**SignalR:**
- New hub methods: `PlanAction(joinCode, plannedAction)`, `LockIn(joinCode)`, `ForceResolve(joinCode)` (GM-only).
- New broadcasts: `PlanningPhaseStarted` (round number, timer duration, per-character available actions), `PlayerLockedIn` (characterName, no action details), `AllLockedIn` (resolution is about to begin).
- Remove: `EndTurn`, `TurnAdvanced`.

**NGXS:**
- Rework `EncounterState` — remove `activeTurnCombatantId`, `isMyTurn` logic. Add `roundPhase`, `roundNumber`, `myPlannedAction`, `lockedInPlayerIds[]`, `planningTimerExpiresAt`.
- New selectors: `isPlanning`, `isResolving`, `amILockedIn`, `allLockedIn`, `planningTimeRemaining`.

**Angular:**
- Rework `GridComponent` — instead of "click to move on your turn," all players see their movement range and can place a planned move simultaneously. Planned moves render as ghost tokens (translucent) until lock-in.
- Rework encounter tracker — remove initiative order column. Show round number, phase indicator, and lock-in status per player (checkmark when locked in, spinner when still planning).
- Add lock-in button and planning timer countdown.
- Remove "End Turn" button entirely.

**Two-tab test:** Both players see the planning UI simultaneously. Each can plan a move. Each can lock in. When both lock in, the phase transitions (even though nothing resolves yet — that's the next slice).

---

### ⬜ Slice 3A.2 — Resolution Engine & Playback

Delivers: After all players lock in (or timer expires), the server resolves all planned actions simultaneously and broadcasts the results. All clients play back the resolution sequence. A new round begins automatically.

This is the most complex new server component in the project.

**Postgres:** `game_events` already exists. Each resolution writes a batch of events — one per resolved action — in a single transaction.

**Redis:** Resolution reads from `encounter:{encounterId}:planned_actions`, applies all state deltas to `character:live:*` and `encounter:map:*`, clears the planned_actions and locked_in keys, increments `roundNumber`, and sets `roundPhase` back to `planning`.

**Server:**
- New `IResolutionService` / `ResolutionService` — the core of the new system. Takes all planned actions, resolves them according to the chosen conflict model (start with phase-based: movement → attacks → abilities → effects), computes all HP deltas, position changes, and defeated combatants, writes everything to Redis and Postgres, and returns a `ResolutionPayload` for broadcast.
- Resolution must be atomic from the clients' perspective — no partial updates leak during computation. Write all Redis mutations, then broadcast the full result.
- Conflict handling (start simple, iterate later): movement conflicts resolved by Speed stat tiebreaker. If two combatants attack the same target and both kill it, the second attack is wasted (not redirected). Damage is applied simultaneously within a phase — mutual kills are possible.

**SignalR:**
- New broadcast: `ResolutionCompleted` — the big payload. Contains an ordered list of resolution steps (movement results, then attack results, then ability results), each with actor, target, outcome, and resulting state deltas. This is what the client animates.
- New broadcast: `RoundStarted` — fires after resolution playback, signals the next planning phase.

**NGXS:**
- New `ResolutionCompleted` action — applies all state deltas from the resolution payload to `EncounterState`, `MapState`, and `FeedState` in one dispatch.
- Add `resolutionSteps[]` to `EncounterState` for playback rendering.

**Angular:**
- Resolution playback component — renders the resolution steps sequentially as a fast animation (target: 3–5 seconds). Tokens slide to new positions, damage numbers float, defeated combatants fade. After playback, auto-transition to next planning phase.
- Feed entries generated from resolution steps — the combat log now shows everything that happened in the round as a batch rather than individual actions.

**Two-tab test:** Both players plan moves and attacks. Both lock in. Resolution plays back on both tabs simultaneously — tokens move, HP updates, feed fills. A new planning phase begins.

---

### ⬜ Slice 3A.3 — GM Enemy Planning

Delivers: During the planning phase, the GM plans actions for all enemy combatants. Enemies participate in the same resolution cycle as players.

**Redis:** Enemy planned actions are written to the same `encounter:{encounterId}:planned_actions` hash as player actions, keyed by monster combatant ID.

**Server:**
- Extend `PlanningService` — GM can submit plans for any combatant where `combatantType == 'monster'`. Validate that the caller is the GM.
- Extend lock-in logic — the GM must lock in enemy actions before resolution triggers. The GM counts as one "lock-in" covering all enemies, not one per enemy.
- Default enemy action if GM doesn't lock in: defend in place (same as players).

**SignalR:**
- New hub method: `PlanEnemyAction(joinCode, combatantId, plannedAction)`, `LockInEnemies(joinCode)` (GM-only).
- `PlayerLockedIn` broadcast updated to also show "GM has locked in" when enemies are committed.

**Angular:**
- GM view: during planning, the GM sees a control panel listing all living enemies. Each enemy has a simplified action picker (move to cell, attack target). The GM queues actions for each enemy, then locks in all at once.
- Player view: no change — players don't see enemy plans during planning.

**Two-tab test:** GM tab plans enemy moves and attacks. Player tabs plan their own actions. All lock in. Resolution resolves player and enemy actions together.

---

## Phase 3B — Custom Rule System

*Goal: Replace D&D 5E assumptions with the game's own stat system, abilities, and resources. Each slice replaces one legacy concept.*

### ⬜ Slice 3B.1 — Custom Stats & Character Rework

Delivers: Characters use the six custom stats (Power, Speed, Precision, Resilience, Focus, Influence) instead of D&D's Str/Dex/Con/Int/Wis/Cha. All derived values (HP, movement range, vision range, etc.) flow from the new stats.

**Postgres:** Migration to rework `characters` table:
- Replace `BaseStats` JSONB shape: `{ str, dex, con, int, wis, cha }` → `{ power, speed, precision, resilience, focus, influence }`.
- Remove `Race` and `Class` columns (or make them purely cosmetic/flavor text with no mechanical effect).
- Remove `Level` or repurpose as a general advancement indicator.
- Add `MovementRange INT` (derived from Speed, but stored for quick access).
- Add `VisionRange INT` (derived from Focus).

**Redis:** Update `character:live` hash — replace `class` field (currently used for damage dice lookup) with stat-derived combat values: `attackBonus`, `damageBase`, `dodgeChance`, `maxAp`.

**Server:**
- Rework `CombatDice` — damage no longer keyed to class name. Instead, derive from Power (melee) or Precision (ranged) stat.
- New stat-to-mechanic mapping (centralized, easy to tune):
  - Power → melee damage modifier, carrying capacity
  - Speed → movement range, resolution priority tiebreaker, dodge chance
  - Precision → ranged attack bonus, critical hit chance
  - Resilience → max HP, condition resistance
  - Focus → ability resource pool, vision range, perception
  - Influence → information sharing range (matters when per-player vision lands), team buff radius

**Angular:**
- Rework character sheet component — new stat names, new derived value display.
- Rework `DevService.randomCharacter()` — generate random values for the new stats.

**Two-tab test:** Create characters with new stats. Start an encounter. Verify movement range varies by Speed, damage varies by Power/Precision, HP varies by Resilience.

---

### ⬜ Slice 3B.2 — Action Points

Delivers: Each round during planning, characters have a budget of Action Points to spend. Moving costs AP. Attacking costs AP. Players must allocate limited AP across their planned actions.

**Redis:** Add `maxAp`, `currentAp` to `character:live`. Add `apCost` field to each entry in `encounter:{encounterId}:planned_actions`.

**Server:**
- `PlanningService.PlanAction` validates that the player has enough AP remaining for the planned action. Multiple actions can be planned per round as long as total AP doesn't exceed the budget.
- AP resets to max at the start of each planning phase.
- AP budget derived from stats (base AP + Speed modifier, perhaps).

**SignalR:** `PlanningPhaseStarted` payload now includes `maxAp` and available actions with their AP costs.

**NGXS:** Add `myCurrentAp`, `myMaxAp` to `EncounterState`. Update on each `PlanAction`.

**Angular:** AP budget display during planning — a bar or pip count that depletes as actions are queued. Actions that cost more AP than remaining are greyed out. Players can undo planned actions to reclaim AP before locking in.

---

### ⬜ Slice 3B.3 — Abilities & Cooldowns

Delivers: Characters have abilities (discrete mechanical packages) instead of class features. Abilities have AP costs and cooldown timers. This replaces the D&D spell slot system.

**Postgres:**
- New table: `ability_definitions` (id, name, description, ap_cost, cooldown_rounds, range, target_type, effect JSONB, tags[]).
- New table: `character_abilities` (character_id → characters, ability_definition_id → ability_definitions). Replaces `character_known_spells`.
- Drop or repurpose `spell_definitions` and `character_known_spells`.

**Redis:** Add `abilityCooldowns JSONB` to `character:live` — `{ abilityId: roundsRemaining }`. Decremented at round start by `ResolutionService`.

**Server:**
- Extend `PlanningService` — validate ability is known, not on cooldown, AP cost affordable, target in range.
- Extend `ResolutionService` — resolve abilities during the appropriate phase (fast abilities before slow ones, or a dedicated ability phase). Apply effects (damage, healing, condition, movement, etc.) from the ability's `effect` JSONB.
- Cooldown management: after resolution, set cooldown on used abilities. At round start, decrement all cooldowns by 1.

**Angular:** Ability panel during planning — list of known abilities with AP cost, cooldown status, and range indicators. Click ability → enter targeting mode on grid → confirm → AP deducted from budget.

---

### ⬜ Slice 3B.4 — Conditions & Status Effects

Delivers: Abilities and environmental effects can apply conditions (stunned, burning, slowed, etc.) that modify combatant behavior for a duration. Conditions are the game's own vocabulary, not D&D's.

**Postgres:**
- New table: `condition_definitions` (id, name, effects JSONB, description). The `effects` JSONB defines mechanical impacts: `{ blocksActions: true, damagePerRound: 5, movementModifier: -2, ... }`.

**Redis:** `character:live` already has a `conditions` array. Extend each entry: `{ conditionId, name, roundsRemaining, sourceId, effects }`.

**Server:**
- Extend `ResolutionService` — at the start of resolution, tick all conditions (decrement duration, apply per-round effects like burn damage, remove expired conditions). Conditions that block actions cause the affected combatant's planned action to be replaced with "no action" during resolution.
- Conditions applied by abilities take effect at the end of the resolution phase (so they don't affect the current round's actions — you need to plan around them).
- Resilience stat governs condition resistance: when a condition is applied, the target rolls Resilience vs. a DC. On success, duration is halved or condition is resisted entirely.

**Angular:** Condition icons on tokens and in the combatant list. Tooltip on hover showing effect and remaining duration. Feed entries when conditions are applied, tick, or expire.

---

### ⬜ Slice 3B.5 — Defeat & Incapacitation

Delivers: What happens when a combatant reaches 0 HP. Replaces D&D death saves with a system tied to the custom stats.

**Design decision needed:** Choose one of these models (or hybrid) and playtest:
- **Knockout model:** At 0 HP the character is incapacitated (can't act) but not dead. Allies can spend AP to stabilize. If not stabilized within N rounds, the character dies. Resilience stat extends the window.
- **Last stand model:** At 0 HP the character gets one final action next round (at reduced AP), then dies if not healed above 0. Creates dramatic "go out swinging" moments.
- **Bleedout model:** Below 0 HP the character loses HP each round. Healing must bring them above 0 to stabilize. Resilience reduces bleedout rate.

**Postgres:** Add `is_incapacitated` and `is_dead` flags to character state (or handle purely in Redis during encounters).

**Redis:** Extend `character:live` — `isIncapacitated`, `isDead`, `roundsUntilDeath` (if using knockout model).

**Server:**
- `ResolutionService` checks for 0 HP after applying damage. Transitions combatant to incapacitated state. Incapacitated combatants' planned actions are voided in subsequent rounds.
- Stabilization mechanic: an ally ability or AP expenditure that prevents death.

**Angular:** Incapacitated visual state on tokens (greyed, prone icon). Stabilize action available to allies during planning. Death announcement in feed.

---

## Phase 3C — Per-Player Hidden Information

*Goal: Each player's client renders a personalized view of the game state based on what their character can perceive. This is a core pillar, not a nice-to-have.*

### ⬜ Slice 3C.1 — Vision Ranges & Fog of War

Delivers: Each character has a vision range (derived from Focus stat). Cells outside that range are fogged on that player's client. Different players see different amounts of the map.

**Postgres:** No new tables. Vision range is derived from Focus stat (already on `characters` table after 3B.1).

**Redis:** Rework `encounter:map:{encounterId}` — instead of a single `fogOfWar.revealedCells` array, the server computes per-player visibility on each request. Alternatively, store a `visibility:{encounterId}:{characterId}` key per player containing their currently visible cell set (recomputed after each resolution phase when positions change).

**Server:**
- New `IVisionService` / `VisionService` — given a character's position and vision range, compute the set of visible cells (simple radius for now; line-of-sight with wall occlusion is a later enhancement).
- After each resolution (positions change), recompute visibility for all players and update Redis.
- Modify all broadcasts that include map/position data: filter the payload per-recipient. Use SignalR's `Clients.Client(connectionId)` instead of `Clients.Group()` for position-sensitive data so each player gets their own filtered view.
- GM always receives unfiltered data.

**SignalR:**
- `ResolutionCompleted` payload becomes per-player: each client receives only the resolution steps involving combatants they can see. If a player can't see an enemy, they don't see that enemy's movement or attacks (unless the attack targets them).
- New broadcast: `VisibilityUpdated` (per-player) — sent after resolution with the player's new visible cell set and any newly revealed/hidden combatants.

**NGXS:** Add `myVisibleCells`, `visibleCombatantIds` to `MapState`. Fog rendering driven by these fields.

**Angular:**
- Fog layer on grid — cells outside `myVisibleCells` render with a dark overlay.
- Enemy tokens only render if their combatant ID is in `visibleCombatantIds`.
- GM toggle: "View as [player name]" to see any player's perspective.

**Two-tab test:** Two players with different Focus stats see different fog extents. A player can't see an enemy that's outside their vision range. The GM sees everything.

---

### ⬜ Slice 3C.2 — Information Sharing

Delivers: When one player sees an enemy, that information can optionally propagate to allies based on Influence stat and campaign settings.

**Design decision needed:** Pick the default model (configurable per campaign):
- **Auto-share within range:** A player's scouting information automatically shares with allies within their Influence radius. Allies see "reported" enemy positions (possibly stale if the enemy moves later).
- **Manual callout:** Players spend AP during planning to share information with allies. Creates a meaningful cost for communication.
- **Full share:** All ally vision is pooled (simplest, least tactically interesting).

**Redis:** If using auto-share or manual callout, each player's visibility set is the union of their own vision plus information shared by allies. Store `shared_intel:{encounterId}:{characterId}` with reported enemy positions and the round they were reported.

**Server:** Extend `VisionService` to merge personal vision with shared intelligence when computing a player's visible combatants. Stale intel (enemy has moved since it was reported) renders differently on the client.

**Angular:** "Reported" enemies render with a dashed outline or question mark, indicating the position may be outdated. Fresh sightings (from own vision) render normally.

---

### ⬜ Slice 3C.3 — Hidden Enemies & Ambush

Delivers: Enemies can be hidden from all players until they enter a player's vision range or attack. Enables genuine ambush encounters.

**Redis:** Add `isHidden: boolean` to monster combatant state. Hidden monsters are excluded from all player visibility calculations regardless of range.

**Server:**
- GM sets `isHidden` on enemies during encounter setup.
- When a hidden enemy acts (attacks, moves into a player's vision), `isHidden` flips to `false` and the enemy appears on affected players' maps.
- Surprise mechanic: if a hidden enemy's first action is an attack, the target doesn't get to plan a reaction for that round (their planned action still executes, but they couldn't have accounted for the ambush).

**Angular:** Hidden enemies don't render on player maps at all. When revealed during resolution playback, they fade in with a visual cue ("Ambush!"). GM always sees hidden enemies with a distinct marker.

---

## Phase 3D — Adaptive Encounters

*Goal: Enemies make their own decisions based on encounter state, reducing GM workload and creating dynamic combat.*

### ⬜ Slice 3D.1 — Tier 1 AI: Instinctive Behavior

Delivers: The simplest enemy AI. Monsters with Tier 1 behavior automatically plan their actions during the planning phase — the GM doesn't need to manually control them. Behavior: attack nearest visible threat, flee when HP is low.

**Postgres:** Add `behavior_tier INT` to `monster_definitions` (or to `encounter_combatants` so the GM can override per encounter).

**Server:**
- New `IEnemyAiService` / `EnemyAiService` — given the encounter state and a monster's behavior tier, produce a planned action.
- Tier 1 logic: find the nearest player combatant (by grid distance), move toward them, attack if in range. If HP < 25% of max, move away from the nearest threat instead.
- Called during the planning phase after the planning timer starts. Enemy plans are computed server-side and written to `encounter:{encounterId}:planned_actions` automatically.
- GM can still override any AI-planned action before locking in.

**Angular:** GM view shows AI-suggested actions for each Tier 1 enemy with an "Override" button. If the GM doesn't override, the AI plan stands.

---

### ⬜ Slice 3D.2 — Tier 2 AI: Tactical Behavior

Delivers: Smarter enemies that focus fire, use terrain, and prioritize squishy targets.

**Server:** Extend `EnemyAiService` with Tier 2 logic:
- Target selection: prefer wounded targets (finish them off), prefer low-Resilience targets (squishy).
- Positioning: move to cells that provide cover (adjacent to walls/obstacles) when possible.
- Retreat: when the group has taken >50% casualties, surviving Tier 2 enemies cluster together defensively.

---

### ⬜ Slice 3D.3 — Tier 3 AI: Strategic Behavior

Delivers: Coordinated enemy groups that adapt to player patterns over multiple rounds.

**Server:** Extend `EnemyAiService` with Tier 3 logic:
- Coordination: one enemy draws attention (moves to engage the front line) while another flanks.
- Pattern tracking: track which player the party focuses (across rounds). If the same target is focused 2+ rounds, Tier 3 allies reposition to protect it.
- Ability awareness: if an enemy has observed a player using the same ability repeatedly, they prioritize moving out of that ability's typical range or targeting the caster.

**Redis:** New key `encounter:{encounterId}:ai_memory` — tracks cross-round patterns (focus target history, ability usage frequency per player). Read by Tier 3 AI each planning phase.

---

### ⬜ Slice 3D.4 — Dynamic Environments

Delivers: Environmental effects that change the battlefield during combat — fire spread, destructible terrain, interactive objects.

**Postgres:** New table: `environment_effect_definitions` (id, name, behavior JSONB — spread rules, damage, duration, trigger conditions).

**Redis:** Add `environmentEffects[]` to `encounter:map` — each entry has a type, affected cells, remaining duration, and behavior parameters.

**Server:**
- Extend `ResolutionService` — after resolving combatant actions, process environment effects: spread fire to adjacent cells, tick damage on combatants in hazard cells, collapse terrain if destruction threshold met.
- Environment effects can block movement (collapsed terrain), deal damage (fire, poison), or modify visibility (smoke blocks line of sight).

**Angular:** Environmental effects render as cell overlays on the grid — fire flickers, poison glows, smoke obscures. Destruction animations during resolution playback.

---

## Phase 4 — Feature Depth

*These slices add richness without changing the core loop. Order is flexible.*

### ⬜ Slice 4.1 — AoE Abilities & Spatial Targeting

Extends 3B.3. Abilities that affect areas (cone, sphere, line) rather than single targets.

**Server:** AoE targeting during planning — player places a shape on the grid. Server calculates affected cells and combatants during resolution. Per-player vision applies: if you can't see a cell, you can't tell who's in it when you place the AoE.

**Angular:** Shape placement UI during planning. Ghost overlay shows affected area. Affected tokens highlighted (only the ones you can see).

---

### ⬜ Slice 4.2 — Inventory & Equipment

Characters carry and equip items that modify their stats and available actions.

**Postgres:** `character_inventory` and `item_definitions` tables (already in schema design). Equipment modifies derived stats (a sword increases Power-based damage, armor increases effective Resilience).

**Server:** Equip/unequip recalculates derived combat values in `character:live`. Consumables (potions, scrolls) are usable as planned actions during combat, costing AP.

---

### ⬜ Slice 4.3 — GM Manual Overrides

The GM needs escape hatches for situations the rule engine doesn't cover.

**Server:** GM can override any combatant's HP, apply/remove conditions, reveal/hide enemies, and add/remove environment effects mid-encounter. All overrides logged to `game_events` with `event_type: 'gm_override'`.

**Angular:** GM context menu on any token — Edit HP, Add Condition, Toggle Visibility. Changes broadcast immediately to affected players.

---

### ⬜ Slice 4.4 — Reinforcements & Encounter Triggers

The GM defines trigger conditions that spawn new enemies or change the environment mid-encounter.

**Server:** Trigger system: `{ condition: 'round >= 3', action: 'spawn', monsters: [...] }` or `{ condition: 'boss_hp < 50%', action: 'environment_change', effect: 'collapse_bridge' }`. Evaluated by `ResolutionService` at the end of each resolution phase.

---

## Phase 5 — Hardening

- **Reconnection resilience** — player reconnects mid-planning or mid-resolution without losing state. `SessionHydrated` sends the current phase, planned action (if any), and timer state.
- **Conflict resolution** — Redis distributed lock on encounter during resolution to prevent race conditions if the timer and last lock-in fire simultaneously.
- **Redis persistence** — RDB snapshot interval config in docker-compose (60s).
- **Load testing** — simulate 5–10 concurrent WebSocket connections through a full planning → resolution cycle.
- **Auth audit** — confirm all GM-only hub methods and enemy AI overrides are enforced server-side.
- **Resolution edge cases** — exhaustive testing of conflict scenarios: mutual kills, movement collisions, targeting a combatant who dies before your action resolves, AoE hitting allies through fog, etc.

---

## Suggested Sequencing

```
Month 1     3A.1 Planning Phase → 3A.2 Resolution Engine → 3A.3 GM Enemy Planning
Month 2     3B.1 Custom Stats → 3B.2 Action Points → 3B.3 Abilities & Cooldowns
Month 3     3B.4 Conditions → 3B.5 Defeat & Incapacitation → 3C.1 Vision & Fog
Month 4     3C.2 Info Sharing → 3C.3 Hidden Enemies → 3D.1 Tier 1 AI
Month 5     3D.2 Tier 2 AI → 3D.3 Tier 3 AI → 3D.4 Dynamic Environments
Month 6     Phase 4 feature depth (flexible order) + Phase 5 hardening
```

Phase 3A is the critical path — nothing else works until the simultaneous turn system is in place. After that, 3B (custom rules), 3C (hidden information), and 3D (adaptive AI) have lighter dependencies on each other and can be interleaved if you want variety.

---

## Migration Notes

The transition from sequential to simultaneous will temporarily break the existing encounter flow. Plan for this:

1. **Start with 3A.1.** This breaks the old flow — `StartEncounter` no longer rolls initiative, `EndTurn` is removed. The game is unplayable until 3A.2 lands.
2. **3A.1 + 3A.2 together form the minimum playable state.** Don't ship 3A.1 without 3A.2 close behind. These two slices should be developed as a single focused sprint.
3. **The sequential encounter code doesn't need to be preserved.** Once simultaneous planning is working, the old `EndTurn` / `TurnAdvanced` / `activeTurnCombatantId` code path is dead. Remove it cleanly rather than maintaining both.
4. **Existing NGXS tests will break.** The encounter state tests reference `activeTurnCombatantId` and `TurnAdvanced`. Rewrite them alongside the slice work.
5. **The smoke test (smoke-test.md) needs a full rewrite** after 3A.2 — the "playing a turn" section assumes sequential turns.

---

## Design Decisions Log

| Decision | Choice | Rationale |
|---|---|---|
| Conflict resolution model | Phase-based macro + simultaneous within phase (start here) | Learnable mental model; iterate toward hybrid based on playtesting |
| Stat system | Power, Speed, Precision, Resilience, Focus, Influence | Setting-agnostic; each stat mechanically relevant to the simultaneous + vision systems |
| Resource model | Action Points (per round) + Cooldowns + Consumables | AP creates round-to-round tension; cooldowns prevent ability spam; consumables add session-level strategy |
| Character progression | Abilities over classes | Composable; avoids rigid class hierarchies; ability choice affects resolution phase timing |
| Default action on timer expiry | Defend in place (no movement, no attack, reduced damage taken) | Least punishing; doesn't waste resources or move the character to an unintended position |
| Enemy AI | 3-tier system (Instinctive / Tactical / Strategic) | Complexity matches narrative fiction; GM can override any tier; Tier 1 is trivial to implement as a starting point |
| Per-player vision | Computed server-side, filtered per-client | Server is the authority; prevents client-side cheating; Focus stat makes vision a meaningful build choice |
