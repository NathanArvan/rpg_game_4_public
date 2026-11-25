# Combat scenarios

Each `*.json` file in this folder describes one round of combat, or several in a row (see
[`rounds`](#rounds-optional)). The file lists who is where, what each combatant plans to do,
and which numbers the dice roll. It also says what the result should be. `ScenarioTests` runs every file through `RoundResolver` (the
pure rules engine in `server/src/RpgGame.Rules`).

```bash
dotnet test server/tests/RpgGame.Rules.Tests                     # all rules tests, about a second
dotnet test server/tests/RpgGame.Rules.Tests --filter Scenario   # just the scenarios
```

To add a scenario, drop a new `.json` file here. You don't need to touch any C#.

## Example

```jsonc
{
  "description": "Chase: fighter starting 2 cells away catches the retreating mage",
  "grid": [20, 20],                       // optional, default 20x20
  "combatants": [
    { "id": "Fighter", "hp": 20, "speed": 10, "damage": "d8", "at": [3, 5] },
    { "id": "Mage",    "hp": 20, "damage": "d4", "at": [5, 5] }
  ],
  "plans": {
    "Fighter": ["move 4,5", "move 5,5", "melee Mage"],
    "Mage":    ["move 6,5", "ranged Fighter", "defend"]
  },
  "dice": [14, 3, 11, 6],
  "expect": {
    "positions": { "Fighter": [5, 5], "Mage": [6, 5] },
    "hp": { "Fighter": 17, "Mage": 14 },
    "hits": [ { "tick": 2, "attacker": "Mage" }, { "tick": 3, "attacker": "Fighter" } ]
  }
}
```

Comments (`//`, `/* */`) and trailing commas are allowed.

## Fields

### `combatants`

| Field | Default | Meaning |
|---|---|---|
| `id` | required | The name used in `plans` and `expect` |
| `name` | `id` | Display name (not used in checks) |
| `hp` | 20 | HP at the start of the round |
| `maxHp` | `hp` | Max HP |
| `speed` | 10 | Movement tiebreaker when two movers claim the same cell |
| `move` | 6 | Movement range: max cells per round, shared by all of the combatant's moves |
| `maxAp` | 4 | Action points per round. A queue costing more is cut to the actions that fit |
| `meleeAccuracy` | 0 | Added to the d20 of this combatant's melee attacks |
| `rangedAccuracy` | 0 | Added to the d20 of this combatant's ranged attacks |
| `dodge` | 10 | An attack on this combatant hits when d20 + the attacker's accuracy is at least this |
| `damage` | `"d8"` | Damage die, written as `"d4"`, `"d6"`, `"d8"`, or `"1d8"` |
| `at` | none | `[x, y]` start cell. If you leave it out, the combatant is treated as standing at (0, 0) |
| `defeated` | false | Already dead before this round (same as `"status": "dead"`) |
| `downs` | false | Goes **Downed** at 0 HP instead of dying (3B.5; party profiles do) |
| `status` | `"standing"` | Starting status: `standing`, `downed`, `stable` or `dead` |
| `bleedout` | 2 | With `"status": "downed"`: rounds left before bleeding out |
| `side` | none | Team: used by `"ai"` plans (missing = `"party"` there) and by ability target filters (missing = no team, everyone's enemy). Any two different sides are enemies |
| `abilities` | none | Ability ids from the scenario's top-level `abilities` block (3B.3): the loadout |
| `cooldowns` | none | `{ "power_strike": 1 }`: abilities already on cooldown at the start of the round |
| `conditions` | none | `[{ "id": "marked", "rounds": 1 }]`: conditions at the start of the round (3B.4). `rounds` = full rounds it lasts after this one (default 0: until the end of this round) |
| `conditionResist` | 0 | Percent chance (0–100) to resist a harmful condition from an enemy: a d100 at or under it resists (3B.4 PR 2). At 0 nothing is rolled |
| `opportunityAttacks` | true | Strikes enemies that step out of its reach (3B.6). `false` for a harmless dummy |

### `plans`

Each combatant has an ordered **queue** of actions. Tick 1 runs everyone's first action,
tick 2 runs everyone's second action, and so on. The number of ticks equals the longest
queue. A combatant with a shorter queue idles once it runs out. Combatants you don't list
do nothing.

**Action points (3B.2).** Each action costs AP: move 1, melee 2, ranged 2, defend 1. A queue
must fit the combatant's `maxAp`. If it doesn't, the resolver drops every action from the first
one that doesn't fit (the server would have rejected such a plan). All of a combatant's moves in
one round share its `move` cells: each move starts where the last one stopped, and a move stops
early once the budget runs out. Steps a move doesn't take because it was blocked aren't counted.

**`"ai"`** as a combatant's whole plan (`"Goblin": ["ai"]`) lets the enemy AI (`EnemyAi`)
choose its queue from the starting positions, the same way the server does for AI-controlled
monsters: melee the nearest enemy as often as its AP allows if it's adjacent; otherwise walk
toward it, then melee with the AP left if the walk ends next to it. Give combatants a
`side` so the AI knows who its enemies are. Use `expect.plans` to check what it chose.

| Action | Meaning |
|---|---|
| `"move X,Y"` | Walk to cell (X, Y) along the shortest path, one cell per step |
| `"disengage X,Y"` | A move that provokes no opportunity attacks (3B.6). Costs 2 AP; shares the movement budget like a move |
| `"melee ID"` | Melee attack. Requires the target to be adjacent (diagonals count) **after** this tick's movement |
| `"ranged ID"` | Ranged attack, unlimited range |
| `"defend"` | No mechanical effect yet |
| `"stabilize ID"` | Stops an adjacent **Downed** combatant bleeding out (they become Stable). Resolves with the effects, after the tick's attacks |
| `"ability ABILITY ID"` | Uses one of the combatant's abilities (3B.3) on the target. It takes one queue slot and costs the ability's own AP. Its attacks and heals resolve with the tick's other attacks, one hit roll per target |
| `"ability ABILITY A,B"` | An ability with several chosen targets (`"kind": "combatants"`), in the order their rolls are made |
| `"ability ABILITY"` | An ability that finds its own targets (`"kind": "auto"`) |
| `"ability ABILITY X,Y"` | An ability aimed at a cell: a move to that cell (Dash). Two numbers mean a cell, anything else target ids |

**Opportunity attacks (3B.6).** A mover that steps from a cell within an enemy's melee reach to one
outside it gives that enemy a free melee hit with its weapon die, rolled at that step (stepping **into**
reach, or moving while staying in reach, provokes nothing). The enemy must be standing, not Stunned, have
`opportunityAttacks`, and not be moving itself this tick; each combatant reacts at most once per tick. A
`disengage` provokes none. The step's reactions are one netted batch, then the 0-HP check: a mover dropped
there stops on that cell and loses the rest of its queue. A Dash or Charge steps like a move, so it provokes too.
Conditions apply to the hit check and damage as usual; damage-taken modifiers see the tags `melee` and `opportunity`.

**Prepared reactions (3B.6 PR 2).** An ability with `"activation": "prepared"` and a `trigger` (library: `brace`,
`overwatch`) is queued as `"ability brace"`, with no target, as the **last** action of a queue (the server rejects
anything after it; the resolver doesn't check). It starts watching at the start of its own tick, before that tick's
movement, and its cooldown starts then whether or not it fires. Triggers checked at each movement step, with the
opportunity attacks: `enemy_enters_reach` (an enemy's step ends within melee reach, having started outside it) and
`enemy_moves_within` (`range`: an enemy's step ends within `range`, even if it was already inside). Disengaging doesn't
avoid them. When it fires it resolves its `attack` effects on the mover (target kind `"trigger"`), in the same netted
batch as that step's opportunity attacks (a reactor's opportunity attack rolls before its prepared one), and stops
watching: it fires once. Several watchers on the same step all fire, in combatant order. It also stops when the watcher
falls, or from the tick after it's Stunned. One still watching at the end of the round is logged `unused`. Reactions
never set off other reactions. An `"ai"` combatant ends its queue with a ready prepared reaction (instead of a lone
Defend or leftover AP) when an enemy could set it off this round.

Within each tick, all movement happens first, then all attacks, then effects. Damage
within a tick is simultaneous, so two combatants can kill each other. Anyone who falls in
a tick loses the rest of their queue. At 0 HP a combatant with `downs` goes **Downed**
(bleeding out); anyone else dies. At the end of the round, everyone who was already Downed
when it started loses a bleedout round, and dies at 0.

**Plan order matters.** The order of keys in `plans` decides the order dice are consumed.
It does **not** decide who wins a contested cell: the fastest claimant always gets it,
and the others are blocked by it. If the top two claimants tie on speed, every claimant
stops; plan order then only decides who each one reports as its blocker.

### `abilities` (optional, 3B.3)

A combatant's `abilities` list names abilities by id. They come from the **shared library**,
[`library/abilities.json`](library/abilities.json) (Brace and Overwatch (prepared, 3B.6), Power Strike, Cleave, Twin Shot, Mend,
Charge, Dash, Firebolt), so most scenarios don't need an `abilities` block at all. The library holds test
versions of the shipped abilities, owned by these tests: retuning the server's `abilities.json`
doesn't change them, so scenarios keep testing mechanics rather than current balance.

A scenario's own `abilities` block adds abilities or **overrides** a library entry with the same id
(to try other numbers). Definitions use the same shape as the server's
`server/src/RpgGame.Api/Abilities/abilities.json`:

```jsonc
"abilities": [
  {
    "id": "power_strike", "name": "Power Strike", "ap": 3, "cooldown": 2,
    "target": { "kind": "combatant", "filter": "enemy", "range": 1 },
    "effects": [ { "kind": "attack", "roll": "melee", "damage": { "die": "weapon", "bonus": 4 } } ]
  }
]
```

Target kinds: `combatant` (one chosen target), `combatants` (up to `count` chosen targets, each
checked on its own: one out of range or already down is wasted, the rest still resolve) and `auto`
(found when the ability resolves, after the tick's movement: every **standing** combatant matching
`filter` within `range` of the caster, nearest first, ties in the order the scenario lists combatants, at most `count` if given). An
`auto` ability that finds nobody is a use wasted with `no_targets`; its cooldown still starts.
`includeSelf: true` lets an ally ability target the caster.

Effects: `{ "kind": "attack", … }` (below) and `{ "kind": "heal", "amount": { "die": "d6", "bonus": 2 } }`
(`die` optional; `"canRevive": false` to stop it bringing someone back up). A heal never rolls to
hit: it rolls its die (in the same dice order as the attacks) and adds the amount to the tick's
netting batch, so a heal can save someone from that tick's damage. After netting, HP is capped at
max. A heal on someone **Downed or Stable** gets them back up (Standing, healed HP, no more bleedout),
unless a finishing blow hits them in the same tick (the blow wins). They stay off their queue for the
rest of the round. Dead targets can't be healed.

Movement (3B.3 PR 3): `{ "kind": "move", "to": "cell" | "target", "maxCells": 3, "usesMovementBudget": true }`.
It moves with the tick's other moves, in lockstep (blocking and Speed tiebreaks as usual). `"cell"`
goes to the planned cell (target kind `self`). `"target"` goes to the free cell next to the target's
position at the start of the Movement phase, closest to the caster (ties: most in line with the
target, then top-to-bottom, left-to-right); no move if already next to it, or if the target is beyond
the ability's `range`. The walk is capped at `maxCells`, the grid edge and, with `usesMovementBudget`,
the movement left this round; with `false` (Dash) its cells don't count toward the budget. An
ability that moves checks its `range` only when the move starts: its melee attack needs melee reach
from where the move ended. `"damage": { "bonusIfMoved": { "minCells": 2, "bonus": 3 } }` adds the
bonus when the move covered at least `minCells`.

`roll` is `melee`, `ranged` (d20 + that accuracy + `accuracyBonus`) or `none` (always hits, no d20).
`die` is `"weapon"` (the caster's own `damage`) or a die like `"d8"`; damage is `(die + bonus) × multiplier`.
An ability is **used** once its tick is reached by a caster who is still standing, even if its
target has moved out of `range` or fallen (that attack is wasted); a use starts the cooldown. An
ability still on cooldown, or not in the loadout, is refused (no roll, no new cooldown). At the end
of the round every cooldown goes down by one, then each ability used this round is set to its
`cooldown`: used in round 3 with cooldown 2 → usable again in round 6.

### `conditions` (optional, 3B.4)

A combatant's `conditions` list and an ability's `apply_condition` effect name conditions by id. They
come from the **shared library**, [`library/conditions.json`](library/conditions.json) (Blessed,
Dazzled, Exposed, Emboldened, Fortified, Marked, Flame Ward, Burning, Poisoned, Regenerating, Slowed, Hasted, Rooted, Weakened,
Stunned), owned by these tests like the ability
library. A scenario's own `conditions` block adds or overrides entries, in the same shape as
`server/src/RpgGame.Api/Conditions/conditions.json`:

```jsonc
"conditions": [
  { "id": "fortified", "name": "Fortified", "harmful": false, "modifiers": { "damageTaken": { "percent": -50 } } }
]
```

Modifiers: `accuracy`, `dodge` (with `attackKind`: `any` / `melee` / `ranged`), `damageDealt`,
`damageTaken` (`{ percent, flat, tags }`), `periodic` (`{ kind: "damage" | "heal", die, bonus }`), `movement`, `ap` and
`blocks` (`["all"]` or `["move"]`). Numbers from several conditions are added up. **Guarded** (Defend's +3
Dodge) is built into the rules and needs no entry.

An ability applies one with `{ "kind": "apply_condition", "condition": "marked", "duration": 1 }`
(`requiresHit` defaults to true when the ability also attacks). It resolves in the **Effects** phase,
on the targets the ability found, so it counts from the **next** tick. Defend's Guarded is the
exception: it lands at the **start** of the Defend's tick. The same condition again doesn't stack: it
keeps the longer duration. Falling (Downed or Dead) clears every condition, and none land on someone
who is down.

Durations: `duration` / `rounds` = full rounds after the current one. At every end of round a
condition at 0 is removed and any other goes down by one, so `duration: 1` lasts the rest of this
round and all of the next.

**Periodic effects** (3B.4 PR 2) resolve once, at the start of the end of round, before bleedout:
every standing combatant rolls its conditions' `periodic` dice, in combatant order, then the order its
conditions were applied. Damage goes through the bearer's own `damageTaken`, using the **condition's**
`tags` (Burning carries `flame`, so Flame Ward halves it). A combatant's damage and healing are netted
together, then HP is capped at max. Anyone this drops to 0 falls as in a tick (Downed, with no bleedout
countdown that round, or dead), and falling clears their conditions. A condition with `duration: 1`
therefore acts at the end of the round it lands in and the next.

**Resistance** (C9): a **harmful** condition from an **enemy** is resisted when the target's d100 is at
or under its `conditionResist`. Beneficial conditions, and conditions from allies or yourself, are never
rolled against, and nothing is rolled when `conditionResist` is 0.

**Budgets and blocks** (3B.4 PR 3): `movement` and `ap` change the round's movement budget and AP from the
conditions a combatant has when the round **starts** (a scenario's `conditions` list, or what the previous round left);
a condition applied during the round changes nothing until the next round. A queue over the effective AP is cut. A
combatant with `blocks: ["all"]` (Stunned) at the start of a tick doesn't act in that tick: the action is logged as
blocked and an ability isn't used (no cooldown). `blocks: ["move"]` (Rooted) blocks plain moves (logged) and stops an
ability's move without blocking the rest of it (a Rooted Charge strikes only if already in reach; Dash still starts its
cooldown). An `"ai"` combatant that can't act (Stunned, or 0 AP) gets no plan; a Rooted one doesn't walk. Library
abilities: `hamstring` (melee hit → Slowed 1), `haste` (ally → Hasted 1), `entangle` (Rooted 1), `sap` (Weakened 1),
`shield_bash` (d4 melee hit → Stunned 0), `hex` (Stunned 1).

The library's `firebolt` has no Burning (so older scenarios keep their dice); `ignite` is Firebolt as
shipped from PR 2 (Burning on a hit, duration 1). `envenom` is a melee weapon hit that Poisons for 2.

Hit check with conditions: d20 + accuracy (+ the attacker's `accuracy` modifiers) ≥ Dodge (+ the
target's `dodge` modifiers). Damage: `(die + bonus + attacker's damageDealt) × multiplier`, then the
target's `damageTaken`: flat added, then percent (rounded down, never below 0). Finishing blows ignore
conditions.

### `dice`

These are the exact rolls, used in order. Every attack that isn't wasted rolls a d20 to
hit. It hits on **10 or higher**. A hit then rolls the attacker's damage die, except
against a Downed or Stable target: that's a finishing blow, and a hit kills with no damage
roll. Within a tick, attackers and healers roll in plan order (an ability rolls once per target, in target order; a heal rolls only its die). Wasted attacks don't roll at all. A
wasted attack is one where the target is already dead, or a melee target is out of reach.

Conditions add two kinds of rolls (3B.4 PR 2): a **resist d100** for each harmful condition an enemy
puts on a combatant whose `conditionResist` is above 0, in the Effects phase (after that tick's attack
and heal rolls, in plan order, then effect order, then target order); and **periodic dice** at the end of
the round, before anything else rolls there (combatant order, then the order the conditions were applied).

**Reactions roll first** (3B.6): an opportunity attack or a prepared movement reaction rolls at its movement step,
before any of the tick's attack rolls. Within a step: movers in plan order, then reactors in the order the scenario
lists combatants (a reactor's opportunity attack before its prepared one), and for each reaction its d20, then (on a
hit) its damage die.

The test fails if the resolver asks for more rolls than you listed. It also fails if a
roll is impossible for the die (for example, 7 on a d6), or if any rolls are left over.

### `config` (optional)

These override rule constants for this scenario only: `meleeReach` (default 1) and
`bleedoutRounds` (default 2).
Accuracy, dodge and movement range are per combatant (see `combatants`).

### `expect`

Every key is optional, and only what you list gets checked. **Ticks are numbered from 1.**

| Key | Checks |
|---|---|
| `ticks` | Number of ticks resolved |
| `positions` | `{ "id": [x, y] }`, the final cell |
| `hp` | `{ "id": hp }`, the final HP. HP stops at 0 |
| `defeated` | The **exact** set of combatants out of the fight (downed, stable or dead) at the end of the round, including ones already out before it |
| `status` | `{ "id": "downed" }`, the status at the end of the round (`standing`, `downed`, `stable`, `dead`) |
| `bleedout` | `{ "id": 1 }`, bleedout rounds left at the end of the round (0 unless downed) |
| `blocked` | The exact set of `{ "tick", "id" }` movers that were stopped short |
| `hits` | The exact set of `{ "tick", "attacker" }` attacks that hit |
| `wasted` | The exact set of `{ "tick", "attacker" }` attacks that were wasted |
| `cooldowns` | `{ "id": { "power_strike": 2 } }`, the **exact** cooldowns at the end of the round (`{}` = none) |
| `conditions` | `{ "id": { "marked": 0 } }`, the **exact** conditions and their rounds left at the end of the round, after the countdown (`{}` = none) (3B.4) |
| `abilityUses` | The exact set of `{ "tick", "id", "ability" }` activations; add `"wasted": "on_cooldown"` (or `"not_in_loadout"`, `"no_targets"`) for a failed one, and optionally `"targets": ["G1", "G2"]` to check its targets in order (for `auto`, the ones it found) |
| `heals` | The exact set of `{ "tick", "id" (healer), "target", "amount" }` heals (`amount` = die + bonus, before the max-HP cap); add `"revived": true` when it got the target back up, or `"wasted": "out_of_range"` (amount ignored) |
| `revived` | The exact set of combatants who were down at the start of the round and are standing at its end |
| `plans` | `{ "id": "move 0,13" }` or `{ "id": ["move 5,6", "melee Hero"] }`, the queue the AI chose for an `"ai"` plan (same syntax as `plans`). A single string means a one-action queue |
| `events` | Lines of the round's **event log** (below) that must happen, in the order listed. Other lines may come between them |

When a check fails, the test lists every mismatch, for example
`hp of B: expected 12, got 13`, followed by the round's whole event log.

### The event log

The runner writes down everything a round did, one line per event. `expect.events` checks lines
of it, and a failing scenario prints all of it, so a quick way to write expectations is to run
the scenario once and copy the lines you care about. Within each tick (`t1`, `t2`, …) the order is:
blocked actions, ability uses, prepared reactions starting to watch (3B.6), Defend's Guarded, moves, reactions (3B.6), falls mid-move, attacks, heals, effects, conditions from abilities; then the end of round (unused prepared reactions last).

| Event | Line |
|---|---|
| Ability use | `t1 Bruiser uses cleave on G1,G2`, `t1 Bruiser uses cleave (no_targets)`, `t2 Bruiser uses power_strike on G1 (on_cooldown)` |
| Move | `t1 Hero moves to 3,4`, `t1 Hero moves to 3,4 (blocked by Goblin)`, `t1 Hero stays at 2,2`; a move made by an ability adds its id: `t1 Scout moves to 9,5 (dash)`, `t1 Kestrel moves to 5,7 (charge) (blocked by Runner)`; a Disengage: `t1 Hero moves to 3,5 (disengage)` |
| Reaction (3B.6) | `t1 s2 Goblin opportunity Hero: hit 4`, `t1 s1 Goblin opportunity Hero: miss`, `t1 s1 Goblin opportunity Hero: hit 5, Hero downed` (`s2` = movement step 2); a prepared one names its ability: `t1 s2 Bruiser brace Grunt: hit 6`; a mover it dropped: `t1 s1 Hero falls` |
| Prepared (3B.6 PR 2) | Watching starts (after the tick's ability uses): `t2 Bruiser prepares brace`; never fired (last line of the round): `end Scout: overwatch unused` |
| Attack | `t1 Hero melee Goblin: hit 5`, `t1 Scout ranged G1: miss`, `t2 Bruiser cleave G2: wasted out_of_range`, `t1 Goblin melee Hero: finishing blow`, `t1 Kestrel charge Goblin: hit 8 (+3 moved)`, `t1 A melee B: hit 3 (-4 conditions)`; when the target falls, `, Hero downed` / `, Goblin dead` is added |
| Heal | `t1 Mira mend Hero: +6`, `t1 Mira mend Hero: +6, revived`, `t1 Mira mend Hero: +4, too late`, `t1 Mira mend Hero: wasted out_of_range` |
| Blocked (3B.4 PR 3) | `t2 Goblin stunned: melee Hero blocked`, `t1 Hero rooted: move 3,0 blocked` (first in the tick) |
| Effect | `t1 Hero defends`, `t1 Knight stabilizes Archer`, `t1 Knight stabilizes Archer: wasted target_not_downed` |
| Condition (3B.4) | `t1 Kestrel mark Goblin: marked applied (1)`, `t1 Kestrel mark Goblin: marked refreshed (2)`, `t1 A blinding_strike B: dazzled wasted missed`, `t1 Kestrel mark Brute: marked resisted`; Defend's: `t1 B defend B: guarded applied (0)`. The number is rounds left |
| End of round | Periodic effects first (3B.4 PR 2): `end G burning: -3`, `end G burning: -1 (-2 conditions)`, `end Mira regenerating: +4`, `end Hero burning: -3, Hero downed`; then `end Hero bleeds, 1 left`, `end Hero bleeds out`, `end Goblin: marked expires` |

Attacks name the ability that made them (`cleave`), or `melee` / `ranged` for basic attacks.
Wasted reasons are written in snake case, as on the wire. A new kind of outcome gets one line
in `ScenarioTests.EventLog` and is then checkable with `events`, with no new `expect` key needed.

### `rounds` (optional)

To play several rounds in a row, put `plans`, `dice` and `expect` inside each entry of a `rounds`
list instead of at the top level. Each round starts from the state the previous one left: HP,
positions, statuses, bleedout, cooldowns and conditions carry over, exactly as they would between two server
rounds. `"ai"` plans are chosen from the state at the start of their round. Leftover dice are
checked per round, and failures say which round they're in.

```jsonc
{
  "description": "Power Strike is refused while it cools down",
  "combatants": [
    { "id": "Bruiser", "side": "party", "at": [0, 0], "abilities": ["power_strike"] },
    { "id": "Goblin",  "side": "enemy", "hp": 40, "at": [1, 0] }
  ],
  "rounds": [
    { "plans": { "Bruiser": ["ability power_strike Goblin"] }, "dice": [14, 5],
      "expect": { "cooldowns": { "Bruiser": { "power_strike": 2 } } } },
    { "plans": { "Bruiser": ["ability power_strike Goblin"] },
      "expect": { "events": ["t1 Bruiser uses power_strike on Goblin (on_cooldown)"] } }
  ]
}
```
