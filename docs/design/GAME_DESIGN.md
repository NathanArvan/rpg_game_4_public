# Game Design — Core Rules & Philosophy

> **Status:** Draft. This document captures the design identity of the game — the rules, mechanics, and principles that make it its own system rather than a digitization of an existing tabletop RPG.

---

## Design Identity

This is a **tactics-first, setting-agnostic** digital RPG. The mechanical layer is the product. Any fantasy setting, homebrew world, or narrative tone can be draped over it — the rules don't assume elves, dragons, or any particular fiction.

The game is built for a **digital platform from the ground up.** It is not a simulation of a physical tabletop. Wherever the digital medium enables something that cardboard, dice, and human memory cannot do, the game should exploit that advantage. Three principles follow from this:

1. **Simultaneous planning, simultaneous resolution.** All players plan their turns at the same time. When everyone is ready, all actions — player and enemy — resolve together. No waiting for your turn. No watching someone else think for three minutes.

2. **Per-player hidden information.** Each player sees only what their character would see. The map, enemy positions, and even ally status can differ between screens. The server is the omniscient narrator; each client gets a filtered view.

3. **The world reacts.** Encounters are not static puzzles. Enemies adapt their tactics, the environment shifts, and the game state evolves in ways that would be impossibly complex for a human GM to track in real time.

---

## Simultaneous Turn Resolution

This is the single biggest departure from traditional RPG combat. Instead of an initiative order where one combatant acts at a time, combat flows in **rounds** composed of two phases:

```
┌─────────────────────────────────────────┐
│  PLANNING PHASE                         │
│  All players choose actions at once.    │
│  GM queues enemy actions.               │
│  A timer (configurable) counts down.    │
│  Players can see allies planning but    │
│  not the specifics of their choices.    │
└─────────────────┬───────────────────────┘
                  │ All players lock in
                  ▼
┌─────────────────────────────────────────┐
│  RESOLUTION PHASE                       │
│  Server resolves all actions together.  │
│  Results animate/display for everyone.  │
│  New round begins.                      │
└─────────────────────────────────────────┘
```

### Why This Matters

Sequential initiative creates a fundamental pacing problem: in a 5-player game with 4 enemies, each player spends roughly 90% of combat waiting. Simultaneous resolution means everyone is engaged every round, and combat that takes 45 minutes sequentially can resolve in 10.

It also creates a new design space. Players must **anticipate** what allies and enemies will do rather than reacting to known outcomes. Coordination becomes a skill. Miscommunication becomes a risk. This is the kind of tension that emerges naturally from simultaneous systems and doesn't exist in sequential ones.

### The Conflict Problem

When everyone acts at once, conflicts arise. Three models for resolving them are worth considering. The right choice (or hybrid) should be validated through playtesting before committing.

#### Model A — Phase-Based Resolution

Actions are categorized into phases, and each phase resolves simultaneously within itself but sequentially across phases:

```
1. Movement Phase     — all movement resolves simultaneously
2. Fast Action Phase  — ranged attacks, quick spells
3. Melee Phase        — melee attacks resolve after movement is settled
4. Slow Action Phase  — channeled spells, heavy abilities
5. Effect Phase       — conditions tick, AoEs pulse, environment changes
```

**Pros:** Predictable. Players learn to reason about phase ordering. "If I move first and then attacks resolve, I can get into cover before the archer shoots." Tactical depth comes from understanding phase timing.

**Cons:** Can feel rigid. Some actions don't fit neatly into a single phase. Creates a meta where "fast" actions are strictly better because they resolve before reactions are possible.

**Conflict handling:** If two combatants try to move to the same cell, they both stop one cell short (or the one with higher speed stat claims it). If two combatants attack the same target and the first hit kills it, the second attack either hits the corpse (wasted) or is redirected to the nearest valid target (a design choice about forgiveness vs. punishment).

#### Model B — True Simultaneous

Everything resolves at the exact same instant. The server processes all actions as if they happen in parallel.

**Pros:** The purest expression of "everyone acts at once." Mutual kills are possible — two combatants can take each other down in the same round, which creates dramatic moments. No one gets an inherent advantage from acting "first."

**Cons:** Requires the most careful design around edge cases. Healing a character at the same moment they take lethal damage — do they live or die? The answer needs to be consistent and learnable.

**Conflict handling:** All damage is calculated, then all damage is applied simultaneously. A character at 10 HP who takes 15 damage and receives 8 healing in the same round ends at 3 HP (10 - 15 + 8), not dead. Movement conflicts resolved by a deterministic tiebreaker (e.g., character with higher speed stat, or random with visible seed). This model requires a clear "state snapshot → apply all deltas → produce new state" pipeline on the server.

#### Model C — Commitment + Hidden Priority

Players commit actions blind during planning. After lock-in, the server assigns a hidden priority to each action based on a stat (e.g., speed/initiative), randomness, or the action type chosen. Actions then resolve in priority order — but players don't know the order until they see the results.

**Pros:** Preserves the tension of not knowing whether your action will resolve before the enemy's. Creates a reason to invest in speed-like stats. The closest model to "fog of war for time" — you don't know if you'll act before or after the enemy.

**Cons:** Can feel arbitrary if priority isn't well communicated. Players may feel cheated if they planned based on an assumption about ordering. Needs strong UI feedback during resolution to show the sequence clearly.

**Conflict handling:** Since there is a sequence (just hidden during planning), traditional sequential resolution applies. The priority system is the design lever — a character with high speed and a fast action type might get priority 1, while a slow character casting a big spell gets priority 9.

#### Hybrid Considerations

These models aren't mutually exclusive. A likely sweet spot:

- Use **Phase-Based** for the macro structure (movement before attacks before spells).
- Within each phase, use **True Simultaneous** for the resolution (all melee attacks within the melee phase are simultaneous, mutual kills possible).
- Use a **speed stat** as the tiebreaker for conflicts that need one (two characters claiming the same cell in the movement phase).

This gives players a learnable mental model ("movement happens first, then attacks") while preserving the dramatic tension of simultaneous damage within each phase.

### The Planning Phase in Detail

During planning, each player sees:

- Their own character's available actions, movement range, and resources.
- The current state of the map as their character perceives it (filtered by visibility — see Per-Player Hidden Information below).
- **Planning indicators** for allies — visual signals that an ally is targeting a specific enemy or moving to a specific area, without revealing the exact action. Think "Ally A has targeted Enemy B" but not "Ally A is casting Fireball at Enemy B." The level of information shared between allies is a tunable design lever.
- A countdown timer. When it expires, any player who hasn't locked in gets a default action (defend/hold position). This prevents one player from stalling the group indefinitely.

Players can change their planned action freely until they lock in. Once locked in, the action is committed and they wait for other players (with a visual indicator of who's still planning). The GM can force-start resolution before the timer expires if all players are locked in.

### Resolution Playback

After all actions are committed, the resolution phase plays out as an **animated sequence** that all players watch together. This is the "replay" — the server has already computed the results, and the client is rendering them.

This is a major UX moment. The animation should be:

- Fast enough to not feel like a cutscene (target: 3–5 seconds for a typical round).
- Clear enough that every player can see what happened to their action.
- Scrubbbable or replayable so players can review after the fact.

The resolution playback is a place where the feed/combat log matters more than in a sequential game — players need to understand what happened and why, since they didn't see actions resolve one at a time.

---

## Per-Player Hidden Information

In a physical tabletop game, everyone at the table sees the same map (with the possible exception of GM screens). The digital platform removes that constraint. Each player's client renders a **personalized view of the game state** based on what their character can perceive.

### What Each Player Sees

- **Their own character's vision.** Each character has a vision range (in grid cells). Cells outside that range are fogged. Characters with different vision capabilities (darkvision, enhanced perception, etc.) see different amounts of the map.
- **Enemies in their line of sight.** A rogue scouting ahead might see enemies that the rest of the party can't. Those enemies don't appear on other players' maps until they enter someone else's vision or the scout shares the information.
- **Shared information from allies.** When an ally sees an enemy, the server can decide whether that information propagates to the party automatically (everyone shares a telepathic link, perhaps) or only when explicitly communicated. This is a campaign-level setting the GM configures.
- **Environmental hazards and traps.** A character with high perception might see a trap that others don't. The trap exists for everyone, but only the perceiving character's client renders the warning.

### Information Asymmetry as a Mechanic

This isn't just atmosphere — it creates real tactical decisions:

- **The scout role becomes meaningful.** A character who moves ahead to reveal the map is doing something mechanically valuable, not just narratively interesting.
- **Communication is a resource.** If information doesn't auto-share, players need to spend actions or use abilities to warn allies. This can be a meaningful cost during the planning phase.
- **Ambushes work properly.** In a traditional tabletop game, an ambush is hard to pull off because all players can see the GM placing miniatures. Here, enemies can be genuinely hidden until they attack or are spotted.
- **Friendly fire and targeting errors.** If a player targets an area they can't fully see, they might hit an ally who's moved into that area during the simultaneous resolution. This is a natural consequence of imperfect information and simultaneous action — and it's exactly the kind of consequence that creates stories.

### GM Perspective

The GM always sees the full, unfiltered map — all enemies, all traps, all environmental state. The GM's view is the source of truth. The GM can also see what each individual player currently perceives, which helps when narrating or making rulings.

---

## Adaptive Encounters

Enemies in this game are not static stat blocks that follow a predetermined behavior pattern. The server runs enemy decision-making using a rule engine that considers the current state of the encounter.

### Enemy Behavior Tiers

Not every enemy needs to be smart. Complexity should match the narrative fiction of the creature:

**Tier 1 — Instinctive.** Attacks the nearest threat. Flees when HP is low. No coordination. Suitable for beasts, undead, mindless creatures.

**Tier 2 — Tactical.** Focuses fire on wounded targets. Prioritizes squishy characters (low armor). Uses terrain for cover. Retreats to regroup. Suitable for trained soldiers, intelligent monsters.

**Tier 3 — Strategic.** Coordinates with other enemies. One enemy draws attention while another flanks. Casters target clusters. Reserves abilities for optimal moments. Adapts to player patterns — if the party always focuses the same target, Tier 3 enemies protect it. Suitable for bosses, rival adventuring parties, military commanders.

### Adaptation Rules

The "adapts to player patterns" element deserves specifics. The server can track patterns across rounds:

- **Focus fire tracking.** If the party concentrates all attacks on a single target for consecutive rounds, Tier 3 enemies reposition to protect high-value allies or spread out to make focus fire less efficient.
- **Ability pattern recognition.** If a player opens every round with the same ability, enemies begin countering it — interrupting the caster, moving out of the AoE zone, or saving defensive abilities to resist it.
- **Retreat and regroup.** When an enemy group takes heavy casualties, survivors can attempt to retreat to a defensible position, call reinforcements, or change tactics entirely (melee fighters switch to ranged, for example).

This is a place where the digital platform has a massive advantage. A human GM can approximate this, but tracking five enemy behaviors while managing rules, narration, and pacing is exhausting. The server does it for free.

### Dynamic Environments

The encounter map is not a static backdrop. Environmental elements can change during combat:

- **Destructible terrain.** Walls can be broken, bridges can collapse, cover can be destroyed. These changes affect movement, line of sight, and tactical positioning for the rest of the encounter.
- **Hazard escalation.** A fire that starts in one cell can spread to adjacent cells each round. Flooding can raise water levels. Poison gas can fill an enclosed space. These effects are tracked server-side and rendered per-player based on visibility.
- **Interactive objects.** Levers, doors, pressure plates, and other objects that players (or enemies) can interact with during combat to change the battlefield. Pulling a lever might open a gate that releases more enemies — or provides an escape route.

### Procedural Encounter Elements

Some encounter elements can be generated or randomized rather than fully pre-designed by the GM:

- **Reinforcement waves.** The GM defines reinforcement pools and trigger conditions. The server spawns them at appropriate moments based on encounter state (e.g., "after round 3" or "when the boss drops below 50% HP").
- **Random environmental events.** A volcanic battlefield might have random eruptions. A ship battle might have waves that shift combatant positions. These events are seeded randomly but deterministically (same seed produces same sequence) so they can be replayed.
- **Loot and resource spawns.** Health potions, ammunition, or other resources can appear on the battlefield during combat at designated spawn points, creating incentives for movement and map control.

---

## Character Design Philosophy

Characters are defined by **what they can do**, not by which class label they wear. The system should support character building through composable abilities rather than rigid class hierarchies.

### Stats

A small set of core stats that feed directly into mechanical outcomes. These are not tied to any specific fantasy convention:

| Stat | Governs |
|------|---------|
| **Power** | Melee damage, carrying capacity, breaking things |
| **Speed** | Turn priority tiebreakers, movement range, dodge chance |
| **Precision** | Ranged accuracy, critical hit chance, trap detection |
| **Resilience** | Max HP, condition resistance, death save bonuses |
| **Focus** | Spell/ability resource pool, concentration, perception range |
| **Influence** | NPC interactions, team buff radius, information sharing range |

Six stats, each with a clear mechanical identity. No dump stats if the design is right — Speed matters because of simultaneous resolution priority, Influence matters because of information sharing mechanics, and so on.

### Abilities Over Classes

Rather than choosing a class that grants a fixed progression, characters acquire **abilities** — discrete mechanical packages that define what they can do in combat and exploration.

Abilities might be organized into trees, purchased with advancement points, or unlocked through narrative milestones — the exact progression system is a later design decision. The key principle is that a character's mechanical identity is the sum of their chosen abilities, not a class label.

This matters for the simultaneous resolution system because ability choice directly affects which phase your actions fall into, how fast they resolve, and what information you can share with allies.

### Resource Management

Every powerful ability has a cost. The resource system should create round-to-round decision tension:

- **Action Points (AP).** Each round, a character has a budget of AP to spend on actions during planning. Moving costs AP. Attacking costs AP. Abilities cost AP. Players must decide how to allocate limited AP across movement, offense, defense, and utility each round.
- **Cooldowns.** Powerful abilities can't be used every round. Cooldowns are tracked server-side and displayed during planning. Abilities on cooldown are visible but greyed out.
- **Consumable Resources.** Spell slots, ammunition, potions, and other finite resources that deplete across the encounter (or across the session). These force strategic thinking beyond the current round.

---

## What This Changes in the Existing Architecture

The current codebase was built around sequential initiative. Adopting simultaneous resolution requires rethinking several layers. This section maps design decisions to architectural impact so the engineering work can be planned.

### Redis State Changes

The `encounter:live` key currently tracks `activeTurnCombatantId` and a sequential `initiativeOrder`. Under simultaneous resolution:

- **Remove** `activeTurnCombatantId`. There is no single active turn holder.
- **Replace** `initiativeOrder` with `roundPhase` (`planning` | `resolving` | `between_rounds`) and `roundNumber`.
- **Add** a `plannedActions` structure — a per-combatant map of committed actions, written during planning and consumed during resolution.
- **Add** `lockedIn` set — tracks which players have committed their actions. Resolution triggers when all players are locked in (or the timer expires).

### SignalR Message Changes

New messages needed:

- `PlanningPhaseStarted` — broadcast to all clients. Includes round number, timer duration, and per-character available actions/resources.
- `PlayerLockedIn` — broadcast to group. "Player X has locked in." No action details revealed.
- `ResolutionStarted` — broadcast with the full resolution sequence. This is the big payload: an ordered list of every action that resolved, its outcome, and the resulting state changes.
- `RoundEnded` — cleanup, transition to next planning phase.

Removed or reworked messages:

- `TurnAdvanced` — no longer needed. Replaced by `PlanningPhaseStarted`.
- `ActionRejected` — still exists but now fires during planning (e.g., "you can't target that, it's out of range") rather than during a turn.

### Angular State Changes

The `EncounterState` NGXS slice needs significant rework:

- Replace the `activeTurnCombatantId` / `isMyTurn` paradigm with a `roundPhase` model.
- Add a `myPlannedAction` state field for the local player's current plan (before lock-in).
- Add `lockedInPlayers` set for the planning phase UI.
- The `MapState` needs a "planning overlay" mode — showing projected movement, target lines, and AoE placement before committing.

### Server Logic Changes

The `EncounterService` and `CombatService` currently resolve actions inline when a hub method is called. Under simultaneous resolution:

- Hub methods during planning (`PlanAction`, `LockIn`) write to Redis but **don't resolve anything**.
- A new `ResolutionService` triggers when all players lock in (or the timer fires). It reads all planned actions from Redis, resolves them according to the phase model, computes all state deltas, writes results to Redis and Postgres, and returns the full resolution payload for broadcast.
- This is the most complex new server component. It needs to handle every conflict type described in the resolution models above.

---

## Open Questions

These are decisions that should be made through playtesting rather than upfront design:

1. **How much ally information is shared during planning?** Spectrum from "you see exactly what every ally is doing" to "you see nothing until resolution." Probably configurable per campaign.

2. **What's the default action for players who don't lock in?** Defend in place? Repeat last round's action? Do nothing? Each has different strategic implications.

3. **Should mutual kills be possible?** True simultaneous says yes. Some players may find it frustrating. Might be a campaign-level toggle.

4. **How does healing interact with simultaneous damage?** If healing and damage resolve in the same instant, does healing prevent death or just reduce the net? The answer defines whether healers need to predict damage or react to it.

5. **What's the right planning phase timer?** Too short and players feel rushed. Too long and it becomes sequential play with extra steps. Likely needs per-encounter tuning by the GM. A starting point: 30 seconds per round, scaling up for complex encounters.

6. **How visible should enemy adaptation be?** If players can tell that enemies are adapting, they can game the adaptation system. If it's invisible, it might feel random or unfair. Consider subtle UI cues: "The enemies seem to be changing tactics" in the feed.

7. **Should the resolution playback be skippable?** Speed-focused players will want to skip. Narrative-focused players will want to watch. A "skip if all players agree" model might work.
