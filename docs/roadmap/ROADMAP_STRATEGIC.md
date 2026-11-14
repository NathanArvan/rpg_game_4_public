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

**Phase 3A** (merged April–May 2026, PRs #62–#69) — Simultaneous planning, tick-based resolution with playback, GM enemy planning, and a combat UI pass. The sequential turn system has been removed. Known gaps carried forward: no end-of-encounter detection (`EncounterEnded` is never broadcast), and the planning timer is 300s rather than the 30s default below.

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

*Goal: Replace D&D 5E assumptions with the game's own stat system, abilities, and resources. Each slice replaces one legacy concept.*

### ⬜ Slice 3B.1 — Custom Stats & Character Rework

Delivers: Characters use six custom stats (Power, Speed, Precision, Resilience, Focus, Influence) instead of D&D ability scores. All derived values (HP, movement range, vision range, damage, dodge) flow from the new stats. Race, Class, and Level are removed or made cosmetic.

### ⬜ Slice 3B.2 — Action Points

Delivers: Each round, characters have an AP budget to spend on actions during planning. Moving, attacking, and using abilities all cost AP. Multiple actions can be planned per round. AP resets each round. The tick loop's queue length constraint lifts — this is where the multi-action architecture pays off.

### ⬜ Slice 3B.3 — Abilities & Cooldowns

Delivers: Characters have abilities (discrete mechanical packages) instead of class features. Abilities have AP costs, cooldown timers, range, target types, and effect definitions. Environmental magic (ley points and resonance) also lands in this slice — abilities need resonance to be meaningful. See `MAGIC_SYSTEM.md` for the full design.

### ⬜ Slice 3B.4 — Conditions & Status Effects

Delivers: Abilities and environmental effects can apply conditions (stunned, burning, slowed, etc.) that modify combatant behavior for a duration. Conditions are the game's own vocabulary. Resilience stat governs resistance. Resonance-triggered secondary effects (e.g., "applies Burning at resonance 3") use this system.

### ⬜ Slice 3B.5 — Defeat & Incapacitation

Delivers: What happens at 0 HP. Replaces D&D death saves. Design decision needed — knockout, last stand, or bleedout model (see `GAME_DESIGN.md`). Allies can stabilize incapacitated combatants.

---

## Phase 3C — Per-Player Hidden Information

*Goal: Each player's client renders a personalized view based on what their character can perceive. This is a core pillar.*

### ⬜ Slice 3C.1 — Vision Ranges & Fog of War

Delivers: Each character has a vision range (derived from Focus). Cells outside range are fogged per-player. Different players see different amounts of the map. Server computes per-player visibility and filters all broadcasts per-recipient. GM always sees everything. Ley points are visible through fog.

### ⬜ Slice 3C.2 — Information Sharing

Delivers: When one player sees an enemy, that information can propagate to allies based on Influence stat and campaign settings. Design decision needed — auto-share within range, manual callout (costs AP), or full share. Stale intel renders differently from fresh sightings.

### ⬜ Slice 3C.3 — Hidden Enemies & Ambush

Delivers: Enemies can be hidden until they enter a player's vision range or attack. GM sets hidden state during encounter setup. Enables genuine ambush encounters with a surprise mechanic.

---

## Phase 3D — Adaptive Encounters

*Goal: Enemies make their own decisions based on encounter state, reducing GM workload and creating dynamic combat.*

### ⬜ Slice 3D.1 — Tier 1 AI: Instinctive Behavior

Delivers: Simplest enemy AI. Attack nearest visible threat, flee when HP is low. No coordination. GM can override any AI-planned action.

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

Characters carry and equip items that modify stats and available actions. Equipment modifies derived combat values. Consumables are usable as planned actions during combat, costing AP.

### ⬜ Slice 4.3 — GM Manual Overrides

Escape hatches for situations the rule engine doesn't cover. GM can override HP, apply/remove conditions, reveal/hide enemies, add/remove environment effects. All overrides logged to `game_events`.

### ⬜ Slice 4.4 — Reinforcements & Encounter Triggers

GM defines trigger conditions that spawn enemies or change the environment mid-encounter. Trigger system evaluated at end of each resolution phase.

---

## Phase 5 — Hardening

Reconnection resilience, Redis distributed locking during resolution, Redis persistence configuration, load testing (5–10 concurrent WebSocket connections), auth audit on GM-only methods, exhaustive resolution edge case testing (mutual kills, movement collisions, targeting defeated combatants, AoE through fog).

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

Phase 3A is the critical path — nothing else works until the simultaneous turn system is in place. After that, 3B, 3C, and 3D have lighter dependencies on each other and can be interleaved.

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

**3B.1** — Rework `Character`: replace `BaseStats` JSONB shape `{ str, dex, con, int, wis, cha }` → `{ power, speed, precision, resilience, focus, influence }`. Remove or make cosmetic: `Race`, `Class`, `Level`. Add `MovementRange INT`, `VisionRange INT`.

**3B.3** — New table: `ability_definitions` (id, name, description, ap_cost, cooldown_rounds, range, target_type, effect JSONB, tags[]). New table: `character_abilities` (character_id, ability_definition_id). Drop or repurpose `spell_definitions` and `character_known_spells`.

**3B.4** — New table: `condition_definitions` (id, name, effects JSONB, description). Effects JSONB: `{ blocksActions, damagePerRound, movementModifier, ... }`.

**3B.5** — Add `is_incapacitated`, `is_dead` to character state (Postgres or Redis-only during encounters — decision deferred).

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

**3B.1** — Update `character:live` hash: replace `class` field with stat-derived combat values (`attackBonus`, `damageBase`, `dodgeChance`, `maxAp`).

**3B.2** — Add `maxAp`, `currentAp` to `character:live`. Add `apCost` field to each entry in planned actions.

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
    { "type": "move", "targetCell": { "x": 5, "y": 8 }, "apCost": 1 },
    { "type": "melee_attack", "targetCombatantId": "abc-123", "apCost": 2 }
  ],
  "totalApSpent": 3,
  "maxAp": 5
}
```

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

| Broadcast | Direction | Added In | Notes |
|---|---|---|---|
| `PlanningPhaseStarted` | Server → Group | 3A.1 | round number, timer, per-character available actions; adds AP info in 3B.2 |
| `PlayerLockedIn` | Server → Group | 3A.1 | characterName only, no action details |
| `AllLockedIn` | Server → Group | 3A.1 | resolution about to begin |
| `ResolutionCompleted` | Server → Group (per-player in 3C.1) | 3A.2 | ordered resolution steps for playback; filtered per-recipient after 3C.1 |
| `RoundStarted` | Server → Group | 3A.2 | signals next planning phase |
| `VisibilityUpdated` | Server → Client | 3C.1 | per-player visible cells and revealed/hidden combatants |

Removed from Phase 2: `EndTurn`, `TurnAdvanced`.

---

## Stat-to-Mechanic Mapping

Defined in `DESIGN_SPIKE.md` Decision 4. Placeholder values used in 3A; real formulas land in 3B.1.

| Stat | Governs |
|---|---|
| **Power** | Melee damage modifier, carrying capacity |
| **Speed** | Movement range (2 + Speed), resolution priority tiebreaker, dodge chance (8 + Speed), max AP (3 + floor(Speed/3)) |
| **Precision** | Ranged attack bonus, ranged damage modifier, critical hit chance (Precision × 2%) |
| **Resilience** | Max HP (10 + Resilience × 5), condition resistance (Resilience × 5%) |
| **Focus** | Ability resource pool, vision range (3 + Focus), perception |
| **Influence** | Information sharing range (Influence × 2), team buff radius |

Stat range: 1–10 per stat, 30-point budget across 6 stats. These formulas are a starting point for playtesting.

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
| Movement range | 6 cells Chebyshev in 3A | Becomes 2 + Speed in 3B.1 |

Ley point and resonance tuning defaults are in `MAGIC_SYSTEM.md`.

---

## Design Decisions Log

| Decision | Choice | Source | Rationale |
|---|---|---|---|
| Conflict resolution model | Phase-based macro + simultaneous within phase, wrapped in tick loop | `DESIGN_SPIKE.md` D1, `TICK_RESOLUTION.md` D1 | Learnable mental model; tick loop supports AP without structural changes |
| Mutual kills | Enabled | `DESIGN_SPIKE.md` D2 | Natural consequence of simultaneous damage; no tiebreaker needed |
| Healing + damage | Net calculation (all deltas summed, then applied) | `DESIGN_SPIKE.md` D3 | Never temporarily "dead"; well-predicted heal saves from lethal damage |
| Stat system | Power, Speed, Precision, Resilience, Focus, Influence | `GAME_DESIGN.md` | Setting-agnostic; each stat mechanically relevant to simultaneous + vision systems |
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
| Info sharing model | 3C.2 | Auto-share within range / Manual callout (costs AP) / Full share | Not decided |
| Unused prepared actions refund AP? | 3B.3 | Full / partial / none | No refund |
| Multiple prepared reactions on same trigger? | 3B.3 | All fire / only first | All fire (wasted duplicates) |
| Ranged attacks in melee penalty? | 3B+ | Penalty / no penalty | Penalty (disadvantage) |
| Player-drawn movement paths? | 3B+ | Server-computed shortest / player-drawn | Server-computed; player-drawn is a UI enhancement |
