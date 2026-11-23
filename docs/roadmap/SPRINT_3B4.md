# Sprint — Slice 3B.4: Conditions & Status Effects

> **Purpose:** Implementation guide for Slice 3B.4. Hand this to Claude Code alongside `CLAUDE.md`. There is no separate design doc for conditions: the decisions are in the **Decisions** section below (C1–C14). Settle the ones marked *to confirm* before PR 1, then treat them as locked and note any change made during implementation here, as `SPRINT_3B3.md` did. For the high-level plan see `ROADMAP_STRATEGIC.md`. For how abilities work see `ABILITIES.md` (conditions are applied through the `apply_condition` effect it reserves in A5).
>
> **Status:** ⬜ Not started.
>
> **Goal:** Abilities (and Defend) can put **conditions** on combatants: named, time-limited bundles of modifiers in the game's own vocabulary. A condition can deal or heal damage every round, make someone more or less likely to hit or be hit, change how much damage they take or deal, change how far they can move, change how many AP they have, or stop them from moving or acting at all. Resistance comes from a `ConditionResist` profile value. The enemy AI plays around conditions and uses a few. All of it can be tried in a new **Conditions** Quick Battle.

---

## Requirements

What this slice has to support. The first six were asked for explicitly on top of the roadmap entry.

| # | Requirement | Modifier (C3) | Example conditions | PR |
|---|---|---|---|---|
| R1 | Ongoing damage | `periodic` (damage, die and/or flat, optional tags) | Burning (d4 flame / round), Poisoned (2 / round) | 2 |
| R2 | More or less likely to hit | `accuracy` (attacker side) and `dodge` (target side), optionally melee or ranged only | Blessed (+2 to hit), Dazzled (−3 ranged to hit), Guarded (+3 dodge), Exposed (−3 dodge) | 1 |
| R3 | Resistant or vulnerable to damage | `damageTaken` (percent and/or flat, optionally only from tagged sources) | Fortified (−50%), Marked (+2 per hit), Flame Ward (−50% from `flame`) | 1 |
| R4 | More or less movement | `movement` (± cells to the round's movement budget) | Slowed (−2), Hasted (+2) | 3 |
| R5 | More or fewer AP | `ap` (± to the round's AP budget) | Weakened (−1 AP), Hasted (+1 AP) | 3 |
| R6 | Unable to take actions | `blocks: ["all"]` (and `["move"]` for the lighter version) | Stunned (no actions), Rooted (no movement) | 3 |
| R7 | Roadmap: game's own vocabulary, durations, `ConditionResist` (0% default) | Condition catalog (C1), durations (C5), resist roll (C9) | — | 1, 2 |
| R8 | Roadmap: resonance-triggered secondary effects use this system | Condition ids are what `resonanceScaling.thresholdEffects` names; **applying them is 3M.5** | Firebolt → Burning at flame resonance 3 | — |
| Extra | Defend finally does something (`ABILITIES.md` quirk 4) | Defend grants Guarded (C7) | Guarded | 1 |
| Extra | Damage buffs (`ABILITIES.md`: "Battle Cry: allies +2 damage") | `damageDealt` (flat) | Emboldened (+2 damage) | 1 |
| Stretch | Shove (forced movement, carried over from 3B.3) | `push` ability effect (not a condition) | — | 3 |

---

## Decisions

Recommendations are written as the decision. Those marked **to confirm** are the ones with a real alternative worth a look; the rest follow directly from existing locked decisions.

### C1. Conditions are data: a JSON catalog, not a table

`server/src/RpgGame.Api/Conditions/conditions.json`, loaded and validated at startup by a `ConditionCatalog` (same pattern as `AbilityCatalog`). **No `condition_definitions` table**: this replaces the table in the roadmap's 3B.4 database changes, for the same reasons as `ABILITIES.md` A2. Conditions aren't attached to profiles; they arrive through abilities (and Defend).

```
ConditionDefinition
  id, name, description
  harmful        bool   a debuff: resistible (C9), shown in red, the AI avoids stacking it
  tags[]         e.g. ["flame"]; the tags its periodic damage carries (C4)
  modifiers      Modifiers (C3)
```

The duration is **not** part of the definition: it belongs to whoever applies the condition (`apply_condition` effect, C2), so a short and a long Burning share one definition.

### C2. Applying a condition: the `apply_condition` ability effect

```jsonc
{ "kind": "apply_condition", "condition": "burning", "duration": 2, "requiresHit": true }
```

- Resolves in the **Effects** phase of the ability's tick (`ABILITIES.md` A5), in plan order, then target order.
- Applies to the ability's targets (the same list its attack used; for `self`, the caster; for `auto` abilities with no attack, targets are found in the Effects phase with the same rules).
- `requiresHit` (default **true** when the ability also has an `attack` effect, false otherwise): only targets that the same activation hit get the condition. A miss or a wasted attack means no condition.
- An ability with no attack still needs a legal target at resolution: it goes through the same reach check as attacks and heals (`CheckAttackTarget` / its heal twin: filter → status → range), so line of sight lands there in 3M.2 for conditions too.
- No condition is applied to a target that is Downed, Stable or Dead (C10), or that fell earlier in this tick.
- **Snapshot:** `AbilityCatalog` resolves the condition id when it loads, so the ability snapshot on `EncounterCombatant.Profile` carries the **full condition definition** inside the effect. The resolver never reads a catalog (A2), and retuning `conditions.json` never changes a running fight.

### C3. Modifiers

One record covers every kind of condition. All fields are optional; a condition usually sets one or two.

```
Modifiers
  accuracy      int     + to the d20 of this combatant's attacks                       (R2)
  dodge         int     + to this combatant's Dodge                                    (R2)
  attackKind    "melee" | "ranged" | "any" (default any): which attacks accuracy/dodge apply to
  damageDealt   int     flat + to this combatant's damage on each hit                  (extra)
  damageTaken   { percent: int, flat: int, tags: [] }   on each hit this combatant takes (R3)
                        percent −50 = resistant, +50 = vulnerable, −100 = immune;
                        flat −2 = armour-like, +2 = "marked"; tags: only from sources with one of
                        these tags (empty = all damage)
  movement      int     ± cells to the round's movement budget                         (R4)
  ap            int     ± AP to the round's AP budget                                  (R5)
  periodic      { kind: "damage" | "heal", die: "d4" | null, bonus: int }  each round (R1)
  blocks        ["all"] | ["move"]   what this combatant can't do                      (R6)
```

`blocks` is a list so more categories (`"abilities"` for Silenced, `"attack"` for Disarmed) can be added later with one check each; only `all` and `move` are built.

**Stacking across different conditions:** every numeric modifier from every active condition is **summed** (Blessed +2 and Dazzled −3 → −1). Percentages are summed too and clamped to [−100, +100]. Blocks are a union.

**Effective values** are clamped: movement budget ≥ 0, AP ≥ 0, damage per hit ≥ 0. Dodge and accuracy aren't clamped. A combatant at 0 effective AP can't plan anything and is treated like a stunned one for planning (C8).

### C4. Where modifiers enter the hit check and the damage pipeline

Hit check (`ComputeAttack`):

```
d20 + profile accuracy + ability accuracyBonus + Σ attacker accuracy (matching attackKind)
  ≥ target Dodge + Σ target dodge (matching attackKind)
```

`AttackRoll` keeps `Accuracy` and `TargetDodge` as totals (so the hit rule stays `Total ≥ TargetDodge`) and gains `ConditionAccuracy` / `ConditionDodge`: the parts of those totals that came from conditions, so the feed and the timeline tooltip can explain a hit or miss. Attacks with `roll: none` ignore both.

Damage pipeline (fills a new slot; the existing ones stay):

```
die + statModifier + weaponBonus + abilityModifier + conditionModifier (attacker's damageDealt) + resonanceBonus + leyBonus
→ × multiplier → × 2 if crit
→ target's damageTaken: + Σ flat, then × (100 + Σ percent) / 100, rounded down, min 0
```

- `damageTaken` with `tags` only applies when the source has a matching tag. A source's tags are: the ability's tags; `melee` or `ranged` for basic attacks; the condition's tags for periodic damage.
- **Per hit**, before the tick's netting (so a heal in the same tick isn't reduced).
- **Finishing blows are untouched**: any hit on someone already down kills (3B.5).
- Heals aren't modified by conditions in this slice (no "healing received" modifier).
- `AttackOutcome` gains `ConditionDamage` (the net change from `damageDealt` and `damageTaken`, signed), so the feed can say "(−4 resisted)" / "(+2 marked)".

### C5. Durations ✅ Decided (2026-11-23)

- `duration: N` (N ≥ 0) on the effect. The condition is active **from the moment it is applied until the end of the round it was applied in, plus N more full rounds.**
- At the end of each round, every condition that wasn't applied this round counts down by 1; one that reaches 0 (or was applied with duration 0) is removed after the round. Same rule as bleedout: the round it started in doesn't count.
- Examples: Slowed `duration 1` applied in round 3 → rest of round 3 and all of round 4 (so it does cut round 4's movement budget). Guarded `duration 0` → rest of this round only.
- Stored as `roundsLeft` (N, counting down) plus `appliedRound`.
- **Players never see the number.** The data keeps `duration` / `roundsLeft`, but the UI (chips, tooltips, feed, ability bar) says it in words: 0 → "until end of round", 1 → "until end of next round", N ≥ 2 → "for N more rounds". There is no "0-round" anything on screen. One shared client helper (`describeDuration(roundsLeft, appliedThisRound)`) and its feed-formatter twin produce the wording, so every surface says the same thing.
- **One condition per effect, whatever its length.** A short stun and a long stun are both **Stunned** (one definition); the ability that applies it picks the duration. No separate names for short and long versions.

*Alternative considered:* count the application round (duration 1 = rest of this round only). Rejected: round-level modifiers (AP, movement, stun at planning) would then need duration 2 to do anything, which reads wrong in tooltips.

### C6. When modifiers take effect ✅ Decided

The tick model already says effects applied in tick *i* matter from tick *i+1* (`TICK_RESOLUTION.md`, "If a condition applied in Tick 1 blocks movement, their Tick 2 move fails"). **Decided 2026-11-23:** the round's **budgets** (AP and movement) only change at the next planning phase, so a condition never shrinks or grows a plan that's already locked in. Slowed and Hasted are even-handed: neither one wastes nor invalidates the current plan. Stunned and Rooted do take effect from the next tick: they're the deliberate way to disrupt a plan in progress.

| Modifier | Takes effect | Why |
|---|---|---|
| `accuracy`, `dodge`, `damageDealt`, `damageTaken` | From the **next tick** after it's applied (it was applied in the Effects phase, after this tick's attacks) | Per roll; no planning involved |
| `blocks` | From the **next tick**: remaining queued actions are blocked (C8) | Matches the tick doc |
| `movement` | From the **next planning phase** only | Like AP, it's a budget the plan was checked against. Changing it mid-round could only hurt (Slowed cuts a planned move), never help (Hasted's extra cells can't be used by a locked-in plan) |
| `ap` | From the **next planning phase** only | AP is spent at planning (3B.2: no `currentAp`). Cutting a queue mid-round is what Stunned is for |
| `periodic` | End of round (C11) | One place, one batch |

At planning, the Api uses the conditions active **at the start of the planning phase**: these are the effective `MaxAp`, `MovementRange` and blocks sent to the client and checked by `PlanValidator`. The resolver uses the same values for the whole round (from the conditions in the `EncounterState` it's given), so planning and resolution always agree on the budgets.

With C5, this means "Slowed, 1 round" applied in round 3 does nothing more in round 3 and gives round 4 two fewer movement cells; then it expires. (Its chip shows from the moment it lands, marked "from next round" until then.)

### C7. Defend grants Guarded ✅ Decided (for now, 2026-11-23)

Defend (1 AP, basic action) applies **Guarded** (`dodge +3`, duration 0) to the defender **at the start of its own tick** (before Movement), not in the Effects phase, so it protects against that tick's attacks and every later one this round. Defend stays a basic action (wire format and `EffectOutcome` unchanged; the outcome additionally lists the condition applied). Defending twice refreshes, it doesn't stack (C12).

This makes the timeout default ("defend in place") and the AI's fallback a real choice, and gives `ABILITIES.md`'s quirk 4 its fix.

*Alternative:* Defend anywhere in the queue guards for the whole round. Rejected: a 1-AP filler at the end of every queue would make +3 Dodge nearly free.

*Characterization tests:* Defend now produces an extra feed line ("Brakka is Guarded"). Old strings must stay byte-for-byte; add the new line as a separate entry and new characterization cases, as 3B.3 did. Scripted-dice tests where a defender is attacked later in the same round will change outcomes: review each one rather than editing expectations blindly.

### C8. Blocked actions (Stunned, Rooted, 0 AP)

**Mid-round** (condition applied during resolution):
- `blocks: all`: from the next tick, the combatant's remaining queued actions don't resolve. Each is reported once as a blocked action (`TickOutcome.Blocked`: `BlockedAction(CombatantId, PlannedAction, ConditionId)`), so the feed says "Brakka is Stunned and can't act".
- **When a stun lands**, the feed and the playback cue say how long it lasts, in C5's words: "Brakka is Stunned until the end of the round" (duration 0) or "Brakka is Stunned until the end of next round" (duration 1). The next planning phase then shows the result: after a duration-0 stun the character plans as normal (the condition has expired); after a longer one their action bar shows they skip the round (below). A blocked ability **doesn't start its cooldown** (it never happened, same as a caster defeated first, A10). AP isn't refunded (there's no AP to refund).
- `blocks: move`: plain moves are blocked (reported the same way); an ability's `move` effect doesn't move the caster, and its other effects still resolve from where they stand (a Rooted Charge strikes only if already in reach). Forced movement (Shove, stretch) still moves a Rooted target.

**At planning** (condition active when the planning phase opens):
- `blocks: all`, or 0 effective AP: the combatant skips the round. The server **locks it in automatically with an empty plan** when the phase opens (like AI combatants, one `PlayerLockedIn` each), and `PlanAction` for it is rejected ("Brakka is Stunned and can't act this round"). Its timeout default is nothing, not defend.
- `blocks: move`: `PlanValidator` rejects moves and abilities with a move effect ("Brakka is Rooted and can't move").

### C9. ConditionResist

- New profile value `ConditionResist` (percent, 0–100, default 0; `conditionResist` in `profiles.json` and as a battle-preset override). Roadmap: 0% by default.
- When a **harmful** condition is applied by an **enemy** (`Relations.Between`), the target rolls `d100`; ≤ `ConditionResist` → resisted (reported, nothing applied). Beneficial conditions and conditions from allies or yourself are never rolled against.
- **The d100 is only rolled when `ConditionResist > 0`**, so every existing scripted-dice test and scenario keeps its dice sequence. Dice order: Effects phase, plan order, then target order, after that tick's attack and heal rolls.
- Ships with Brute `conditionResist: 25`; every other profile stays at 0. To be revisited with stats (Resilience × 5% in `DESIGN_SPIKE.md`).

### C10. Conditions and defeat

- **Going Downed or dying clears all conditions.** A revived combatant (heal, A8) comes back with none. This avoids "Stunned while Downed" questions and periodic damage acting as an unplanned finishing blow.
- Periodic damage never targets someone who is Downed, Stable or Dead (they have no conditions anyway).
- An `apply_condition` on a target that is out is wasted (`TargetDefeated`).

### C11. Periodic effects (ongoing damage) resolve at end of round ✅ Decided (for now, 2026-11-23)

New step at the **start of end of round**, before bleedout:

1. **Periodic effects**: every standing combatant with `periodic` conditions rolls them (combatant order, then the order conditions were applied), damage modified by its own `damageTaken` (C4, using the condition's tags), heals as positive deltas. All of it is netted as **one batch**, like a tick's attacks (DESIGN_SPIKE D3), then HP is capped at max.
2. **0-HP check** over everyone: a combatant who falls here goes Downed (or dies) exactly as in a tick. They fell this round, so their bleedout doesn't count down this round (3B.5 rule), and C10 clears their conditions.
3. Bleedout (as today).
4. **Conditions count down and expire** (C5).
5. Cooldowns (as today).

The encounter-end check already runs after resolution, so a hero killed by Burning at the end of a round can end the encounter. Periodic damage has no attacker and no hit roll; `RoundOutcome` gains `PeriodicEffects` (`PeriodicOutcome(CombatantId, ConditionId, Amount, Kind, HpBefore, HpAfter, Status)`) and `ExpiredConditions`.

*Alternative considered:* tick periodic damage in every tick's Effects phase. Rejected: rounds have a variable number of ticks (AP), so damage per round would depend on how long everyone's queues were.

### C12. Re-applying the same condition

The same condition on the same target **doesn't stack**: it refreshes to the longer of the remaining and the new duration, and the newest source becomes its source. Two different conditions with the same modifier (Slowed and Hamstrung) do stack (C3). Reported as `Refreshed` rather than `Applied`.

### C13. Storage

- **Redis:** new hash `encounter:{encounterId}:conditions`: combatantId → JSON list of `{ conditionId, roundsLeft, appliedRound, sourceId, definition }` (the definition is the snapshot from C2). Per encounter, **not** `character:live` as the roadmap says, for the same reason cooldowns moved in 3B.3: `character:live` outlives the encounter. Deleted with the rest of the encounter state.
- **Postgres:** no new table. The `CombatProfileValues` JSONB grows `ConditionResist` and the `apply_condition` effect (with its embedded condition) in ability snapshots. Migration `AddConditionEffects` (model snapshot only, like `AddMovementEffects`).
- **Rules:** `CombatantState.Conditions` (`IReadOnlyList<ActiveCondition>`, compared by content like `Cooldowns`); `RoundOutcome.Next` carries the new list.

### C14. Enemy AI: minimal use

Per the 3B.0 "ongoing cost" rule; still deterministic.
- **Plans within its effective budgets** (AP, movement) and doesn't plan moves while Rooted. A stunned AI combatant is auto-locked with an empty plan (C8) and isn't planned.
- **Uses a ready condition ability** as a new step between multi-target (A13 step 2) and stronger single-target (step 4): a harmful one on the enemy it's attacking if that enemy doesn't already have the condition; a beneficial one (Battle Cry, Haste) on itself or allies in range if at least one of them doesn't have it.
- Doesn't otherwise reason about conditions (no fleeing Burning, no focus on Exposed targets: 3D.2).

---

## PR plan

Three PRs, in order, each playable in Quick Battle on its own (like 3B.3).

| PR | Delivers | Requirements | Conditions / abilities |
|---|---|---|---|
| **1. Framework, hit & damage modifiers** | Catalog, `ActiveCondition` state, `apply_condition`, durations and expiry, refresh, accuracy / dodge / damage dealt / damage taken, Defend → Guarded, chips on tracker and tokens | R2, R3, R7 (catalog), extras | Guarded, Blessed, Dazzled, Exposed, Emboldened, Fortified, Marked, Flame Ward · Battle Cry, Stoneskin, Mark |
| **2. Ongoing damage & resistance** | `periodic` at end of round (damage and heal), falling at end of round, `ConditionResist` + resist roll | R1, R7 (resist) | Burning, Poisoned, Regenerating · Firebolt gains Burning, Envenom |
| **3. Movement, AP & action denial** | `movement`, `ap`, `blocks` (all / move), effective budgets at planning, auto lock-in for the stunned, blocked actions in resolution and playback. Stretch: Shove | R4, R5, R6 | Slowed, Hasted, Rooted, Weakened, Stunned · Hamstring, Haste, Entangle, Shield Bash, Sap, Hex (stretch: Shove) |

Follow the design loop for each PR: rules + scenarios first (`dotnet test server/tests/RpgGame.Rules.Tests`), then server, then client, then E2E.

---

## PR 1 — Framework, hit & damage modifiers

### Rules (`server/src/RpgGame.Rules/`)

New file `Conditions.cs`:
- `ConditionDefinition(Id, Name, Harmful, Tags, Modifiers)`.
- `Modifiers(Accuracy = 0, Dodge = 0, AttackKindFilter AttackKind = Any, DamageDealt = 0, DamageTakenModifier? DamageTaken = null, Movement = 0, Ap = 0, PeriodicEffect? Periodic = null, IReadOnlyList<BlockKind> Blocks = [])`. PR 1 only acts on accuracy, dodge, damage dealt and damage taken; the rest are parsed and stored from PR 1 but acted on in PRs 2–3 (so the wire format and snapshot change once).
- `DamageTakenModifier(int Percent, int Flat, IReadOnlyList<string> Tags)`.
- `ActiveCondition(ConditionDefinition Definition, int RoundsLeft, int AppliedRound, string? SourceId)`. `Id` → `Definition.Id`.
- `ConditionMath` (static, pure): `AccuracyBonus(conditions, AttackKind)`, `DodgeBonus(conditions, AttackKind)`, `DamageDealtBonus(conditions)`, `ApplyDamageTaken(damage, conditions, sourceTags)`, and from PR 3 `EffectiveMovementRange(profile, conditions)`, `EffectiveMaxAp(profile, conditions)`, `IsBlocked(conditions, BlockKind)`. Everything that asks "what do this combatant's conditions do?" goes through it: resolver, `PlanValidator`, `EnemyAi` and (via the Api) the client payload.

`Abilities.cs`: `ApplyConditionEffect(ConditionDefinition Condition, int Duration, bool RequiresHit)` (`AbilityEffect` subtype; Effects phase).

`Model.cs`:
- `CombatantState.Conditions` (default empty; content equality, like `Cooldowns`).
- `EncounterState` gains `RoundNumber` (default 1) so `AppliedRound` can be set; the Api already knows it.

`RoundResolver`:
- `RoundContext.Conditions`: working copy per combatant.
- **Effects phase**: after Defend and Stabilize, resolve each active ability's `ApplyConditionEffect`s in plan order, then target order. Track which targets each activation **hit** in the Attack phase (`RoundContext.TickHits`: caster → set of target ids) for `requiresHit`. New / refreshed / wasted results go to `TickOutcome.Conditions` (`ConditionOutcome(SourceId, TargetId, ConditionId, AbilityId?, Result /* Applied | Refreshed | Resisted | Wasted */, RoundsLeft, WastedReason)`).
- **Defend** applies Guarded at the **start of its tick** (C7). Guarded is a built-in definition in `RulesConfig` (`GuardedCondition`, dodge +3), so Rules doesn't need the catalog and scenarios get it for free. It's reported as a `ConditionOutcome` with no ability id.
- **`ComputeAttack`**: add the accuracy and dodge bonuses (C4) and fill `AttackRoll.ConditionAccuracy` / `ConditionDodge`; add the `conditionModifier` slot; apply the target's `damageTaken` after multiplier and crit; set `AttackOutcome.ConditionDamage`. Conditions applied earlier in the **same tick** don't count yet (C6): read them from a snapshot taken at the start of the tick's Attack phase.
- **Falling** (`SetStatus` to Downed / Dead) clears the combatant's conditions (C10).
- **End of round**: count down and expire (C5), next to `ResolveCooldowns`. `RoundOutcome.ExpiredConditions` (combatant id + condition id). `Next` carries the new lists.
- `WastedReason` needs no new values in PR 1 (`TargetDefeated`, `OutOfRange`, `InvalidTargetType` cover it).

`EnemyAi`: the condition-ability step (C14) for beneficial self/ally conditions (Battle Cry) and harmful ones (Mark). Treat the existing `Defend` fallback as is (it now grants Guarded).

### Server (`server/src/RpgGame.Api/`)

- **`Conditions/conditions.json`** + **`ConditionCatalog`** (Singleton, validated at startup, stops the API naming the broken condition; copied to output). Validation: unique ids, known modifier fields, `percent` in [−100, 100], `attackKind` / `blocks` values known, `periodic` has a die or a non-zero bonus, at least one modifier set. Registered **before** `AbilityCatalog`.
- **`AbilityCatalog`**: accepts `apply_condition` (`condition` must exist in `ConditionCatalog`, `duration ≥ 0`, `requiresHit` defaults per C2); embeds the definition. `resonanceScaling.thresholdEffects` values that name a condition are now checked against the catalog (others, like `ember`, are left alone until 3M.5/3D.4).
- **Snapshot**: `AbilityValues` effects gain the `apply_condition` shape with a nested `ConditionValues`. `AbilityMapping.ToRules` maps it. Migration `AddConditionEffects` (model snapshot only).
- **Redis** (`IRedisGameState`): get/set the `encounter:{encounterId}:conditions` hash (C13); `EncounterStateLoader` fills `CombatantState.Conditions` and `RoundNumber`; `ResolutionService` writes the new lists after resolution (GameEvents to DB before Redis, as today); cleared with the encounter.
- **`RulesWireMapping`**: condition result strings (`applied`, `refreshed`, `resisted`, `wasted`), attack kind filter strings.
- **`ResolutionFeedFormatter`**: new lines, existing strings byte-for-byte unchanged:
  - "Brakka's Battle Cry: Brakka, Mira and Kestrel are Emboldened (1 round)"
  - "Kestrel's Mark: Goblin is Marked (2 rounds)"
  - "Brakka is Guarded"
  - an attack that conditions changed gets a suffix: "… for 6 damage (−6 Fortified) …", "(+2 Marked)"
  - end of round: "Goblin is no longer Marked"
- **`CombatantDtos`**: `PlanningCombatantDto` and `CombatantDto` gain `conditions`.

### Hub contracts (`Contracts/Hub/`, then `npm run contracts:hub`)

Add every field the slice needs now (as 3B.3 PR 1 did with `TargetCombatantIds`), so the wire format changes once:

| Record | Change |
|---|---|
| new `ConditionDto` | `Id, Name, Description, Harmful, Tags, Modifiers` (a flat `ModifiersDto` mirroring C3) |
| new `ActiveConditionDto` | `ConditionId, Name, RoundsLeft, SourceId, Harmful` (the client looks up the description in the combatant's known conditions or `conditionCatalog`) |
| `PlanningCombatantDto` | + `Conditions` (`ActiveConditionDto[]`); from PR 3 also `BaseMaxAp`, `BaseMovementRange`, `Blocks` (`maxAp` / `movementRange` become the effective values) |
| `CombatantDto` | + `Conditions` |
| `PlanningPhaseStarted` | + `ConditionCatalog` (`ConditionDto[]`: every condition that can appear in this encounter, i.e. those in any combatant's loadout plus Guarded) |
| `AbilityDto` | + `AppliesConditions` (`{ conditionId, duration, requiresHit }[]`) for tooltips |
| Resolution attack `roll` | + `ConditionAccuracy`, `ConditionDodge` |
| Resolution attack entry | + `ConditionDamage` |
| Resolution tick | + `Conditions[]` (`ConditionOutcome`), + `Blocked[]` (PR 3) |
| `ResolutionCompleted` | + `EndOfRound.Periodic[]` (PR 2), + `EndOfRound.Expired[]`, + `Conditions` (combatantId → `ActiveConditionDto[]` after the round, so clients don't recompute) |

Add each new payload shape to `ContractShape` in the integration tests.

### Client (`client/src/app/`)

- **NGXS `EncounterState`**: combatants carry `conditions`; store `conditionCatalog` from `PlanningPhaseStarted`. Builders (`testing/builders.ts`): `makeCondition`, `makeActiveCondition`, defaults for the new required fields (`conditions: []`).
- **New `ConditionChipsComponent`** (`npx ng generate component features/encounter/condition-chips`, separate template + style): small chips (short name + rounds left, red for harmful, green for beneficial), tooltip with the description and modifiers in plain words ("+2 to hit", "takes 50% less damage", "−2 movement"). Used by `EncounterTrackerComponent` (per row) and as a compact icon row under each token in `GridComponent`'s token layer.
- **Playback** (`playback-timeline.ts`): a **condition cue** in the effects beat: floating text with the condition name over each target (red/green), a short tinted aura; "RESISTED" text for a resist (PR 2). The end-of-round beat removes expired chips. Condition state shown on screen follows playback (like HP), so add `conditionsOf(combatantId)` to `ResolutionPlaybackService`.
- **`round-lanes.ts` / `RoundTimelineComponent`**: the effects outcome shows applied conditions; the tick details show `ConditionAccuracy` / `ConditionDodge` / `ConditionDamage` next to the roll ("d20 12 + 1 (+2 Blessed) vs 12 (+3 Guarded)").
- **`feed-format.ts`**: highlight condition names (harmful red, beneficial green).
- **`AbilityBarComponent`** tooltip: "Applies Marked (2 rounds) on hit".
- **`EncounterPreviewComponent`**: mock conditions on a few combatants.

### Content

`conditions.json` (PR 1 set):

| Id | Name | Harmful | Modifiers |
|---|---|---|---|
| `guarded` | Guarded | no | dodge +3 (built in; also listed for display) |
| `blessed` | Blessed | no | accuracy +2 |
| `dazzled` | Dazzled | yes | accuracy −3, ranged only |
| `exposed` | Exposed | yes | dodge −3 |
| `emboldened` | Emboldened | no | damage dealt +2 |
| `fortified` | Fortified | no | damage taken −50% |
| `marked` | Marked | yes | damage taken +2 per hit |
| `flame_ward` | Flame Ward | no | damage taken −50% from `flame` |

`abilities.json` additions:

| Id | AP | CD | Targeting | Effects |
|---|---|---|---|---|
| `battle_cry` | 2 | 3 | auto / ally / range 3 / includeSelf | apply Emboldened, duration 1 |
| `stoneskin` | 1 | 3 | self | apply Fortified, duration 1 |
| `mark` | 1 | 2 | combatant / enemy / range 8 | apply Marked + Exposed, duration 1 (no attack, always applies) |

New Quick Battle preset **`conditions.json`** ("Conditions"): Bruiser (Battle Cry, Power Strike), Skirmisher (Stoneskin, Mend, Charge), Scout (Mark, Twin Shot) vs Brute + Goblin Shaman (Battle Cry) + two Grunts. Grows in PRs 2–3. Profiles are left alone in PR 1, so the other presets play exactly as before (apart from Defend now guarding).

### Tests

- **Scenarios**: extend the format (README) with a top-level `"conditions"` block (inline, like `"abilities"`; a shared `Scenarios/library/conditions.json` used unless the scenario defines its own), combatant field `conditions` (`[{ "id": "blessed", "rounds": 1 }]`), and `expect.conditions` (combatant → `{ id: roundsLeft }`). New files:
  - `condition-blessed-turns-miss-into-hit.json`, `condition-guarded-turns-hit-into-miss.json` (Defend in tick 1, attacked in tick 1 and 2).
  - `condition-dazzled-ranged-only.json` (melee unaffected).
  - `condition-fortified-halves-damage.json` (rounding down), `condition-marked-flat-bonus.json`, `condition-flame-ward-only-flame.json` (basic melee unaffected, Firebolt halved).
  - `condition-applied-tick1-counts-from-tick2.json` (Exposed applied in tick 1 doesn't help a tick-1 attack, does help tick 2).
  - `condition-requires-hit-miss-applies-nothing.json`.
  - `condition-duration-counts-down.json` / `rounds-condition-expires.json` (duration 1 lasts through the next round, gone after it; duration 0 gone at end of round).
  - `condition-refresh-not-stack.json`.
  - `condition-cleared-when-downed.json`, `condition-on-downed-target-wasted.json`.
  - `ai-battle-cry.json`, `ai-marks-target.json`.
- **`ModelTests` / `RoundResolverTests`**: `ConditionMath` (summing, clamping, tag filter, attack kind filter), damage pipeline order (multiplier, then damage taken, floor, min 0), finishing blow ignores modifiers.
- **Api**: `ConditionCatalogTests` (shipped file loads; each validation error names the condition), `AbilityCatalogTests` (unknown condition id stops startup; embedded snapshot), new `ResolutionServiceCharacterizationTests` cases (feed lines, conditions persisted to Redis), `QuickBattleServiceTests` (Conditions preset loads).
- **Integration**: Redis round trip for `encounter:{id}:conditions` (key name, type, stored format); a `Hub/` test that applies Mark and sees `conditions` in the next `PlanningPhaseStarted`.
- **Client**: `ConditionChipsComponent` spec, `EncounterState` spec for conditions from payloads, playback-timeline spec for the condition cue, feed-format spec.
- **E2E**: `quick-battle.spec.ts` gains a case: start **Conditions**, Mark a Grunt with the Scout, lock in, see the Marked chip on the Grunt and "is Marked" in the feed.

**Done when:** in the Conditions Quick Battle, Battle Cry and Mark show chips that count down and expire, Fortified visibly halves a hit in the feed, Defend makes a hit miss, and the Goblin Shaman uses Battle Cry.

---

## PR 2 — Ongoing damage & resistance

### Rules
- **End of round, step 1–2** (C11): `ResolvePeriodic(round)` before `ResolveBleedout`: rolls each standing combatant's `periodic` conditions in combatant order then application order (damage die rolls go into the dice sequence here), applies the target's own `damageTaken` with the condition's tags, nets damage and healing per combatant, caps at max, then runs the same 0-HP check as a tick (extract it into one helper used by both). New falls go into `DefeatedThisRound`; C10 clears their conditions; their bleedout doesn't count down this round.
- `PeriodicOutcome` on `RoundOutcome.PeriodicEffects`.
- **`ConditionResist`** on `CombatProfile` (default 0). In the Effects phase, a harmful condition from an enemy rolls `d100` only if the target's resist > 0 (C9); ≤ resist → `ConditionOutcome.Result = Resisted`.
- `EnemyAi`: no change needed beyond PR 1's step (Firebolt with Burning counts as a harmful condition ability; the AI doesn't reapply Burning to someone already burning).

### Server
- `profiles.json` / `CombatProfileValues` / preset override: `conditionResist`. `CombatProfileCatalog` validates 0–100. Brute gets 25.
- Feed: "Goblin takes 3 burning damage (HP: 7 → 4)", "Mira regenerates 2 HP (HP: 5 → 7)", "Goblin burns down!" (a fall at end of round reuses the existing down/death wording), "Ogre resists Burning".
- Payload: `EndOfRound.Periodic[]`.

### Client
- Playback: the **end-of-round beat** (exists for bleedout) shows periodic damage/heal first (orange `-3` with a small flame/drop icon by condition tag, green `+2`), then any falls (DOWN), then bleedout, then expiries. Tracker HP follows it.
- "RESISTED" cue on the effects beat.
- Round timeline: an "End of round" column for periodic effects (if it doesn't crowd the lanes; otherwise in the tick details).

### Content
- Conditions: `burning` (harmful, tags `flame`, periodic d4 damage), `poisoned` (harmful, periodic 2 damage), `regenerating` (periodic heal d4).
- `firebolt` gains `apply_condition burning, duration 1, requiresHit` (a hit deals the bolt now and burns at the end of this round and the next).
- New `envenom`: 2 AP, cooldown 2, melee weapon die, applies Poisoned duration 2 on hit.
- Conditions preset: Scout gets Firebolt, one Grunt gets Envenom, and the Brute shows the 25% resist.

### Tests
- Scenarios: `periodic-burning-end-of-round.json`, `periodic-damage-and-regen-net.json`, `periodic-flame-ward-halves-burning.json`, `periodic-downs-hero-no-bleedout-tick.json`, `periodic-kills-monster-ends-encounter.json` (Api level for the end), `rounds-burning-two-rounds.json`, `resist-roll-succeeds.json`, `resist-roll-fails.json`, `resist-zero-rolls-nothing.json` (dice sequence unchanged), `resist-not-rolled-for-ally-buff.json`.
- Api: characterization for periodic feed lines and their order relative to bleedout lines; `EncounterEndCheck` with an end-of-round death.
- Client: playback-timeline spec for the end-of-round order.
- E2E: Firebolt a Grunt and see the burning damage at the end of the round in the feed.

**Done when:** Firebolt sets a Grunt Burning, it takes damage at the end of this round and the next, a burn can down or kill, Flame Ward halves it, and the Brute sometimes resists.

---

## PR 3 — Movement, AP & action denial

### Rules
- `ConditionMath.EffectiveMovementRange` / `EffectiveMaxAp` / `IsBlocked`.
- **Resolver**:
  - Movement budget: `cellsLeft = EffectiveMovementRange(round start) − CellsMoved` for every move (plain moves and budget ability moves). The effective range is computed once, in `RoundContext`, from the conditions in the input state; conditions applied during the round don't change it (C6). Off-budget moves (Dash) aren't affected by `movement`, only by `blocks: move`.
  - Queues are still cut to what the combatant could afford with its **effective AP at the start of the round** (the state's conditions), as a guard.
  - **Blocked actions** (C8): before each tick, a combatant with `blocks: all` (from a condition applied in an earlier tick, or at the start of the round) doesn't act; its action at this tick is reported in `TickOutcome.Blocked` (once per action). `blocks: move` blocks plain moves and ability move effects (the rest of the ability resolves). A blocked ability isn't recorded as used, so no cooldown starts.
- **`PlanValidator.Validate`** takes the combatant's conditions (new optional parameter) and validates against effective AP and movement; rejects any non-empty plan when blocked from acting ("… is Stunned and can't act this round") and moves when Rooted ("… is Rooted and can't move"). Over-budget messages show the effective numbers.
- **Stretch — `PushEffect(int Cells)`** as written in `SPRINT_3B3.md` PR 3 (after the tick's HP is applied, slide straight away from the caster, stop at the first blocked or off-grid cell, `MovementOutcome` with `AbilityId` and `Forced`). Ignores Rooted.
- `EnemyAi`: plans with effective AP and movement; no moves while Rooted (attack if in reach, else Defend); uses Hamstring / Entangle / Shield Bash through the C14 step. Stunned AI combatants aren't planned (the Api auto-locks them).

### Server
- `PlanningService` passes conditions to `PlanValidator`; `contracts/plan-validation-cases.json` gains rows (Slowed shrinks the budget, Hasted extends it, Weakened over-budget, Stunned rejects any plan, Rooted rejects a move and a Dash but allows an attack) with the server's exact messages.
- **`RoundOrchestrator.StartPlanningPhaseAsync`**: after AI planning, auto-lock-in every combatant that can't act (C8) with an empty plan, one `PlayerLockedIn` each. `PlanningService` "all living combatants locked in" logic must treat them as locked (they are, in the set). `LockIn` for a user whose combatants are all stunned is a no-op, not an error. Timer expiry doesn't default them to defend.
- `PlanningCombatantDto`: `maxAp` / `movementRange` become **effective** values; add `baseMaxAp`, `baseMovementRange`, `blocks`.
- Feed: on application, "Brakka is Stunned until the end of the round" / "… until the end of next round" (C5 wording); "Brakka is Stunned and can't act" (once per blocked combatant per tick), "Goblin is Rooted and can't move"; round start: nothing extra (the chips say it).

### Client
- `plan-queue.ts`: mirror the effective budgets and block rules (`validatePlan` takes conditions or the effective values from the DTO; prefer the DTO values so the client doesn't recompute modifiers). Shared-case spec passes the new rows.
- **Action bar**: AP counter shows `AP 3/4 (−1 Weakened)` when the effective AP differs; move highlight uses the effective range (already per combatant). A stunned character's action bar shows "Stunned — skips this round" with everything disabled and the lock-in check already ticked; "Lock In All (n/m planned)" counts them as done. Rooted: move highlight hidden, Dash/Charge disabled with a reason tooltip.
- `GmEnemyPlannerComponent`: same for manual monsters.
- **Playback**: a blocked action shows a ⛔ / "STUNNED" floating text over the combatant in the tick where its action would have run; round timeline shows the lane cell as blocked (grey, struck through).

### Content
- Conditions: `slowed` (movement −2), `hasted` (movement +2, AP +1), `rooted` (blocks move), `weakened` (AP −1), `stunned` (blocks all).
- Abilities:

| Id | AP | CD | Targeting | Effects |
|---|---|---|---|---|
| `hamstring` | 2 | 2 | combatant / enemy / range 1 | melee attack, weapon die; on hit Slowed, duration 1 |
| `haste` | 2 | 3 | combatant / ally / range 3 / includeSelf | apply Hasted, duration 1 |
| `entangle` | 2 | 3 | combatant / enemy / range 6 | apply Rooted, duration 1 (no attack) |
| `sap` | 2 | 2 | combatant / enemy / range 6 | apply Weakened, duration 1 (no attack) |
| `shield_bash` | 3 | 3 | combatant / enemy / range 1 | melee attack, d4; on hit Stunned, duration 0 (the rest of this round) |
| `hex` | 3 | 4 | combatant / enemy / range 6 | apply Stunned, duration 1 (no attack; resistible) |

  Shield Bash's stun only costs the target the rest of the round it lands in, so it's worth more early in the queue (it lands in tick 1 and voids the target's later ticks). A longer stun is the same condition with `duration ≥ 1` (it also skips the target's next round); `hex` (below) is that version, so both lengths can be tried in the Conditions preset.
- Conditions preset: Bruiser gets Shield Bash, Skirmisher Haste, Scout Entangle; Grunts get Hamstring, the Goblin Shaman Sap and Hex.

### Tests
- Scenarios: `condition-slowed-cuts-move.json`, `condition-slowed-mid-round-no-effect-this-round.json` (Hamstring in tick 1, the target's tick 3 move still uses its full budget; next round's budget is 2 smaller), `condition-hasted-extends-move.json`, `condition-hasted-mid-round-no-effect-this-round.json`, `condition-rooted-blocks-move-and-dash.json`, `condition-rooted-charge-strikes-if-adjacent.json`, `condition-stunned-tick1-voids-rest.json` (and no cooldown for a blocked ability), `rounds-stunned-duration0-acts-next-round.json` (stunned in tick 1 with duration 0: rest of the round voided, plans and acts normally next round), `rounds-stunned-duration1-skips-next-round.json`, `condition-weakened-queue-cut.json` (resolver guard), `ai-rooted-attacks-or-defends.json`, `ai-plans-with-reduced-ap.json`, `ai-uses-hamstring.json`; stretch: `ability-shove-blocked.json`, `ability-shove-moves-rooted.json`.
- `PlanValidator` tests + shared cases.
- Api: `PlanningServiceTests` (stunned rejected, auto-locked, Rooted move rejected), `RoundOrchestrator` / hub integration test: a round where the only human combatant is stunned resolves without input after the AI locks in.
- Client: `plan-queue` specs, action bar stunned/rooted states, playback blocked cue.
- E2E: Shield Bash a Grunt (seeded dice: `seeded-dice.spec.ts` can pin a hit), see "Stunned until the end of the round" in the feed and no action from it in the later ticks; Slowed shrinks a hero's highlighted move range.

**Done when:** a Slowed hero's move highlight is 2 cells shorter, Haste adds a cell budget and an AP, a Weakened hero's AP counter shows one less, a Grunt stunned by Shield Bash loses the rest of the round and acts again the next one, a longer stun skips the next round with the action bar saying so, a Rooted Brute can't walk, and the AI plans sensibly under all of them.

---

## Not in this slice

- **Resonance-triggered conditions** (`thresholdEffects`, e.g. Burning at flame resonance 3) → 3M.5. The catalog ids are ready.
- **Environmental sources** (burning cells, hazards, fire spread) → 3D.4. They'll apply conditions through the same `ActiveCondition` path.
- **Prepared reactions and opportunity attacks** → 3B.6. Conditions that interact with them (e.g. "can't take reactions") come with it.
- **Concealed / Revealed / vision modifiers** → after 3C.1 (`ABILITIES.md`, perception section); a `vision` modifier is a field to add then.
- **GM manual apply/remove** → 4.3 GM Manual Overrides.
- **Cleanse / dispel abilities**, conditions that end early on a trigger (e.g. Burning ends when you move into water), concentration, healing-received modifiers, condition immunities by tag. Each is small on top of this model; add when content needs it.
- **Silenced / Disarmed** (`blocks: abilities` / `attack`): the `blocks` list is ready for them.
- **Hiding enemy conditions** from players → 3C (all conditions are public for now, like cooldowns).

---

## Docs to update

At the end of each PR, as the feature lands:
- `CLAUDE.md`: Current Status; Rules description (conditions, `ConditionMath`, Defend → Guarded, end-of-round order, `ConditionResist`); Redis key table (`encounter:{encounterId}:conditions`); profile/preset fields (`conditionResist`); hub payload fields; components list (`ConditionChipsComponent`); migrations count; Quick Battle presets (Conditions).
- `ROADMAP_STRATEGIC.md`: 3B.4 status block; replace the `condition_definitions` table with the JSON catalog (3B.4 database changes); replace "extend `character:live` conditions array" with the per-encounter hash (3B.4 Redis changes); resolution pipeline's end of round (periodic → falls → bleedout → expiry → cooldowns); damage pipeline gains `conditionModifier` and the target's damage taken; Combat Profile Values table (`ConditionResist` done); Design Decisions Log rows for C5, C6, C7, C9, C11.
- `TICK_RESOLUTION.md`: Decision 2 table: `apply_condition` in Effects, Defend's Guarded at the start of its tick; end-of-round order; note that `movement` and `ap` apply from the next planning phase (budgets never change mid-round), while blocks apply from the next tick.
- `ABILITIES.md`: A5 table (`apply_condition` details), quirk 4 resolved, A13 gains the condition step.
- `DESIGN_SPIKE.md` / `MAGIC_SYSTEM.md`: damage pipeline gains the condition slots.
- `DEFEAT_AND_INCAPACITATION.md`: falling at end of round from periodic damage; falling clears conditions.
- `Scenarios/README.md`: conditions block and library, combatant `conditions`, `expect.conditions`, `expect.periodic`, `expect.blocked`, dice order for resist rolls and periodic dice.
- `docs/smoke-test.md`: condition steps in the manual test.

---

## Risks & notes

- **Characterization tests pin feed text and dice order.** Defend → Guarded changes outcomes in any scripted test where a defender is attacked after its Defend. Run `ResolutionServiceCharacterizationTests` and the scenarios right after adding Guarded, before anything else, and decide case by case (update the expectation with a comment, or move the Defend so the test still pins what it was written for). Don't touch existing strings.
- **Dice order.** Resist rolls (only when resist > 0) come in the Effects phase after the tick's attack and heal rolls; periodic dice at end of round, before nothing else rolls. Write it in the Scenarios README; ScriptedDice tests depend on it.
- **Long stuns are strong.** With C5 + C6, a Stunned with duration 1 that lands in tick 1 costs the target most of two rounds. Shield Bash uses duration 0 for that reason; keep `duration ≥ 1` stuns rare and expensive (cooldown, AP, no-attack abilities that the target can resist), and retune in playtesting. It's a content change, not an engine change.
- **AP and movement at planning vs resolution.** The client must use the server's effective values (from the DTO), not recompute them, or the UI will accept plans the server rejects. Every new plan rule goes into `contracts/plan-validation-cases.json` first.
- **Auto lock-in for stunned combatants** touches the "everyone locked in" logic, which several paths share (lock-in, timer expiry, `ForceResolve`, mid-fight `AddMonster`). Grep for every reader of the locked-in set before PR 3, as 3B.3 did for the defeated set.
- **End-of-round falls** are new: before this slice nobody could fall outside a tick. Check `DefeatedThisRound`, `CombatantDefeated` broadcasts, the playback end-of-round beat and `EncounterEndCheck` all handle a fall that happens after the last tick.
- **Snapshot size.** Embedding condition definitions in every ability snapshot duplicates small JSON per combatant. Fine at this scale; revisit only if profiles grow a lot.
- **Playback events held during playback** (`GameHubService` `BROADCAST_TIMING`): this slice adds no new broadcasts (everything rides on `ResolutionCompleted` and `PlanningPhaseStarted`). If one is added, it must go in the table.
