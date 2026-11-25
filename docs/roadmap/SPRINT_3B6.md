# Sprint — Slice 3B.6: Prepared Reactions & Opportunity Attacks

> **Purpose:** Implementation guide for Slice 3B.6. Hand this to Claude Code alongside `CLAUDE.md`. The starting point is `TICK_RESOLUTION.md` Decisions 4 (reactive actions) and 5 (opportunity attacks and Disengage). The decisions below (X1–X16) make those concrete, and a few of them **change** D5 (see X2, X4). They are all settled; treat them as locked and note any change made during implementation here, as `SPRINT_3B3.md` and `SPRINT_3B4.md` did. For the high-level plan see `ROADMAP_STRATEGIC.md`. For how abilities work see `ABILITIES.md` (A1 reserves `activation: "prepared"` for this slice, A9 split it out).
>
> **Status:** ✅ Done. PR 1 (opportunity attacks & Disengage), PR 2 (prepared reactions: Brace, Overwatch) and PR 3 (attack triggers: Parry, Intercept) are done. All decisions are settled (2026-11-25): X2–X5, X7–X10, X12, X13 and the slice scope were chosen explicitly; the rest were accepted as written.
>
> **Goal:** Movement has a cost near enemies. Stepping out of an enemy's reach gives them a free **opportunity attack** (a full weapon hit, rolled and applied on the spot), unless the mover pays for a **Disengage**. Combatants can also spend AP and the rest of their queue on a **prepared reaction**: they watch for a trigger for the rest of the round and act the moment it happens. 3B.6 ships movement triggers (**Brace**: strike whoever steps into reach; **Overwatch**: shoot the first enemy who moves nearby) and attack triggers (**Parry**: harder to hit for the attacks aimed at you; **Intercept**: take a hit meant for an adjacent ally). The enemy AI uses them a little. All of it can be tried in a new **Reactions** Quick Battle.

---

## Requirements

| # | Requirement | Where | PR |
|---|---|---|---|
| G1 | Passive opportunity attacks: leaving an enemy's reach provokes a free melee hit | X2, X3, X5 | 1 |
| G2 | Disengage: a move that doesn't provoke passive opportunity attacks | X4 | 1 |
| G3 | Prepared reactions as abilities (`activation: "prepared"`): a queue slot, AP, a watching state, a trigger | X6–X11 | 2 |
| G4 | Movement triggers: enemy enters my reach (Brace), enemy moves near me (Overwatch) | X12 | 2 |
| G5 | Attack triggers: I'm attacked (Parry), an adjacent ally is attacked (Intercept), resolved before the attack is rolled (interrupt) | X12, X13 | 3 |
| G6 | Settle the roadmap's open questions: AP refund, duplicate triggers, same-tick readiness, Disengage vs prepared reactions | X8, X9, X10, X4 | — |
| G7 | Enemy AI: minimal use (doesn't walk away from melee carelessly, uses a ready reaction instead of an idle Defend) | X15 | 1, 2, 3 |
| G8 | Players can see what a move will provoke before they queue it, and watch reactions play back | PR 1, PR 2 client | 1, 2 |

**Not in scope** (decided 2026-11-25): Counterspell / ability triggers, response-timing reactions (emergency heal on `ally_health_drops_below`), and a ranged-in-melee penalty. See **Not in this slice**.

---

## Decisions

All decisions are locked (2026-11-25). ✅ **Decided** marks the ones chosen explicitly; the rest follow from existing locked decisions.

### X1. Two kinds of reaction, one place in the resolver

- **Passive** reactions need no planning and cost nothing. The only one in this slice is the **opportunity attack**. It's a rule in `RoundResolver`, not an ability, so every combatant has it without a loadout entry (X5 says who can make one).
- **Prepared** reactions are **abilities** with `activation: "prepared"` (`ABILITIES.md` A1). They are queued, cost AP, start cooldowns and use the same effects as active abilities, plus two new effect kinds for attack triggers (X13). This means the roadmap's inline `{ "type": "prepared", "trigger": …, "reaction": … }` planned action isn't needed. On the wire, a prepared reaction is an ordinary `ability` action with an `abilityId` and no target (X7).
- **Reactions don't trigger reactions.** An opportunity attack, a Brace strike or an Overwatch shot never provokes or triggers anything else, and a redirected attack (Intercept) doesn't trigger a second Intercept (X13). One level deep only. This keeps resolution finite and readable.
- All reaction checks run at **two hooks** that the 3A tick loop already reserves (`TICK_RESOLUTION.md` D4, "Architectural note"):
  1. **After each lockstep movement step:** opportunity attacks and movement triggers (X3).
  2. **Before the Attack phase rolls:** attack triggers (X13).

  The Effects-phase "response" hook stays empty: there are no response reactions in this slice.

### X2. What provokes an opportunity attack ✅ Decided (changes D5)

**Only leaving reach provokes.** At a movement step, mover M goes from cell `a` to cell `b`. Enemy E (per `Relations.Between`) gets an opportunity attack on M if:

- E was within `MeleeReach` of `a` and is **not** within `MeleeReach` of `b`, **and**
- E can make one this tick (X5), **and**
- M isn't disengaging (X4) or being moved by forced movement (none exists yet).

Moving **into** reach no longer provokes passively. "Strike whoever comes close" is the **Brace** prepared reaction instead (X12), so it costs AP and a plan. Circling an enemy (staying in reach) never provokes, as D5 already said.

*Why it changes D5:* paths are server-picked shortest paths (D6), so players can't walk around an enemy. With entering-provokes, every melee approach (and every AI walk) would pay a tax nobody could plan around, and the dice in most existing melee scenarios would change.

**Ability moves provoke too.** Dash and Charge steps are movement steps like any other. A Charge that runs from one enemy to another provokes the one it leaves. `MoveEffect` gains `Disengage` (bool, default false) so a future "Tumble" can be a disengaging ability move. No shipped ability sets it.

### X3. When an opportunity attack resolves ✅ Decided

**Immediately, at the step it was provoked on, and it stops the mover only if it drops them.**

- After each lockstep step (positions applied, contested cells settled), the resolver finds every provoked reaction for **that step**: passive opportunity attacks and prepared movement triggers (X12). Order: movers in plan order, then reactors in plan order.
- They roll and resolve as **one batch for that step**: hit checks, damage, then netting and HP update (the D3 netting rule, applied per step), then `ResolveFalls`. A mover who goes Downed or dies **stops on `b`**. Their remaining path is cut and, like anyone who falls, they don't act in later ticks.
- A mover who isn't dropped keeps walking. A hit doesn't pin anyone.
- The tick's Attack phase still runs afterwards as normal, from the HP the movement phase left. The Attack phase's `TargetHpBefore` is the HP at the start of the Attack phase, so a feed line doesn't count reaction damage twice.
- **Dice order:** reaction rolls happen at their step, before any of the tick's Attack-phase rolls. Within a step: mover order, then reactor order, then (per reaction) d20 then damage die. Write this in `Scenarios/README.md`, because `ScriptedDice` tests depend on it.

### X4. Disengage ✅ Decided (changes D5's cost)

- A new basic action: `ActionType.Disengage`, wire `"disengage"`, **2 AP** (`RulesConfig.DisengageApCost`). D5's 1 AP would have made it free, since a Move already costs 1.
- It is a Move in every other way: a target cell, the same shared movement budget (`MovementRange`, changed by conditions at the start of the round), the same path, lockstep and contested-cell rules. Rooted blocks it like a move.
- It doesn't provoke **passive** opportunity attacks during that move. It **doesn't** protect against **prepared** movement reactions (Brace, Overwatch: the roadmap's lean, "committed AP is not easily nullified").
- `PlanValidator` and `plan-queue.ts` treat it like a Move for the budget and for "measured from where the previous move ends".

### X5. Who can make an opportunity attack, and how hard it hits ✅ Decided

- **Damage: a full melee hit** with the weapon die (✅ decided, instead of D5's half). It's a normal hit check, with no special bonus: `d20 + MeleeAccuracy + condition accuracy (melee) ≥ Dodge + condition dodge (melee)`. Guarded, Blessed, Exposed and the rest apply. Damage is `BaseDamage` die + `damageDealt`, then the target's `damageTaken` with source tags `["melee", "opportunity"]`. No ability bonuses, and it never applies conditions.
- **One passive reaction per combatant per tick** (D4). If several movers provoke the same reactor in one tick, it reacts to the first in processing order (earliest step, then mover plan order).
- **Can react:** Standing, not blocked from acting (Stunned blocks reactions too; Rooted doesn't), and `CombatProfile.OpportunityAttacks` is true. That's a new profile value, default **true**, with `opportunityAttacks` in `profiles.json` and as a battle-preset override. Set it false for the Training Dummy preset's dummy and for anything meant to be harmless.
- **A combatant whose own action this tick moves it can't make opportunity attacks in that tick** (plain move, Disengage, or an ability move, even if its path ended early). Reason: two adjacent fighters who both step back in the same tick would otherwise each "leave" the other and trade free hits, and a combatant mid-stride isn't set to strike. *Rejected alternative:* anyone standing can react at any step, including movers between their own steps.
- An opportunity attack doesn't use AP, doesn't count as the reactor's attack for `requiresHit` (`TickHits`), and isn't affected by what the reactor queued this tick. A reactor who also has a melee attack queued this tick still makes it in the Attack phase.

### X6. Prepared reactions are abilities

```jsonc
{
  "id": "brace", "name": "Brace", "activation": "prepared",
  "ap": 2, "cooldown": 1, "tags": ["melee"],
  "trigger": { "kind": "enemy_enters_reach" },
  "target": { "kind": "trigger" },               // whoever set it off
  "effects": [ { "kind": "attack", "roll": "melee", "damage": { "die": "weapon", "bonus": 2 } } ]
}
```

- `trigger`: `{ kind, range?, attackKind? }`. The kinds built in this slice are `enemy_enters_reach`, `enemy_moves_within` (with `range`), `attacked` (optionally `attackKind`) and `ally_attacked` (with `range`, default `MeleeReach`). `AbilityCatalog` rejects any other kind, and a `trigger` on an active ability, or a prepared ability without one.
- A new target kind, **`trigger`**: the combatant who set off the reaction. Prepared abilities may only use `trigger` or `self` targeting. `TargetKind.Trigger` is added in Rules.
- The reaction's effects resolve with the existing code (`attack`, `apply_condition`, …) against that target, inside the hook that fired it (X3 for movement triggers, X13 for attack triggers), not in the tick the ability was queued in.
- Rules: `AbilityDefinition.Trigger` (`ReactionTrigger(TriggerKind Kind, int? Range, AttackKind AttackKind = Any)`), `Activation = Prepared`. The snapshot on `EncounterCombatant.Profile` carries it, so retuning `abilities.json` never changes a running fight (A2). Migration `AddReactions` (model snapshot only, like `AddConditionEffects`).

### X7. Planning a prepared reaction: it ends the queue ✅ Decided

- Queued as `{ "type": "ability", "abilityId": "brace" }`, with no target.
- **It must be the last action in the queue.** Nothing can follow it. `PlanValidator`: "A prepared reaction must be the last action in the queue." This goes in `contracts/plan-validation-cases.json` first.
- **At most one prepared reaction per queue** (it's last, so this follows). Different combatants can each prepare one.
- AP left over after it is simply unused. The cost of committing to watching is the rest of the round.
- The usual ability rules apply: in the loadout, off cooldown, AP.

### X8. The watching window ✅ Decided (with X7)

- Watching **starts at the beginning of the reaction's own tick**, before that tick's movement step 1 (**same-tick readiness**, the roadmap's lean), and lasts until the end of the round. The round still has as many ticks as the longest queue, so a combatant who prepares on tick 2 watches through ticks 2, 3, 4… even though their own queue ended.
- The **cooldown starts when the watching tick is reached**, whether or not it fires (A10: committing is the cost). If the combatant fell or was Stunned before that tick, it never started: no watching, no cooldown (same as a blocked ability, SPRINT_3B4 C8).
- **Watching ends** when it fires (X9), at the end of the round, when the watcher falls, or from the tick after a condition blocks them from acting (Stunned lands in the Effects phase and counts from the next tick, SPRINT_3B4 C6).
- The watcher doesn't move while watching (nothing follows in the queue). Range is measured from where they stand when the trigger happens.

### X9. Fires once, then it's spent ✅ Decided

One trigger per preparation. The first event that matches fires it, and it stops watching. To react twice you'd need two preparations, which X7 doesn't allow in one round.

### X10. AP isn't refunded; duplicates all fire ✅ Decided

- **No refund** for a reaction that never fires (the roadmap's lean). At end of round, an unfired reaction is reported (`RoundOutcome.UnusedReactions`), so the feed can say "Brakka's Brace goes unused".
- **Several watchers on the same event all fire** (the roadmap's lean): two Overwatchers shoot the same runner in the same step. The exception follows from the event changing: once Intercept redirects an attack, that attack is no longer aimed at an ally, so a second Intercept doesn't see it (X13).

### X11. Visibility

Plans are already private until resolution: the AI plans when the phase opens, and players never see enemy queues. A prepared reaction is just part of the plan, so the other side only learns about it when it fires during playback. During playback, every screen shows a watching badge (👁) on the watcher's token from the start of its tick, because resolution is public. Per-player hiding is 3C.

### X12. Movement triggers: Brace and Overwatch ✅ Decided

Checked at the same hook as opportunity attacks (after each lockstep step, X3) and resolved in the same batch.

| Trigger | Fires when | Notes |
|---|---|---|
| `enemy_enters_reach` (Brace) | An enemy's step ends within the watcher's `MeleeReach` and started outside it | The entering-provokes rule from D5, now something you pay for. Hits before the enemy's Attack phase: a Grunt that Brace drops doesn't get to swing that tick |
| `enemy_moves_within` (Overwatch) | An enemy takes a step that ends within `range` of the watcher, including an enemy who was already inside and moves | Classic overwatch: any movement in the zone. *Rejected alternative:* only crossing from outside to inside. Line of sight joins the check in 3M.2 |

- Disengage doesn't stop either one (X4).
- A movement trigger fires on movement steps only. Someone who stands still inside the zone, or walks in during the watcher's *earlier* ticks (before watching started), doesn't trigger it.
- Ranged prepared attacks use the normal ranged hit check (`RangedAccuracy`) and are a ranged attack for conditions (Dazzled applies).

### X13. Attack triggers: Parry and Intercept (interrupt) ✅ Decided

Checked **after** the tick's Movement phase and before any Attack-phase roll, against the tick's collected attacks: basic melee and ranged, and attack effects of abilities with `combatant` / `combatants` targeting. `auto` (Cleave) targets are found after movement, so they're known here too and count.

| Trigger | Fires when | Effect |
|---|---|---|
| `attacked` (Parry) | The watcher is the target of at least one attack this tick (filtered by `attackKind`; Parry is melee only) | New effect **`modify_incoming`** `{ "dodge": 4 }`: +4 Dodge against **every attack on the watcher in that tick's Attack phase** (it fires once and covers the whole batch, since those attacks are simultaneous). Shown in `AttackRoll.ReactionDodge` |
| `ally_attacked` (Intercept) | An ally within `range` (1) of the watcher is the target of a single-target attack (basic attack, or a `combatant`-targeted ability) | New effect **`redirect`**: the **first** such attack in plan order is made against the watcher instead. The attacker's reach and range checks are waived (the interceptor stepped into it), and the watcher's own Dodge and conditions apply. `AttackOutcome.InterceptedFromId` keeps the original target. Multi-target and `auto` attacks aren't redirected in this slice |

- Both happen before the rolls, so they're true interrupts. The attacks then roll and net together as usual (D3). Mutual kills are still possible.
- An attack on a target that's already wasted (out of reach, target down) doesn't trigger anything: the reach check runs first, so Parry isn't spent on an attack that was never going to happen.
- **Numbers:** Parry +4 Dodge, melee only (1 AP, cooldown 1); Intercept (1 AP, cooldown 2). Cheap because they're defensive and end the queue; retune after playtesting.

### X14. Movement step outcome when a mover falls

`MovementOutcome` gains `StopReason` (`None` | `Blocked` | `Fell`) next to `WasBlocked` / `BlockedById`. For a mover dropped mid-path, the path ends on the cell they fell on. Playback stops the token there and shows DOWN in that step's beat.

### X15. Enemy AI: minimal use

Per the 3B.0 "ongoing cost" rule, and still deterministic.

- **PR 1:** the AI already prefers attacking an adjacent enemy, so it rarely leaves reach. When its plan *would* leave the reach of a standing enemy (walking or charging toward a different target), it uses **Disengage** for that move if it can afford it, and otherwise attacks the adjacent enemy instead. It doesn't otherwise reason about where opportunity attacks fall.
- **PR 2:** an AI combatant with a ready prepared reaction ends its queue with it instead of the trailing Defend or leftover AP: Brace if an enemy could reach it this round (within that enemy's movement + reach), Overwatch if no enemy is in range of a ranged attack after its walk.
- **PR 3:** Parry instead of the trailing Defend when an enemy is in melee reach at the end of its walk. Intercept when an adjacent ally is below half HP and an enemy is next to that ally.
- No reading of the players' likely reactions (3D).

### X16. Storage

- **Redis:** nothing new. Watching lives inside one round of resolution. The profile gains `OpportunityAttacks`, and the ability snapshots gain `activation`, `trigger` and the new effect kinds, all inside the stored `Profile` JSONB.
- **Postgres:** migration `AddReactions` (model snapshot only).
- **Rules:** `RoundContext` gains `Watching` (combatantId → active prepared ability and the tick it started) and `PassiveUsedThisTick`. Nothing is carried in `RoundOutcome.Next`, since reactions never outlive the round.

---

## PR plan

Three PRs, in order, each playable in Quick Battle on its own (like 3B.3 and 3B.4).

| PR | Delivers | Requirements | Abilities |
|---|---|---|---|
| **1. Opportunity attacks & Disengage** | Step hook, mid-move reaction batch, falls stop movers, `OpportunityAttacks` profile value, Disengage action, threat preview when planning a move, playback cue, AI Disengage, **Reactions** preset | G1, G2, G7, G8 | — |
| **2. Prepared reactions: framework & movement triggers** | `activation: "prepared"`, `trigger`, `trigger` targeting, ends-the-queue rule, watching window, cooldown/refund rules, unused report, Brace and Overwatch, watching badge, AI uses them | G3, G4, G6, G7, G8 | Brace, Overwatch |
| **3. Attack triggers** | Pre-attack hook, `modify_incoming` and `redirect` effects, Parry and Intercept, AI uses them | G5, G7 | Parry, Intercept |

Follow the design loop for each PR: rules + scenarios first (`dotnet test server/tests/RpgGame.Rules.Tests`), then server, then client, then E2E.

---

## PR 1 — Opportunity attacks & Disengage

*Status: done. Differences from the plan below:*
- *Reactors are checked in **combatant order** (the state's order, which the Api loads by `SortOrder`), not plan order, so a combatant with no plan this round (idle, or not listed in a scenario) can still react. In the Api the two orders are the same.*
- *`TickOutcome.Reactions` holds each reaction's `Step` from 1 (a mover's first cell after its start). `MovementOutcome` also gained `Disengaging` (wire `disengaging`), so playback and the feed can say "disengages to" without reading the queue.*
- *A combatant whose action this tick moves it is anyone with a mover this tick, including a move to its own cell; a move blocked by Rooted isn't a mover, so a Rooted combatant can still react (X5: Rooted doesn't block reactions).*
- *The enemy AI rule (X15) is built, but with nearest-enemy targeting an AI walk can't step out of a standing enemy's reach: anyone next to a cell of the walk is at least as near as the target (triangle inequality on Chebyshev distance). So `ai-disengages-to-switch-target.json` became `EnemyAiTests` cases with a bystander the AI doesn't target (no team in `teamById`). The rule matters once targeting changes (focus fire, 3D). A Charge that would provoke isn't used; the walk logic then Disengages.*
- *Profile value stored as `bool? OpportunityAttacks` on `CombatProfileValues` (null = yes), so existing rows and ordinary presets store nothing. That's a model change, so the migration is **`AddOpportunityAttacks`** (model snapshot only) in this PR rather than `AddReactions` in PR 2.*
- *`MoveEffect.Disengage` exists in Rules and the scenario format; the server's `abilities.json` / `AbilityCatalog` don't read it yet (no shipped ability uses it).*
- *Hub contracts got every field of the slice at once, as planned. The PR 2–3 ones (`watching`, `unusedReactions`, `reactionDodge`, `interceptedFromId`, `activation`, `trigger`) are sent empty or at their defaults for now.*
- *GameEvent payload for `opportunity_attack`: `attackerId`, `targetId`, `roll`, `hit`, `damage`, `targetNewHp`, `step`. The `movement` event payload is unchanged.*
- *"Mira disengages to (5, 6)" and "Mira falls at (5, 7)" are client lines (`movementFeedText`), like every movement line; the "falls at" line plays at the step it happened, after the reaction's line.*
- *`PlanningService` accepts `"disengage"` (it has its own list of action types).*
- *Client: the threat preview is `provokedBy` in `plan-queue.ts` (pure, shared helper) over enemies standing at planning time with `opportunityAttacks`; `Combatant.opportunityAttacks` carries the profile value. The Disengage toggle is local to the grid (players only; the GM planner doesn't have it). The grid stylesheet's warning budget went from 6 to 7 kB (`angular.json`), as 3B.5 did before.*

### Rules (`server/src/RpgGame.Rules/`)

- `Model.cs`: `ActionType.Disengage`; `CombatProfile.OpportunityAttacks` (default true).
- `RulesConfig`: `DisengageApCost = 2`, added to `ApCost`. `BasicActionTypes` picks it up, so `PlanningPhaseStarted.apCosts` carries it with no extra work.
- `Abilities.cs`: `MoveEffect.Disengage` (default false).
- `RoundResolver.ResolveMovementPhase`:
  - Disengage builds a `Mover` exactly like Move (budget, Rooted block reported as `BlockKind.Move`), flagged `Disengaging`. Ability movers take `Disengaging` from their `MoveEffect`.
  - After each lockstep step's positions are applied, call `ResolveStepReactions(round, tick, step, stepMoves)`. `stepMoves` holds each mover that actually stepped (`from`, `to`). The method finds provocations (X2, X5), rolls them (reuse `RollAttack` / `ComputeAttack` with an opportunity flag: weapon die, melee, source tags `melee` + `opportunity`), nets the batch, updates HP and calls `ResolveFalls`. A fallen mover gets `Stopped = true`, `StopReason = Fell`, and is dropped from the remaining steps.
  - `round.PassiveUsedThisTick` resets at the start of each tick. Reactors whose own action this tick moves them are skipped (X5).
- `Outcomes.cs`: `ReactionOutcome(string ReactorId, string TriggerId, ReactionKind Kind /* Opportunity | Prepared */, string? AbilityId, int Step, AttackOutcome? Attack)` on `TickOutcome.Reactions` (in resolution order); `MovementOutcome.StopReason` (X14). The `AttackOutcome` inside uses `AttackType = MeleeAttack`, and its HP before / after are the HP around the step batch.
- `PlanValidator`: Disengage costs 2, uses the movement budget, measured from the previous move's end, and is rejected while Rooted ("This combatant is Rooted and can't move.").
- `EnemyAi`: the X15 PR 1 rule. `DestinationNear` and the walk helpers are unchanged.

### Server (`server/src/RpgGame.Api/`)

- `RulesWireMapping`: `"disengage"` ⇄ `ActionType.Disengage`; reaction kind strings `opportunity` / `prepared`; stop reasons.
- `profiles.json` / `CombatProfileValues` / `CombatProfileCatalog` / preset override: `opportunityAttacks` (bool, default true). The Training Dummy preset's dummy gets `"opportunityAttacks": false`.
- `ResolutionFeedFormatter` (existing strings byte-for-byte unchanged; new lines added):
  - "Mira disengages to (5, 6)" (as the move line, with the verb changed)
  - "Goblin's opportunity attack on Mira hits for 4 damage (HP: 12 → 8)" / "… misses" / "… — goes down!" / "… — defeated!"
  - a mover stopped by falling: "Mira falls at (5, 7)" in place of the usual arrival line
- GameEvent `opportunity_attack` (attacker, target, roll, damage, step).
- `ResolutionService`: map `TickOutcome.Reactions`; Redis/DB persistence is unchanged (HP, status and the defeated set already come from the round's final state).

### Hub contracts (`Contracts/Hub/`, then `npm run contracts:hub`)

Add every field the slice needs now, so the wire format changes once (as 3B.3 and 3B.4 did):

| Record | Change |
|---|---|
| `PlannedActionDto.type` | + `"disengage"` |
| `MovementResult` | + `StopReason` (`"none"` \| `"blocked"` \| `"fell"`) |
| new `ReactionResult` | `ReactorId, TriggerId, Kind` (`"opportunity"` \| `"prepared"`), `AbilityId?, AbilityName?, Step, Attack?` (`AttackResult`), `FeedText` |
| `TickResult` | + `Reactions[]`, + `Watching[]` (PR 2: `{ combatantId, abilityId, abilityName }` who started watching this tick) |
| `ResolutionPayload` | + `UnusedReactions[]` (PR 2) |
| Attack `roll` | + `ReactionDodge` (PR 3) |
| `AttackResult` | + `InterceptedFromId?` (PR 3) |
| `AbilityDto` | + `Activation` (`"active"` \| `"prepared"`), + `Trigger?` (`{ kind, range?, attackKind?, description }`) |
| `PlanningCombatantDto` / `CombatantDto` | + `OpportunityAttacks` |

Add the new shapes to `ContractShape` in the integration tests.

### Client (`client/src/app/`)

- `plan-queue.ts`: Disengage in `validatePlan` (cost, budget, Rooted); shared cases pass.
- **Grid / action bar:** a **🏃 Disengage** toggle next to Defend. While it's on, the next move click queues a Disengage (2 AP) and the toggle turns off. The queue shows "🏃 → (5, 6)".
- **Threat preview:** while the move highlight is up, cells whose path (the client already computes the server's path in `plan-queue.ts` / `grid.component.ts`) would leave the reach of an enemy standing there at planning time get a hatched red tint, with a tooltip "Provokes an opportunity attack from Goblin". With Disengage armed, the tint goes away. It's a forecast from planning positions only: enemies may move, and the tooltip says so.
- **Playback** (`playback-timeline.ts`): a new **reaction cue** inside the movement beat of the step where it fired: a short lunge aura on the reactor, `⚔ -4` / `MISS` over the mover, and the label "OPPORTUNITY". A fall shows DOWN and the token stops there. HP revealed follows it (`hpOf`).
- `round-lanes.ts` / `RoundTimelineComponent`: a small ⚔ marker on the mover's cell for the tick, with the reaction in the tick details.
- `feed-format.ts`: style "opportunity attack" lines like attacks.
- Builders: `makeReaction`, defaults for `stopReason`, `reactions: []`, `opportunityAttacks: true`.
- `EncounterPreviewComponent`: a mock opportunity attack in the resolving mock.

### Content

New Quick Battle preset **`reactions.json`** ("Reactions"): the party starts **next to** the enemies (Bruiser and Skirmisher in melee with a Brute and a Grunt; the Scout one cell from a Grunt), so the first round shows the choice between stepping back and eating a hit, or Disengaging. It grows in PRs 2–3. Profiles keep their loadouts (as in 3B.4), so other presets change only where someone walks out of reach.

### Tests

- **Scenarios** (README: `disengage` action, `opportunityAttacks` combatant field, event-log lines `t1 s2 Goblin opportunity Hero: hit 4` / `… miss`, `t1 s2 Hero falls`, and the X3 dice order):
  - `oa-leaving-reach-provokes.json`, `oa-entering-reach-doesnt.json`, `oa-circling-doesnt.json`
  - `oa-disengage-avoids.json` (and costs 2 AP: a 4-AP queue of Disengage + melee fits, Disengage ×2 + melee doesn't)
  - `oa-one-per-tick.json` (two movers leave the same Grunt in one tick: only the first is hit)
  - `oa-mover-downed-stops.json` (path cut, no attack from the mover that tick)
  - `oa-mover-killed-monster.json`
  - `oa-guarded-and-exposed-apply.json`
  - `oa-reactor-moving-cant.json` (X5)
  - `oa-stunned-cant-react.json`, `oa-flag-false-cant-react.json`
  - `oa-dash-provokes.json`, `oa-charge-provokes-from-left-enemy.json`
  - `oa-step-batch-nets.json` (two reactors hit the same mover on one step: netted, then the fall check)
  - `ai-disengages-to-switch-target.json`
- **`PlanValidator`** tests + shared cases (Disengage cost, budget, Rooted).
- **Api:** characterization cases for the new feed lines (old strings unchanged), `ResolutionServiceTests` for a mover downed mid-path (status and defeated set in Redis), `BattlePresetCatalogTests` / `QuickBattleServiceTests` for the Reactions preset and the `opportunityAttacks` override.
- **Integration:** a `Hub/` round where a hero steps away from a Grunt and `ResolutionCompleted` carries a `reactions` entry (`ContractShape` checks it).
- **Client:** `plan-queue` specs, grid threat preview and Disengage toggle, playback-timeline reaction cue, feed-format.
- **E2E:** `quick-battle.spec.ts`: start **Reactions**, move the Bruiser away from the Brute, lock in, see "opportunity attack" in the feed; then Disengage away and see none. `seeded-dice.spec.ts` gains a pinned opportunity attack line.

**Done when:** in the Reactions Quick Battle, stepping away from an adjacent enemy draws a hit at the moment of leaving (visible mid-move in playback), a hero dropped by it stops where they fell, Disengage avoids it for 2 AP, the move preview shows which cells provoke, and the AI Disengages instead of walking away from a fight carelessly.

---

## PR 2 — Prepared reactions: framework & movement triggers

*Status: done. Differences from the plan below:*
- *A prepared ability that starts watching is reported in `TickOutcome.Watching` (`PreparedReaction`: combatant, ability) and not in `TickOutcome.AbilityUses`, so the feed says "Brakka prepares Brace" and not "uses". One refused at its tick (on cooldown, not in the loadout) is still an `AbilityUse` with its wasted reason. `RoundOutcome.UnusedReactions` uses the same record.*
- *`WatchingResult` (hub) gained `CombatantName` and `FeedText`, like the other result records.*
- *`ReactionTrigger.AttackKind` is `AttackKind?` (null = any) rather than an `Any` value, since `AttackKind` has no such member.*
- *A reactor can make both its passive opportunity attack and its prepared reaction on the same step (e.g. an adjacent Overwatcher whose enemy steps away): they're independent, the opportunity attack rolls first. X5's "one passive reaction per tick" still holds.*
- *"Unused" means still watching when the round ends. A watcher that fell or was Stunned stopped watching, so it isn't reported unused.*
- *`AbilityCatalog` accepts only `"attack"` effects on a prepared ability for now, and refuses the `attacked` / `ally_attacked` triggers until PR 3 brings their effects. A `"trigger"` target has no `range` and defaults to the `enemy` filter.*
- *AI (X15): Brace when a standing enemy outside its reach could walk into it this round (within that enemy's movement + reach, Rooted enemies can't); Overwatch when a standing enemy that can move could end a step within its range. The plan's "no enemy in range of a ranged attack after its walk" didn't apply: the AI has no ranged attacks yet. Either way it only replaces a lone Defend or uses AP left at the end of the queue.*
- *The Goblin Archer starts at (16, 2), out of reach in round 1; it walks toward the party and takes up Overwatch with its last AP. In the Reactions preset the Scout's Overwatch catches it walking in (the seeded E2E pins that shot).*
- *Playback: the 👁 badge comes on with the first beat of the watcher's tick (with a `👁 Brace` float), comes off when it fires (`⚡ BRACE` cue) or in a new end-of-round `unused` beat (first in the end of round, before periodic effects). Badges only show during playback.*
- *Integration preset `it-brace.json` (a hero with Brace three cells from a manual goblin) for the hub test.*
- *Migration `AddReactions` (model snapshot only): the ability snapshot's `Activation` and owned `Trigger`.*

### Rules

- `Abilities.cs`: `ReactionTrigger`, `TriggerKind { EnemyEntersReach, EnemyMovesWithin, Attacked, AllyAttacked }` (the last two acted on in PR 3), `TargetKind.Trigger`, `AbilityDefinition.Trigger`.
- `RoundResolver`:
  - `ResolveAbilityActivations` at a prepared ability's tick: not blocked and standing → add to `round.Watching`, record the use (cooldown starts, X8), report it in `TickOutcome.Watching`. A prepared ability never resolves effects at its own position.
  - `ResolveStepReactions` (from PR 1) also checks every watcher with a movement trigger (X12) and resolves its effects against the triggering mover in the same step batch. A fired watcher leaves `Watching` (X9). Disengaging movers still trigger it (X4).
  - Watching ends for a watcher who falls, or who is Stunned from the next tick (X8).
  - End of round: whatever is still in `Watching` → `RoundOutcome.UnusedReactions`.
- `PlanValidator`: prepared ability last in the queue (X7); no target on a prepared action. Shared cases.
- `EnemyAi`: X15 PR 2.

### Server

- `AbilityCatalog`: `activation`, `trigger` (kind known, `range` where needed, only on prepared abilities, and required there), `target.kind: "trigger"` only on prepared abilities. Snapshot fields, `AbilityMapping.ToRules`, migration `AddReactions`.
- Feed: "Brakka braces" / "Kestrel takes up Overwatch" (when watching starts, from the ability's `name`: "Brakka prepares Brace"), "Brakka's Brace hits Goblin for 6 damage (HP: 10 → 4)", end of round "Brakka's Brace goes unused". GameEvents `reaction_prepared`, `reaction_fired`, `reaction_unused`.

### Client

- **AbilityBar:** a prepared ability shows 👁 and its trigger in the tooltip ("Reacts when an enemy steps into reach"). Clicking it queues it straight away (no target). Once it's queued, the grid stops queueing further actions and the action bar says "👁 Watching: nothing can follow a prepared reaction" (undo removes it).
- **Playback:** a 👁 badge on the watcher's token from the first beat of its tick until it fires or the round ends. When it fires: the reaction cue from PR 1 with the ability's name ("⚡ BRACE") instead of "OPPORTUNITY". An `unused` note in the end-of-round beat.
- **Round timeline:** the prepared cell shows 👁 and stretches as a faint bar to the end of the round; the tick where it fired gets ⚡.
- `GmEnemyPlannerComponent`: the same queue rule for manual monsters.

### Content

| Id | AP | CD | Trigger | Target | Effects |
|---|---|---|---|---|---|
| `brace` | 2 | 1 | `enemy_enters_reach` | trigger | melee attack, weapon die + 2 |
| `overwatch` | 2 | 1 | `enemy_moves_within`, range 6 | trigger | ranged attack, weapon die |

Reactions preset: the Bruiser gets Brace, the Scout Overwatch, the Brute Brace, and a new Goblin Archer (Grunt profile, ranged accuracy override) gets Overwatch.

### Tests

- Scenarios: `prepared-brace-hits-arriving-enemy.json` (it drops a Grunt before the Grunt's attack in the same tick), `prepared-brace-same-tick-readiness.json`, `prepared-brace-not-before-its-tick.json` (an enemy arriving on tick 1, Brace queued for tick 2: no fire), `prepared-overwatch-fires-once.json`, `prepared-overwatch-two-watchers-both-fire.json`, `prepared-disengage-doesnt-avoid.json`, `prepared-unused-no-refund.json` (cooldown still starts), `prepared-stunned-before-tick-no-cooldown.json`, `prepared-watcher-stunned-stops-watching.json`, `prepared-watcher-downed-stops.json`, `prepared-reactions-dont-chain.json`, `ai-braces.json`, `ai-overwatch.json`.
- `PlanValidator` shared cases: a prepared ability last is valid; anything after it → the X7 message; a target on it → rejected.
- Api: `AbilityCatalogTests` (trigger validation messages name the ability), characterization for the new lines, `ResolutionServiceTests` (cooldown persisted for an unused reaction).
- Client: ability bar prepared state, grid refuses clicks after a prepared ability, playback watching badge, round-lanes.
- E2E: Reactions preset: Brace with the Bruiser, lock in, see "Brace hits" (seeded) or "goes unused" in the feed.

**Done when:** Brace hits an enemy as it steps into reach, before that enemy can attack; Overwatch shoots the first enemy that moves within 6; an unused reaction says so and still starts its cooldown; nothing can be queued after a prepared reaction; and the AI braces when an enemy can reach it.

---

## PR 3 — Attack triggers

*Status: done. Differences from the plan below:*
- *`ResolveAttackPhase` now collects every attack and heal of the tick first (with its reach check), runs `ResolveAttackTriggers` on that list, then rolls in the same order as before, so no existing dice order changed.*
- *Watchers are checked in **combatant order** (as in PR 1 and 2), Intercept watchers first, then Parry watchers on the final targets.*
- *Intercept takes only an attack by an **enemy** of the watcher (a party member hitting another isn't taken). When it takes an ability's attack, that ability's targets follow it, so a condition that needs the hit (Shield Bash's stun) lands on the interceptor, with the range waived like the attack's.*
- *Parry's extra Dodge covers every **matching** attack on the parrier that tick (melee, per its `attackKind`), not ranged ones.*
- *Attack-trigger reactions are reported in `TickOutcome.Reactions` with `Step` 0 and no attack; `ReactionOutcome` gained `InterceptedFromId` (Intercept) and `ReactionDodge` (Parry), used for the feed lines. The attack itself carries `AttackOutcome.InterceptedFromId` and `AttackRoll.ReactionDodge`. No hub contract changed (PR 1 had added the fields).*
- *Both abilities target `"self"`. `AbilityCatalog`: an `attacked` reaction takes exactly one `modify_incoming` effect (`dodge` 1–10), an `ally_attacked` one exactly one `redirect` (`range` optional, default melee reach); movement triggers still take only `attack` effects (targeting `"trigger"`); `modify_incoming` / `redirect` are refused on active abilities; `attackKind` only on `attacked` (`melee` / `ranged`).*
- *The effect's `Dodge` is stored in the ability snapshot: migration **`AddAttackReactions`** (model snapshot only).*
- *Client: Parry gets a blue (buff) aura and "⚡ PARRY" on the attacks beat; Intercept "⚡ INTERCEPT" on the interceptor, who the attack's number lands on; the reaction's feed line comes before the attack lines. The round timeline shows "(+4 Parry)" after the dodge and "(intercepted)" after the target.*
- *Reactions preset: with Parry, the Brute's 1 AP left after Power Strike now goes on Parry. The E2E test is in `quick-battle.spec.ts` (unseeded, the intercept line is deterministic): the Skirmisher steps to (10, 10), next to the Bruiser, and the Bruiser intercepts Goblin 1's swing at her (the Brute attacks the Bruiser himself, so his own Intercept can't take that).*
- *Extra scenario: `prepared-intercept-condition-follows.json`.*

### Rules

- `Abilities.cs`: `ModifyIncomingEffect(int Dodge)`, `RedirectEffect()`.
- `RoundResolver.ResolveAttackPhase`: after collecting the tick's attacks and running their reach checks, and before rolling, run `ResolveAttackTriggers`:
  1. `ally_attacked` watchers (Intercept), in plan order: the first eligible single-target attack on an ally in range is retargeted to the watcher (reach waived, X13) and the watcher stops watching.
  2. `attacked` watchers (Parry), on the final targets: the watcher gets `ReactionDodge` against every matching attack on them this tick and stops watching.
  Both are reported in `TickOutcome.Reactions` (`Attack` = null, with the affected attack ids).
- `ComputeAttack`: + `ReactionDodge` to the target's Dodge (in `AttackRoll.TargetDodge`, with the part in `AttackRoll.ReactionDodge`); `AttackOutcome.InterceptedFromId`.
- `EnemyAi`: X15 PR 3.

### Server & client

- Catalog: effect kinds `modify_incoming` (dodge ≥ 1) and `redirect`, only on prepared abilities with the matching trigger.
- Feed: "Kestrel parries (+4 Dodge)", "Brakka intercepts Goblin's attack on Mira", attack lines on a redirected attack name the interceptor as the target. GameEvent `reaction_fired` covers both.
- Playback: on the attacks beat, a shield aura and "PARRY" on the parrier; for Intercept, the interceptor flashes "INTERCEPT" and the attack's aura and number land on them. The round timeline tick details show "(+4 Parry)" next to the dodge.

### Content

| Id | AP | CD | Trigger | Effects |
|---|---|---|---|---|
| `parry` | 1 | 1 | `attacked`, melee | `modify_incoming` dodge +4 |
| `intercept` | 1 | 2 | `ally_attacked`, range 1 | `redirect` |

Reactions preset: the Skirmisher gets Parry, the Bruiser Intercept (keeping Brace), and the Brute Parry.

### Tests

- Scenarios: `prepared-parry-turns-hit-into-miss.json`, `prepared-parry-covers-all-attacks-that-tick.json`, `prepared-parry-ranged-doesnt-trigger.json`, `prepared-parry-wasted-attack-doesnt-trigger.json`, `prepared-intercept-redirects-first-attack.json`, `prepared-intercept-reach-waived.json`, `prepared-intercept-two-interceptors-one-fires.json`, `prepared-intercept-auto-not-redirected.json`, `prepared-intercept-can-down-interceptor.json`, `ai-parries.json`, `ai-intercepts-for-hurt-ally.json`.
- Api characterization, client playback specs, E2E: Intercept with the Bruiser while the Brute attacks the Scout (seeded), see "intercepts" in the feed and the Bruiser's HP drop.

**Done when:** Parry visibly turns a hit into a miss (the roll in the timeline shows +4 Parry), Intercept takes a hit meant for the adjacent Scout, and the Brute parries when it's in melee.

---

## Not in this slice

- **Counterspell and other ability triggers** (`enemy_casts_ability`, interrupt-negate). Not in scope (2026-11-25). When it comes, it needs more abilities tagged `spell` (today only Firebolt is: Hex, Entangle, Sap, Haste and Mend are candidates) and a rule for whether a countered ability's cooldown starts (lean: yes, as with A10).
- **Response reactions** (`ally_health_drops_below`, emergency heal or stabilize after the tick's damage). Not in scope. The Effects-phase response hook stays empty.
- **Riposte / simultaneous-timing reactions** (strike back in the same batch). The `attacked` trigger is ready for it, and it needs one more effect.
- **Penalty for ranged attacks while an enemy is adjacent.** Deferred (2026-11-25) to 3D.2 or playtesting.
- **Player-drawn paths** to walk around threatened cells. Most useful once walls make routes differ: 3M.2 or later.
- **Line of sight** for Overwatch and opportunity attacks → 3M.2. **Height** for reach (`|Δh| ≤ 1`, `ELEVATION.md`) → 3M.4. Both go through the reach / range checks this slice reuses.
- **Cell-zone overwatch** (watch a chosen area or cone) → 4.1 with cell targeting. 3B.6's Overwatch is a radius around the watcher.
- **"Can't react" conditions** (Dazed: `blocks: ["reactions"]`). The `blocks` list is ready for it (Stunned already blocks reactions).
- **Reach weapons** (`MeleeReach` 2 for one profile) and stances. A profile reach value would also widen opportunity attacks; add it when content needs it.
- **Hiding enemy prepared reactions from the other side's playback** → 3C.

---

## Docs to update

At the end of each PR, as the feature lands:

- `CLAUDE.md`: Current Status; Rules description (opportunity attacks at the step hook, Disengage, prepared reactions and the watching window, `TriggerKind`, new effects); profile and preset fields (`opportunityAttacks`); hub payload fields; Quick Battle presets (Reactions); migrations count; Documentation list (add this doc).
- `TICK_RESOLUTION.md`: **D5 provocation table** (entering no longer provokes passively → Brace, X2), opportunity attack damage (full hit, X5), Disengage cost (2 AP, X4); D4's prepared schema (an ability with `activation` + `trigger`, ending the queue, X6/X7) and the tick-loop pseudocode (step batch, pre-attack hook); Open Questions: move the four 3B.6 rows into decisions.
- `ROADMAP_STRATEGIC.md`: 3B.6 status block; the "3B.6+ (prepared reactions)" planned-action schema; Open Design Decisions rows for refund and duplicates → Design Decisions Log; Design Decisions Log rows for X2, X3, X4, X5, X7.
- `ABILITIES.md`: A1 (`activation: "prepared"` built, `trigger`), A4 (`trigger` target kind), A5 (`modify_incoming`, `redirect`, `MoveEffect.Disengage`), A9 resolved.
- `DEFEAT_AND_INCAPACITATION.md`: falling mid-move (a mover dropped by a reaction stops there).
- `Scenarios/README.md`: `disengage`, `opportunityAttacks`, prepared abilities in the library, event-log lines for reactions, dice order for step reactions (X3).
- `docs/smoke-test.md`: Reactions steps in the manual test.

---

## Risks & notes

- **Dice order and characterization tests.** Opportunity attacks roll in the middle of the Movement phase, before the tick's attack rolls. Any scripted test where someone walks out of an enemy's reach gets extra rolls, and every later roll shifts. Run the scenarios and `ResolutionServiceCharacterizationTests` right after the step hook lands, before anything else, and decide case by case (update the expectation with a comment, or move a combatant so the test still pins what it was written for). Leaving-only provocation (X2) keeps this small: the AI almost never walks out of reach. Don't touch existing feed strings.
- **Seeded E2E.** `seeded-dice.spec.ts` pins exact outcomes in existing presets. Check whether any of its scripted moves leave reach; if one does, its numbers change.
- **HP inside a tick is no longer one snapshot.** Until now HP only changed after a tick's Attack phase (and at end of round). With step batches, it also changes during Movement. Check every reader of "HP at the start of the tick" (`TargetHpBefore`, heal netting, the finishing-blow rule "down at the start of the round", `DefeatedThisRound`, playback's `hpOf`), and decide whether it means start of tick or start of Attack phase. This doc says start of Attack phase.
- **A fall during Movement** is new, the way end-of-round falls were new in 3B.4. The fallen mover's later actions this tick (an ability's attack after its move, Charge) must be voided. Their `TickAbilities` entry must not resolve its Attack-phase effects. Grep for `Acts` / `CanAct` and every place `TickAbilities` is read.
- **Client and server must agree on paths** for the threat preview to be honest. The client already mirrors the server's Chebyshev path. Add shared fixtures if they ever drift (the roadmap already plans this for 3M.2).
- **Prepared reactions make quiet rounds longer to play back.** A watcher who never fires still has a cell in the timeline and an unused line in the feed. Keep the unused note short.
- **Playback events held during playback** (`GameHubService` `BROADCAST_TIMING`): this slice adds no new broadcasts (everything rides on `ResolutionCompleted` and `PlanningPhaseStarted`). If one is added, it must go in the table.
- **Strength.** A full-damage opportunity attack (X5) makes retreating expensive, especially from a d10 Brute. That's intended, and Disengage at 2 AP is the counter. Watch in playtesting whether 2 AP is too much for 4-AP profiles (a Disengage plus one attack is the whole round). It's a `RulesConfig` value.
