# Sprint — Slice 3B.3: Abilities & Cooldowns

> **Purpose:** Implementation guide for Slice 3B.3. Hand this to Claude Code alongside `CLAUDE.md`. Design decisions (the ability model, targeting, effects, healing, cooldowns, and how future ability families fit) are in `docs/design/ABILITIES.md`; this doc says what to build, in which PR, and how to test it. For the high-level plan see `ROADMAP_STRATEGIC.md`.
>
> **Status:** 🔄 In progress. PR 1 (framework & cooldowns) and PR 2 (multi-target & healing) done; PR 3 not started.
>
> **Goal:** Combatants have a fixed loadout of abilities with AP costs, cooldowns, ranges and target rules. Four kinds work end to end: stronger attacks, multi-target attacks, healing, and movement (including move + attack). The resolver runs abilities as data-driven effects, so later families (conditions, AoE, spells, reactions, perception) add effect kinds, not new resolver structure. All of it can be tried in a new **Abilities** Quick Battle against AI enemies that use abilities too.

---

## Decisions

Design decisions are in `ABILITIES.md`. Three were settled on 2026-11-22:

| # | Decision | Outcome |
|---|---|---|
| A8 | Can a heal bring a Downed ally back to Standing? | **Yes.** A finishing blow in the same tick still wins |
| A9 | Prepared reactions (counterspell, overwatch) in 3B.3? | **No.** Their own slice, **3B.6**, after 3B.4, with opportunity attacks and Disengage |
| A12 | Ley points & resonance (map side) in 3B.3? | **No.** 3B.3 adds only the ability-side hooks (tags, zero bonus slots, `resonanceScaling` parsed). The map side is **3M.5**, right after 3M.2 |

The rest of `ABILITIES.md` was locked as written on the same day, with one amendment to **A3**: all ally/enemy questions go through a single `Relation(a, b)` function, so non-hostile rival teams (a `Neutral` relation) can be added later without touching the targeting code.

---

## PR plan

Three PRs, in order. Each one is playable in Quick Battle on its own (the CLAUDE.md "one roadmap slice per PR" rule is about not merging *several slices* at once; these are parts of one slice, like 3B.0).

| PR | Delivers | Abilities |
|---|---|---|
| **1. Framework & cooldowns** | Ability model, catalog, loadouts, snapshot, cooldowns, teams in the engine, ability bar, single-target attack abilities end to end | Power Strike, Firebolt (no resonance yet) |
| **2. Multi-target & healing** | `combatants` and `auto` targeting, `heal` effect, revive from Downed | Cleave, Twin Shot, Mend |
| **3. Movement abilities** | Multi-phase abilities, `move` effect (budget and off-budget), Charge bonus. Stretch: `push` | Dash, Charge (stretch: Shove) |

Follow the design loop for each PR: rules + scenarios first (`dotnet test server/tests/RpgGame.Rules.Tests`), then server, then client, then E2E.

---

## PR 1 — Framework & cooldowns

*Status: done. Differences from the plan below:*
- *Cooldowns live in a per-encounter Redis hash, `encounter:{encounterId}:cooldowns` (combatantId → JSON `{ abilityId: roundsLeft }`), not in `character:live`. `character:live` is keyed by character ID and outlives the encounter, so cooldowns would have carried into the next fight.*
- *`resonanceScaling` is validated by `AbilityCatalog` but not stored on the snapshot or in Rules yet; 3M.5 adds both.*
- *The catalog only accepts `"target": { "kind": "combatant" }` for now; PR 2 opens `combatants` / `auto`, PR 3 `self`.*
- *`AbilityUse` carries a `StartedCooldown` flag; the client shows the "uses …" line only for a refused activation (an ability's attacks already name it: "Bruiser's Power Strike hits …").*
- *`CombatantState`, `CombatProfile` and `AbilityDefinition` compare their collections (cooldowns, loadout, tags, effects) by content, so record equality keeps working in tests.*
- *Fixed on the way: Quick Battle's copy of a preset profile dropped `MaxAp` and `CanBeDowned` (so since 3B.2, Quick Battle heroes had the default 4 AP). `CombatProfileValues.Copy()` now copies every field, abilities included.*

### Rules (`server/src/RpgGame.Rules/`)

New file `Abilities.cs`:
- `AbilityDefinition(Id, Name, ApCost, Cooldown, Tags, Targeting, Effects, Charges = null, Activation = Active)`.
- `Targeting(Kind, Filter, Range, Count = null, IncludeSelf = false)`, with enums `TargetKind { Self, Combatant, Combatants, Auto, Cell }` and `TargetFilter { Enemy, Ally, Any, DownedAlly }`. `Cell` is reserved: reject it in validation until 4.1.
- `abstract record AbilityEffect`. PR 1 needs `AttackEffect(AttackKind Roll /* Melee | Ranged | None */, int AccuracyBonus, DamageSpec Damage)` (`AttackKind` is a new enum; don't confuse it with the existing `AttackRoll` result record).
- `DamageSpec(DamageDie? Die /* null = the caster's BaseDamage */, int Bonus = 0, int Multiplier = 1)`. Keep "weapon" as null in Rules; the JSON word `"weapon"` is an Api concern.

`Model.cs`:
- `ActionType.Ability`.
- `PlannedAction` gains `string? AbilityId` and `IReadOnlyList<string>? TargetCombatantIds` (used from PR 2). Factory `PlannedAction.Ability(id, targetId)`.
- `CombatProfile` gains `IReadOnlyList<AbilityDefinition> Abilities` (default empty) and a lookup helper.
- `CombatantState` gains `string Team = ""` (a free string, not an enum: more than two teams must work) and `IReadOnlyDictionary<string, int> Cooldowns` (default empty).

`RulesConfig`: replace `ApCost(ActionType)` as the cost entry point with `ApCost(PlannedAction, CombatProfile)`: basic actions as now, `Ability` → the definition's `ApCost` (unknown id → throw; the Api validates first). Keep `ApCost(ActionType)` for the `apCosts` payload.

`PlanValidator.Validate` additionally checks, per ability entry:
- the combatant has the ability,
- it isn't on cooldown (`Cooldowns`),
- an ability with cooldown ≥ 1 appears at most once in the queue,
- the target shape matches the targeting kind (one id for `combatant`, none for `self`).
It doesn't check range or teams (that's resolution time, and the Api for teams). `AffordableCount` uses the new cost function.

`RoundResolver`:
- **Attack phase** gathers basic attacks *and* ability `AttackEffect`s for this tick into one pending list, so they share the same netting batch and dice order (plan order, then target order within an ability). Extract the existing per-attack logic into one function used by both; basic melee/ranged are an attack with `Die = null, Bonus = 0`.
- `ComputeAttack` takes a `DamageSpec` and accuracy bonus and fills the `abilityModifier` slot (Bonus), applies `Multiplier` before the (still disabled) crit. Add the `resonanceBonus` and `leyBonus` slots (always 0) so the pipeline matches `MAGIC_SYSTEM.md`.
- **`Relation(a, b)`** (Rules, static): `Self` / `Ally` / `Enemy` from `Team` (A3). The only place that compares teams; `CanTarget`, `PlanValidator` and `EnemyAi` call it. A `Neutral` relation is deferred.
- **One reach helper** `CanTarget(casterId, targetId, Targeting)`: exists → filter (via `Relation`) → status → Chebyshev range from current positions. Melee reach for basic attacks goes through it too (`range = MeleeReach`). Comment that 3M.2 adds line of sight here.
- An ability whose tick is reached is recorded as used (even if wasted) unless the caster is out. An ability on cooldown or missing from the loadout resolves as a wasted no-op (guard only).
- **End of round** (next to `ResolveBleedout`): decrement cooldowns, drop zeros, set abilities used this round to their `Cooldown`. Write them into `Next`.

`Outcomes.cs`:
- `AttackOutcome` gains `string? AbilityId`.
- New `AbilityUse(CombatantId, AbilityId, IReadOnlyList<string> TargetIds, WastedReason WastedReason)` and `TickOutcome.AbilityUses`, one per activation, so playback and the feed can say "X uses Power Strike" once.
- `WastedReason` gains `OutOfRange`, `OnCooldown`, `NotInLoadout`, `InvalidTargetType` (wrong team), and from PR 2 `NoTargets`.

`EnemyAi`: step 4 of A13 (use a stronger single-target attack off cooldown instead of a basic melee, as long as the queue still fits). Read targets' teams from `Team` when it's set, else `teamById`.

### Server (`server/src/RpgGame.Api/`)

- **`Abilities/abilities.json`** + **`AbilityCatalog`** (Singleton, validated at startup, stops the API naming the broken ability; copy the file to output like `profiles.json`). Format below. Validation: unique ids, known effect kinds, `apCost ≥ 1`, `cooldown ≥ 0`, targeting kind/filter/count combinations make sense, `cell` rejected, `charges` and `activation ≠ active` rejected for now. An optional `resonanceScaling` block (`MAGIC_SYSTEM.md` format) is parsed, validated and snapshotted with the ability but not applied: 3M.5 uses it.
- **`profiles.json`**: profiles gain `"abilities": [...]`. `CombatProfileCatalog` checks every id exists in `AbilityCatalog` (register `AbilityCatalog` first).
- **Battle presets**: per-combatant `"abilities": [...]` replaces the profile's list. `BattlePresetCatalog` validates the ids.
- **Snapshot**: `CombatProfileValues` (`RpgGame.Domain/Entities/CombatProfileValues.cs`) gains `List<AbilityDefinitionValues>? Abilities` (a JSON-friendly copy of each definition). `CombatProfileCatalog.Snapshot()` fills it. `CombatProfileMapping.ToRules` maps it to `AbilityDefinition`s. Null on older rows → no abilities. Migration `AddAbilityLoadouts` (model snapshot only, like `AddMaxAp`).
- **Redis**: `character:live:{id}` hash gains `abilityCooldowns` (JSON `{ abilityId: roundsLeft }`, missing = none). `IRedisGameState` get/set; `EncounterStateLoader` puts it on `CombatantState.Cooldowns` and sets `Team` from `CombatantType`. `ResolutionService` writes the new cooldowns after resolution (GameEvents to DB before Redis, as today).
- **`PlanningService`**: ability entries are checked through the extended `PlanValidator`, plus what only the Api knows: the target exists and isn't dead, and matches the filter (`enemy` / `ally` / `downed_ally` by side). Rejections go out as `ActionRejected` with a readable reason ("Power Strike is on cooldown for 1 more round").
- **`RulesWireMapping`**: `"ability"` ⇄ `ActionType.Ability`; new wasted reasons (`out_of_range`, `on_cooldown`, `not_in_loadout`, `invalid_target_type`, `no_targets`).
- **`ResolutionFeedFormatter`**: new lines for ability uses ("Brakka uses Power Strike on Goblin — hit for 9 (12 → 3)"). **Existing strings must stay byte-for-byte**; `ResolutionServiceCharacterizationTests` must pass unchanged. Add new characterization cases rather than editing old ones.
- **`AiPlanningService`**: nothing structural; abilities arrive through the loaded state.
- **`GET /api/combat-profiles`**: `CombatProfileDto` gains `abilities` (id + name + description) so the character picker can show a profile's loadout. Regenerate `contracts/openapi.json` (build does it) and `npm run codegen`.

`abilities.json` format (comments allowed, like `profiles.json`):

```jsonc
{
  "abilities": [
    {
      "id": "power_strike", "name": "Power Strike",
      "description": "A heavy melee blow: weapon damage +4.",
      "ap": 3, "cooldown": 2, "tags": ["melee"],
      "target": { "kind": "combatant", "filter": "enemy", "range": 1 },
      "effects": [ { "kind": "attack", "roll": "melee", "damage": { "die": "weapon", "bonus": 4 } } ]
    },
    {
      "id": "firebolt", "name": "Firebolt",
      "description": "A bolt of flame at a target up to 8 cells away.",
      "ap": 3, "cooldown": 1, "tags": ["flame", "ranged", "spell"],
      "target": { "kind": "combatant", "filter": "enemy", "range": 8 },
      "effects": [ { "kind": "attack", "roll": "ranged", "damage": { "die": "d8", "bonus": 1 } } ]
    }
  ]
}
```

### Hub contracts (`Contracts/Hub/`, then `npm run contracts:hub`)

| Record | Change |
|---|---|
| `PlannedActionEntry` | + `string? AbilityId`, + `IReadOnlyList<string>? TargetCombatantIds` (PR 2 uses it; add now so the wire format changes once). `Type` gains `"ability"` |
| new `AbilityDto` | `Id, Name, Description, ApCost, Cooldown, Tags, Targeting (Kind, Filter, Range, Count, IncludeSelf)` |
| `PlanningCombatantDto` | + `IReadOnlyList<AbilityDto> Abilities`, + `IReadOnlyDictionary<string,int> Cooldowns` (at the start of this planning phase) |
| `CombatantDto` | + `Abilities` (so the tracker and preview can show loadouts before round 1 data) |
| Resolution attack entry | + `AbilityId` |
| Resolution tick | + `AbilityUses[]` (`CombatantId, AbilityId, TargetIds, WastedReason`) |

`apCosts` stays as is (basic actions only); abilities carry their own cost.

### Client (`client/src/app/`)

- **NGXS `EncounterState`**: combatants carry `abilities` and `cooldowns`; new `armedAbilityId` (which ability the next grid click uses, per selected combatant), with actions `ArmAbility` / `DisarmAbility`. Cleared on lock-in, round change and combatant switch.
- **`state/encounter/plan-queue.ts`**: mirror the `PlanValidator` changes (ability cost, cooldown, once-per-queue). Unit specs for each rule.
- **New `AbilityBarComponent`** (`npx ng generate component features/encounter/ability-bar`, separate template + style files): one button per ability of the selected combatant, showing name, AP cost and cooldown ("2 rnd"); disabled when on cooldown, already queued (cooldown ≥ 1), or unaffordable with the AP left; tooltip with the description, range and targets. Lives in the grid's action bar next to Defend. Reused by `GmEnemyPlannerComponent` for manual monsters.
- **`GridComponent`**: while an ability is armed, highlight cells within its range of where the queue's moves end, and treat a token click as "use the ability on this target" (legal targets only; illegal clicks show why). Esc / right-click disarms. Numbered queue markers show the ability's short name.
- **Playback** (`core/services/playback-timeline.ts`): ability attacks reuse the attack beat; add a short **ability-name cue** over the caster at the start of the attacks beat that has an `AbilityUse`. `ResolutionPlaybackService` renders it.
- **`round-lanes.ts` / `RoundTimelineComponent`**: ability cells show the ability name and its AP cost.
- **`feed-format.ts`**: highlight ability names.
- **`EncounterTrackerComponent`**: small cooldown chips per combatant (own side; enemy cooldowns are visible too for now; revisit with 3C hidden information).
- **`EncounterPreviewComponent`**: add mock abilities/cooldowns to the seeded state.

### Content

- `power_strike` on Bruiser and Brute, `firebolt` in the catalog with no profile (sample spell; usable via presets).
- New Quick Battle preset **`abilities.json`** ("Abilities"): Bruiser, Skirmisher, Scout (Scout gets `firebolt` via override) vs Brute + three Grunts. Grows in PRs 2–3.

### Tests

- **Scenarios** (`server/tests/RpgGame.Rules.Tests/Scenarios/`): extend the format (README) with a top-level `"abilities"` block (same shape as `abilities.json`, inline, so Rules tests stay independent of the Api catalog), combatant fields `abilities` and `cooldowns`, and the plan syntax `"ability power_strike Goblin"`. New files:
  - `ability-power-strike-hit.json`: bonus damage applied, cooldown set to 2 in `expect.cooldowns`.
  - `ability-out-of-range-wasted-starts-cooldown.json`: target walked away, wasted, cooldown still starts.
  - `ability-caster-downed-first-no-cooldown.json`: caster falls in tick 1, ability in tick 2 voided, no cooldown.
  - `ability-cooldown-ticks-down.json`: starts with cooldown 2 on another ability, ends with 1.
  - `ability-and-basic-attack-net-together.json`: mutual kill with one side using an ability.
  - `ai-uses-power-strike.json`: `"ai"` plan picks Power Strike when adjacent and off cooldown.
- **`RoundResolverTests` / `ModelTests`**: `DamageSpec` (weapon die, fixed die, multiplier), `CanTarget` cases, cooldown end-of-round order.
- **`PlanValidator` tests**: on cooldown, not in loadout, duplicate, cost.
- **Api**: `AbilityCatalogTests` (shipped file loads; each validation error names the ability), `CombatProfileCatalogTests` / `BattlePresetCatalogTests` (unknown ability id stops startup), `PlanningServiceTests` (rejections), new `ResolutionServiceCharacterizationTests` cases (feed text, cooldown persisted to Redis), `QuickBattleServiceTests` (loadout snapshotted).
- **Client**: `plan-queue` specs, `EncounterState` arm/disarm specs, `AbilityBarComponent` smoke spec, playback-timeline spec for the ability cue.
- **E2E**: `quick-battle.spec.ts` gains a case: start **Abilities**, arm Power Strike on the Bruiser, click a Grunt, lock in, see "uses Power Strike" in the feed and the cooldown on the button next round. (May need the Grunt in reach at start; the preset can place one there.)

**Done when:** in the Abilities Quick Battle you can queue Power Strike, it resolves with bonus damage, the button shows the cooldown for two rounds, and the Brute uses Power Strike on you.

---

## PR 2 — Multi-target & healing

*Status: done. Differences from the plan below:*
- *`auto` and the confirm step: an `auto` ability isn't queued on the button press. Arming it highlights its area (cells within range of where the queued moves end) and shows a **Use Cleave** button; that button, a click anywhere on the grid, or Enter queues it. The same flow works for the GM's monsters. `combatants` abilities show **Done (n/count)**; a second click on a picked target drops it.*
- *`auto` only finds Standing combatants (Downed ones for a `downed_ally` filter), so a Cleave never finishes off a Downed hero by accident; finishing blows stay planned (3B.5).*
- *A combatant healed back up mid-round stands again, but the rest of their queue stays void this round (only those standing since the start of the round act). They plan again from the next round.*
- *`RoundOutcome.RevivedThisRound` lists those who were down at the start and stand at the end; `DefeatedThisRound` is now the net list (someone downed and healed up in the same round is in neither). The payload carries `revivedCombatantIds` (no new broadcast), and `ResolutionService` calls the new `IRedisGameState.ReviveAsync` (removes them from `defeated` and `status`). Every reader of the defeated set goes through `IsDefeatedAsync`, so nothing else needed changing.*
- *A heal with `canRevive: false` on a Downed or Stable target is wasted (`TargetDefeated`); a `downed_ally` heal on a standing target is `TargetNotDowned`. Healing yourself (with `includeSelf`) ignores range.*
- *`abilities.json` heal format: `{ "kind": "heal", "amount": { "die": "d6", "bonus": 2 }, "canRevive": true }` (die optional). The catalog now accepts `combatants` (needs `count` 1–6) and `auto` (optional `count`), rejects `includeSelf` with an `enemy` filter and heals that target enemies. Migration `AddHealEffects` (model snapshot only).*
- *Feed: there's no separate "uses Cleave" line for a successful use (as in PR 1, ability attacks and heals name the ability: "Bruiser's Cleave hits …", "Mira's Mend heals Brakka for 6 (HP: 0 → 6) — back on their feet!"). An `auto` ability that found nobody: "Bruiser uses Cleave — no targets in range". A heal beaten by a finishing blow: "… comes too late".*
- *Enemy AI: step 1 heals from where it stands (Downed first, then the lowest share of HP), then carries on with the rest of its AP; step 2 checks for 2+ enemies from where it will attack (after its walk). It reads teams from `CombatantState.Team`, falling back to `teamById`.*
- *Content: Bruiser gets Cleave, Scout Twin Shot, Skirmisher Mend (on their profiles). The Abilities preset puts Goblins 1 and 2 next to the Bruiser, starts the Scout on 5 HP (new preset field `startHp`) next to the Skirmisher, and adds a goblin Shaman with Mend.*

### Rules
- `TargetKind.Combatants` (up to `Count` distinct chosen ids from `TargetCombatantIds`) and `TargetKind.Auto` (found at resolution: all matching `CanTarget` within range after this tick's movement, ordered by distance then id, capped at `Count`). Each target gets its own `AttackOutcome` and its own hit roll, in target order. An `auto` ability with no targets is a wasted `AbilityUse` (`NoTargets`) and still starts its cooldown.
- `HealEffect(DamageDie? Die, int Bonus, bool CanRevive = true)`, resolved in the **Attack phase**: adds a positive delta to the same `hpDeltas` batch as damage. After netting, clamp HP to `[0, MaxHp]`.
- **Revive** (A8): a heal on a target that is Downed or Stable at the start of the tick sets it Standing with the healed HP and clears bleedout, unless a finishing blow hit it in the same tick (finishing blow wins). Dead targets: wasted (`TargetDefeated`). Order matters for `DefeatedThisRound` and `EncounterEndCheck`: a revived combatant counts as standing again at end of round.
- New `HealOutcome(HealerId, TargetId, AbilityId, Amount, TargetHpBefore, TargetHpAfter, Revived, WastedReason)` on `TickOutcome.Heals`.
- `PlanValidator`: `combatants` → 1..Count ids, no duplicates; `auto`/`self` → none.
- `EnemyAi`: A13 steps 1 (heal) and 2 (multi-target).

### Server
- Wire: `targetCombatantIds` read for `combatants` abilities; `PlanningService` checks each id (exists, filter).
- Redis: a revive removes the combatant from `encounter:{id}:defeated` and its `status` entry. Check every reader of the defeated set (`PlanningService` "living combatants", lock-in counts, `EncounterEndCheck`) still behaves: a revived combatant must be planned and locked in from the next round.
- Resolution payload: tick `Heals[]`. `CombatantDefeated` already exists; add **`CombatantRevived`** broadcast (or carry `revivedCombatantIds` on `ResolutionCompleted`, preferred: one fewer event to hold during playback) so clients clear the downed look.
- Feed: "Mira uses Mend on Brakka: +6 HP (0 → 6), back on their feet". Cleave: one line per target under one "uses Cleave" line.

### Client
- `GridComponent` multi-target flow: arming a `combatants` ability collects clicks on distinct legal targets (selected tokens get a numbered ring) and queues the entry when `count` is reached or on Enter / a "Done (1/2)" button. `auto` and `self` abilities queue on the button press (show the affected area: cells within range for `auto`).
- Ally targeting: when an ally ability is armed, clicking allies (and yourself, if `includeSelf`) targets them instead of selecting them for planning.
- Playback: **heal cue** (green `+n`, green aura), and a **revive** beat (Downed look removed, "UP" text). Multi-target attacks already fit the attacks beat (several auras at once).
- Tracker: revived combatants show Standing again as the beat plays.

### Content & tests
- `cleave` (Bruiser), `twin_shot` (Scout), `mend` (Skirmisher). Abilities preset adds a Grunt pair standing together, for Cleave.
- Scenarios: `ability-cleave-hits-all-adjacent.json`, `ability-cleave-no-targets-wasted.json`, `ability-twin-shot-one-target-dead.json` (second target still resolves), `heal-nets-with-damage-saves-ally.json`, `heal-capped-at-max-hp.json`, `heal-revives-downed.json`, `heal-vs-finishing-blow-same-tick.json`, `ai-heals-downed-ally.json`, `ai-cleaves-two.json`.
- Api: revive clears Redis defeated/status; a revived character can plan next round; feed text characterization.
- E2E: Mend a damaged ally (use a preset that starts someone hurt: presets only set max HP today, so add a starting-HP override such as `"startHp"` to `BattlePresetCatalog`).

**Done when:** Cleave hits both adjacent Grunts with separate rolls, Twin Shot splits across two targets, Mend saves a Downed hero and they act again next round, and the AI healer (give one enemy Mend in the preset) heals its allies.

---

## PR 3 — Movement abilities

### Rules
- `MoveEffect(MoveTo To /* Cell | Target */, int MaxCells, bool UsesMovementBudget = true, MovePath Path = Walk /* Teleport reserved */)`, resolved in the **Movement phase**: ability movers join the same lockstep mover list as plain moves (Speed tiebreaks, blocking, swaps). `To = Target`: the destination is the free cell adjacent to the target's position **at the start of this tick's Movement phase** that is closest to the caster (ties by a fixed order, same as `EnemyAi`), capped at `MaxCells` and (if `UsesMovementBudget`) the budget left.
- Multi-phase: the ability's `AttackEffect` runs in the same tick's Attack phase, with reach checked after movement. Track `cellsMoved` per activation for `DamageSpec.BonusIfMoved(MinCells, Bonus)`.
- Off-budget moves don't add to `CellsMoved` (the shared budget).
- `PlanValidator`: budget moves count toward the round's cells. A `to: target` move is estimated from the target's current position. `to: cell` needs `TargetCell` within `MaxCells` of where the queue's moves end.
- Stretch: `PushEffect(int Cells)`: after the tick's HP is applied, each pushed target slides straight away from the caster (rounded to the 8 directions) cell by cell, in plan order, stopping at the first occupied or off-grid cell. Produces a `MovementOutcome` with `AbilityId` and a `Forced` flag.
- `MovementOutcome` gains `AbilityId`.
- `EnemyAi`: A13 step 3 (Charge when the nearest enemy is out of walk-and-melee reach but within charge distance).

### Server & client
- Wire: `dash` uses `targetCell`; `charge` uses `targetCombatantId`.
- Grid: arming Dash highlights cells within `maxCells` (a different highlight colour from the normal move range, since it's off-budget); arming Charge highlights legal targets within its range and previews the expected path.
- Playback: ability movement reuses the movement beats; the ability-name cue plays at the start of the tick's movement. Forced movement (stretch) gets a quick slide.
- Feed: "Kestrel charges Goblin (4 cells): hit for 9 (+3 charge)".

### Content & tests
- `dash` (Scout, Skirmisher), `charge` (Skirmisher, Brute).
- Scenarios: `ability-dash-beyond-budget.json`, `ability-charge-hits-with-bonus.json`, `ability-charge-target-moves-away.json` (wasted), `ability-charge-contested-cell.json` (charger loses the cell on Speed, attack wasted), `ability-charge-uses-budget.json` (move then charge, charge capped), `ai-charges.json`; stretch: `ability-shove-blocked.json`.
- E2E: Dash then ranged with the Scout.

**Done when:** a Scout can Dash past its normal range and still shoot, Charge closes and strikes in one tick with a bonus, and the Brute charges a hero who stayed out of reach.

---

## Not in this slice

- **Ley points & resonance (map side)** → 3M.5, after 3M.2 (A12). Firebolt ships with tags only; its `resonanceScaling` is parsed but not applied. `MAGIC_SYSTEM.md` open questions 1 and 2 should be settled before 3M.5.
- **Prepared reactions, opportunity attacks, Disengage** → 3B.6, after 3B.4 (A9).
- **Durations, buffs, debuffs, DoT** → 3B.4 conditions.

---

## Docs to update

At the end of each PR, as the feature lands:
- `CLAUDE.md`: Current Status, Rules description (abilities, `CanTarget`, cooldowns, `Team`), hub method/broadcast tables, Redis `character:live` fields, profile/preset fields, components list, migrations count.
- `ROADMAP_STRATEGIC.md`: 3B.3 status block; replace the `ability_definitions` table plan with the JSON catalog (3B.3 database changes); mark "Ability loadout storage" decided; add the Design Decisions Log rows; the PlannedAction schema section gains the `ability` entry.
- `TICK_RESOLUTION.md`: Decision 2 table gains an "Ability" row (phase per effect); note that a heal resolves in the Attack phase.
- `Scenarios/README.md`: abilities block, new combatant fields, plan syntax, `expect.cooldowns`, `expect.heals`.
- `docs/smoke-test.md`: ability steps in the manual test.
- `ABILITIES.md`: note any decisions changed during implementation.

(Already done when the decisions were made: the roadmap entries for 3B.6 and 3M.5, `MAGIC_SYSTEM.md` sequencing, and the slice column in `TICK_RESOLUTION.md`'s open questions.)

---

## Risks & notes

- **Characterization tests pin feed text and roll order.** Routing basic attacks through the shared attack function must not change dice order or strings. Run `ResolutionServiceCharacterizationTests` after the Rules refactor, before adding anything.
- **Dice order with multi-target.** Plan order, then target order inside an ability (for `auto`: distance, then id). Write it in the Scenarios README; ScriptedDice tests depend on it.
- **Revive touches the defeated set**, which several services read. Grep for every reader before PR 2.
- **High-AP abilities resolve in one tick.** A 3-AP Power Strike lands in tick 1 while a basic attacker's second swing waits for tick 2. That's intended (abilities are front-loaded power), but watch it in playtesting.
- **Client/server validation drift.** `plan-queue.ts` mirrors `PlanValidator`; every rule added in Rules needs its client twin and a spec, or the UI accepts plans the server rejects.
- **Playback events held during playback** (`GameHubService`): if PR 2 adds a `CombatantRevived` broadcast instead of a payload field, it must be added to the held list.
