# Combat scenarios

Each `*.json` file in this folder describes one round of combat. The file lists who is
where, what each combatant plans to do, and which numbers the dice roll. It also says
what the result should be. `ScenarioTests` runs every file through `RoundResolver` (the
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
| `"melee ID"` | Melee attack. Requires the target to be adjacent (diagonals count) **after** this tick's movement |
| `"ranged ID"` | Ranged attack, unlimited range |
| `"defend"` | No mechanical effect yet |
| `"stabilize ID"` | Stops an adjacent **Downed** combatant bleeding out (they become Stable). Resolves with the effects, after the tick's attacks |
| `"ability ABILITY ID"` | Uses one of the combatant's abilities (3B.3) on the target. It takes one queue slot and costs the ability's own AP. Its attacks and heals resolve with the tick's other attacks, one hit roll per target |
| `"ability ABILITY A,B"` | An ability with several chosen targets (`"kind": "combatants"`), in the order their rolls are made |
| `"ability ABILITY"` | An ability that finds its own targets (`"kind": "auto"`) |

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

Ability definitions combatants can list, in the same shape as the server's
`server/src/RpgGame.Api/Abilities/abilities.json` (written inline so these tests don't depend on
the server's catalog):

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
`filter` within `range` of the caster, nearest first, ties by id, at most `count` if given). An
`auto` ability that finds nobody is a use wasted with `no_targets`; its cooldown still starts.
`includeSelf: true` lets an ally ability target the caster.

Effects: `{ "kind": "attack", … }` (below) and `{ "kind": "heal", "amount": { "die": "d6", "bonus": 2 } }`
(`die` optional; `"canRevive": false` to stop it bringing someone back up). A heal never rolls to
hit: it rolls its die (in the same dice order as the attacks) and adds the amount to the tick's
netting batch, so a heal can save someone from that tick's damage. After netting, HP is capped at
max. A heal on someone **Downed or Stable** gets them back up (Standing, healed HP, no more bleedout),
unless a finishing blow hits them in the same tick (the blow wins). They stay off their queue for the
rest of the round. Dead targets can't be healed.

`roll` is `melee`, `ranged` (d20 + that accuracy + `accuracyBonus`) or `none` (always hits, no d20).
`die` is `"weapon"` (the caster's own `damage`) or a die like `"d8"`; damage is `(die + bonus) × multiplier`.
An ability is **used** once its tick is reached by a caster who is still standing, even if its
target has moved out of `range` or fallen (that attack is wasted); a use starts the cooldown. An
ability still on cooldown, or not in the loadout, is refused (no roll, no new cooldown). At the end
of the round every cooldown goes down by one, then each ability used this round is set to its
`cooldown`: used in round 3 with cooldown 2 → usable again in round 6.

### `dice`

These are the exact rolls, used in order. Every attack that isn't wasted rolls a d20 to
hit. It hits on **10 or higher**. A hit then rolls the attacker's damage die, except
against a Downed or Stable target: that's a finishing blow, and a hit kills with no damage
roll. Within a tick, attackers and healers roll in plan order (an ability rolls once per target, in target order; a heal rolls only its die). Wasted attacks don't roll at all. A
wasted attack is one where the target is already dead, or a melee target is out of reach.

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
| `abilityUses` | The exact set of `{ "tick", "id", "ability" }` activations; add `"wasted": "on_cooldown"` (or `"not_in_loadout"`, `"no_targets"`) for a failed one, and optionally `"targets": ["G1", "G2"]` to check its targets in order (for `auto`, the ones it found) |
| `heals` | The exact set of `{ "tick", "id" (healer), "target", "amount" }` heals (`amount` = die + bonus, before the max-HP cap); add `"revived": true` when it got the target back up, or `"wasted": "out_of_range"` (amount ignored) |
| `revived` | The exact set of combatants who were down at the start of the round and are standing at its end |
| `plans` | `{ "id": "move 0,13" }` or `{ "id": ["move 5,6", "melee Hero"] }`, the queue the AI chose for an `"ai"` plan (same syntax as `plans`). A single string means a one-action queue |

When a check fails, the test lists every mismatch, for example
`hp of B: expected 12, got 13`.
