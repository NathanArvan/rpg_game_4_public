# Strategic Roadmap

> **Purpose:** This document is the high-level plan — what to build, in what order, and the data contracts that downstream slices depend on. It does not contain implementation details (service methods, NGXS selectors, Angular component behavior, pseudocode). Those live in the active sprint document for the current slice.
>
> **Design philosophy:** Tactics-first, setting-agnostic digital RPG. Three pillars: simultaneous turn resolution, per-player hidden information, and adaptive encounters. See `GAME_DESIGN.md` for the full design philosophy.
>
> **Locked decision documents:** This roadmap defines *what to build and when*. For *how it works*, defer to:
> - `DESIGN_SPIKE.md` — conflict resolution model, mutual kills, HP netting, damage pipeline, placeholder stat values for 3A
> - `TICK_RESOLUTION.md` — tick-based resolution loop, path-based movement, reactive action framework, action queue schema, opportunity attacks & disengage
> - `MAGIC_SYSTEM.md` — ley points, resonance, environmental magic pipeline, implementation sequencing across slices

Status: ✅ Done · 🔄 In Progress · ⬜ Not Started

---

## Completed

**Phase 1 & 2** — Auth, campaigns, sessions, character CRUD, a sequential encounter loop (initiative → move → attack → end turn), a 20×20 grid, and a combat feed. All works end-to-end across multiple browser tabs. The sequential turn system will be replaced, but the infrastructure beneath it — Redis state, SignalR broadcasting, NGXS slices, the service layer pattern — carries forward.

**Phase 3A** (merged April–May 2026, PRs #62–#69) — Simultaneous planning, tick-based resolution with playback, GM enemy planning, and a combat UI pass. The sequential turn system has been removed. Known gaps carried forward: no end-of-encounter detection (`EncounterEnded` is never broadcast — fixed in 3B.0), and the planning timer is 300s rather than the 30s default below.

---

## Phase 3A — Simultaneous Turn Foundation

*Goal: Replace sequential initiative with the planning/resolution cycle. This is the single largest architectural change in the project and everything else builds on it.*

*Architecture note:* The resolution engine is built as a **tick loop** from day one. In 3A, every combatant queues exactly one action, so the loop runs exactly one tick per round. When AP lands in 3B.2, the constraint lifts and queue length becomes variable with zero structural changes. See `TICK_RESOLUTION.md`.

### ✅ Slice 3A.1 — Planning Phase & Lock-In

Delivers: All players enter a planning phase simultaneously. Each player plans one action (move, melee attack, ranged attack, or defend) submitted as an action array of length 1. Players lock in when ready. A countdown timer enforces a deadline. The GM can force-start resolution. No actions resolve yet.

### ✅ Slice 3A.2 — Resolution Engine & Playback

Delivers: After all players lock in (or timer expires), the server resolves all planned actions simultaneously via the tick loop (Movement → Attacks → Effects) and broadcasts results. All clients play back the resolution sequence. A new round begins automatically. This is the most complex new server component in the project.

### ✅ Slice 3A.3 — GM Enemy Planning

Delivers: During the planning phase, the GM plans actions for all enemy combatants. Enemies participate in the same resolution cycle as players. The GM counts as one lock-in covering all enemies.

---

## Phase 3B — Custom Rule System

*Goal: Replace D&D 5E assumptions with the game's own combat values, abilities, and resources, so a single battle plays end-to-end. Each slice replaces one legacy concept.*

*Scope note (November 2026):* Character stats and progression are **deferred** (see [Deferred: Character Stats & Progression](#deferred-character-stats--progression)). Phase 3B and 3C build against **combat profiles**: hand-authored bundles of derived combat values (HP, movement, accuracy, dodge, damage, AP, vision, …). The rules engine only ever consumes derived values, so whatever later produces them (six core stats, equipment, abilities, or a mix) is a mapping layer in the Api and doesn't touch `RoundResolver`. Building items, abilities, and conditions first should make it clearer what stats, if any, need to do.

*Execution order:* 3B.0 → 3B.1 → 3B.2 → 3B.5 → 3B.3 → 3B.4 (3B.2 was pulled ahead of 3B.5 in November 2026). Slice numbers are kept stable for cross-references. 3B.0 comes first so every later slice can be tried by hand in a one-person battle; 3B.5 moves up because the defeat model is part of what makes that battle meaningful.

### ✅ Slice 3B.0 — Manual Testing Spike

*Status: done (PRs #90 encounter end, #91 enemy AI, and Quick Battle). What shipped differs slightly from the plan below: no `Side` column (`CombatantType` already does the job), and party stats for Quick Battle live in nullable `MaxHp` / `Speed` / `DamageDie` overrides on `EncounterCombatant`, which 3B.1 combat profiles extend.*

*Goal: one person can start a battle in one click, command a party of several characters against preset AI enemies, and play it to a win or a loss. This is the harness for trying out every later 3B/3C feature by hand.*

Delivers, as three PRs in this order (each is useful on its own):

1. **Encounter end.** Closes the known 3A gap. The two copies of the post-resolution flow (`GameHub.RunResolutionAsync` and `PlanningTimerService.RunResolutionAsync`) are merged into one round-orchestration service. After each round, if one side has no living combatants, the server sets the encounter to `Ended`, cancels the timer, broadcasts `CombatantDefeated` / `EncounterEnded` (with the winning side), and does not start another planning phase. The client handles `EncounterEnded` with a result overlay (back to lobby / rematch). Applies to normal GM-run encounters too.
2. **Simple enemy AI.** A pure `EnemyAi.Plan(state)` function in `RpgGame.Rules`: attack an adjacent enemy if there is one; otherwise move up to movement range toward the nearest living enemy, ending on a free adjacent cell when possible. Ties break deterministically so the AI can be covered by scenario tests. Combatants get an AI-controlled flag; at the start of each planning phase the server plans and locks in every AI combatant, so a round resolves as soon as the human locks in. Unflagged monsters still work the 3A way (GM plans them).
3. **Quick Battle & multi-character control.** A lobby "⚡ Quick Battle" button calls a `StartQuickBattle(presetId)` entry point that creates the campaign, session and encounter in one step from a **battle preset**: a JSON file in the server (same combatant shape as the Rules `Scenarios/` files: name, HP, damage die, Speed, start cell, side, AI flag). Party combatants are owned by the user and need no `Character` rows; enemies are AI-controlled. The planning timer is off by default. Behind the scenes the user is the campaign GM (so debug tools like Force Resolve and Add Monster stay available), but the screen shows the player view plus a small debug panel. `PlanAction` takes an optional `combatantId` (ownership checked); `LockIn` locks all of the caller's living combatants, defaulting unplanned ones to defend. Client: select which of your characters you're planning for (token or sidebar row), see per-character plan markers, "Lock In All". Client planned-action state becomes per combatant (`Record<combatantId, PlannedActionEntry[]>`) so 3B.2's multi-action queues fit without a redesign.

A new single-browser E2E test (Quick Battle → play to victory) becomes the default per-slice smoke test; the two-browser GM + player test stays for multiplayer and per-player vision.

**Ongoing cost:** from here on, each slice that adds something combatants can do (AP queues, abilities, conditions, hiding) also teaches `EnemyAi` a minimal use of it, so the feature shows up in Quick Battle. Budget a small AI task in each sprint doc.

Not in scope: smart AI (fleeing, focus fire, terrain), a preset editor UI, combat profiles (3B.1 extends battle presets with profile fields).

### ✅ Slice 3B.1 — Combat Profiles

*Status: done. What shipped: profiles live in `server/src/RpgGame.Api/CombatProfiles/profiles.json` (Bruiser, Skirmisher, Scout, Grunt, Brute), loaded and validated at startup by `CombatProfileCatalog`. Each `EncounterCombatant` stores a **snapshot** of its resolved profile as JSONB (`Profile`), replacing the 3B.0 `MaxHp` / `Speed` / `DamageDie` override columns, so a running encounter never depends on the catalog. `BaseDamage` stays a die (e.g. `d8`). Hit check: d20 + attacker's melee/ranged accuracy ≥ target's Dodge. Characters pick a profile (`Character.CombatProfile`, default `skirmisher`) and start each encounter at its MaxHp. The GM picks one per monster (`AddMonster(..., profileId)`, default `grunt`). Quick Battle combatants name a `profile` and may override single values (`hp`, `damage`, `speed`, `move`, `meleeAccuracy`, `rangedAccuracy`, `dodge`). Combatant DTOs carry `profileId`, `profileName`, `movementRange`, and the grid's move highlight and `PlanningService` use the per-combatant range. Presets are served by `GET /api/combat-profiles`.*

Delivers: Each combatant carries its own derived combat values instead of global constants and D&D class dice. Profiles extend the 3B.0 battle presets. A small set of hand-authored presets (e.g. Bruiser, Skirmisher, Scout for players; Grunt, Brute for monsters) defines `MaxHp`, `MovementRange`, `Speed` (contested-cell priority), melee/ranged `Accuracy`, `Dodge`, and `BaseDamage`. Characters and monsters each select a preset. Values that move from `RulesConfig` to per-combatant: `HitThreshold` (→ target Dodge), `AccuracyBonus`, `MovementRange`. Class-based damage dice (`CombatMath.DamageDieForClass`) are removed. Presets must differ meaningfully — Speed tiebreaks and per-player vision (3C.1) are only testable if combatants differ.

Later slices add fields to the profile as they need them: `MaxAp` (3B.2), `ConditionResist` (3B.4), `VisionRange` and `Perception` (3C.1/3C.3).

Not in scope: the six core stats, point-buy, character creation rework, progression. The `Character` entity's `Race`, `Class`, `Level` and D&D `BaseStats` are left in place but unused by combat (no migration until the stats decision is made).

### ✅ Slice 3B.2 — Action Points

*Status: done. What shipped: `MaxAp` is a profile value (`ap` in `profiles.json`: Bruiser 4, Skirmisher 5, Scout 5, Grunt 4, Brute 4; default 4; battle presets may override it). Costs are shared constants in `RulesConfig`: move 1, melee 2, ranged 2, defend 1. **All of a round's moves share the profile's `move` cells** (each Move costs 1 AP but they can't go further in total than one move used to), so a combatant can move → attack → move without covering more ground. `PlanValidator` (Rules) checks a queue's AP total and movement budget, each move measured from where the previous queued move ends; `PlanningService` uses it to accept or reject a plan, and `RoundResolver` cuts any over-budget queue and caps each move at the cells left. An empty plan clears it. `EnemyAi` plans queues: melee as often as AP allows, or walk and then melee with the AP left. The client queues actions per combatant (append, undo, clear, AP counter, numbered move markers), plays back every tick, and labels ticks in the feed. The server's playback delay scales with the tick count. No `currentAp` in Redis and no per-entry `apCost`: AP resets every round and the server computes costs, sending the cost table in `PlanningPhaseStarted.apCosts`.*

Delivers: Each round, characters have an AP budget (`MaxAp` from their combat profile) to spend on actions during planning. Moving, attacking, and using abilities all cost AP. Multiple actions can be planned per round. AP resets each round. The tick loop's queue length constraint lifts — this is where the multi-action architecture pays off.

### ⬜ Slice 3B.3 — Abilities & Cooldowns

Delivers: Characters have abilities (discrete mechanical packages) instead of class features. Abilities have AP costs, cooldown timers, range, target types, and effect definitions. Each combatant (player or monster) has a fixed **loadout** of abilities — how characters *acquire* abilities (trees, points, milestones) is progression and is deferred. Environmental magic (ley points and resonance) also lands in this slice — abilities need resonance to be meaningful. See `MAGIC_SYSTEM.md` for the full design. The damage pipeline's `statModifier` slot is 0 or a profile value until the stats decision.

### ⬜ Slice 3B.4 — Conditions & Status Effects

Delivers: Abilities and environmental effects can apply conditions (stunned, burning, slowed, etc.) that modify combatant behavior for a duration. Conditions are the game's own vocabulary. Resistance comes from a `ConditionResist` value on the combat profile (0% by default). Resonance-triggered secondary effects (e.g., "applies Burning at resonance 3") use this system.

### ⬜ Slice 3B.5 — Defeat & Incapacitation

Delivers: What happens at 0 HP. Replaces D&D death saves. Design decision needed — knockout, last stand, or bleedout model (see `GAME_DESIGN.md`). Allies can stabilize incapacitated combatants. The encounter-end check from 3B.0 is updated so "no combatants left standing" accounts for incapacitated-but-not-dead combatants. No stat dependency. Runs right after 3B.1.

---

## Phase 3C — Per-Player Hidden Information

*Goal: Each player's client renders a personalized view based on what their character can perceive. This is a core pillar.*

### ⬜ Slice 3C.1 — Vision Ranges & Fog of War

Delivers: Each character has a vision range (`VisionRange` on the combat profile). Cells outside range are fogged per-player. Different players see different amounts of the map. Server computes per-player visibility and filters all broadcasts per-recipient. GM always sees everything. Ley points are visible through fog.

### ⬜ Slice 3C.2 — Information Sharing

Delivers: When one player sees an enemy, that information can propagate to allies based on campaign settings. Design decision needed — auto-share within range, manual callout (costs AP), or full share. Stale intel renders differently from fresh sightings.

*Stat deferral impact:* this was Influence's only combat role (share range = Influence × 2). Without stats, the model must work on its own: a fixed share range, a `ShareRange` profile value, or a callout action/ability that costs AP. The callout option fits best with AP and abilities already in place. If Influence ever returns, its job has to be justified against whatever this slice ships.

### ⬜ Slice 3C.3 — Hidden Enemies & Ambush

Delivers: Enemies can be hidden until they enter a player's vision range or attack. GM sets hidden state during encounter setup. Enables genuine ambush encounters with a surprise mechanic. Spotting checks, if any, use a `Perception` value on the combat profile.

---

## Phase 3D — Adaptive Encounters

*Goal: Enemies make their own decisions based on encounter state, reducing GM workload and creating dynamic combat.*

### ⬜ Slice 3D.1 — Tier 1 AI: Instinctive Behavior

Delivers: Simplest enemy AI. Attack nearest visible threat, flee when HP is low. No coordination. GM can override any AI-planned action.

*After 3B.0:* the base (`EnemyAi` in Rules, AI-controlled flag, auto plan + lock-in) already exists from the manual testing spike. This slice adds fleeing, respecting per-player vision (target only what the AI's side can see), the GM override, and `behavior_tier`.

### ⬜ Slice 3D.2 — Tier 2 AI: Tactical Behavior

Delivers: Focus fire on wounded targets, prioritize squishy characters, use terrain for cover, retreat and regroup when casualties are heavy.

### ⬜ Slice 3D.3 — Tier 3 AI: Strategic Behavior

Delivers: Coordinated enemy groups that adapt to player patterns over multiple rounds. One draws attention while another flanks. Tracks focus fire patterns and ability usage to counter predictable play.

### ⬜ Slice 3D.4 — Dynamic Environments

Delivers: Environmental effects that change the battlefield during combat — fire spread, destructible terrain, interactive objects. Volatile ley point behaviors (drift, surge, depletion, eruption) also land here. Dynamic resonance changes (fire spread → flame resonance, mass death → death resonance).

---

## Phase 4 — Feature Depth

*These slices add richness without changing the core loop. Order is flexible.*

### ⬜ Slice 4.1 — AoE Abilities & Spatial Targeting

Extends 3B.3. Abilities that affect areas (cone, sphere, line). Player places a shape on the grid during planning. Per-player vision applies — you can't see who's in a cell you can't see.

### ⬜ Slice 4.2 — Inventory & Equipment

Characters carry and equip items that modify their combat profile and available actions. Consumables are usable as planned actions during combat, costing AP.

*Candidate to pull forward:* if items should inform the stats decision, run this slice after Phase 3C and before that decision. An "equipment loadout" (fixed per encounter, no persistent inventory) is enough for that purpose; persistent inventory belongs with progression.

### ⬜ Slice 4.3 — GM Manual Overrides

Escape hatches for situations the rule engine doesn't cover. GM can override HP, apply/remove conditions, reveal/hide enemies, add/remove environment effects. All overrides logged to `game_events`.

### ⬜ Slice 4.4 — Reinforcements & Encounter Triggers

GM defines trigger conditions that spawn enemies or change the environment mid-encounter. Trigger system evaluated at end of each resolution phase.

---

## Phase 5 — Hardening

Reconnection resilience, Redis distributed locking during resolution, Redis persistence configuration, load testing (5–10 concurrent WebSocket connections), auth audit on GM-only methods, exhaustive resolution edge case testing (mutual kills, movement collisions, targeting defeated combatants, AoE through fog).

---

## Deferred: Character Stats & Progression

*Not scheduled. Revisit after Phase 3C (and optionally Slice 4.2) is playable.*

Covers: whether characters have core stats at all, and if so which ones and how they map to combat profile values; character creation (point-buy or otherwise); ability acquisition (trees, advancement points, milestones); persistent inventory; removing D&D `Race` / `Class` / `Level` / `BaseStats` from `Character`.

**Why deferred:** stats only matter through the derived values they produce, and the most important consumers of those values (AP, abilities, conditions, vision, info sharing, items) don't exist yet. Hand-authored combat profiles let a single battle be built and played first; the stat design can then be judged against real abilities and items instead of guessed in advance.

**The open question — are stats needed?** The candidate six-stat model (Power, Speed, Precision, Resilience, Focus, Influence; formulas in `DESIGN_SPIKE.md` Decision 4) is kept as reference, not a commitment. Things to evaluate at decision time:
- Several candidate stats map one-to-one onto a single derived value (Focus → vision, Influence → share range, Resilience → HP). A stat that drives one number is that number renamed, with no decision attached. Stats pay off when one stat feeds several values and forces trade-offs — Speed does this, possibly too strongly (movement, dodge, AP, contested-cell priority).
- Stats give a common balance baseline and character identity independent of abilities and gear. Without them, identity comes entirely from loadouts.
- **Retrofit path:** if stats are adopted, implementing them means writing a stats → combat profile function, calibrated against the hand-tuned profiles in use. Abilities and items authored against profiles may need rebalancing; keeping profile values on a consistent scale from 3B.1 onward reduces that cost.

---

## Suggested Sequencing

```
Done        3A.1 Planning Phase → 3A.2 Resolution Engine → 3A.3 GM Enemy Planning
Done        3B.0 Manual Testing Spike (encounter end #90 → enemy AI #91 → Quick Battle)
Done        3B.1 Combat Profiles
Done        3B.2 Action Points
Next        3B.5 Defeat & Incapacitation
Then        3B.3 Abilities & Cooldowns → 3B.4 Conditions
Then        3C.1 Vision & Fog → 3C.2 Info Sharing → 3C.3 Hidden Enemies
Decision    [optional: 4.2 Items as fixed loadouts] → Character Stats & Progression decision
Later       3D.1–3D.4 Adaptive Encounters, remaining Phase 4, Phase 5 hardening
```

Phase 3A was the critical path and is done. The goal of 3B + 3C is one complete, replayable battle on a single battlefield with hard-coded combatants. 3D has light dependencies on 3B/3C and can be interleaved once that battle plays end-to-end.

---

## Migration Notes

1. **3A.1 breaks the old flow** — `StartEncounter` no longer rolls initiative, `EndTurn` is removed. The game is unplayable until 3A.2 lands.
2. **3A.1 + 3A.2 are a single sprint.** Don't ship one without the other.
3. **Sequential encounter code is dead after 3A.** Remove `EndTurn` / `TurnAdvanced` / `activeTurnCombatantId` cleanly.
4. **Existing NGXS tests will break.** Rewrite alongside slice work.
5. **The smoke test needs a full rewrite** after 3A.2.

---

## Database Entities

### Current (Post Phase 2)

| Entity | Key Fields |
|---|---|
| **Campaign** | Id, Name, CreatedAt; has many Members[], Sessions[] |
| **CampaignMember** | CampaignId, UserId, Role (`Gm` \| `Player`); unique on (CampaignId, UserId) |
| **GameSession** | Id, CampaignId, JoinCode (6-char alphanumeric, unique), StartedAt |
| **Character** | Id, UserId, Name, Race, Class, Level, BaseStats (JSONB: Str/Dex/Con/Int/Wis/Cha), MaxHp, CurrentHp |
| **Encounter** | Id, GameSessionId, Status (`Active` \| `Ended`), StartedAt; unique-active index on GameSessionId |
| **EncounterCombatant** | Id, EncounterId, UserId, CharacterId, CharacterName, Initiative, InitiativeRoll, DexModifier |
| **GameEvent** | Id, EncounterId, OccurredAt, EventType (string), Payload (JSONB); append-only log |

### Changes by Slice

**3A.1** — Remove Initiative-related fields from `EncounterCombatant` (Initiative, InitiativeRoll, DexModifier). Add `RoundNumber` to `Encounter`.

**3B.1** (done) — `Character.CombatProfile` (string preset key, default `skirmisher`). `EncounterCombatant.Profile` (JSONB snapshot: profile id and name, MaxHp, MovementRange, Speed, MeleeAccuracy, RangedAccuracy, Dodge, BaseDamage) replaces the 3B.0 `MaxHp` / `Speed` / `DamageDie` columns. Presets live in `CombatProfiles/profiles.json`, not a table. `Race`, `Class`, `Level`, `BaseStats`, `MaxHp`, `CurrentHp` on `Character` are left untouched and unused by combat. Monsters get a preset too (staged with the monster name). Migration `AddCombatProfiles`.

**3B.2** (done) — `MaxAp` added to the `EncounterCombatant.Profile` JSONB snapshot (null on older rows, read as the default 4). Migration `AddMaxAp` only updates the model snapshot.

**3B.3** — New table: `ability_definitions` (id, name, description, ap_cost, cooldown_rounds, range, target_type, effect JSONB, tags[]). Loadouts: either a `character_abilities` (character_id, ability_definition_id) join table or abilities listed on the combat profile preset — pick in the sprint doc. No acquisition rules. Drop or repurpose `spell_definitions` and `character_known_spells`.

**3B.4** — New table: `condition_definitions` (id, name, effects JSONB, description). Effects JSONB: `{ blocksActions, damagePerRound, movementModifier, ... }`.

**3B.5** — Add `is_incapacitated`, `is_dead` to character state (Postgres or Redis-only during encounters — decision deferred).

**3B.0** (done) — `Encounter.EndedAt`; `Encounter.Status` becomes `Ended` when one side is defeated. `EncounterCombatant` gets `IsAiControlled` and nullable preset stat overrides `MaxHp`, `Speed`, `DamageDie` (what 3B.1 combat profiles grow into). Party combatants may have no `Character` row. A separate `Side` column turned out not to be needed: `CombatantType` (`player` / `monster`) already separates party and enemies. Battle presets live as JSON files in the server, not in the database.

**Deferred (Stats & Progression)** — Rework or remove `Character.BaseStats`, `Race`, `Class`, `Level`; persistent inventory and ability acquisition.

**3D.1** — Add `behavior_tier INT` to monster definitions or `encounter_combatants` (GM-overridable per encounter).

**3D.4** — New table: `environment_effect_definitions` (id, name, behavior JSONB). New table (deferred from `MAGIC_SYSTEM.md`): `encounter_templates` for persistent encounter design.

**4.2** — New tables: `item_definitions`, `character_inventory`.

---

## Redis State Contracts

### Current Keys (Post Phase 2)

| Key Pattern | Type | Description |
|---|---|---|
| `session:{sessionId}:players` | Hash | field = `{userId}:{displayName}:{characterId}`, value = connectionId |
| `session:{sessionId}:active_encounter` | String (JSON) | encounter metadata including `activeTurnCombatantId`, `initiativeOrder` |
| `encounter:map:{encounterId}` | String (JSON) | gridWidth, gridHeight, positions, fogOfWar |
| `character:live:{encounterId}` | Hash | field = combatantId, value = HP and combat state |
| `encounter:defeated:{encounterId}` | Set | defeated combatant IDs |

### Changes by Slice

**3A.1** — Rework `session:{sessionId}:active_encounter`: remove `activeTurnCombatantId`, `initiativeOrder`. Add `roundPhase` (`planning` | `resolving` | `between_rounds`), `roundNumber`, `planningTimerExpiresAt`. New key `encounter:{encounterId}:planned_actions` (Hash): one field per combatant ID, value is JSON action array. New key `encounter:{encounterId}:locked_in` (Set): combatant IDs that have locked in.

**3B.0** (done) — `CombatantRef` entries in `session:{sessionId}:active_encounter` gain `isAiControlled`; `active_encounter` gains `planningTimerEnabled` (false for Quick Battle) and is deleted when the encounter ends (the winner goes out in `EncounterEnded`). `character:live` is keyed by combatant ID when there's no `Character` row (as for monsters). Pending monsters are stored as JSON `{ name, isAiControlled }`.

**3B.1** (done) — `character:live` hash: the `class` field is replaced by `profile` (the preset key). The values themselves are read from the Postgres snapshot on `EncounterCombatant`, not duplicated in Redis. Pending monsters gain `profile` (`{ name, isAiControlled, profile }`).

**3B.2** (done) — Nothing new in Redis. Planned actions are multi-entry `PlannedActionArray`s; AP is checked when a plan is submitted and resets every round, so no `currentAp` is stored, and entries carry no `apCost` (the server computes costs).

**3B.3** — Add `abilityCooldowns` JSONB to `character:live` (`{ abilityId: roundsRemaining }`). Add ley point and resonance data to `encounter:map:{encounterId}`: `leyPoints[]` (position, power, resonance, volatility, radius) and `resonanceProfile` (map-level resonance levels by type). Optional `resonanceZones[]` for cell-level overrides (deferred to 3D.4).

**3B.4** — Extend `character:live` conditions array: `{ conditionId, name, roundsRemaining, sourceId, effects }`.

**3C.1** — New key `visibility:{encounterId}:{characterId}` per player, containing their currently visible cell set. Recomputed after each resolution phase.

**3C.2** — New key `shared_intel:{encounterId}:{characterId}` with reported enemy positions and the round they were reported.

**3C.3** — Add `isHidden: boolean` to monster combatant state.

**3D.3** — New key `encounter:{encounterId}:ai_memory` — cross-round pattern tracking (focus target history, ability usage frequency per player).

**3D.4** — Add `environmentEffects[]` to `encounter:map` (type, affected cells, remaining duration, behavior parameters).

---

## Action & Resolution Contracts

### PlannedAction Schema

Defined in `TICK_RESOLUTION.md`. The schema evolves across slices but is always an action array:

**3A (single action):**
```json
{
  "actions": [
    {
      "type": "move | melee_attack | ranged_attack | defend",
      "targetCell": { "x": 5, "y": 8 },
      "targetCombatantId": "abc-123"
    }
  ]
}
```

**3B.2+ (AP budget):**
```json
{
  "actions": [
    { "type": "move", "targetCell": { "x": 5, "y": 8 } },
    { "type": "melee_attack", "targetCombatantId": "abc-123" }
  ]
}
```

As shipped in 3B.2: the queue costs 1 + 2 = 3 AP, which must fit the combatant's `MaxAp`. Costs come from `RulesConfig` and are sent to clients in `PlanningPhaseStarted.apCosts`; entries don't carry them, and there's no `totalApSpent` / `maxAp` on the wire plan.

**3B.3+ (prepared reactions):**
```json
{
  "actions": [
    { "type": "move", "targetCell": { "x": 5, "y": 8 }, "apCost": 1 },
    {
      "type": "prepared",
      "apCost": 2,
      "trigger": { "condition": "enemy_casts_ability", "filter": { "range": 5 } },
      "reaction": { "type": "counterspell", "timing": "interrupt" }
    }
  ],
  "totalApSpent": 3,
  "maxAp": 5
}
```

### Resolution Pipeline

Per-tick, three phases in fixed order. See `TICK_RESOLUTION.md` for the full loop and `DESIGN_SPIKE.md` for damage/HP pipeline specifics.

```
for tick in 0..max_ticks:
    Phase 1 (Movement)  — path-based, cell-by-cell, Speed tiebreaker for conflicts
    Phase 2 (Attacks)    — snapshot HP → hit checks → damage → net deltas → apply → defeat check
    Phase 3 (Effects)    — conditions, environment, defend actions

End of round: tick conditions, process environment, decrement cooldowns, reset AP, advance round
```

### Ability Effect Schema (3B.3)

Defined in `MAGIC_SYSTEM.md`. Stored in `ability_definitions.effect` JSONB:

```json
{
  "baseDamage": 6,
  "targetType": "single",
  "range": 8,
  "tags": ["flame", "ranged"],
  "resonanceScaling": {
    "flame": { "damagePerLevel": 2, "thresholdEffects": { "2": "ember", "3": "burning" } }
  }
}
```

### Damage Pipeline

See `DESIGN_SPIKE.md` Decision 4 and `MAGIC_SYSTEM.md`. Slots are built from 3A, filled progressively:

```
baseDamage + statModifier + weaponBonus + abilityModifier + resonanceBonus + leyBonus
```

### SignalR Hub Methods (Planned End State)

| Method | Direction | Added In | Auth |
|---|---|---|---|
| `PlanAction(joinCode, plannedAction)` | Client → Server | 3A.1 | Player (own combatant) |
| `LockIn(joinCode)` | Client → Server | 3A.1 | Player |
| `ForceResolve(joinCode)` | Client → Server | 3A.1 | GM only |
| `PlanEnemyAction(joinCode, combatantId, plannedAction)` | Client → Server | 3A.3 | GM only |
| `LockInEnemies(joinCode)` | Client → Server | 3A.3 | GM only |
| `StartQuickBattle(presetId)` | Client → Server | 3B.0 | Any signed-in user (becomes GM of a new campaign/session) |
| `AddMonster(joinCode, monsterName, isAiControlled, profileId?)` | Client → Server | 3B.1 (`profileId`; null → default enemy profile) | GM only |
| `PlanAction(joinCode, plannedAction, combatantId?)` | Client → Server | 3B.0 (optional `combatantId`) | Player (owned combatant) |
| `LockIn(joinCode)` | Client → Server | 3B.0 (locks all owned living combatants) | Player |

| Broadcast | Direction | Added In | Notes |
|---|---|---|---|
| `PlanningPhaseStarted` | Server → Group | 3A.1 | round number, timer, combatants (with `maxAp` and `movementRange` since 3B.1/3B.2), `apCosts` per action type (3B.2) |
| `PlayerLockedIn` | Server → Group | 3A.1 | characterName only, no action details |
| `AllLockedIn` | Server → Group | 3A.1 | resolution about to begin |
| `ResolutionCompleted` | Server → Group (per-player in 3C.1) | 3A.2 | ordered resolution steps for playback; filtered per-recipient after 3C.1 |
| `RoundStarted` | Server → Group | 3A.2 | signals next planning phase |
| `CombatantDefeated` | Server → Group | 3B.0 | contract existed since 3A; first broadcast in 3B.0 |
| `EncounterEnded` | Server → Group | 3B.0 | winning side; no further planning phase |
| `VisibilityUpdated` | Server → Client | 3C.1 | per-player visible cells and revealed/hidden combatants |

Removed from Phase 2: `EndTurn`, `TurnAdvanced`.

---

## Combat Profile Values

What the rules engine consumes per combatant from 3B.1 on. Values are hand-authored per preset until the stats decision.

| Value | Used by | Added in |
|---|---|---|
| `MaxHp` | HP pipeline | 3B.1 |
| `MovementRange` | Move validation: cells per round, shared by all moves (3B.2) | 3B.1 |
| `Speed` | Contested-cell priority | 3B.1 |
| `MeleeAccuracy`, `RangedAccuracy` | Hit check | 3B.1 |
| `Dodge` | Hit check threshold (d20 + attacker accuracy ≥ target Dodge) | 3B.1 |
| `BaseDamage` (a die, e.g. `d8`) | Damage pipeline | 3B.1 |
| `MaxAp` | AP budget (queue cost per round) | 3B.2 |
| `ConditionResist` | Condition application | 3B.4 |
| `VisionRange` | Fog of war | 3C.1 |
| `ShareRange` (if the chosen model needs it) | Info sharing | 3C.2 |
| `Perception` | Spotting hidden enemies | 3C.3 |

## Stat-to-Mechanic Mapping (Deferred)

*Reference only — not scheduled.* The candidate six-stat model from `DESIGN_SPIKE.md` Decision 4. If adopted, each stat feeds the combat profile values above. See [Deferred: Character Stats & Progression](#deferred-character-stats--progression).

| Stat | Governs |
|---|---|
| **Power** | Melee damage modifier, carrying capacity |
| **Speed** | Movement range (2 + Speed), resolution priority tiebreaker, dodge chance (8 + Speed), max AP (3 + floor(Speed/3)) |
| **Precision** | Ranged attack bonus, ranged damage modifier, critical hit chance (Precision × 2%) |
| **Resilience** | Max HP (10 + Resilience × 5), condition resistance (Resilience × 5%) |
| **Focus** | Ability resource pool, vision range (3 + Focus), perception |
| **Influence** | Information sharing range (Influence × 2), team buff radius |

Stat range: 1–10 per stat, 30-point budget across 6 stats. These formulas are a starting point, not a commitment.

---

## Tuning Defaults

Values that are not blocking decisions — build with these, adjust through playtesting.

| Setting | Default | Notes |
|---|---|---|
| Planning timer | 30 seconds | GM-configurable per encounter |
| Default action on timeout | Defend in place (`[{ "type": "defend" }]`) | Least punishing |
| Ally planning visibility | Show target only ("Player X is targeting Enemy B"), not action type | Campaign toggle later |
| Resolution playback duration | ~3 seconds per round | Skippable if all players click "skip" |
| Melee range | Adjacent (Chebyshev ≤ 1) | Post-movement |
| Ranged attack range | Unlimited in 3A | Range limits tied to Precision in 3B |
| Grid size | 20×20 | Unchanged from Phase 2 |
| Movement range | Per combatant (`MovementRange` on the combat profile, 4–8 in the shipped presets) | Was 6 cells Chebyshev for everyone in 3A |

Ley point and resonance tuning defaults are in `MAGIC_SYSTEM.md`.

---

## Design Decisions Log

| Decision | Choice | Source | Rationale |
|---|---|---|---|
| Conflict resolution model | Phase-based macro + simultaneous within phase, wrapped in tick loop | `DESIGN_SPIKE.md` D1, `TICK_RESOLUTION.md` D1 | Learnable mental model; tick loop supports AP without structural changes |
| Mutual kills | Enabled | `DESIGN_SPIKE.md` D2 | Natural consequence of simultaneous damage; no tiebreaker needed |
| Healing + damage | Net calculation (all deltas summed, then applied) | `DESIGN_SPIKE.md` D3 | Never temporarily "dead"; well-predicted heal saves from lethal damage |
| Combat values before stats | Hand-authored combat profiles; stats & progression deferred | This roadmap (Nov 2026) | Rules engine only consumes derived values; build abilities, conditions, vision (and optionally items) first, then decide what stats need to do |
| Combat profile storage | Presets in a JSON file; each combatant stores a snapshot of its resolved profile | 3B.1 | Running encounters don't change when presets are retuned; Quick Battle overrides apply once, at creation |
| Base damage | A die (1dN), not a flat number | 3B.1 | Keeps the existing roll order and damage feel; switching to flat or die + bonus is a later tuning call |
| Resource model | AP (per round) + Cooldowns + Consumables | `GAME_DESIGN.md` | AP creates round-to-round tension; cooldowns prevent spam; consumables add session-level strategy |
| Character progression | Abilities over classes | `GAME_DESIGN.md` | Composable; avoids rigid hierarchies; ability choice affects resolution phase timing |
| Default action on timeout | Defend in place | `DESIGN_SPIKE.md` D5 | Least punishing; doesn't waste resources or move character |
| Movement resolution | Path-based, cell-by-cell (not teleport) | `TICK_RESOLUTION.md` D6 | Required for reactive trigger checks at cell transitions; Speed tiebreaker for contested cells |
| Reactive actions | Passive (free, always available) + Prepared (costs AP, queued) | `TICK_RESOLUTION.md` D4 | Two categories, three timings (interrupt/simultaneous/response); not implemented until 3B+ but hooks built in 3A |
| Opportunity attacks | Provoked by entering or leaving adjacency; Disengage avoids passives only | `TICK_RESOLUTION.md` D5 | Sticky melee creates positional tension; Disengage is a meaningful AP trade |
| Environmental magic | Ley points (positional power) + Resonance (elemental/metaphysical affinity) | `MAGIC_SYSTEM.md` | Magic is external; environment shapes abilities; ley points are tactical objectives |
| Resonance penalties | No penalties, only bonuses (v1) | `MAGIC_SYSTEM.md` | Simplest; add opposing-resonance penalties if testing shows diversity is too safe |
| Enemy AI | 3-tier system (Instinctive / Tactical / Strategic) | `GAME_DESIGN.md` | Complexity matches narrative fiction; GM can override any tier |
| Per-player vision | Computed server-side, filtered per-client | `GAME_DESIGN.md` | Server is authority; prevents cheating; Focus stat makes vision meaningful |

### Open Design Decisions

| Decision | Needed By | Options | Current Lean |
|---|---|---|---|
| Defeat model | 3B.5 | Knockout / Last stand / Bleedout | Not decided |
| Info sharing model | 3C.2 | Auto-share within fixed range / Manual callout (costs AP) / Full share | Not decided; no Influence stat to lean on, which favors callout |
| Core stats | After 3C | Six-stat model / fewer stats / no stats (profiles from abilities + gear) | Not decided — see Deferred: Character Stats & Progression |
| Win condition when an AI side and a human side share a session | 3B.0 | Last side standing / per-preset objectives | Last side standing |
| Ability loadout storage | 3B.3 | `character_abilities` join table / listed on profile preset | Not decided |
| Unused prepared actions refund AP? | 3B.3 | Full / partial / none | No refund |
| Multiple prepared reactions on same trigger? | 3B.3 | All fire / only first | All fire (wasted duplicates) |
| Ranged attacks in melee penalty? | 3B+ | Penalty / no penalty | Penalty (disadvantage) |
| Player-drawn movement paths? | 3B+ | Server-computed shortest / player-drawn | Server-computed; player-drawn is a UI enhancement |
