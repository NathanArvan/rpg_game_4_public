# Abilities — Framework Design (3B.3)

> **Status:** ✅ Locked 2026-11-22. A8 (revive by healing), A9 (prepared reactions get their own slice, 3B.6) and A12 (ley points and resonance move to 3M.5, after 3M.2) were chosen explicitly; A3 was amended (team relations go through one function, so non-hostile rival teams can be added later); everything else is locked as first written. Change a decision here (with a note) if implementation shows it doesn't work. Decisions for Slice 3B.3, written so the ability families planned for later (spells, perception, concealment, AoE, conditions, reactions) slot in without reworking `RoundResolver`. Implementation plan: `docs/roadmap/SPRINT_3B3.md`.
>
> **Implementation notes (3B.3 PR 2, 2026-11-22):** `auto` targets are Standing combatants only (Downed ones for a `downed_ally` filter), so area attacks never finish anyone off by accident. A combatant healed back up mid-round stands again but doesn't act for the rest of that round. A heal that can't revive is wasted on a Downed or Stable target. Details in `SPRINT_3B3.md` (PR 2 differences).
>
> **Builds on:** `TICK_RESOLUTION.md` (tick loop, phases, targeting, reactions), `DESIGN_SPIKE.md` (HP netting, damage pipeline), `DEFEAT_AND_INCAPACITATION.md` (statuses, Stabilize), `MAGIC_SYSTEM.md` (tags, resonance, ley points).

---

## Goal

An **ability** is a named package of game data: what it costs, who or what it can target, and an ordered list of **effects**. The resolver runs effects. It doesn't know about specific abilities. Adding a new ability should mean adding a JSON entry. A new **kind of effect** should need one new effect type and one handler in the resolver, and nothing else should change.

Ability families the framework has to support, and when each one lands:

| Family | Example | Lands | What it needs from the framework |
|---|---|---|---|
| Stronger attack | Power Strike: melee, +4 damage, cooldown 2 | 3B.3 | `Attack` effect with its own damage spec |
| Multi-target attack | Cleave: melee every adjacent enemy. Twin Shot: two chosen targets | 3B.3 | Targeting that resolves to several combatants; one roll per target |
| Movement | Dash: move 3 cells outside the movement budget | 3B.3 | `Move` effect that can skip the shared budget |
| Movement + attack | Charge: run at a target and strike it in the same tick, bonus damage if you moved 2+ cells | 3B.3 | Several effects in **different phases** of one tick |
| Healing | Mend: heal an ally in range for 1d6+2. Can bring a Downed ally back up | 3B.3 | `Heal` effect netted with damage (DESIGN_SPIKE D3) |
| Spells | Firebolt `[flame, ranged, spell]` | 3B.3 framework, content later | Tags, resonance and ley bonuses on the numeric output (`MAGIC_SYSTEM.md`) |
| Forced movement | Shove: push the target 2 cells | 3B.4 (the 3B.3 stretch wasn't built) | Positional change on a target after damage |
| Buffs, debuffs, damage over time | Battle Cry: allies +2 damage for 2 rounds. Slow | 3B.4 | `ApplyCondition` effect. **All durations go through conditions** |
| Area of effect on a cell | Fireball: radius 2 around a chosen cell | 4.1 | Cell targeting + shapes |
| Prepared reactions | Counterspell, Overwatch, Parry | 3B.6 (decision A9) | `activation: "prepared"` + triggers (TICK_RESOLUTION D4) |
| Perception & concealment | Keen Eye: see farther / reveal hidden. Smoke: hide allies | After 3C | `ApplyCondition` (Concealed, Revealed) + vision modifiers. Target checks already in one place |
| Consumables / limited uses | Potion, 1 use per encounter | 4.2 (schema reserves it now) | `charges` next to cooldowns |

---

## Is the codebase ready?

**Ready:**
- **Pure engine.** `RoundResolver` takes state + plans + dice and returns an outcome, so abilities can be built and tested in Rules scenarios before any server or client work.
- **The tick loop and fixed phases** (Movement → Attacks → Effects) already let one action run in several phases. TICK_RESOLUTION D2 says this is how move+attack abilities should work, with no compound-action framework.
- **HP netting.** The attack phase sums deltas per target (`hpDeltas`) and applies them together. Healing is a positive delta in the same batch.
- **Damage pipeline slots** (`abilityModifier`, `weaponBonus`, `statModifier`) are already in `ComputeAttack`.
- **The profile snapshot model.** Each combatant stores a copy of its profile (`EncounterCombatant.Profile`), so a loadout can be snapshotted the same way and retuning a JSON file never changes a running fight.
- **One plan validator** (`PlanValidator`) walks the queue in order, which a cooldown, ability-cost and charge-distance check can extend.
- **The 0-HP check covers every combatant**, so damage from any source (abilities now, damage over time in 3B.4) is handled.

**Gaps that 3B.3 has to close:**

| Gap | Where | Fix |
|---|---|---|
| Actions are a closed `ActionType` enum, each hard-coded into one phase method | `Model.cs`, `RoundResolver` | Add `ActionType.Ability` + `AbilityId`. The resolver dispatches the ability's effects into the phase each effect belongs to |
| A plan has **one** target (`TargetCell` *or* `TargetCombatantId`) | `PlannedAction`, `PlannedActionEntry` | Add `TargetCombatantIds` (list). Keep the single fields for the basic actions |
| AP cost depends only on the action type | `RulesConfig.ApCost(ActionType)` | `ApCost(PlannedAction, CombatantState)`: basic actions from `RulesConfig`, abilities from their definition |
| No state carries across rounds except HP, status and bleedout | `CombatantState`, Redis `character:live` | `CombatantState.Cooldowns`. Redis `abilityCooldowns` (already in the roadmap contract) |
| **The engine has no teams.** Only `EnemyAi` receives `teamById` | `CombatantState` | Add `Team` to `CombatantState` (decision A3). Ally/enemy filters, heals and "every adjacent enemy" need it |
| Attack outcomes are one-attacker-one-target, with no link back to the action that made them | `Outcomes.cs` | Add `AbilityId` to attack and movement outcomes, a new `HealOutcome`, and one `AbilityUse` record per activation |
| Range checks are inline (`InMeleeReach`, ranged has no range) | `RoundResolver` | One `CanReach(caster, target, range)` helper. 3M.2 adds line of sight there, 3M.4 high ground, 3C vision |
| Basic attacks have unlimited range | `RoundResolver` | Abilities have ranges. Basic ranged stays unlimited for now (tuning decision, not framework) |
| Defend does nothing (quirk 4) | `RoundResolver` | Not 3B.3. *Done in 3B.4 PR 1:* Defend grants Guarded (+3 Dodge) from the start of its tick until the end of the round |

Nothing here requires restructuring the tick loop. The work is: widen the action and outcome records, add an ability catalog, and route effects by phase.

---

## Decisions

All decisions are locked (2026-11-22). ✅ **Decided** marks the ones chosen explicitly rather than accepted as recommended.

### A1. Abilities are data; effects are code ✅

```
AbilityDefinition
  id, name, description
  apCost            int
  cooldown          int   rounds unusable after the round it was used (0 = none)
  charges           int?  uses per encounter (reserved; not implemented in 3B.3)
  activation        "active" | "prepared" (3B.6 PR 2; "passive" reserved, A9)
  trigger           prepared only (3B.6): { kind, range?, attackKind? }: enemy_enters_reach,
                    enemy_moves_within (+ range); attacked, ally_attacked (3B.6 PR 3)
  tags[]            mechanic tags (melee, ranged, healing, movement, spell, aoe)
                    + resonance tags (flame, holy, …, MAGIC_SYSTEM.md)
  targeting         Targeting (A4)
  effects[]         ordered list of Effect (A5)
```

> 3B.6 PR 2 (`SPRINT_3B6.md` X6–X9): a **prepared** ability is queued as the last action of a queue, with no target. From the start of its tick to the end of the round it watches for its `trigger` and resolves its effects (only `attack` so far) on whoever set it off, the moment that happens; its cooldown starts when its tick is reached, whether or not it fires. Shipped: Brace, Overwatch.

In Rules these are immutable records. `AbilityEffect` is an abstract record with one sealed subtype per effect kind (`MoveEffect`, `AttackEffect`, `HealEffect`, `StabilizeEffect`, later `ApplyConditionEffect`, `PushEffect`, …). In JSON the effect kind is a `"kind"` field. Parsing lives in the Api, like profiles: Rules has no serialization.

### A2. Storage and loadouts: JSON catalog + loadout on the profile, snapshotted ✅

- **Catalog:** `server/src/RpgGame.Api/Abilities/abilities.json`, loaded and validated at startup by an `AbilityCatalog` (same pattern as `CombatProfileCatalog` and `BattlePresetCatalog`). **No `ability_definitions` table.** This replaces the table in the roadmap and settles the roadmap's open "ability loadout storage" decision.
- **Loadout:** a profile lists ability ids (`"abilities": ["power_strike", "mend"]`). Battle presets can replace the list for one combatant (`"abilities": [...]`). GM-added monsters get their profile's loadout.
- **Snapshot:** when a combatant is created, the full definitions of its loadout are copied into `EncounterCombatant.Profile` (JSONB, new `Abilities` field). Rules receives them on `CombatProfile.Abilities`. The resolver never reads a catalog. It only uses what the combatant carries.
- **Per-character choice** (a `character_abilities` join table, trees, points) is progression and stays deferred. Until then, a character's loadout is its profile's loadout.
- There are no `spell_definitions` / `character_known_spells` tables left in the schema, so the roadmap's "drop or repurpose" item has nothing to do.

### A3. Teams move into the engine ✅

`CombatantState` gains `Team` (string, default `""`). The Api fills it from `CombatantType` (`party` / `enemy`), as `EnemyAi` and `EncounterEndCheck` already do. `EnemyAi` keeps its `teamById` parameter for now. A later cleanup can switch it to `Team`. Scenario files already have `side`. It starts feeding `Team` too.

**Relations go through one function.** `Relation(a, b)` returns `Self`, `Ally` or `Enemy`, and every targeting filter, the AI and anything else that asks "is this one hostile?" calls it. Nothing compares `Team` strings directly. For 3B.3 the rule inside it is: same non-empty team → **ally**, otherwise **enemy**. (An empty team means "everyone is my enemy", which keeps every current scenario's behaviour.)

**Deferred: teams that aren't allies but aren't hostile either.** Later there may be several teams in one encounter that aren't all at war, e.g. two rival parties racing for the same objective without fighting. That will add a `Neutral` relation and a per-encounter table of team relations (default: hostile), read only inside `Relation`. Things to settle then: whether `enemy`-filter abilities and `auto` targets skip neutrals (expected: yes, so a Cleave doesn't start a fight), whether a direct attack on a neutral turns the relation hostile, how the AI treats neutrals, and what "winning" means when sides aren't enemies (`EncounterEndCheck`, which ties in with per-preset objectives). Until then, keep `Team` a free string (not a two-value enum) so more than two teams already work.

### A4. Targeting ✅

```
Targeting
  kind      "self" | "combatant" | "combatants" | "auto" | "trigger" (3B.6) | "cell" (cell: 4.1, reserved)
  filter    "enemy" | "ally" | "any" | "downed_ally"  (who is a legal target)
  range     int   Chebyshev cells from the caster at resolution time (1 = adjacent)
  count     int   "combatants": max chosen targets (e.g. 2). "auto": max hit (null = all)
  includeSelf bool  ally abilities only (Mend on yourself)
```

- **`combatant`**: one chosen target, tracked like an attack (TICK_RESOLUTION D3).
- **`combatants`**: up to `count` **distinct** chosen targets, each tracked separately. Each target that is out of range or already defeated is wasted on its own; the rest still resolve.
- **`auto`**: targets are found **at resolution time**: every combatant matching `filter` within `range` of the caster, after this tick's movement, in a fixed order (by distance, then id). Cleave is `auto / enemy / range 1`. This is the first "area" shape (a burst around the caster) and doesn't need the 4.1 cell-targeting UI.
- **`trigger`** (3B.6 PR 2): prepared abilities only. The target is whoever set off the reaction (the enemy that stepped into reach). Nothing is chosen at planning; no `range` (the trigger decides who).
- **`cell`** is reserved for 4.1 (cell-targeted AoE with friendly fire, TICK_RESOLUTION D3).
- **One reach check.** Every target test goes through one helper (`CanTarget(caster, target, targeting)`: exists → filter → status → range). 3M.2 adds line of sight, 3M.4 the high-ground bonus, 3C.1 "visible to the caster". Nothing else in the resolver may check range.
- **Planning only validates what can't change:** the ids exist, the filter matches (team), the count isn't exceeded, there are no duplicates. Range is checked at resolution, because positions change during the round. That matches how melee reach works today.

### A5. Effects run in their own phase, in definition order ✅

> 3B.6: a move effect's steps provoke opportunity attacks like any move (Dash, Charge); `MoveEffect.Disengage` (default false) makes an ability move that doesn't. No shipped ability sets it yet.

Each effect kind has a fixed phase:

| Effect | Phase | Notes |
|---|---|---|
| `move` | Movement | Moves the caster. `to: "target"` (toward a free cell next to the target, like the AI does) or `to: "cell"` (a planned cell). `maxCells`. `usesMovementBudget` (bool, A7) |
| `attack` | Attacks | `roll: "melee" \| "ranged" \| "none"` (none = auto-hit). `accuracyBonus`. `damage` (A6). One hit roll **per target** |
| `heal` | Attacks | Positive HP delta in the same netting batch as damage (A8) |
| `stabilize` | Effects | Same as the Stabilize action (3B.5) |
| `push` (stretch) | Attacks, after damage | Pushes the target `cells` straight away from the caster. Stops at the first blocked or off-grid cell. Resolved in plan order after the tick's HP is applied |
| `apply_condition` (3B.4) | Effects | Puts a condition (copied from `conditions.json`) on each target for `duration` full rounds after the current one; `requiresHit` (default: the ability attacks) limits it to targets the same activation hit. Counts from the next tick. See `SPRINT_3B4.md` |

When an ability is at queue position *i*, its effects all run in **tick *i***, each in its own phase. A Charge (`move` to target + `attack`) moves in tick *i*'s Movement phase together with everyone else's moves (lockstep, Speed tiebreaks), then attacks in tick *i*'s Attack phase against post-movement positions. If the target ran out of reach, the attack is wasted (same as melee today). This is the "single action with multi-phase resolution" that TICK_RESOLUTION D2 and its summary call for.

Rules for multi-effect abilities:
- An ability uses **one queue slot** (one tick), whatever its AP cost. (Melee already costs 2 AP for one tick.)
- If the caster is defeated before a later phase in the same tick, the remaining effects are void, the same as the rest of their queue.
- Effects can see what earlier effects of the same activation did. For now that's only `cellsMoved`, for the Charge bonus (A6).

### A6. Damage spec ✅

```
damage
  die        "weapon" (the caster's BaseDamage) | "d10" | null (no die)
  bonus      int   flat, added after the die
  multiplier int   default 1 (applied before crit)
  bonusIfMoved { minCells, bonus }   Charge only; anything more general waits for conditions
```

This fills the pipeline's existing `abilityModifier` slot:

```
die + statModifier(0) + weaponBonus(0) + abilityModifier(bonus, bonusIfMoved) + resonanceBonus + leyBonus
→ × multiplier → × 2 if crit (still always false)
```

Basic melee and ranged attacks are the same as an ability with `die: "weapon"` and no bonus. They keep their own `ActionType`s (wire format and the pinned characterization tests), but run through the same attack function.

"Increased damage" then comes in three forms: a stronger spec (Power Strike), a conditional bonus (Charge), and buffs (Battle Cry, 3B.4 conditions). Only the first two are 3B.3.

### A7. Movement abilities and the movement budget ✅

All of a round's ordinary moves share `MovementRange` (3B.2). For ability movement:
- `usesMovementBudget: true` (default): the cells come out of the shared budget. A Charge is "a move that ends in an attack", not free extra ground.
- `usesMovementBudget: false`: the ability's own `maxCells`, on top of the budget. Dash is `move maxCells 3, usesMovementBudget false`. Its AP cost and cooldown are what pay for it.
- `PlanValidator` keeps walking the queue in order. A budget move counts toward the total. A `to: "target"` move is estimated from the target's current position (the real distance is known only at resolution, and the resolver caps it).
- Teleport (no path, ignores blockers) is a future `move` option (`path: "teleport"`). It isn't in 3B.3, but the field is reserved.
- Movement **debuffs** (Slowed, Rooted) are 3B.4 conditions that change the effective `MovementRange` / `maxCells`.
- *Added in implementation (PR 3):* an ability with a `move` effect checks its targeting `range` when the move starts; its melee attack then needs melee reach from where the move ended. Otherwise Charge (range 6) could strike from 6 cells away.

### A8. Healing ✅ (revive: decided)

- ✅ Heals are positive deltas in the **Attack phase** netting batch (DESIGN_SPIKE D3: never temporarily dead, a predicted heal can save someone from lethal damage). This means a heal must not run in the Effects phase.
- ✅ HP is netted, then clamped to `[0, MaxHp]`. (At 18/20 HP, taking 5 damage and healing 8 → 20, not 15.)
- ✅ Healing doesn't roll to hit. `amount` is a die + bonus (`"d6"`, `bonus: 2`).
- ✅ **Decided: a heal brings a Downed ally back to Standing** with the healed HP (it clears bleedout). That makes the healer a real role and gives Stabilize (cheap, anyone) a reason to exist next to Mend (cooldown, healer only). Stable allies can be healed up too. Dead stays dead. If a finishing blow and a heal hit the same Downed ally in the same tick, **the finishing blow wins** (same as `bleedout-finishing-blow-beats-stabilize`). (Rejected alternative: forbid healing Downed allies, Stabilize first.)
- ✅ Healing a target that was Standing at the start of the tick and goes to 0 in the same tick is just netting: if damage − heal leaves HP ≤ 0, they fall.

### A9. Prepared reactions are not in 3B.3 ✅ Decided

> **Resolved in 3B.6** (`SPRINT_3B6.md`): prepared reactions are abilities with `activation: "prepared"` and a `trigger` (PR 2: Brace, Overwatch; PR 3: Parry, Intercept). No AP refund for an unused one; several on the same event all fire.

Reactions are a separate framework: a watching state, trigger checks at every cell step and attack, interrupts. They get their own slice, **3B.6 Prepared Reactions & Opportunity Attacks**, after 3B.4, together with opportunity attacks and Disengage (TICK_RESOLUTION D4–D5). In 3B.3 only the `activation` field is reserved. The two reaction decisions the roadmap listed as "needed by 3B.3" (AP refund for unused prepared actions, duplicate triggers) move to 3B.6.

### A10. Cooldown rules ✅

- `cooldown: N` = the ability **can't be planned for the N rounds after the round it was used**. Used in round 3 with cooldown 2 → not available in rounds 4 and 5, available in 6.
- Stored as `Cooldowns: { abilityId: roundsLeft }` on `CombatantState`. At end of round, existing entries go down by 1 (removed at 0), *then* abilities used this round are set to N. This step is part of the resolver's end of round, next to bleedout.
- **The cooldown starts when the ability's tick is reached, even if every target is wasted.** Committing is the cost (as with wasted attacks). It doesn't start if the caster was defeated before that tick (the action never happened).
- An ability with cooldown ≥ 1 can appear **at most once** in a queue. Cooldown 0 abilities can repeat as long as AP allows.
- Planning rejects an ability that's on cooldown. The resolver also skips it (no-op entry with a wasted reason), as a guard.
- Cooldowns reset when an encounter ends (they live in Redis encounter state).
- `charges` (uses per encounter) will use the same storage and checks. Reserved, not built.

### A11. Spells are abilities ✅

There's no separate spell system. A spell is an ability tagged `spell` plus resonance tags, so resonance and ley bonuses apply to its main numeric output (damage, healing, move cells, later condition duration). Things that are spell-specific and deferred:
- **Spell accuracy.** No profile value for it yet. Spells use `roll: "ranged"` or `roll: "none"` until the stats decision. Resistance-style saves wait for `ConditionResist` (3B.4).
- **Spell resources** (slots, mana): `charges` covers "N per encounter". Anything more waits for the internal-magic-source design (`MAGIC_SYSTEM.md` deferred section).
- **Counterspell** needs prepared reactions (A9).

### A12. Environmental magic: split out of 3B.3 ✅ Decided

The roadmap and `MAGIC_SYSTEM.md` put ley points and resonance in 3B.3. Two things changed since then:
1. 3M.2 introduces **map files**. Ley points and resonance are map content: built now, they'd live in battle presets and Redis, then move into the map format in 3M.2.
2. The new families (multi-target, charge, heal, dash) already make abilities meaningful without resonance.

So 3B.3 builds only the ability side: `tags[]` (including resonance tags), the `resonanceBonus` / `leyBonus` pipeline slots (always 0), and `resonanceScaling` in the schema (parsed, not applied). The map side (resonance profile, ley points, channeling, rendering, the bonus preview, and applying `resonanceScaling`) is a new slice, **3M.5 Ley Points & Resonance**, which runs right after 3M.2 so it lands once, in the map file format.

### A13. Enemy AI: minimal ability use ✅

Per the 3B.0 "ongoing cost" rule. Fixed priority, still deterministic:
1. Heal (if it has one off cooldown): an ally in range below 50% HP, or Downed.
2. Multi-target (`auto`): if it would hit 2+ enemies from where it stands.
3. Charge: if the nearest enemy is beyond a plain walk-then-melee but within charge distance. *Changed in implementation (PR 3):* since Charge uses the movement budget, it never reaches further than a walk, so the AI charges whenever the nearest enemy isn't adjacent and a ready Charge reaches the cell next to it, instead of walking.
4. A stronger single-target attack off cooldown instead of a basic melee.
5. Otherwise today's behaviour.

*Added in 3B.4 (`SPRINT_3B4.md` C14):* between steps 1 and 2, a ready ability that only applies conditions (a buff someone it reaches lacks, or a debuff its target lacks); step 4 prefers a melee ability that would add a condition the target lacks (Hamstring). It plans within the AP and movement its conditions leave it, isn't planned while Stunned (or at 0 AP), and doesn't walk or charge while Rooted.

The AI never uses an ability to finish off a Downed enemy (same as today).

### A14. What 3B.3 explicitly doesn't do

Durations, buffs, debuffs, DoT (3B.4); cell-targeted AoE and its UI (4.1); prepared reactions and opportunity attacks (A9); crits; line of sight (3M.2); perception, concealment and hidden targets (after 3C); ability acquisition (progression); consumables (4.2).

---

## Perception and concealment: what to keep in mind now

Nothing is built for these in 3B.3, but these choices keep the door open:
- **Concealed / Revealed / Keen Senses** will be **conditions** (3B.4) that the 3C.1 vision computation reads (vision range modifiers, "can't be targeted unless revealed").
- **Every targeting check goes through `CanTarget`** (A4), so "the caster must be able to see the target" is one more clause there.
- **Planning-time validation stays in the Api**, which will know each player's vision after 3C.1. Rules shouldn't assume everyone sees everyone. It already doesn't. Keep it that way.
- **Outcome visibility:** `ResolutionCompleted` becomes per-player in 3C.1. Ability outcomes should stay as plain records (no pre-formatted text holding hidden information) so they can be filtered per recipient.

---

## Starter abilities (proposed content for 3B.3)

Numbers are a starting point for playtesting, on the 3B.1 profile scale (HP 10–24, d4–d8 damage, AP 4–5).

| Id | Name | AP | CD | Targeting | Effects | Loadout |
|---|---|---|---|---|---|---|
| `power_strike` | Power Strike | 3 | 2 | combatant / enemy / range 1 | attack melee, weapon die +4 | Bruiser, Brute |
| `cleave` | Cleave | 3 | 2 | auto / enemy / range 1 | attack melee, weapon die, each target | Bruiser |
| `twin_shot` | Twin Shot | 3 | 1 | combatants / enemy / range 8 / count 2 | attack ranged, weapon die, each target | Scout |
| `charge` | Charge | 3 | 2 | combatant / enemy / range 6 | move to target (budget) → attack melee, weapon die, +3 if moved ≥ 2 | Skirmisher, Brute |
| `dash` | Dash | 1 | 2 | self | move to cell, 3 cells, outside budget | Scout, Skirmisher |
| `mend` | Mend | 2 | 2 | combatant / ally / range 3 / includeSelf | heal d6+2 (revives Downed, A8) | Skirmisher |
| `firebolt` | Firebolt | 3 | 1 | combatant / enemy / range 8 | attack ranged, d8 +1, tags `[flame, ranged, spell]` | none (sample spell for later) |

Add a Quick Battle preset that uses them (e.g. **Abilities**: Bruiser + Skirmisher + Scout vs a Brute and three Grunts).
