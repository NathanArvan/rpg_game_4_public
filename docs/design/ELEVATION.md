# Elevation — Design Proposal (Slice 3M.4)

> **Status:** 📝 **Draft (Nov 2026)** — not locked. Each section gives options and a recommendation; §10 collects the decisions to lock before the 3M.4 sprint.
>
> **Already decided (roadmap, Nov 2026):** walls occupy whole cells; doorways are gaps in v1 (doors come later); **high ground affects combat**, not just movement.
>
> **Depends on:** 3M.2 (map format, pathfinding, line of sight) and 3M.3 (terrain movement costs). No stat dependency.

---

## 1. Where we'll be when this starts

After 3M.1–3M.3 the map has:

- a size per encounter (up to the cap), with pan and zoom;
- a **terrain layer** as text rows: wall cells (block movement and sight), floor cells of a terrain type (movement cost, blocks-movement / blocks-sight flags);
- a **pathfinder** shared by `RoundResolver`, `PlanValidator`, `EnemyAi` and the client (TypeScript port), working over step costs, with no corner cutting past walls;
- a **line-of-sight** function in Rules, used by ranged attacks (and abilities) and later by 3C.1 vision.

Combat today: an attack hits when `d20 + attacker accuracy ≥ target Dodge`; melee needs Chebyshev distance ≤ `MeleeReach` (1) after movement; ranged attacks have unlimited range (ability ranges arrive in 3B.3).

Elevation adds a third dimension to that flat model. The goal is to do it with as few new rules as possible, each of which a player can read off the map during planning.

## 2. What a good model has to do here

| Criterion | Why it matters in this game |
|---|---|
| **Readable at planning time** | Everyone plans at once, with no chance to react mid-round. A player must be able to tell from the map where they can go, what it costs and who has the advantage. |
| **Creates decisions** | High ground should be worth AP and movement to take, and worth denying, like ley points (`GAME_DESIGN.md`). |
| **One source of truth** | Movement cost, line of sight and combat bonuses all derive from one height value per cell. No separate "slope" or "cliff" painting that can disagree with heights. |
| **Fits the tick model** | Height changes nothing about phase order: costs apply in Movement, bonuses in Attacks. No mid-movement damage in v1. |
| **Cheap to author** | Maps are hand-written text grids; height should be one more grid of digits. |
| **Cheap to draw** | Top-down 2D grid, no 3D rendering. |
| **AI can cope** | The pathfinder handles movement for free; the AI only needs a small nudge toward high ground. |

---

## 3. Height model

**Recommendation: each cell has an integer height level, 0–9.** One level is "about a person's height": low enough to scramble up, high enough to matter in a fight. In practice maps will use 0–4; the extra digits cost nothing and leave room for towers and deep pits.

- Stored as a second text grid, the same size as the terrain grid (§8).
- Wall cells have a height too (the floor they stand on), but walls are full-height obstacles in v1: they block sight whatever the heights (§6).
- Combatants stand *on* their cell; a combatant's height is its cell's height.

Options considered:

| Option | For | Against |
|---|---|---|
| **A. Integer levels per cell** (recommended) | One number per cell; slopes and cliffs fall out of differences (§4); easy to author and draw | No in-between heights (a gentle ramp is a staircase of levels) |
| B. Real-valued heights | Smooth terrain | Hard to read on a grid; rules need thresholds anyway |
| C. Discrete "layers" (ground, raised, tower) | Very simple | Too coarse for hills; a layer is just a level with a name |

## 4. Slopes and cliffs

The question is what happens between two neighboring cells at different heights.

**Recommendation: derive it from the height difference `d = |h(a) − h(b)|`.**

| `d` | Edge type | Movement |
|---|---|---|
| 0 | Flat | Normal terrain cost |
| 1 | **Slope** | Going **up** costs +1 movement point on top of the terrain cost. Going down is normal |
| ≥ 2 | **Cliff** | Impassable in both directions (v1) |

Options considered:

| Option | For | Against |
|---|---|---|
| **A. Derived from height difference** (recommended) | No extra data; can't contradict the heights; a staircase is just 0-1-2-3 in a row | A sheer one-level step (a low wall) can't be expressed. Use a wall cell instead |
| B. Explicit slope cells (ramps painted on the terrain layer) | Author controls exactly where you can climb | Two sources of truth; what does a ramp between d = 3 mean? |
| C. Edge annotations (slope/cliff per cell edge) | Most expressive | Contradicts the cell-walls decision; much harder to author and draw |

### 4.1 Diagonal steps

A diagonal step crosses a corner shared with two orthogonal neighbors. To match the walls rule (no corner cutting), **a diagonal step is legal only if both orthogonal neighbors could be stepped onto from the origin** (not a wall, not a cliff away). The step's cost and edge type use the height difference between origin and destination. Example: from height 0, a diagonal step to height 1 is a slope and costs +1, but it is blocked if either neighbor is at height 2+ (a cliff corner) or a wall.

### 4.2 Climbing down a cliff (later)

Dropping off a cliff (with fall damage) or climbing one slowly is tempting, but it would put damage in the Movement phase and raise questions about netting and defeat mid-move. **v1: cliffs are impassable both ways.** Revisit together with hazard cells (3D.4), which have the same "damage while moving" question. Possible later rule: dropping `d` levels is allowed, deals `(d − 1) × d6` in the Effects phase of that tick, and ends the move.

### 4.3 Movement budget

`MovementRange` is movement points per round (3M.3). Climbing a slope costs one point more than the same step on the flat, so going uphill covers less ground and going downhill doesn't speed you up. AP costs are unchanged (a Move is still 1 AP whatever its length).

## 5. High ground in combat

High ground is decided: it affects combat. The question is how much and through which rules. "Higher" below always means the attacker's cell height minus the target's: `Δh = h(attacker) − h(target)`.

### 5.1 Hit bonus

**Recommendation: attacking from higher ground (`Δh ≥ 1`) gives +2 accuracy, for melee and ranged. No penalty for attacking uphill.** The bonus doesn't grow with height.

- +2 on a d20 is 10 percentage points: noticeable, not decisive.
- Bonus-only matches the resonance decision ("no penalties, only bonuses", `MAGIC_SYSTEM.md`) and keeps the roll easy to read.
- Flat rather than per level: big cliffs are already rewarded by being unreachable for melee (§5.2) and blocking sight (§6).

Options considered:

| Option | For | Against |
|---|---|---|
| **A. +2 from above, nothing from below** (recommended) | Simple; one rule; reads as "take the hill" | The defender on low ground isn't punished, only the attacker above helped |
| B. +2 from above, −2 from below | Symmetric; a 20-point swing between the two attackers in a duel | Twice as strong as A; two modifiers to show |
| C. +1 per level, capped (e.g. +3) | Rewards towers | More to calculate and explain; per-level numbers argue for a more detailed range rule too |
| D. Higher target gets +Dodge instead | Same maths as B from one side | Dodge changes per attacker, which makes the tracker misleading |

### 5.2 Melee across heights

**Recommendation: melee needs `|Δh| ≤ 1` as well as adjacency.** You can fight up or down a slope, not up or down a cliff. A combatant on top of a cliff can't be meleed from below and can't melee down: it has to use ranged attacks or come down. This also applies to opportunity attacks when they arrive (`TICK_RESOLUTION.md` Decision 5) and to Stabilize (you can't reach a downed ally up a cliff).

### 5.3 Range bonus

**Recommendation: +1 range per level of `Δh` above the target, capped at +3.** Ranged attacks are unlimited today, so this only matters for abilities with a range (3B.3) and any ranged limits added later. Keep it in the same place as the range check so it applies to everything with a range.

### 5.4 Damage

**Recommendation: none in v1.** The hit bonus is enough; adding damage too makes high ground hard to fight from below. A future ability could use height (e.g. "Plunging Strike: +damage when attacking from above").

### 5.5 Showing the bonus

The attack roll today is `AttackRoll(D20, Accuracy, TargetDodge)`. The feed and the round timeline show the roll; with a bonus, the player needs to see *why* the total is higher. Recommend a list of named modifiers on the roll (`[("high ground", +2)]`) so later sources (conditions in 3B.4, cover in 3D.2, flanking) use the same mechanism. The feed then reads e.g. "d20 12 + 1 + 2 (high ground) = 15 vs Dodge 13". Feed strings are pinned by `ResolutionServiceCharacterizationTests`; plan for updating them.

## 6. Line of sight

3M.2 gives a line-of-sight function: a line between cell centers, blocked by any wall cell (or sight-blocking terrain) it passes through. Elevation adds: **can terrain itself block sight?**

**Recommendation: a cell between A and B blocks sight if its height is greater than both A's and B's heights.** Equivalently: a ridge you're not standing above blocks the view; standing as high as the ridge lets you see past it.

- One comparison per cell already visited by the 3M.2 line, so it's cheap and identical on client and server.
- Easy to explain and to check by eye on the map: "is anything in the way taller than both of us?"
- It's generous to the higher combatant: from a tower you can see into a valley over lower hills. That's the point of high ground.

**Walls stay full-height in v1:** they block sight whatever the heights. Low walls, parapets and "standing high enough to see over a wall" are later cover features (3D.2).

Options considered:

| Option | For | Against |
|---|---|---|
| **A. Blocks if taller than both ends** (recommended) | Simple, symmetric, readable | Not physically exact (a tall hill near the low end should block more than one near the high end) |
| B. Interpolated sight line (eye height at each end, compare at each cell) | Physically better | Fractions and eye-height constants; harder to read on the map; client/server rounding risks |
| C. Elevation never blocks sight | Simplest | Hills don't hide anyone, which undercuts 3C.3 ambushes |

**Symmetry:** with A (and 3M.2's walls), if A can see B then B can see A. Keep that property: simultaneous play would get confusing if a shot could only go one way.

**3C.1 hook:** vision uses the same function. Whether high ground also **extends vision range** (e.g. +1 `VisionRange` per level above the surrounding area) is left for the 3C.1 sprint.

## 7. Display

Top-down 2D, so height has to be encoded in the drawing:

- **Shading:** each level lightens the floor color one step (low = darker). Works with terrain fills from 3M.3 (shade the fill).
- **Cliff edges:** a thick dark line on every cell edge with `d ≥ 2`. This is the most important cue: where you can't go or melee.
- **Slope edges:** a thin line or small chevrons pointing uphill on edges with `d = 1`.
- **Height numbers:** an overlay toggle that prints each cell's level; always shown in the hover tooltip.
- **Planning:** the move highlight already shows reachable cells by cost (3M.2/3M.3). Show a ▲ marker on targets you'd attack with high ground, and grey out melee targets across a cliff.
- **Playback:** nothing new; the "+2 high ground" modifier appears in the roll details.
- **Offline preview** (`/preview`): seed a map with heights so the look can be tuned without a server.

## 8. Map format

Add a `heights` grid to the map file, same dimensions as `terrain`, one digit per cell:

```json
{
  "id": "hill-fort",
  "name": "Hill Fort",
  "terrain": [
    "..........",
    "...####...",
    "...#..#...",
    "...#......",
    "...####...",
    ".........."
  ],
  "heights": [
    "0001111000",
    "0012222100",
    "0012222100",
    "0012222210",
    "0012222100",
    "0001111000"
  ]
}
```

Here the fort sits on a hill at level 2, ringed by a slope at level 1, with the doorway on the east side reached by a slope. `heights` is optional (missing = all 0). Validation at startup (like `BattlePresetCatalog`): same dimensions as `terrain`, digits only, and every combatant's start cell reachable or at least on floor.

Scenario tests (`Scenarios/`) gain the same `terrain` / `heights` fields so rule changes can be tested without the server.

## 9. Enemy AI

- **Movement:** free. The pathfinder already handles slope costs and cliffs, so the AI walks around cliffs and up slopes.
- **Small nudge (v1):** when `EnemyAi` chooses among equally good destination cells, prefer the higher one. Deterministic and cheap, and enemies will visibly take hills.
- **Melee across cliffs:** the AI's "adjacent enemy" check must use the same `|Δh| ≤ 1` rule, or it will plan wasted attacks.
- **Seeking high ground on purpose, and denying it to players** belongs to Tier 2 AI (3D.2).

## 10. What it touches (sketch, for sizing)

**Rules (`RpgGame.Rules`):**
- `EncounterState` (or its map) gains a height grid; a helper for `EdgeType(a, b)` (flat / slope / cliff).
- Pathfinder step cost: +1 uphill on slopes; cliffs impassable; diagonal rule (§4.1).
- Melee reach check: adjacency and `|Δh| ≤ 1`. Same for Stabilize and, later, opportunity attacks.
- Hit check: `+2` when `Δh ≥ 1`; `AttackRoll` gains named modifiers.
- Range check (abilities): `+min(3, Δh)` when `Δh > 0`.
- Line of sight: the "taller than both ends" test.
- `EnemyAi`: height tie-break; melee check uses the new reach rule.
- Tests: `RoundResolverTests` / `EnemyAiTests` cases and scenarios for slope cost, cliff block, diagonal corner, melee across a cliff, the high-ground hit bonus, sight over and behind a ridge.

**Api:**
- Map files gain `heights`; catalog validation.
- Redis `encounter:map` gains `heights`; `MapDto` carries it.
- Feed formatter: modifier text in attack lines (characterization tests updated).

**Client:**
- Pathfinder port: same slope/cliff costs, checked against the shared fixtures.
- Grid background: shading, cliff and slope edges, height overlay toggle, tooltip.
- Planning: ▲ high-ground marker on targets, cliff-blocked melee targets greyed out.
- Round timeline / feed: roll modifiers.
- E2E: one Quick Battle preset on a hill map (e.g. "Hill Fort").

## 11. Out of scope (later)

| Feature | Where it might land |
|---|---|
| Dropping/climbing down cliffs, fall damage | With hazard cells, 3D.4 |
| Knockback (pushing someone off a ledge) | An ability, after 3B.3/3B.4; needs fall rules first |
| Multi-storey buildings (floors stacked on the same cell), stairs between floors | Needs several heights per cell; not planned |
| Flying / levitating combatants | A condition or ability; ignores slope costs and cliffs |
| Low walls, parapets, seeing over walls from high ground | Cover, 3D.2 |
| High ground extending vision range | 3C.1 sprint decision |
| Terrain that changes height mid-fight (collapse, earthworks) | 3D.4 |
| Wind resonance from elevation (`MAGIC_SYSTEM.md`) | Resonance zones, 3D.4 |

## 12. Decisions to lock

| # | Decision | Recommendation | Alternatives |
|---|---|---|---|
| 1 | Height model | Integer level per cell, 0–9 | Real values; named layers |
| 2 | Slopes and cliffs | Derived: `d = 1` slope, `d ≥ 2` cliff | Painted slope cells; edge annotations |
| 3 | Slope cost | +1 movement point going up; normal going down | +1 both ways; no cost |
| 4 | Diagonal steps | Legal only if both orthogonal neighbors are steppable from the origin | Allow corner cutting past cliffs |
| 5 | Cliffs | Impassable both ways in v1 | Drop down with fall damage |
| 6 | Hit bonus | +2 accuracy when attacker is higher; no uphill penalty | ±2; +1 per level capped |
| 7 | Melee across heights | Needs `\|Δh\| ≤ 1` | Any adjacency |
| 8 | Range bonus | +1 per level above target, max +3 | None; per-level uncapped |
| 9 | Damage bonus | None | +1 / +die from above |
| 10 | Line of sight | Blocked by a cell taller than both ends; walls always block | Interpolated sight line; elevation never blocks |
| 11 | Roll display | Named modifiers on `AttackRoll` | Fold into accuracy silently |
| 12 | AI | Prefer higher cell on ties; obey melee rule | No height awareness until 3D.2 |
