# Defeat & Incapacitation — Design Proposal (Slice 3B.5)

> **Status:** ✅ **Locked (Nov 2026)** — Model C, **Bleedout**, with every recommended default in §7. This is the reference for slice 3B.5, like `DESIGN_SPIKE.md` and `TICK_RESOLUTION.md` were for 3A.
>
> **To revisit with character stats:** how long a combatant bleeds out (`BleedoutRounds`) and how much it takes to kill a downed combatant (today: any hit) are placeholders. Once stats are designed (see the roadmap's deferred *Character Stats & Progression*), both should come from a stat such as **Hardiness**, e.g. more rounds before bleeding out, or several hits to finish off. Until then they are flat values in `RulesConfig` / the rules.
>
> **Replaces:** D&D death saves. **Depends on:** the tick model (`TICK_RESOLUTION.md`) and AP (3B.2). No stat dependency.

---

## 1. Where we are today

At HP ≤ 0 a combatant is **defeated**, which means:

- The defeat check runs once per tick, after **all** of that tick's attack damage is netted (`DESIGN_SPIKE.md` Decisions 2–3). Mutual kills are possible.
- Their remaining queued actions are voided (`TICK_RESOLUTION.md` Decision 1).
- Their token stays on its cell and still blocks movement (resolver quirk 9).
- HP can go below 0 (e.g. "HP: 2 → −4").
- A side with no undefeated combatants loses (`EncounterEndCheck`).

Party members and monsters are treated the same. There's no way back, and nothing to do for a fallen ally.

The roadmap asks 3B.5 for:

1. a 0-HP model that replaces death saves;
2. allies being able to **stabilize** an incapacitated combatant;
3. an encounter-end check that accounts for "incapacitated but not dead".

## 2. What a good model has to do here

| Criterion | Why it matters in this game |
|---|---|
| **Fits simultaneous planning** | Everyone plans at once and nothing reacts mid-round. Whatever "going down" means must be readable in the *next* planning phase and answerable with an AP queue. |
| **Fits the tick model** | Each tick resolves Movement → Attacks → Effects, and status changes must slot into that order with no new special cases. |
| **Creates decisions** | The tactics-first pillar (`GAME_DESIGN.md`): a fallen ally should pose a question ("go to them, or finish the fight?"), not just remove a piece. |
| **Cheap in rules, code and UI** | Abilities (3B.3) and conditions (3B.4) come right after. 3B.5 shouldn't be the slice that bogs down. |
| **AI can cope** | The enemy AI is deliberately simple (Tier 1 comes in 3D.1). |
| **Hooks for what's next** | Heals and revives (3B.3), and conditions (3B.4: Downed could become a condition later). |

---

## 3. The three models

### Model A — Knockout

**Rule:** at HP ≤ 0 you are **Knocked Out** for the rest of the encounter. You can't act, can't be targeted, and can't die during the fight. If your side wins, you wake up afterwards. If your side loses, the outcome is narrative (the GM decides: captured, left for dead, …).

- **In the tick model:** identical to today's defeat, just renamed and non-lethal. It's checked at the same point (after a tick's attacks), with the same voided actions.
- **Decisions created:** almost none. A KO'd ally is out exactly like a dead one, and there's no reason to go to them. It meets roadmap point 2 only if a **Revive** action is added, and then it's really Model C without the timer.
- **Cost:** very low. Mostly a rename, plus a "stable/knocked out" state for the tracker.
- **AI:** nothing new.
- **Feel:** forgiving, but low stakes. Nothing is ever at risk.

### Model B — Last Stand

**Rule:** at HP ≤ 0 you enter **Last Stand**. You keep acting for the rest of the current round (your queue isn't voided), and you can plan one more round at reduced AP (for example MaxAp − 2). Any hit while in Last Stand drops you. When it ends you drop anyway, which still needs a terminal state: knocked out (A) or bleeding out (C).

- **In the tick model:** this changes *when* defeat takes effect, from "after this tick's attacks" to "at the end of the round (or on the next hit)". It's appealing in one way. It removes the main feel-bad of the tick model, where going down in tick 1 silently cancels your ticks 2–4. But it muddies the other rules:
  - **Mutual kills and wasted attacks:** an attack on a "defeated" target stops being "wasted: target defeated" if the target is still swinging, and players have to track two kinds of 0 HP.
  - **Ending:** the encounter-end check has to wait for Last Stands to run out, so fights end a round late.
  - **AI:** it has to decide what a Last-Stand enemy does, and whether to finish it.
- **Decisions created:** some, for the downed player ("spend my last round on what?"). Few for allies, because there's nothing to rescue.
- **Cost:** medium. A new state, a delayed defeat point, reduced-AP planning, a terminal state anyway, and AI and UI for all of it.
- **Feel:** dramatic. Applied to everyone, though, it makes every kill feel delayed and less decisive.

**Observation:** Last Stand isn't really a defeat *model*. It's a *timing modifier* layered on top of one (A or C). That makes it a good **trait or ability** (a Bruiser passive in 3B.3, say) and a poor universal rule.

### Model C — Bleedout

**Rule:** at HP ≤ 0 you go **Downed** and start bleeding out. While Downed you can't act and your queued actions are voided, as today. An adjacent ally can spend AP on **Stabilize** to stop the bleeding, which makes you **Stable**: still out of the fight, but safe. If nobody does, you **die** when the countdown runs out.

- **In the tick model:** going Downed happens exactly where defeat happens now (after a tick's attacks). Stabilize is an ordinary queued action that resolves in the **Effects** phase. The countdown advances at **end of round**, the slot the roadmap already reserves for "tick conditions, decrement cooldowns" (`ROADMAP_STRATEGIC.md`, resolution loop). No new timing rules are needed.
- **Decisions created:** the most of the three, and they're the right kind for this game:
  - **Allies:** "do I spend 3 of my 4 AP to move and stabilize, or kill the goblin first?"
  - **Enemies (or the GM):** "finish them off, or deal with the standing threats?"
  - **Everyone:** positioning matters more, because staying near each other keeps rescues possible.
- **Cost:** medium. It needs:
  - a status field (Standing / Downed / Stable / Dead) with a countdown;
  - one new action type;
  - an end-of-round step;
  - UI for the status and the countdown.

  All of it maps onto existing structures: the action queue, the phases, playback beats and the round timeline.
- **AI:** Tier 1 ignores downed targets, and monsters don't go down by default (see C.4), so the AI needs almost nothing new.
- **Feel:** real stakes and real rescues, without D&D's dice-roll death saves. It's deterministic: you can always see how long someone has.

**Knockout is a special case of Bleedout:** with no countdown, nobody dies. Building C also gives A for free as a setting (for example, a non-lethal campaign option later).

---

## 4. Side by side

| | A. Knockout | B. Last Stand | C. Bleedout |
|---|---|---|---|
| Fits simultaneous planning | ✅ trivially | ⚠️ adds reduced-AP planning for downed players | ✅ downed state is visible next planning; answer with a queued action |
| Fits the tick model | ✅ unchanged | ⚠️ moves the defeat point; complicates mutual kills / wasted attacks | ✅ Effects-phase action + end-of-round countdown, both already in the loop |
| Decisions for allies | ❌ none (unless Revive is added) | ⚠️ few | ✅ rescue vs. press the attack |
| Decisions for the downed player | ❌ | ✅ last actions | ❌ (they wait) |
| Stakes | ❌ low | ⚠️ medium | ✅ high, deterministic |
| Meets roadmap "allies can stabilize" | ⚠️ only with a Revive action | ❌ | ✅ |
| Rules / code / UI cost | Low | Medium (and still needs A or C underneath) | Medium |
| AI impact | None | Medium | Low (with the defaults below) |
| Hooks for 3B.3 / 3B.4 | Heals could revive | Good as an ability/trait | Heals raise Downed; Downed/Stable can become conditions |
| Encounter ends promptly | ✅ | ❌ waits for Last Stands | ✅ (downed counts as out) |

---

## 5. Recommendation

**Adopt Model C, Bleedout,** with the defaults below. Treat **Knockout** as a future campaign setting (Bleedout with no countdown), and **Last Stand** as a 3B.3 ability or trait candidate, not a universal rule.

### C.1 States

| State | Can act | Can be targeted by attacks | Blocks movement | Counts as standing (encounter end) |
|---|---|---|---|---|
| **Standing** | ✅ | ✅ | ✅ | ✅ |
| **Downed** (bleeding, `N` rounds left) | ❌ | ✅ (see C.5) | ✅ | ❌ |
| **Stable** | ❌ | ✅ (see C.5) | ✅ | ❌ |
| **Dead** | ❌ | ❌ | ✅ (as today, quirk 9) | ❌ |

HP is clamped at 0 when a combatant goes down, so overkill doesn't matter.

### C.2 Going down

- **When:** at the same check point as today's defeat, after a tick's attack damage is netted. Anyone at HP ≤ 0 who can be downed (C.4) becomes **Downed** with `BleedoutRounds` rounds left. Anyone who can't is **Dead**.
- **Queue:** as today, the rest of the combatant's queue is voided, and later ticks show those slots as lost.
- **Mutual kills:** unchanged. Both combatants go down (or die) in the same tick.

### C.3 The countdown

`BleedoutRounds = 2` (in `RulesConfig`) means **allies get two planning phases to reach them.** It's a flat value for everyone until character stats exist; then it should come from a stat such as Hardiness. The round you fall in doesn't count. At the end of each later round the count drops by one, and at 0 the combatant dies.

```
Round 3, tick 2: Archer goes Downed                 → "bleeds out in 2 rounds"
Round 4 planning: allies see "Downed (2)"            → can queue move + Stabilize
End of round 4:   still Downed                       → "Downed (1)"
Round 5 planning: last chance
End of round 5:   nobody stabilized them             → Archer dies
```

### C.4 Who can go down

- **By side:** `CombatProfile.CanBeDowned` is decided by the combatant's side, so party combatants go down and **monsters die at 0 HP** as they do now. *(As built: a side default rather than a value in `profiles.json`, so a GM who gives a monster a party profile such as Bruiser doesn't create a downable monster by accident. The stored profile has an optional `CanBeDowned` override.)*
- **Battle presets** can override it per combatant (`"downs": true`), for example a named enemy whose allies can save it later.
- **Why:** it keeps fights moving, keeps the AI simple (no enemy rescues to plan), and puts the stakes on the player side, where they're felt.

### C.5 Stabilize

| Property | Proposal |
|---|---|
| Wire type | `stabilize` |
| Target | An ally who is **Downed** when the action resolves |
| Reach | Adjacent (`MeleeReach`), measured after that tick's movement |
| AP cost | **2** (same as an attack: rescuing competes with attacking) |
| Phase | **Effects** (after Movement and Attacks in its tick) |
| Effect | Target becomes **Stable** (countdown stops). HP stays 0; they stay out of this fight. |
| Wasted when | Target isn't adjacent, is already Stable/Dead, isn't Downed any more, or the stabilizer was downed earlier in the tick |
| Planning validation | At planning time the target must be a Downed ally (going down mid-round can't be planned for; that's prepared-reaction territory, `TICK_RESOLUTION.md` Decision 4) |

**A typical rescue is two ticks:** "move (1 AP) → stabilize (2 AP)" = 3 AP. The move happens in tick 1 and the stabilize in tick 2. That's why being near your allies matters.

**Phase order is the rule, not a special case.** In a single tick, Attacks resolve before Effects, so a finishing blow (C.6) beats a stabilize aimed at the same target in the same tick, and the stabilize is wasted. The round timeline already shows this order.

### C.6 Finishing blows

- **Rule:** an attack can target a Downed or Stable combatant. Any hit kills them: no extra damage roll, and the normal hit check applies.
- **Finishing blows must be planned** *(added during implementation)*: only someone who was already Downed or Stable **when the round started** can be finished off. An attack on someone who went down earlier in the same round is wasted ("target down"). Without this, a queued second attack (melee, melee) would finish off a hero in tick 2 right after dropping them in tick 1, and allies would never get a planning phase. It mirrors the Stabilize rule: neither can be planned around someone going down mid-round.
- **Defaults:** the Tier-1 enemy AI never does this. It attacks standing targets only. A GM planning monsters manually can, which keeps the threat real without the AI being cruel.

Locked as above for now; how many hits it takes is a candidate for a stat (e.g. Hardiness) later, see the status note at the top.

### C.7 Encounter end

- **When:** a side **loses when it has no Standing combatants**. Downed and Stable members count as out, so fights don't drag on for a bleeding last survivor.
- **Party wins:** any still-Downed party members are stabilized automatically (the battle is over and allies tend to them). *(As built this has no mechanical effect yet, because encounter state is cleared and every encounter starts at full HP.)*
- **Party loses:** it's a defeat, and what happens to downed characters is narrative for now.

Today every combatant starts each encounter at full HP, so "after the fight" has no mechanical effect yet. That arrives with persistent HP and progression.

### C.8 Heals and revives (3B.3 hook, not built in 3B.5)

A heal that nets a Downed or Stable combatant's HP above 0 brings them back to **Standing** from the next tick. This uses the existing netting rule (`DESIGN_SPIKE.md` Decision 3), so a heal and lethal damage in the same tick cancel out as they always have. 3B.5 only needs to make sure the status model allows this transition.

---

## 6. What it touches (sketch, for sizing)

**Rules (`RpgGame.Rules`)**
- `CombatantState`:
  - add `Status` (Standing / Downed / Stable / Dead) and `BleedoutRoundsLeft`;
  - keep `IsDefeated` as a derived "out of the fight" (≠ Standing) to limit churn.
- Configuration:
  - `CombatProfile.CanBeDowned`;
  - `RulesConfig.BleedoutRounds` (2) and `StabilizeApCost` (2).
- `ActionType.Stabilize`: resolved in the Effects phase, plus `EffectType.Stabilize`.
- End-of-round step after the last tick: count bleedouts down and record deaths.
- `RoundOutcome`: a list of status changes (combatant, from → to, tick or end of round, cause) in place of `DefeatedThisRound`.
- `EnemyAi`: target only Standing enemies.
- `PlanValidator`: Stabilize rules.

**Server**
- Redis: `encounter:{id}:defeated` (set) becomes `encounter:{id}:status` (hash: status + rounds left).
- GameEvents: `combatant_downed`, `combatant_stabilized`, `combatant_died`.
- `EncounterEndCheck`: "standing" means Status = Standing.
- Feed text:
  - "Archer goes down! (bleeds out in 2 rounds)"
  - "Knight stabilizes Archer"
  - "Archer bleeds out"
- Contracts:
  - `PlanningCombatantDto` gains `status` and `bleedoutRoundsLeft`;
  - `CombatantDefeated` becomes `CombatantStatusChanged`;
  - `AttackResult` gains the target's new status;
  - presets gain `"downs"`.

**Client**
- Tracker: "DOWN (2)", "STABLE", "DEAD".
- Token styles: downed is tilted or dimmed with a countdown badge; stable and dead get their own looks.
- Playback: the existing defeat beat becomes a "goes down" beat, plus a final end-of-round beat for bleedouts.
- Round timeline: lost slots read "went down".
- Planning: clicking a Downed ally who is adjacent to your projected position queues Stabilize.
- Combat log: party/enemy colours already apply.

**Testing**
- Rules scenarios: down → stabilize; down → bleed out; finishing blow vs. stabilize in the same tick; mutual down; monster dies at 0.
- A Quick Battle preset to try it by hand: a fragile hero next to a medic-style ally facing a hard hitter.

**Delivery:** one PR for the slice (statuses, bleedout, Stabilize and the UI), as each part is only testable with the others.

---

### As built later: falling at the end of the round (3B.4 PR 2)

Conditions' periodic damage (Burning, Poisoned) resolves once at the start of the end of round, before bleedout (`SPRINT_3B4.md` C11). Anyone it takes to 0 HP falls there with the same rules as in a tick: a party combatant goes Downed with the full `BleedoutRounds` (the round they fell in doesn't count, so no countdown that round), a monster dies. Falling clears every condition, so periodic damage can never act as an unplanned finishing blow, and nobody Downed, Stable or Dead takes periodic damage. The encounter-end check runs after the end of round, so a burn that drops the last enemy ends the fight.

## 7. Decisions (locked)

| # | Question | Decision |
|---|---|---|
| 1 | Which model? | **C. Bleedout** (Knockout later as a setting; Last Stand as a 3B.3 trait) |
| 2 | Can attacks finish off Downed/Stable combatants? | **Yes**, any hit kills; the Tier-1 AI never does it; GMs can. *Revisit with stats (e.g. Hardiness: hits needed).* |
| 3 | How long is the bleedout? | **2 rounds** (two planning phases to respond), tunable in `RulesConfig`. *Revisit with stats (e.g. Hardiness: more rounds).* |
| 4 | Do monsters go down? | **No** by default (die at 0 HP); per-profile flag and per-preset override |
| 5 | Stabilize AP cost | **2** (competes with an attack) |
| 6 | Does a side with only Downed/Stable members lose? | **Yes**, downed counts as out |
| 7 | Overkill instant death (e.g. damage ≥ MaxHp past 0)? | **No**, keep it simple; revisit with abilities |
| 8 | Do downed bodies keep blocking movement? | **Yes** for 3B.5 (consistent with quirk 9); revisit with 4.x terrain |

---

## Falling mid-move (3B.6)

Since 3B.6 a combatant can fall during the Movement phase: an opportunity attack resolves at the movement step that provoked it (`SPRINT_3B6.md` X3). The usual 0-HP check runs after that step's reactions; a mover who goes Downed (or dies) stops on the cell it reached, its remaining path is cut, and like anyone who falls it loses the rest of its queue, including the same tick's attack (a Charge's strike). It falls in that tick, so the finishing-blow rule treats it as down this round, and its bleedout doesn't count down until the next end of round.

