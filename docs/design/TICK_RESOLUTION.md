# Tick-Based Resolution — Locked Decisions for Action Sequencing

> **Status:** Core model locked. This document is the authoritative reference for how multiple actions per round resolve in the simultaneous turn system. It supersedes the single-pass phase model described in `DESIGN_SPIKE.md` Decision 1 — the phase ordering (Movement → Attacks → Effects) is preserved, but is now applied *per tick* rather than once per round.
>
> **Depends on:** `DESIGN_SPIKE.md` (damage pipeline, hit checks, HP netting), `ROADMAP_STRATEGIC.md` (slice sequencing), `GAME_DESIGN.md` (design philosophy).
>
> **Core principle:** Each round, players queue an ordered sequence of actions funded by an AP budget. Resolution processes one tick at a time. Within each tick, all actions at that queue position resolve simultaneously using the existing phase ordering. This preserves the simultaneous feel while giving AP real meaning as a budget for action sequencing.

---

## Why Ticks, Not Single-Pass Phases

The original phase model (all movement → all attacks → all effects) works when every combatant performs exactly one action per round. AP budgets break that model. A character with 3 AP who queues Move, Move, Attack needs the second move to resolve *before* the attack, and another character's attack at queue position 1 needs to interact with positions as they exist after the first move, not the second.

A single pass through phases collapses all movement into one instant, erasing the temporal relationship between a character's sequential actions. The tick model preserves that relationship: each tick is a discrete moment within the round where one action per combatant resolves, processed through the familiar phase ordering.

---

## Decision 1: The Tick Model ✅

**Choice: One tick per queue position. Within each tick, phase ordering applies. Characters with shorter queues idle on later ticks.**

### How It Works

During the planning phase, each player queues an **ordered list** of actions that fit their AP budget:

```
Character A (3 AP): [Move (1 AP), Move (1 AP), Melee Attack (1 AP)]
Character B (3 AP): [Ranged Attack (2 AP), Defend (1 AP)]
Character C (3 AP): [Move (1 AP), Move (1 AP), Move (1 AP)]
```

The server determines the maximum queue length across all combatants. That is the number of ticks.

```
Max queue length: 3 → 3 ticks
```

**Tick 1:** Gather everyone's action at queue position 0.
- A: Move. B: Ranged Attack. C: Move.
- Phase ordering within this tick: A and C move first (Movement), then B's ranged attack resolves against post-movement positions (Attacks).

**Tick 2:** Gather everyone's action at queue position 1.
- A: Move. B: Defend. C: Move.
- A and C move. B defends (no resolution needed).

**Tick 3:** Gather everyone's action at queue position 2.
- A: Melee Attack. B: No action (queue exhausted, implicitly idles). C: Move.
- C moves first (Movement), then A's melee attack resolves against post-movement positions (Attacks). Adjacency checked at this point.

After all ticks complete, process end-of-round effects: condition duration ticks, ley point volatility, cooldown decrements.

### Sub-decisions

| Question | Answer |
|---|---|
| What happens when a combatant's queue is exhausted? | They idle. No action, no implicit defend. They're simply done for the round. |
| Can a combatant's later actions be affected by earlier ticks? | **Yes.** If a combatant is defeated in Tick 1, their Tick 2 and 3 actions are voided. If a condition applied in Tick 1 blocks movement, their Tick 2 move fails. |
| Is there a maximum queue length? | Not enforced by the engine. Limited naturally by AP budget. A character with 3 AP and minimum-cost actions (1 AP each) queues at most 3 actions. |
| Does the phase ordering within a tick ever change? | No. Movement → Attacks → Effects is fixed within every tick. |

### Resolution Loop (Pseudocode)

```
resolve_round(all_combatants):
    max_ticks = max(len(combatant.action_queue) for combatant in all_combatants)

    for tick in 0..max_ticks:
        actions_this_tick = {}
        for combatant in living_combatants:
            if tick < len(combatant.action_queue):
                actions_this_tick[combatant] = combatant.action_queue[tick]

        // --- Phase 1: Movement ---
        movements = [a for a in actions_this_tick if a.type == "move"]
        resolve_movements_simultaneously(movements)    // path-based, speed tiebreaker
        update_positions()
        recompute_adjacency()
        recompute_channeling()

        // --- Phase 2: Attacks ---
        attacks = [a for a in actions_this_tick if a.type in ("melee_attack", "ranged_attack")]
        validate_attacks_against_current_positions(attacks)  // melee: adjacency, ranged: range
        snapshot_hp()
        compute_all_hit_checks(attacks)
        compute_all_damage(attacks)
        apply_all_hp_deltas()                           // net calculation per combatant
        check_defeats()
        void_future_actions_for_defeated()

        // --- Phase 3: Effects ---
        resolve_defend_actions()
        // (conditions, environment — empty in 3A, populated in 3B.4+)

    // --- End of Round ---
    tick_conditions()
    process_environment_effects()
    decrement_cooldowns()
    reset_ap_budgets()
    advance_round_number()
    begin_next_planning_phase()
```

---

## Decision 2: Action Types and Phase Routing ✅

**Choice: Each action has a fixed phase affinity. The phase it belongs to determines when it resolves within a tick.**

| Action Type | Phase | AP Cost (3A placeholder) | Notes |
|---|---|---|---|
| Move | Movement | 1 | Path-based. Since 3B.2, all of a round's moves share the combatant's movement range (see below) |
| Melee Attack | Attack | 2 | Requires adjacency (Chebyshev ≤ 1) post-movement |
| Ranged Attack | Attack | 2 | Requires target within range post-movement. Unlimited range in 3A. |
| Defend | Effects | 1 | No resolution in 3A. Future: damage reduction buff. |
| Disengage | Movement | 1 | Move without provoking reactive attacks. See Decision 5. |
| Prepared Action | Special | Varies | Doesn't resolve at its queue position. Activates reactively. See Decision 4. |

### Movement Budget per Round (3B.2) ✅

**Choice: Each Move costs 1 AP, but all of a combatant's moves in one round share a budget of `MovementRange` cells.** Each queued move is measured from where the previous one ends. At resolution a move stops early once the budget runs out; steps a move didn't take because it was blocked aren't counted.

Why: if each Move could go the full range, a 5-AP combatant with range 6 could cover 30 cells, more than the 20×20 map. A shared budget keeps ground covered per round the same as the single-move model and still lets the AP queue mix movement with attacks (Move → Attack → Move). As shipped: MaxAp 4–5 per profile; costs move 1, melee 2, ranged 2, defend 1 (`RulesConfig`). Queues over budget are rejected at planning (`PlanValidator`) and cut by the resolver.

### Compound Actions Are Eliminated

The tick model makes explicit compound actions (like "Charge") unnecessary. A charge is simply queuing Move followed by Melee Attack — two separate actions in the queue that resolve across two ticks (or within the same tick if they're at the same queue position — they won't be, since they're separate entries).

Future ability designs that conceptually combine movement and attack (e.g., a "Lunge" ability that moves one cell and attacks in the same moment) can be implemented as a single action with phase affinity "Attack" that includes a one-cell positional adjustment during the Attack phase, rather than requiring a compound action framework.

---

## Decision 3: How Targeting Interacts with Ticks ✅

**Choice: Attacks target combatants, not cells. Validity is checked against positions at the moment the attack resolves (post-movement within the same tick). Invalid attacks are wasted.**

### Scenario Matrix

| Attacker Plans | Target Plans | Resolution |
|---|---|---|
| Melee Attack on B | B stays still | Adjacency checked post-movement in that tick. If adjacent, attack resolves. |
| Melee Attack on B | B moves away (same tick) | B's movement resolves in Phase 1. Melee adjacency check in Phase 2 fails. Attack is **wasted**. |
| Melee Attack on B | B moves away (earlier tick) | B already moved. Attacker's melee resolves in a later tick. Adjacency check uses current positions. Likely wasted unless attacker also moved to close distance. |
| Ranged Attack on B | B moves anywhere | B's movement resolves first. Ranged attack tracks B to new position. Range checked against new position. If in range, attack resolves. |
| Ranged Attack on B | B moves behind cover (future) | In 3A, no cover system. When cover lands, range check also checks line-of-sight post-movement. |
| Move toward B, then Melee Attack B | B moves away in Tick 1 | Tick 1: both move simultaneously. Tick 2: attacker's melee checks adjacency against current positions. Whether it connects depends on relative movement and Speed. |

### Cell-Targeted vs. Combatant-Targeted Abilities

When abilities land in 3B.3, a second targeting paradigm is introduced:

- **Combatant-targeted** (melee attacks, ranged attacks, single-target spells): Track the target combatant. Validity checked against the target's current position at resolution time.
- **Cell-targeted** (AoE abilities, area denial): Hit the committed cell regardless of who occupies it after movement. Friendly fire risk exists — if an ally moves into the targeted area between planning and resolution, they take the effect.

For 3A, all attacks are combatant-targeted. Cell targeting is deferred to 3B.3.

---

## Decision 4: Reactive Actions ✅

**Choice: Two categories of reactive actions — passive reactions (always available, free) and prepared reactions (explicitly planned, costs AP). Both fire during tick processing when their trigger condition is met.**

Reactive actions are the framework for opportunity attacks, counterspells, parries, overwatch, and similar "if X happens, I do Y" mechanics. They are **not implemented in 3A** but the tick resolution loop is built to accommodate them. The trigger evaluation points are documented here so the architecture doesn't need reworking when these features land.

### Passive Reactions

Always available to qualifying combatants. No AP cost. No planning required. Limited in power.

**3B+ examples:**
- Opportunity Attack: melee combatants get a free reactive strike when an adjacent enemy moves away (unless the enemy uses Disengage).

**Constraints on passive reactions:**
- One passive reaction per tick per combatant. If multiple triggers fire in the same tick, only the first applies.
- Passive reactions deal reduced damage compared to proactive attacks (tuning parameter).
- Only available to combatants with the appropriate capability (melee weapon equipped for opportunity attacks, etc.).

### Prepared Reactions

Explicitly queued during planning. Cost AP. Occupy a slot in the action queue. More powerful than passive reactions.

A prepared action does not resolve at its queue position. Instead, it places the combatant in a **watching** state for the remainder of the round. If the trigger condition is met during any subsequent tick, the reaction fires. If the trigger never fires, the AP is spent and nothing happens.

**Schema:**

```json
{
  "type": "prepared",
  "apCost": 2,
  "trigger": {
    "condition": "enemy_casts_ability",
    "filter": { "range": 5 }
  },
  "reaction": {
    "type": "counterspell",
    "timing": "interrupt"
  }
}
```

**Trigger conditions (planned set, not exhaustive):**

| Trigger | Fires When | Timing | Example Reactions |
|---|---|---|---|
| `enemy_enters_melee_range` | Enemy moves adjacent to you | Before (interrupt movement) or After (strike after they arrive) — per reaction definition | Counterattack, shove, brace |
| `enemy_leaves_melee_range` | Adjacent enemy moves away | During (interrupts their movement) | Opportunity strike (stronger than passive version) |
| `enemy_attacks_me` | You are targeted by an attack | Simultaneous (both attacks resolve) or Before (parry modifies incoming hit) | Parry, riposte, dodge |
| `enemy_attacks_ally` | An ally within range is targeted | Before (intercept redirects the attack to you) | Intercept, shield ally |
| `enemy_casts_ability` | Enemy uses a tagged ability within range | Before (interrupt negates the ability) | Counterspell, dispel |
| `enemy_enters_area` | Enemy moves into a specified cell zone | After (strike them in the zone) | Overwatch shot |
| `ally_health_drops_below` | Ally HP falls below threshold | After (response heal) | Emergency heal, stabilize |

### Reaction Timing

Each reaction has a timing property that determines when it resolves relative to its trigger:

- **Interrupt (before).** The reaction resolves before the triggering action completes. A counterspell negates the triggering ability. A parry modifies the incoming attack before damage is calculated. The triggering action may be fully negated or weakened.
- **Simultaneous (during).** The reaction and the trigger resolve at the same time. A riposte means both combatants strike each other. Uses the standard simultaneous damage model — snapshot HP, compute both, apply both.
- **Response (after).** The reaction resolves after the triggering action completes. An emergency heal fires after damage is applied. The trigger's full effect happens; the reaction patches the aftermath.

### Where Reactive Actions Resolve in the Tick Loop

```
For each tick:
    Phase 1: Movement
        For each moving combatant, step through path cell by cell:
            → Check passive reactions (opportunity attacks) at each cell transition
            → Check prepared reactions with movement triggers
            → Resolve any triggered reactions immediately
            → If mover is defeated or stopped: halt movement, void remaining path
        Resolve movement conflicts (Speed tiebreaker for contested cells)
        Update positions

    Phase 2: Attacks
        Collect proactive attacks at this queue position
        → Check prepared reactions with attack triggers (parry, intercept, counterspell)
            → Interrupt-timing reactions resolve BEFORE attack computation
            → Simultaneous-timing reactions are added to the attack batch
        Snapshot HP → compute hits → compute damage → apply deltas → check defeats

    Phase 3: Effects
        → Check prepared reactions with effect triggers (ally_health_drops_below)
            → Response-timing reactions resolve AFTER effects are applied
```

### Architectural Note for 3A

The 3A resolution loop should include clearly marked extension points where reactive action checks will be inserted, even though no reactive actions exist yet. This means:

- Movement resolution processes paths cell-by-cell (not teleport), even though no triggers fire in 3A.
- The attack sub-phase has a pre-processing hook for interrupt-timing reactions, even though it's empty in 3A.
- The effect sub-phase has a post-processing hook for response-timing reactions, even though it's empty in 3A.

This ensures that adding reactive actions in 3B+ is inserting logic into existing hooks, not restructuring the resolution loop.

---

## Decision 5: Opportunity Attacks and Disengagement ✅

**Choice: Moving away from an adjacent enemy provokes a passive opportunity attack. Moving into melee range of an enemy also provokes. The Disengage action avoids provocation. Opportunity attacks are a passive reaction — free, always available to melee-capable combatants, weaker than proactive attacks.**

### Provocation Rules

| Movement | Provokes? | Notes |
|---|---|---|
| Moving away from an adjacent enemy | **Yes** | The "sticky melee" rule. Retreating has a cost. |
| Moving into a cell adjacent to an enemy | **Yes** | Approaching has a cost. Punishes reckless charges. |
| Moving past an enemy (through adjacent cell, out the other side) | **Yes, once** | Triggers on entry. One opportunity attack per tick per reactor, so passing through doesn't trigger twice. |
| Moving while using Disengage | **No** | Disengage costs 1 AP and replaces a normal Move. The combatant moves without provoking any reactions. |
| Moving while already adjacent (circling an enemy) | **No** | Lateral movement that maintains adjacency doesn't provoke. Only transitions into or out of adjacency trigger. |

### Opportunity Attack Properties

| Property | Value | Notes |
|---|---|---|
| AP cost | 0 (passive) | Free reaction. No AP spent. |
| Damage | Half of a normal melee attack (rounded down) | Enough to matter as a tax on movement, not enough to be a primary damage source. |
| Hit check | Normal hit roll | Can miss. Not guaranteed damage. |
| Limit | One per tick per combatant | If multiple enemies provoke in the same tick, only one opportunity attack fires. The combatant reacts to the first provocation in processing order. |
| Requirement | Melee capability | Ranged-only combatants cannot make opportunity attacks. |

### Disengage

Disengage is a Movement-phase action that costs 1 AP. The combatant moves normally but does not provoke passive reactions (opportunity attacks) from any enemy during that move. Disengage does **not** prevent *prepared* reactions with movement triggers — a character who explicitly prepared an action against movement still reacts, because they committed AP to watching for exactly this situation.

This creates a meaningful distinction:
- Passive opportunity attacks: avoided by Disengage (free defense is bypassed by a 1 AP cost).
- Prepared movement reactions: not avoided by Disengage (committed AP is not easily nullified).

Players must weigh: "Is spending 1 AP on safe movement worth it, or do I eat the opportunity attack and keep my AP for offense?"

### Not Implemented in 3A

Opportunity attacks and Disengage are part of the reactive action system and land when that system is built (3B+). In 3A, movement never provokes and Disengage doesn't exist as an action type. The path-based movement resolution in 3A is the prerequisite — it processes cell-by-cell transitions, which is where provocation checks will be inserted.

---

## Decision 6: Path-Based Movement ✅

**Choice: Movement resolves cell-by-cell along the path from origin to destination, not as a teleport. The path is computed server-side using the shortest Chebyshev path. Each cell transition is a point where reactive triggers are evaluated.**

### Why Path-Based

The tick model requires knowing *which cells a combatant passes through* during movement, not just where they end up. This is necessary for:

- Opportunity attack triggers (entering/leaving adjacency with an enemy).
- Overwatch triggers (entering a watched zone).
- Future: difficult terrain (costs extra movement to traverse), hazard cells (take damage when passing through), and chokepoint control.

### Path Computation

The server computes the shortest Chebyshev path from origin to destination. If multiple shortest paths exist, the server picks one deterministically (e.g., prefer cardinal directions over diagonals, break ties by north-first-then-east). The path is not player-chosen in 3A — the player picks the destination, and the server determines the route.

**Future consideration:** Player-chosen paths (drag a movement route on the grid) would allow intentional detours — e.g., "walk around the enemy instead of past them to avoid opportunity attacks." This is a planning UI enhancement for a later slice, not a resolution engine change. The engine already processes arbitrary paths; the only question is who generates them.

### Simultaneous Path Resolution

When multiple combatants move in the same tick, their paths are processed **simultaneously but cell-by-cell in lockstep**:

```
All movers take step 1 → check triggers → resolve
All movers take step 2 → check triggers → resolve
...until all paths are exhausted
```

This means a combatant moving 3 cells and a combatant moving 1 cell take their first step together. After step 1, the short-path mover is done. The long-path mover continues alone for steps 2 and 3.

Movement conflicts (two combatants claiming the same cell at the same step) are resolved by Speed tiebreaker, same as the Design Spike decision.

---

## What Changes from DESIGN_SPIKE.md

| Design Spike Decision | Status | Change |
|---|---|---|
| Decision 1: Hybrid — 3 phases, simultaneous within each | **Preserved, wrapped in ticks.** The three phases still apply in the same order — they're now applied per tick rather than once per round. |
| Decision 2: Mutual kills enabled | **Unchanged.** Simultaneous damage within the Attack phase of a tick, same rules. |
| Decision 3: Healing + damage net calculation | **Unchanged.** Net HP deltas applied per tick. |
| Decision 4: Stat-to-mechanic mapping & damage pipeline | **Unchanged.** Pipeline has the same slots. Applies per attack action, evaluated at resolution time per tick. |
| Decision 5: One action per round (Move OR Attack OR Defend) | **Superseded.** Players now queue multiple actions per round funded by AP. The single-action model is a special case (queue length 1) used only in 3A before AP lands. |
| Planning timer, default action on timeout | **Unchanged.** If a player doesn't lock in, they get a default queue of `[Defend]`. |

---

## 3A Implementation: Single-Action Ticks

For Phase 3A (before AP lands in 3B.2), the resolution engine implements the full tick loop but constrains queue length to 1. Every combatant gets exactly one action per round.

This means:
- The tick loop runs exactly once per round (max queue length = 1, so 1 tick).
- Within that single tick, the phase ordering applies: Movement → Attacks → Effects.
- The resolution is functionally identical to the original single-pass phase model from the Design Spike.
- The difference is architectural: the code is structured as a tick loop from day one.

When AP lands in 3B.2, the constraint is lifted. Queue length becomes dynamic, determined by each combatant's AP budget and chosen actions. The resolution engine requires no structural changes — it already iterates ticks.

### 3A Action Set

| Action | Phase | Notes |
|---|---|---|
| Move | Movement | Chebyshev ≤ 6 (per-profile movement range since 3B.1, shared by all moves in a round since 3B.2). Path-based resolution (cell-by-cell). No reactive triggers fire in 3A, but the path infrastructure exists. |
| Attack | Attack | Melee (adjacency) or ranged (unlimited range in 3A). Hit check with placeholder values per Design Spike Decision 4. |
| Defend | Effects | Do nothing. Default action on timeout. No mechanical effect in 3A. |

### PlannedAction Schema (3A)

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

- `actions` is an array of length 1 in 3A, becoming variable-length in 3B.2.
- `targetCell` present when `type == "move"`.
- `targetCombatantId` present when `type` is an attack.
- `type == "defend"` has neither field.

### PlannedAction Schema (3B.2+ with AP)

```json
{
  "actions": [
    { "type": "move", "targetCell": { "x": 5, "y": 8 }, "apCost": 1 },
    { "type": "move", "targetCell": { "x": 6, "y": 9 }, "apCost": 1 },
    { "type": "melee_attack", "targetCombatantId": "abc-123", "apCost": 2 }
  ],
  "totalApSpent": 4,
  "maxAp": 5
}
```

### PlannedAction Schema (3B.3+ with Prepared Reactions)

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

---

## Interaction Scenarios

These scenarios validate the tick model against real gameplay situations. Each shows the planned queues, the tick-by-tick resolution, and the outcome.

### Scenario 1: Chase (Move-Move-Attack vs. Retreat)

```
A (melee fighter, 3 AP): [Move toward B, Move toward B, Melee Attack B]
B (mage, 3 AP):          [Move away from A, Ranged Attack A, Defend]

Tick 1:
  Movement: A moves 1 cell toward B. B moves 1 cell away from A. Simultaneous.
  Attacks:  None this tick.
  Net:      Distance unchanged (both moved).

Tick 2:
  Movement: A moves 1 cell toward B. No movement from B.
  Attacks:  B fires ranged attack at A. A has closed distance by 1 cell but
            ranged has unlimited range in 3A — attack resolves. Hit check, damage.
  Net:      A is closer. A takes ranged damage.

Tick 3:
  Movement: No movement from A (attack action).
  Attacks:  A melee attacks B. Adjacency check: A closed 1 net cell over
            2 ticks of chasing (1 cell gain in Tick 2). If starting distance
            was ≤2, A is now adjacent. Melee connects. If starting distance
            was >2, A is not adjacent. Melee whiffs.
  Net:      Outcome depends on starting distance. B's retreat bought one
            tick of safety. A spent full AP chasing.
```

**Takeaway:** The mage who retreats and shoots has a positional advantage over the fighter who chases. The fighter must start close or commit multiple rounds to closing distance. This is a meaningful ranged-vs-melee dynamic.

### Scenario 2: Focus Fire

```
A (3 AP): [Ranged Attack C, Ranged Attack C, Defend]
B (3 AP): [Move toward C, Melee Attack C, Defend]
C (3 AP): [Melee Attack B, Defend, Defend]

Tick 1:
  Movement: B moves toward C. No other movement.
  Attacks:  A fires ranged at C (resolves against C's current position).
            C melee attacks B — adjacency check: B just moved toward C.
            If B is now adjacent, C's melee connects. Both attacks in this
            tick resolve simultaneously. Snapshot HP, compute damage, apply.
  Net:      C takes ranged damage from A. If B is adjacent, C and B trade
            melee damage.

Tick 2:
  Movement: None.
  Attacks:  A fires second ranged at C. B melee attacks C (if adjacent).
            Both resolve simultaneously against C's current HP (after Tick 1 damage).
  Net:      C takes heavy damage from two attackers. C's only action was
            in Tick 1 — they're defending now.

Tick 3:
  Everyone defends. No resolution.
```

**Takeaway:** Concentrated fire is powerful but requires coordination (both A and B committed AP to attacking C). C's choice to front-load their attack in Tick 1 gets one hit in before the damage piles up. If C had chosen Move (retreat) in Tick 1 instead, they'd take less damage but deal none.

### Scenario 3: Mutual Approach

```
A (3 AP): [Move toward B, Melee Attack B, Defend]
B (3 AP): [Move toward A, Melee Attack A, Defend]

Tick 1:
  Movement: A and B both move toward each other. If they claim the same
            cell, Speed tiebreaker — loser stops one cell short. Either
            way, they end up adjacent or very close.
  Attacks:  None this tick.

Tick 2:
  Movement: None.
  Attacks:  A melee attacks B. B melee attacks A. Both resolve simultaneously.
            Snapshot HP, compute both hit checks, compute both damage, apply
            both deltas. Mutual kill possible if both are low HP.

Tick 3:
  Both defend (or are dead).
```

**Takeaway:** Two melee combatants running at each other both connect in Tick 2. Neither gets an advantage from moving first — they moved simultaneously. The fight is decided by stats, hit rolls, and damage rolls. Clean and fair.

### Scenario 4: Prepared Counterspell (3B.3+ example)

```
A (mage, 5 AP):   [Move to ley point, Cast Fireball at C (cell-targeted)]
B (mage, 5 AP):   [Prepare: Counterspell (trigger: enemy_casts_ability, range 5)]
C (fighter, 3 AP): [Move toward A, Melee Attack A]

Tick 1:
  Movement: A moves to ley point. C moves toward A.
  Attacks:  A casts Fireball at cell [7,7]. BEFORE resolution, check
            prepared reactions: B has a counterspell prepared, trigger is
            enemy_casts_ability within range 5. A is within 5 cells of B.
            Trigger fires. Counterspell timing is "interrupt" — Fireball
            is negated before damage computes.
  Net:      A moved to ley point but Fireball was countered. AP spent on
            both sides. B's preparation paid off.

Tick 2:
  B has no more queued actions (prepared action consumed their AP).
  C melee attacks A. Adjacency check. If C closed enough distance, attack connects.
```

**Takeaway:** B spent AP on a contingency and it paid off — Fireball was negated. If A hadn't cast a spell, B's AP would have been wasted. The risk/reward of preparation is real. Note that B's counterspell also consumed their round — they couldn't attack or move while preparing.

---

## Open Questions (Deferred to Playtesting)

These decisions are explicitly **not locked** and will be resolved through playtesting in the relevant slices.

| Question | Slice | Options | Current Lean |
|---|---|---|---|
| Should unused prepared actions refund AP? | 3B.3 | Full refund / partial / none | No refund. Preparation is a gamble. |
| Can a prepared action trigger in the same tick it's queued? | 3B.3 | Yes (instant readiness) / No (ready next tick) | Yes — you're declaring readiness, not performing a setup ritual. |
| If multiple prepared reactions trigger on the same event, do all fire? | 3B.3 | All fire (wasted duplicates) / only first | All fire. Wasted counterspell is the cost of imperfect coordination. |
| Should opportunity attacks be passive-free or cost a stance? | 3B+ | Free passive / stance slot (no AP but limits other stances) | Free passive. Stances are a good concept but add complexity — defer. |
| Can players choose their movement path? | 3B+ | Server-computed shortest / player-drawn path | Server-computed for 3A. Player-drawn is a UI enhancement. |
| Should ranged attacks in melee range have a penalty? | 3B+ | Penalty (disadvantage on hit roll) / no penalty | Penalty. Makes positioning matter for ranged characters. |
| Does Disengage prevent prepared movement reactions? | 3B.3 | Yes (full safety) / No (only prevents passives) | No. Disengage avoids free reactions, not committed ones. |

---

## Summary: What This Locks for Implementation

**The resolution engine** is a tick loop from 3A onward. Even though 3A has exactly 1 tick per round, the architecture is a `for tick in 0..max_ticks` loop with phase ordering inside it.

**Movement is path-based.** The engine steps through cells, not teleports. This is the foundation for reactive triggers even though no triggers fire in 3A.

**Actions are queued.** The `PlannedAction` schema is an array from day one. In 3A it's always length 1. In 3B.2+ it grows with AP.

**Reactive actions have a clear framework.** Two categories (passive and prepared), three timings (interrupt, simultaneous, response), and defined trigger points in the tick loop. None are implemented in 3A, but the hooks are in place.

**Compound actions are not a concept.** The tick model makes them unnecessary. Move + Attack is just two actions in a queue. Future abilities that combine effects across phases are single actions with multi-phase resolution, not a special compound framework.

**The phase ordering (Movement → Attacks → Effects) is preserved and unchanged.** It just applies per tick instead of per round.
