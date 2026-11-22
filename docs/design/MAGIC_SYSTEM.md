# Magic System — Environmental Magic Design

> **Status:** Draft. This document defines the environmental magic layer — how magic exists in the world, how the battlefield expresses it, and how it interacts with the ability system. The question of *internal* magic sources (how individual characters connect to environmental magic) is explicitly deferred to post-playtesting.
>
> **Depends on:** `GAME_DESIGN.md` (design philosophy), `ROADMAP_STRATEGIC.md` (slices 3B.3 Abilities, 3M.5 Ley Points & Resonance, 3B.4 Conditions, 3D.4 Dynamic Environments), `DESIGN_SPIKE.md` (resolution pipeline, damage pipeline).
>
> **Core principle:** Magic is an external force that exists in the world. The battlefield is a magical landscape. Characters have abilities that interact with that landscape. The environment amplifies, restricts, enables, or transforms what those abilities can do.

---

## Design Identity

Magic is not something a character *has*. It is something the world *offers* and a character *channels*. Different places, different times, different battlefields offer different magical possibilities. A cavern veined with iron ore is a different magical environment than a forest clearing at the confluence of two rivers. The mechanical expression of this idea rests on two systems working together:

**Ley Points** — discrete cells on the encounter grid that are charged with raw magical energy. Standing on or near a ley point amplifies magical abilities. Ley points are a resource to fight over, like high ground or cover.

**Resonance** — the elemental forces and metaphysical energies present in the environment determine *what kinds* of magic the environment supports. This includes physical materials (stone, flame, water) and conceptual forces (holy, unholy, death, life, shadow, order, chaos). A consecrated temple supports holy magic. A desecrated graveyard empowers necromancy. A volcanic forge amplifies flame and metal. An ability tagged `fire` behaves differently on a map with abundant flame resonance than on a map saturated with water — and an ability tagged `holy` behaves very differently in a shrine than in a crypt.

These two systems are orthogonal. Ley points govern *how much* magical energy is available. Resonance governs *what kind*. A ley point in a flooded cavern amplifies water-tagged abilities. The same ley point on a volcanic ridge amplifies fire-tagged abilities. An unholy ley point in a graveyard supercharges necromantic abilities. The intersection of location and resonance is what makes each encounter's magical landscape unique.

---

## Ley Points

### What They Are

A ley point is a grid cell (or cluster of cells) where magical energy concentrates. They are visible to all players (no hidden ley points — the magical landscape is public information, unlike enemy positions). The GM places ley points during encounter design, or they can be procedurally generated for random encounters.

### Mechanical Effects

A combatant standing on a ley point gains the **Channeling** state. Channeling provides:

- **Ability amplification.** Magical abilities used while Channeling gain a flat damage/healing bonus and/or increased range. The exact bonus is a tunable value (see Tuning section).
- **Reduced cooldowns.** Abilities used while Channeling have their cooldown reduced by 1 round. A 3-round cooldown ability becomes a 2-round cooldown when cast from a ley point. (Minimum cooldown: 1 round.)
- **Resonance access.** Some abilities may *require* Channeling to use at all — powerful effects that can only be cast from a ley point. These are the encounter's "power plays": reaching the ley point is the tactical objective, and using the ability is the payoff.

Channeling is checked at the moment of resolution, not at the moment of planning. If a combatant plans an ability from a ley point but gets pushed off it during the Movement phase (by a conflict or environmental effect), they lose Channeling before the Attack phase resolves. This creates counterplay: denying an enemy access to a ley point is a valid tactical move.

### Ley Point Properties

Not all ley points are identical. Each has properties that the GM sets during encounter design:

| Property | Description | Default |
|---|---|---|
| `power` | Amplification strength (1–3 scale). Higher power = bigger bonus. | 1 |
| `resonance` | Resonance affinity, if any. A ley point with `resonance: "fire"` amplifies fire-tagged abilities more than generic ones. A ley point with `resonance: "unholy"` supercharges necromantic abilities. A ley point with no resonance amplifies all magical abilities equally. | none |
| `volatility` | How stable the ley point is. Volatile ley points can shift position, surge, or deplete over the course of the encounter. | stable |
| `radius` | How many cells around the center grant Channeling. `0` = only the exact cell. `1` = the cell and all adjacent cells. | 0 |

### Volatile Ley Points

Volatile ley points change during the encounter, creating a shifting magical landscape. Volatility behaviors:

- **Drift.** The ley point moves 1–2 cells in a random direction every N rounds. Combatants relying on it must reposition. The drift direction is determined at round start and broadcast during `PlanningPhaseStarted`, so players can anticipate it.
- **Surge.** The ley point's power temporarily increases (e.g., from 1 to 3) for one round, then returns to normal. Surges are announced one round in advance ("The ley point at [5,8] is building energy"), creating a race to reach it.
- **Depletion.** Each use of the ley point reduces a hidden durability counter. When it hits zero, the ley point goes dormant for N rounds (visually dims on the grid), then reactivates. Heavy use burns it out; conservative use preserves it.
- **Eruption.** A volatile ley point can discharge its energy in a burst, dealing damage to all combatants within its radius and then going dormant. This can be triggered by a threshold (too many abilities channeled through it in one round) or by GM fiat. Creates a risk/reward dynamic: cluster on the ley point for power, but risk the eruption.

Volatility integrates with the Dynamic Environments system (Slice 3D.4). Ley point behaviors are processed during Phase 3 (Effects) of the resolution pipeline, alongside environmental effects like fire spread and terrain destruction.

### Ley Points and Encounter Design

Ley points are an encounter design tool, not a random feature. The GM uses them to create tactical objectives:

- **Central ley point.** One high-power ley point in the center of the map. Both sides want it. Creates a natural clash point.
- **Split ley points.** Two ley points on opposite sides of the map. The party must decide whether to split forces to control both or concentrate on one.
- **Enemy-held ley point.** Enemies start on the ley point. The party must dislodge them to access the magical advantage. The encounter is a siege.
- **Shifting ley points.** Volatile drift ley points that move each round. No one can hold a position permanently. Rewards adaptability over entrenchment.
- **Gated ley point.** A ley point behind a barrier (locked door, collapsed tunnel, enemy line). Reaching it requires spending rounds on non-combat objectives. The payoff must be worth the investment.

The GM doesn't need to place ley points on every map. A mundane battlefield with no ley points is valid — magical abilities still work, just without amplification. Ley points are a lever the GM pulls when they want magic to be a central tactical concern.

---

## Resonance

### What It Is

Every encounter map has a **resonance profile** — a description of the forces, substances, and energies that permeate the environment. Some of these are physical: a stone dungeon has high stone resonance, a volcanic cavern has high flame and metal resonance. Others are metaphysical: a desecrated graveyard radiates unholy energy, a sun-drenched temple thrums with holy power, a site of ancient slaughter carries echoes of death.

Resonance affects how abilities with matching tags perform. An ability tagged `flame` on a high-flame-resonance map gets a bonus. An ability tagged `holy` in a consecrated shrine hits harder. An ability tagged `death` in a peaceful meadow works at baseline — nothing in the environment feeds it. This makes the environment a factor in ability selection and encounter preparation, and it extends beyond the purely physical into the thematic and spiritual character of a place.

### Resonance Types

Resonance types fall into two categories: **elemental** (rooted in physical materials and natural forces) and **metaphysical** (rooted in spiritual, emotional, or conceptual energies). The system treats both identically — they use the same 0–3 scale, the same tag-matching logic, and the same pipeline slots. The distinction is thematic, not mechanical, and exists to help GMs think about encounter design.

This list is extensible — new resonance types can be added without changing the underlying system.

**Elemental Resonances**

| Resonance | Environmental Sources | Thematic Domain |
|---|---|---|
| **Flame** | Torches, lava, burning terrain, braziers | Destruction, light, purification |
| **Stone** | Walls, rubble, cavern floors, mountains | Defense, stability, crushing force |
| **Water** | Rivers, rain, flooding, ice, fog | Flow, healing, cold, erosion |
| **Metal** | Ore veins, forged structures, weapons | Precision, conductivity, magnetism |
| **Wood** | Trees, roots, vines, living plants | Growth, entanglement, regeneration |
| **Wind** | Open sky, elevation, storms | Speed, reach, displacement |

**Metaphysical Resonances**

| Resonance | Environmental Sources | Thematic Domain |
|---|---|---|
| **Holy** | Temples, consecrated ground, shrines, places of devotion or sacrifice | Protection, healing, banishing, radiance |
| **Unholy** | Desecrated sites, cursed ground, places of betrayal or atrocity | Corruption, draining, fear, necromancy |
| **Death** | Graveyards, battlefields, crypts, places where many have died | Necrotic damage, soul manipulation, undead empowerment |
| **Life** | Sacred groves, fertile land, natural springs, places of birth or renewal | Regeneration, vigor, resurrection, growth amplification |
| **Shadow** | Deep caves, moonless ruins, places untouched by light | Stealth, illusion, deception, cold |
| **Order** | Libraries, courthouses, places of law or rigid structure | Binding, predictability, warding, suppression |
| **Chaos** | Wild magic zones, sites of reality fractures, places of upheaval | Randomness, mutation, amplification with unpredictable side effects |

Twelve resonance types total gives GMs a rich palette without being unmanageable. Most maps will only have 2–4 active resonances — the rest sit at 0. A haunted crypt might have `stone: 2, death: 3, unholy: 2, shadow: 1`. A forest temple might have `wood: 3, water: 1, holy: 2, life: 2`. The resonance profile tells a story about what this place *is* before a single enemy is placed.

Each ability is tagged with 0–2 resonance types. An ability tagged `flame, holy` (a purifying fire) interacts with both resonances.

### How Resonance Works Mechanically

The map has a resonance profile: a set of resonance type → strength mappings. Only non-zero values need to be specified — everything else defaults to 0.

```
Example: Haunted Crypt
{
  "stone": 2,
  "death": 3,
  "unholy": 2,
  "shadow": 1
}

Example: Forest Temple
{
  "wood": 3,
  "water": 1,
  "holy": 2,
  "life": 2
}

Example: Volcanic Forge
{
  "flame": 3,
  "metal": 2,
  "stone": 1
}
```

Resonance strength is a 0–3 scale:
- **0 — Absent.** This energy is not present. Abilities with this tag work at baseline — no bonus, no penalty.
- **1 — Trace.** Minor presence. Small bonus to matching abilities.
- **2 — Dominant.** This force is a major feature of the environment. Meaningful bonus.
- **3 — Saturated.** The environment is defined by this energy. Maximum bonus. Rare — reserved for thematically extreme maps (the heart of a volcano = flame 3, an ancient mass grave = death 3).

### Resonance Effects on Abilities

When an ability is resolved, the resolution engine checks the map's resonance profile against the ability's tags. The effect depends on the resonance strength:

| Resonance Strength | Effect on Matching Ability |
|---|---|
| 0 (Absent) | Baseline — no modification |
| 1 (Trace) | +1 to damage/healing, or minor secondary effect (e.g., fire ability leaves a 1-round ember on target cell; death-tagged ability causes target to feel a chill of dread — cosmetic) |
| 2 (Dominant) | +2 to damage/healing, secondary effect intensifies (ember lasts 2 rounds; death-tagged necrotic ability also reduces target's max HP for 1 round) |
| 3 (Saturated) | +3 to damage/healing, ability gains an additional effect (fire ability applies Burning condition; holy ability in a saturated shrine also cleanses one condition from the caster; unholy ability in a desecrated graveyard also spawns a minor undead minion on the target cell) |

These numbers are placeholders for tuning. The important structural decision is that resonance is a *gradient*, not binary. A flame-tagged ability doesn't just "work" or "not work" — it works better in flame-rich environments and worse (or just baseline) in flame-poor ones. The same applies to metaphysical resonances: a holy-tagged healing ability functions everywhere, but in a consecrated temple it's significantly more powerful.

**Design choice — penalty for opposing resonance:** An early decision to make is whether opposing resonances penalize abilities. Does casting a fire ability on a water-saturated map impose a penalty? Does casting a holy ability in an unholy-saturated graveyard weaken it? Options:

- **Option A — No penalties, only bonuses.** Resonance 0 = baseline. You're never worse off than your ability's base stats. Simpler, friendlier, less punishing to build variety.
- **Option B — Opposing penalties.** Define opposing pairs (flame↔water, stone↔wind, metal↔wood, holy↔unholy, life↔death, order↔chaos, shadow↔flame). If the opposing resonance has higher strength than your matching resonance, you take a penalty equal to the difference. Punishes characters who rely on a single resonance type.
- **Option C — Hybrid.** No penalties in the general case, but *saturated* (level 3) opposing resonance imposes a penalty. The desecrated graveyard doesn't just fail to help the holy caster — it actively suppresses holy magic. Only the most extreme environments create penalties.

**Recommendation for v1: Option A (no penalties, only bonuses).** It's the simplest to implement and the least likely to create frustrating experiences during playtesting. If testing reveals that resonance diversity is too safe (everyone just picks the strongest abilities regardless of environment), upgrade to Option C. The opposing pairs listed in Option B are a reasonable starting point if penalties are adopted later.

### Resonance Zones

The map-level resonance profile is the baseline, but resonance can also vary *within* a map. Specific zones of the grid can have different resonance than the overall map. The river running through the stone dungeon creates a band of `water: 2` cells in an otherwise `stone: 3` environment. The altar at the center of the crypt might radiate `unholy: 3` in a 2-cell radius even if the overall map is only `unholy: 1`.

This is additive, not a separate system. The map has a base resonance profile, and individual cells or cell groups can override specific resonance types. The resolution engine checks: does this cell have an override? If yes, use it. If no, use the map default.

Zone resonance integrates with dynamic environments (3D.4). Destroying a wall might lower stone resonance in that area. A fire spreading across cells raises flame resonance in the affected zone. The magical landscape shifts as the physical landscape does.

### Ley Point + Resonance Interaction

When a ley point has a `resonance` property, it acts as a localized resonance amplifier. A `resonance: "flame"` ley point with `power: 2` standing in a `flame: 1` zone effectively raises flame resonance to 3 for abilities channeled through it. A `resonance: "unholy"` ley point in a desecrated crypt supercharges necromantic abilities for whoever controls it. The ley point amplifies whatever energy it's attuned to.

A ley point with no resonance (`resonance: none`) amplifies all magical abilities equally regardless of tags — it's a generic magical battery.

This creates distinct ley point identities. The party might control a generic ley point (useful for everyone) while the enemies hold an unholy-resonant ley point (devastating for their necromancer, useless for the party's healer). Trading ley points becomes a strategic consideration — and the metaphysical resonances make these choices even more flavorful. Fighting over a holy ley point in a corrupted temple *feels* different than fighting over a fire ley point in a forge, even though the mechanics are identical.

---

## Ability Tags & the Magic Pipeline

### How Abilities Connect to the Environment

The existing ability definition schema (from Slice 3B.3) already includes `tags[]`. Resonance tags are a subset of those tags. An ability's tags serve double duty:

1. **Resonance tags** (flame, stone, water, metal, wood, wind, holy, unholy, death, life, shadow, order, chaos) — interact with the resonance system.
2. **Mechanic tags** (aoe, melee, ranged, healing, condition, movement) — used by the resolution engine for phase routing and effect application.
3. **Future: source tags** — when internal magic sources are designed, they'll add a third tag category (e.g., pact, body, cognitive). These tags will interact with character-specific systems. Deferred.

An ability can have tags from multiple categories. `Firebolt` might be tagged `[flame, ranged]`. `Consecrated Strike` might be tagged `[holy, melee]`. `Healing Spring` might be tagged `[water, life, healing]`. `Raise Dead` might be tagged `[death, unholy, ranged]`.

### The Magic Pipeline

This extends the damage pipeline from `DESIGN_SPIKE.md`. The existing pipeline has slots for `baseDamage`, `weaponBonus`, and `abilityModifier`. The environmental magic system adds two more slots:

```
computeAbilityEffect(caster, target, ability, environment):
    // --- Existing pipeline ---
    hitRoll = roll(1, 20) + caster.accuracyBonus
    if hitRoll < target.dodgeThreshold:
        return { hit: false }

    baseDamage = ability.baseDamage
    statModifier = caster.profile.relevantModifier        // 0 or a combat profile value until the
                                                          // deferred stats decision (Power/Precision
                                                          // in the candidate stat model)
    weaponBonus = caster.equippedWeapon?.bonus ?? 0       // from items (Slice 4.2)

    // --- New: Environmental modifiers ---
    resonanceBonus = computeResonanceBonus(ability.tags, environment.resonanceAt(caster.position))
    leyBonus = 0
    if caster.isChanneling:
        leyBonus = environment.leyPointAt(caster.position).power * LEY_AMPLIFICATION_FACTOR

    totalDamage = baseDamage + statModifier + weaponBonus + resonanceBonus + leyBonus

    isCrit = checkCrit(caster)
    if isCrit: totalDamage *= 2

    // --- New: Environmental secondary effects ---
    secondaryEffects = computeSecondaryEffects(ability.tags, environment.resonanceAt(caster.position))

    return { hit: true, damage: totalDamage, isCrit, secondaryEffects }
```

`computeResonanceBonus` checks each resonance tag on the ability against the map's resonance at the caster's position. `computeSecondaryEffects` determines if the resonance level is high enough to trigger additional effects (the ember-on-cell, condition-application, AoE-expansion effects described above).

This pipeline structure means environmental magic doesn't require any changes to the resolution engine's phase model. Abilities still resolve in their designated phase. The environment just modifies the numbers and adds secondary effects to the output.

### Non-Damage Abilities

Healing, buff, and utility abilities interact with resonance the same way. A `[water, healing]` ability on a `water: 2` map heals for more. A `[stone, defensive]` shield ability on a `stone: 3` map grants a larger shield value or longer duration.

The pipeline above is framed in terms of damage, but the same structure applies to any numeric ability output — healing amount, shield value, condition duration, movement distance, AoE radius. The `resonanceBonus` and `leyBonus` are modifiers on the ability's primary numeric output, whatever that output is.

---

## Encounter Map Schema Changes

### Map Definition

The existing `encounter:map:{encounterId}` Redis key stores grid dimensions, positions, and fog data. Extend it with magical topology:

```json
{
  "gridWidth": 20,
  "gridHeight": 20,
  "positions": { ... },
  "fogOfWar": { ... },

  "resonanceProfile": {
    "stone": 2,
    "death": 2,
    "unholy": 1,
    "shadow": 1
  },

  "resonanceZones": [
    {
      "cells": [{"x": 9, "y": 9}, {"x": 9, "y": 10}, {"x": 10, "y": 9}, {"x": 10, "y": 10}],
      "overrides": { "unholy": 3, "death": 3 }
    },
    {
      "cells": [{"x": 3, "y": 7}, {"x": 3, "y": 8}, {"x": 4, "y": 7}, {"x": 4, "y": 8}],
      "overrides": { "water": 2 }
    }
  ],

  "leyPoints": [
    {
      "id": "ley-1",
      "center": {"x": 10, "y": 10},
      "radius": 0,
      "power": 2,
      "resonance": "unholy",
      "volatility": "stable",
      "durability": null
    },
    {
      "id": "ley-2",
      "center": {"x": 5, "y": 15},
      "radius": 1,
      "power": 1,
      "resonance": null,
      "volatility": "surge",
      "durability": null,
      "nextEvent": { "round": 3, "type": "surge", "newPower": 3, "duration": 1 }
    }
  ]
}
```

### Postgres Encounter Template

For encounter design persistence (the GM designs an encounter template, which gets hydrated into Redis when the encounter starts):

```
encounter_templates
  id UUID PK
  campaign_id → campaigns
  name TEXT
  grid_width INT
  grid_height INT
  terrain JSONB           -- wall cells, difficult terrain, etc.
  resonance_profile JSONB -- base resonance (elemental + metaphysical)
  resonance_zones JSONB   -- cell-level overrides
  ley_points JSONB        -- ley point definitions
  monster_placements JSONB
  created_at TIMESTAMP
```

This table doesn't exist yet. It's a natural fit alongside Slice 3D.4 (Dynamic Environments), as it gives the GM a persistent place to design and save encounter maps with both physical and magical topology.

---

## Client Rendering

### Ley Point Visualization

Ley points render as a visible glow effect on the grid. Design considerations:

- **Glow intensity scales with power.** Power 1 = subtle shimmer. Power 3 = bright pulsing glow. Players can visually estimate ley point strength at a glance.
- **Color indicates resonance.** A fire-resonant ley point glows orange-red. A water-resonant ley point glows blue. A holy-resonant ley point glows warm gold. An unholy-resonant ley point glows sickly green or deep violet. A generic (no resonance) ley point glows white. Resonance → color mapping should be consistent across all UI elements (ley points, zone tints, ability tooltips, combat feed).
- **Radius is visible.** If a ley point has radius > 0, the affected area is outlined or subtly shaded to show the Channeling zone.
- **Volatility indicators.** Volatile ley points have a visual tell — a flicker for drift, a building pulse for incoming surge, a dimming for depletion. Players should be able to read the ley point's state without checking a tooltip (though a tooltip with exact details should also exist).
- **Dormant state.** A depleted or post-eruption ley point renders as a dim, desaturated version of its active state. Still visible on the map (the players know it existed), but clearly inactive.

### Resonance Visualization

The map's resonance profile is communicated through environmental art and subtle cell tinting:

- **Base resonance:** Affects the overall visual tone of the map. A `stone: 3` dungeon looks and feels like stone. A `flame: 2` volcanic map has ember particles and warm lighting. An `unholy: 3` graveyard has a cold, desaturated palette with faint mist. A `holy: 2` temple has warm light and clean lines. This is atmospheric, not mechanical — but it trains players to read the environment.
- **Resonance zones:** Cells with overridden resonance have a subtle tint or border color matching the resonance type. The river cells with `water: 3` have a blue tint. The altar cells with `unholy: 3` have a dark purple tint. Not so strong as to obscure token visibility, but enough to register.
- **Tooltip on cell hover:** Shows the effective resonance at that cell (base + zone override + ley point contribution). During planning, this helps players decide where to position their casters.

### Ability UI Integration

During the planning phase, when a player selects a magical ability:

- **Range indicator** shows as usual (highlighted cells within range).
- **Resonance preview** shows an inline modifier on the ability tooltip: "Firebolt: 8 damage (+2 from flame resonance)" or "Healing Spring: 12 HP (+0, no water resonance here)." The player can see exactly how the environment affects their ability *before* committing.
- **Ley point benefit preview:** If the caster is on a ley point, the tooltip shows the additional bonus: "Firebolt: 8 damage (+2 resonance, +2 ley point)." If they step off the ley point (by planning a move first), the ley bonus disappears from the preview.

This gives players the information they need to make meaningful positional decisions during planning without requiring them to memorize resonance tables.

---

## Integration with Existing Roadmap Slices

### Slice 3A.2 — Resolution Engine

The resolution engine processes ley points and resonance during its existing phases:

- **Phase 1 (Movement):** After all movement resolves, recompute which combatants are Channeling (standing on a ley point). This determines bonuses for Phase 2.
- **Phase 2 (Attacks/Abilities):** The damage/healing pipeline includes `resonanceBonus` and `leyBonus` as described above.
- **Phase 3 (Effects):** Process volatile ley point behaviors (drift, surge, depletion, eruption). Update ley point state in Redis. Broadcast any changes for the next planning phase.

### Slice 3B.3 — Abilities & Cooldowns (ability side) and 3M.5 — Ley Points & Resonance (map side)

*Split in November 2026 (`ABILITIES.md` A12): 3B.3 ships ability tags, the zero-valued `resonanceBonus` / `leyBonus` slots and parses `resonanceScaling`; 3M.5, right after 3M.2's map files, adds the map's resonance profile and ley points and applies the scaling.*

Ability definitions (the 3B.3 JSON catalog) include `tags[]`. Resonance tags are stored there. A definition can include fields for resonance interaction:

```json
{
  "baseDamage": 6,
  "targetType": "single",
  "range": 8,
  "tags": ["flame", "ranged"],
  "resonanceScaling": {
    "flame": { "damagePerLevel": 2, "thresholdEffects": { "2": "ember", "3": "burning" } }
  }
}
```

Another example — a holy-tagged healing ability:

```json
{
  "baseHealing": 8,
  "targetType": "single_ally",
  "range": 5,
  "tags": ["holy", "life", "healing"],
  "resonanceScaling": {
    "holy": { "healingPerLevel": 2, "thresholdEffects": { "3": "cleanse_condition" } },
    "life": { "healingPerLevel": 1 }
  }
}
```

`resonanceScaling` defines what each resonance level adds. At flame resonance 2, the ability drops an ember (a 1-round environmental effect on the target cell). At flame resonance 3, it also applies the Burning condition.

Abilities without `resonanceScaling` (purely physical attacks, basic movements) are unaffected by the resonance system.

### Slice 3B.4 — Conditions & Status Effects

Resonance-triggered secondary effects (like "applies Burning condition at resonance 3") use the condition system from 3B.4. The environmental magic system is a *source* of conditions, not a separate condition system.

### Slice 3C.1 — Vision & Fog of War

Ley points are visible through fog — they glow even in fogged cells. This is a deliberate design choice: the magical landscape is public knowledge. Players can see where ley points are even if they can't see enemies near them. This creates informed decision-making ("there's a ley point in the fog — is it defended?") without revealing enemy positions.

Resonance is also public — the map's resonance profile is shared with all players at encounter start.

### Slice 3D.4 — Dynamic Environments

Environmental changes affect resonance. The mapping:

- **Fire spreads to cells** → flame resonance increases in those cells.
- **Terrain destroyed** → stone resonance decreases in those cells.
- **Flooding** → water resonance increases.
- **Walls collapse** → wind resonance may increase (open air).
- **Consecration/desecration abilities** → holy or unholy resonance changes in affected cells.
- **Mass death event** (multiple combatants defeated in a small area) → death resonance may increase in those cells. The battlefield itself remembers what happened there.

This is processed during Phase 3 of resolution, alongside other environmental effects. The resonance zone list in Redis is updated dynamically. Metaphysical resonance shifts tend to be triggered by abilities and events rather than physical changes, but the pipeline is identical.

Volatile ley point eruptions are a type of environmental effect and use the same damage/condition pipeline as other hazards.

### Slice 3D.1–3D.3 — Enemy AI

AI services consider ley points in their tactical calculations:

- **Tier 1 (Instinctive):** Ignores ley points. Attacks nearest target.
- **Tier 2 (Tactical):** Prefers positioning on ley points when available. Prioritizes targets standing on ley points (deny the advantage).
- **Tier 3 (Strategic):** Assigns one enemy to hold each ley point. Adapts positioning when ley points drift. Times powerful abilities to coincide with ley point surges.

---

## Deferred: Internal Magic Sources

This section documents what is intentionally *not* designed yet and why.

The question of how individual characters connect to environmental magic — whether through pacts, physical channeling, cognitive pattern-reading, or something else — is deferred until the following are playable and tested:

1. The simultaneous resolution system (Phase 3A).
2. The ability and cooldown system (Slice 3B.3).
3. The environmental magic system described in this document.

Once players can use magical abilities on maps with ley points and resonance, the design team can observe:

- Do players naturally gravitate toward resonance specialization, or do they prefer versatile ability sets?
- How much does ley point positioning dominate tactical planning? Is it the right amount?
- Does the resonance system create enough variety between encounters, or does it feel like a static bonus?
- What's missing? What questions do players ask about their characters' relationship to magic?

The answers will inform the internal source design. Some candidates discussed (documented for future reference):

- **Pact magic** — connection to an entity that filters environmental magic. Debt mechanic as a resource cost.
- **Body magic** — physical channeling. Exertion conditions as cost. Power/Resilience as casting stats.
- **Cognitive magic** — pattern analysis. Information gathering feeds ability power. Focus/Precision as casting stats.
- **Resonance/Harmonic magic** — cooperative channeling. Proximity to allies amplifies effects. Influence as casting stat.

These are starting points, not commitments. The internal source system will be designed in a dedicated document after playtesting validates the external magic layer.

---

## Tuning Parameters

All values below are starting points. They're centralized here so they can be adjusted during playtesting without hunting through code.

| Parameter | Default | Notes |
|---|---|---|
| `LEY_AMPLIFICATION_FACTOR` | 1 | Multiplied by ley point power to get ley bonus. Power 2 ley point → +2 bonus. |
| `RESONANCE_DAMAGE_PER_LEVEL` | 1 | Default damage/healing bonus per resonance level. Abilities can override this in their `resonanceScaling`. |
| `LEY_COOLDOWN_REDUCTION` | 1 | Rounds of cooldown reduced when casting from a ley point. |
| `LEY_DEPLETION_PER_USE` | 1 | Durability cost per ability channeled through a depletable ley point. |
| `LEY_DORMANT_ROUNDS` | 3 | Rounds a depleted ley point stays dormant before reactivating. |
| `LEY_ERUPTION_DAMAGE` | 8 | Base damage dealt by an erupting ley point. |
| `LEY_ERUPTION_THRESHOLD` | 4 | Number of abilities channeled through a ley point in one round that triggers eruption (for eruption-type volatile ley points). |
| `LEY_SURGE_ADVANCE_WARNING` | 1 | Rounds of advance notice before a surge occurs. |
| `LEY_DRIFT_DISTANCE` | 1 | Cells a drifting ley point moves per drift event. |
| `LEY_DRIFT_INTERVAL` | 2 | Rounds between drift events. |

---

## Implementation Sequencing

Environmental magic doesn't need its own roadmap phase — it integrates into existing slices. Here's when each piece lands:

| Component | Lands in Slice | Rationale |
|---|---|---|
| Map resonance profile (map file + Redis, base resonance per map) | 3M.5 (Ley Points & Resonance) | Map content; lands once 3M.2's map files exist. (Was 3B.3 until November 2026.) |
| Ability resonance tags, `resonanceScaling` parsed | 3B.3 (Abilities) | Part of the ability definition. |
| Resonance / ley bonus slots in the pipeline | 3B.3 slots (always 0), 3M.5 values | Extends the existing pipeline. No structural change. |
| Ley point data in map schema (map file + Redis) | 3M.5 (Ley Points & Resonance) | Ley points are part of the map. |
| Channeling state check in resolution | 3M.5 (Ley Points & Resonance) | Resolution engine checks position vs. ley points after movement phase. |
| Client: ley point rendering | 3M.5 (Ley Points & Resonance) | Players need to see ley points to make decisions during planning. |
| Client: resonance preview on abilities | 3M.5 (Ley Points & Resonance) | Players need to see environmental modifiers during planning. |
| Resonance zones (cell-level overrides) | 3D.4 (Dynamic Environments) | Zone overrides are a refinement. Base resonance profile is sufficient for 3M.5. |
| Volatile ley point behaviors | 3D.4 (Dynamic Environments) | Drift, surge, depletion, eruption are environmental effects. Ship with that system. |
| Dynamic resonance changes (fire spread → flame, mass death → death, consecration → holy) | 3D.4 (Dynamic Environments) | Requires the environmental effect system to be in place first. |
| AI ley point awareness | 3D.1+ (Enemy AI) | Tier 2+ AI considers ley points in positioning. |
| Encounter template table (Postgres) | 3D.4 (Dynamic Environments) | Persistent encounter design needs this. |
| Resonance penalty for opposing types (if adopted) | Post-playtesting | Only add if testing shows resonance diversity is too safe. |

The minimum viable environmental magic is: base resonance profile on the map (including both elemental and metaphysical types), resonance tags on abilities, resonance bonus in the pipeline, static ley points on the grid, and client rendering. This was planned for Slice 3B.3; in November 2026 the map side moved to 3M.5 (right after 3M.2) so it lands once in the map file format, while 3B.3 ships the ability-side hooks.

---

## Open Questions

1. **Should abilities without resonance tags benefit from ley points?** A basic melee attack has no resonance tag. Does standing on a ley point boost it? Current lean: yes, at a reduced rate. Ley points amplify all combat actions, but magical (tagged) abilities benefit more. This avoids making ley points useless for martial characters.

2. **Can abilities interact with multiple resonances?** A `[flame, wind]` tagged ability — does it benefit from both resonances? Current lean: yes, use the higher of the two matching resonances, not the sum. Prevents double-dipping from being strictly dominant.

3. **Should resonance affect AP costs?** Casting a flame ability in a flame-saturated environment could cost 1 less AP. This makes resonance affect action economy, not just output. Powerful but potentially too swingy — defer to playtesting.

4. **How many ley points per map is right?** Too few and they dominate the encounter. Too many and they become background noise. Starting guess: 1–3 per 20×20 map, depending on encounter design intent. This is a GM guidance question, not a mechanical constraint.

5. **Should players be able to create or destroy ley points?** Some high-level abilities might manipulate the magical landscape directly. This is a rich design space but adds complexity. Defer to post-3M.5 playtesting. If added, it would be a separate ability category with its own tags and costs.

6. **How does resonance interact with the future crafting system?** If crafting is a thing (hinted at in early design conversations), resonance on the map could affect what can be crafted during downtime or between encounters. Park this until crafting is designed.

7. **Can metaphysical resonance be created by player/enemy actions during an encounter?** The dynamic environments section suggests death resonance could increase when combatants die in an area. How far does this go? Can a cleric consecrate ground mid-fight to raise holy resonance? If so, this becomes a powerful tactical action — spending AP to reshape the magical landscape rather than dealing damage. Likely a Phase 4 feature if adopted.

8. **Should metaphysical resonances have narrative prerequisites?** A map can have `flame: 3` simply because there's lava. But should `holy: 3` require a specific narrative justification (ancient temple, site of a miracle) or can the GM assign it freely? Current lean: GM assigns freely — the resonance profile is a design tool, not a simulation. The GM decides what the place *is* and the numbers follow.
