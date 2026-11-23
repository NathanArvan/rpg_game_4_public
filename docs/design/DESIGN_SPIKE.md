# Design Spike — Locked Decisions for Phase 3A

> **Status:** All five blocking decisions are resolved. This document is the authoritative reference for implementing Phase 3A (slices 3A.1, 3A.2, 3A.3). Hand this to Claude Code alongside `ROADMAP_STRATEGIC.md`.

---

## Decision 1: Conflict Resolution Model ✅

**Choice: Hybrid — 3 phases, simultaneous within each phase.**

```
Phase 1: Movement    — all movement resolves simultaneously
Phase 2: Attacks     — all attacks resolve simultaneously (hit check → damage applied as batch)
Phase 3: Effects     — conditions tick, environment changes (empty in 3A, populated in 3B.4)
```

**Sub-decisions:**

| Question | Answer |
|---|---|
| How many phases? | 3 for v1. Split Phase 2 into fast/slow sub-phases when abilities land in 3B.3 if needed. |
| Simultaneous damage within a phase? | **Yes.** Snapshot HP → compute all hit checks → compute all damage → apply all deltas at once. |
| Movement conflicts? | **Higher Speed claims the cell.** If tied, neither moves to the contested cell (both stop one cell short). |
| Attacks on dead combatants? | **Wasted.** The attacker committed to that target during planning — bad prediction has a cost. |

**Resolution pipeline for Phase 2 (Attacks):**
```
1. Collect all attack actions from planned_actions
2. For each attack: roll hit check (can miss — see Decision 4)
3. For each hit: compute damage
4. Group all (combatantId, hpDelta) pairs
5. Sum deltas per combatant
6. Apply all HP changes in one batch
7. Check for defeated combatants (HP ≤ 0) AFTER all damage applied
```

**Iteration path:** Adding more phases is additive (insert into the loop). Changing the movement tiebreaker is a one-line change. Switching from simultaneous to sequential damage within a phase is a deeper rework — this is why we're locking in "simultaneous" now.

---

## Decision 2: Mutual Kills ✅

**Choice: Enabled.**

Mutual kills are a natural consequence of simultaneous damage in Decision 1. Two combatants can defeat each other in the same Attack phase. No tiebreaker needed — all damage applies, then all defeated checks run.

**Implementation notes:**
- The defeated check runs once after ALL damage in the phase is applied, not per-attack
- The resolution payload must support multiple defeats in a single phase
- Client playback shows all attacks landing, then all defeats, so the "mutual kill" moment is visible
- A campaign-level toggle to disable mutual kills can be added later if playtesters dislike it (would require a Speed-based tiebreaker for "who dies first")

---

## Decision 3: Healing + Simultaneous Damage ✅

**Choice: Net calculation.**

All damage and healing in a phase are summed into a single delta per combatant. A character at 10 HP who takes 15 damage and receives 8 healing ends at 3 HP (10 - 15 + 8). They are never temporarily "dead."

**Implementation notes:**
- No healing exists in 3A, but the HP pipeline is built to support it: collect `(combatantId, hpDelta)` pairs where damage is negative and healing is positive
- Sum all deltas per combatant, apply the net
- Defeated check runs against the final HP value after netting
- This means a well-predicted heal can save a combatant from lethal damage — the healer doesn't need to act "before" the damage since there is no ordering within a phase

**Iteration path:** Switching to "damage first, then healing" (Option B or C from the original spike) means splitting the delta application into two passes. The data model (list of deltas) stays the same — just the application logic changes.

---

## Decision 4: Stat-to-Mechanic Mapping ✅

**Choice: Attacks can miss. Use placeholder formulas in 3A; design damage pipeline to accommodate stat + item + ability modifiers in the future.**

### What 3A needs (placeholder values)

> **Update (November 2026):** Character stats are deferred (see `ROADMAP_STRATEGIC.md` → Deferred: Character Stats & Progression). The 3A placeholders below are replaced in 3B.1 by per-combatant **combat profiles** — hand-authored derived values — not by stat formulas. The pipeline structure in this decision is unchanged; only the source of each input changes.

| Value | 3A placeholder | 3B.1 (combat profile) | If stats are adopted later |
|---|---|---|---|
| Movement range | Fixed: 6 cells (Chebyshev) | `profile.MovementRange` | 2 + Speed stat |
| Damage | Existing class-based dice (Fighter: 1d8, Rogue: 1d6, Wizard: 1d4) | `profile.BaseDamage` + weaponBonus + abilityModifier | baseDamage(stat) + weaponBonus + abilityModifier |
| Hit check | `random(1–20) + 0` vs `targetDodge = 10` (flat placeholder) | `random(1–20) + attacker.profile.Accuracy` vs `target.profile.Dodge` | `random(1–20) + attackerAccuracy(stat)` vs `targetDodge(Speed)` |
| HP | `MaxHp` stored on character (unchanged) | `profile.MaxHp` | 10 + (Resilience × 5) |

### Damage pipeline structure (build this now, fill in real formulas later)

```
computeDamage(attacker, target, actionType):
    hitRoll = roll(1, 20) + attacker.accuracyBonus     // 0 for 3A
    if hitRoll < target.dodgeThreshold:                  // 10 for 3A
        return { hit: false, damage: 0 }

    baseDamage = rollClassDice(attacker.class)           // existing logic for 3A
    weaponBonus = 0                                      // placeholder for items (3B+)
    abilityModifier = 0                                  // placeholder for abilities (3B.3+)
    totalDamage = baseDamage + weaponBonus + abilityModifier

    isCrit = false                                       // placeholder for crit system (3B+)
    if isCrit: totalDamage *= 2

    return { hit: true, damage: totalDamage, isCrit }
```

This pipeline has slots for every modifier source without implementing any of them yet. When items land, `weaponBonus` stops being zero. When abilities land, `abilityModifier` stops being zero. When combat profiles replace classes (3B.1), `baseDamage` switches from class dice to the profile value. None of these changes touch the pipeline structure. *3B.4 added two condition steps:* a `conditionModifier` slot (the attacker's damage-dealt conditions) next to `abilityModifier`, and, after the multiplier and crit, the target's damage-taken conditions (flat, then percent, rounded down, never below 0). Hit checks add the attacker's accuracy and the target's dodge conditions (`SPRINT_3B4.md` C4).

### Candidate stat formulas (deferred reference — not scheduled)

Kept for the deferred stats decision. Whether characters have core stats at all is open; if they do, these formulas would produce combat profile values.

Stat range: 1–10 per stat, 30-point budget across 6 stats.

| Derived value | Formula | Example (stat = 7) |
|---|---|---|
| Max HP | 10 + (Resilience × 5) | 45 HP |
| Movement range (cells) | 2 + Speed | 9 cells |
| Melee damage | 1d4 + Power | 1d4+7 (avg 9.5) |
| Ranged damage | 1d4 + Precision | 1d4+7 (avg 9.5) |
| Dodge threshold | 8 + Speed | 15 |
| Accuracy bonus | Power (melee) or Precision (ranged) | +7 |
| Vision range (cells) | 3 + Focus | 10 cells |
| Info share range (cells) | Influence × 2 | 14 cells |
| Crit chance | Precision × 2% | 14% |
| Max AP (per round) | 3 + floor(Speed / 3) | 5 AP |
| Condition resistance | Resilience × 5% | 35% |

These formulas are a starting point, to be revisited once abilities, conditions, vision, and items exist. The important thing is the pipeline structure, not the specific numbers.

---

## Decision 5: Planning Actions ✅

**Choice: One action per round from the minimal set — Move OR Attack OR Defend.**

### PlannedAction schema

```json
{
  "type": "move | attack | defend",
  "targetCell": { "x": 5, "y": 8 },
  "targetCombatantId": "abc-123"
}
```

- `targetCell` is present when `type == "move"`
- `targetCombatantId` is present when `type == "attack"`
- `type == "defend"` has neither — the combatant stays in place and takes no offensive action

### Resolution by type

| Action | Phase | Resolution |
|---|---|---|
| Move | Phase 1 (Movement) | Validate Chebyshev distance ≤ movement range, check cell bounds (0–19), resolve conflicts by Speed. Update position in map. |
| Attack | Phase 2 (Attacks) | Validate target is a living combatant. Hit check → damage roll → collect delta. Melee requires adjacency after Phase 1 movement resolves. Ranged TBD (no range limit in 3A, add range stat in 3B). |
| Defend | — | No resolution needed. Combatant stays put. Possible future effect: reduced incoming damage. For 3A, defend is purely "do nothing." |

### Default action on timeout

If a player doesn't lock in before the timer expires, their action defaults to **Defend** (stay in place, no attack). This is the least punishing default — it doesn't waste resources or move the character somewhere unintended.

### Iteration path

When AP lands in 3B.2, `PlannedAction` becomes a list of sub-actions (`PlannedAction[]`), each with an AP cost, and the total must fit the character's AP budget. The resolution engine processes the list by routing each sub-action to its phase. The single-action model is just a list of length 1 — fully compatible.

---

## Tune-Later Defaults (not blocking, build with these)

| Setting | Default | Notes |
|---|---|---|
| Planning timer | 30 seconds | GM-configurable per encounter |
| Default action on timeout | Defend in place | Decided above |
| Ally planning visibility | Show target only ("Player X is targeting Enemy B"), not action type | Campaign toggle later |
| Resolution playback duration | ~3 seconds per round | Skippable if all players click "skip" |
| Melee range | Adjacent (Chebyshev ≤ 1 after movement resolves) | |
| Ranged attack range | Unlimited for 3A | Add range limits tied to Precision stat in 3B |
| Grid size | 20×20 (unchanged) | |
| Movement range | 6 cells Chebyshev (unchanged) | Per combatant from the combat profile in 3B.1 |

---

## Summary: What This Unlocks for 3A Implementation

**ResolutionService** has a fully specified pipeline:
1. Read all planned actions from Redis
2. Phase 1: resolve all moves simultaneously (Speed tiebreaker for conflicts)
3. Phase 2: resolve all attacks simultaneously (hit check → damage → batch apply → defeated check)
4. Phase 3: no-op for now (effects phase, populated in 3B.4)
5. Write all state deltas to Redis + Postgres in one transaction
6. Return resolution payload for broadcast

**PlanningService** has a contract: accept one `PlannedAction` per combatant per round, validate it, write to Redis.

**The damage pipeline** has a clear structure with placeholder values and explicit slots for future stat/item/ability modifiers.

**The HP pipeline** is simple: collect all deltas, net them per combatant, apply, check for defeats.

Everything else is either additive (more phases, more action types, abilities, AP, items) or tunable (timer, playback speed, visibility, formulas).
