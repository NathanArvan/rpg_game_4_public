## Phase 3 Feature Roadmap

A few principles before the slices. Order matters here — each group builds on the previous one. Saving throws need spells to be meaningful. Death saves need conditions. AoE needs both spells and conditions. The sequence below respects those dependencies so you're never building on unfinished ground.

---

## Group 1 — Combat Foundations
*These close gaps in the Phase 2 combat loop before adding new complexity.*

### Slice 3.1 — Death Saves & Unconscious State
Start here because it completes the basic combat loop you already built. A character can drop to 0 HP but the flow after that is currently undefined.

**Postgres:** No new tables. Add `is_stable` flag consideration to character state.

**Redis:** `character:live` already has `deathSaves` and `isDead`. Add `isUnconscious: boolean`, `isStabilized: boolean`.

**Server logic:**
- When HP reaches 0, set `isUnconscious: true`, remove from initiative order movement but keep in turn order
- On unconscious character's turn, auto-prompt death save roll
- 3 successes → stabilized, stop rolling
- 3 failures → `isDead: true`, broadcast `CombatantDefeated`
- Natural 20 → regain 1 HP, conscious
- Natural 1 → counts as 2 failures

**SignalR messages:** `DeathSaveRolled`, `CharacterStabilized`, `CharacterDied`

**Angular:** Render unconscious state on initiative tracker and character portrait. Death save tracker UI (3 success / 3 failure pips). Disable action buttons when unconscious.

---

### Slice 3.2 — Saving Throws
Needed before spells and conditions since most of them trigger saves.

**Postgres:** No new tables. `spell_definitions` already has save DC concept in `properties JSONB`.

**Redis:** Add `savingThrowBonus JSONB` to `character:live` — `{ str, dex, con, int, wis, cha }`. Populated on session hydration from character sheet.

**Server logic:**
- GM or spell effect triggers a save: specify ability, DC, and what happens on fail/success
- Server rolls for monsters automatically
- Players roll for their own characters, result broadcast to group
- Pass/fail outcome applied immediately (condition applied, half damage, etc.)

**SignalR messages:** `SavingThrowRequested` (server → specific client or group), `SavingThrowResolved` (server → group with outcome)

**Angular:** Modal or inline prompt when a save is requested for the local player. Show pending saves for other players in the feed. Show pass/fail result with context ("Gimli resists the poison").

---

## Group 2 — Spells
*Builds directly on saving throws and the combat loop.*

### Slice 3.3 — Single Target Spells & Slot Tracking
The foundation all other spell features build on.

**Postgres:** `spell_definitions` and `character_known_spells` already exist. Add `current_spell_slots JSONB` to characters table as the persistent record (Redis holds the live working copy).

**Redis:** `character:live` already has `currentSpellSlots` and `concentratingOn`.

**Server logic:**
- Validate caster knows the spell
- Validate spell slot is available at requested level
- Decrement slot in Redis immediately
- Apply spell effect (damage, healing, condition) to target
- If concentration spell: set `concentratingOn`, clear any previous concentration spell
- Flush slot usage to Postgres on turn end

**SignalR messages:** `SpellCast` (already designed), `ConcentrationBroken` (when caster takes damage while concentrating — requires a concentration save)

**Angular:** Spell selection panel showing known spells, available slots, and which slots each spell can be upcast to. Visual indicator on character portrait when concentrating. Spell slot pips that deplete as slots are used.

---

### Slice 3.4 — Multi-Target Spells
Extends 3.3. Spells like *Healing Word* (single) are done; now handle *Chain Lightning*, *Mass Cure Wounds*.

**Server logic:**
- Accept array of `targetIds` instead of single target
- Apply effect independently to each target (each may roll saving throw separately)
- Aggregate results into single `SpellCast` broadcast with per-target outcomes

**Angular:** Multi-select targeting mode on the map — click multiple tokens before confirming cast. Show all affected targets highlighted simultaneously.

---

### Slice 3.5 — AoE Spells & Visual Overlay
Extends 3.4. Adds the spatial dimension to spell targeting.

**Postgres:** No changes.

**Redis:** `encounter:map` already has `aoeEffects` array.

**Server logic:**
- Accept `aoeShape` (sphere, cone, line, cube) and `aoeCenter` or `aoeOrigin`
- Server calculates which combatants fall within the area using grid math
- Apply effect to all combatants in area
- Write AoE visual to `encounter:map` with configurable duration
- Remove AoE effect when spell ends or concentration breaks

**SignalR messages:** `AoeEffectAdded`, `AoeEffectRemoved` (already in map contracts)

**Angular:** AoE placement mode — player drags/positions shape on grid before confirming. Shape snaps to grid. Affected tokens highlighted as the shape moves. Persist the visual overlay after cast for duration of effect. Different colors per spell school or type.

---

## Group 3 — Conditions
*Depends on saving throws (3.2) and spells (3.3) being in place.*

### Slice 3.6 — Core Conditions & Duration Tracking
The condition system itself — the mechanics that spells and abilities apply.

**Postgres:** New table:
```sql
condition_definitions (
  id UUID PK,
  name TEXT,              -- 'poisoned', 'stunned', 'blinded', etc.
  effects JSONB,          -- { disadvantageOn: ['attack', 'str_saves'], ... }
  description TEXT,
  ruleset TEXT
)
```

**Redis:** `character:live` already has `conditions` array. Add `roundApplied INT` and `durationRounds INT | null` to each condition entry.

**Server logic:**
- On round start, decrement duration on all active conditions
- Conditions at 0 duration auto-removed, `ConditionChanged` broadcast
- Condition effects factored into attack rolls, saving throws, ability checks server-side
- Some conditions (stunned, paralyzed) block actions — validate on action receipt

**SignalR messages:** `ConditionChanged` (already designed), `ConditionExpired`

**Angular:** Condition icons on token and character panel. Tooltip showing condition effect on hover. Turn start notification if a condition expires ("Poisoned condition removed from Frodo"). Visual indicator on token for major conditions (stunned = stars, blinded = X over eyes).

---

### Slice 3.7 — Condition Interactions
Builds on 3.6. Handles the rules around conditions affecting each other.

**Server logic:**
- Paralyzed implies incapacitated — server resolves condition hierarchy
- Exhaustion levels stack additively
- Frightened prevents movement toward fear source — requires source tracking on condition
- Concentration broken by certain conditions (stunned, incapacitated) — auto-clear concentration
- Advantage/disadvantage rules: prone + melee attacker = disadvantage, prone + ranged = advantage. Server resolves these before roll rather than expecting clients to track them.

This slice is logic-heavy with no new schema — it's refining the rule engine inside your server services.

---

## Group 4 — GM Tools
*Independent of Groups 2 and 3. Can be worked in parallel if needed.*

### Slice 3.8 — Fog of War & Cell Reveal
**Postgres:** `encounters` table already has `map_data JSONB`. Persist final revealed state there when encounter ends.

**Redis:** `encounter:map` already has `fogOfWar` with `revealedCells`.

**Server logic:**
- GM-only: reveal individual cells, rectangular areas, or radius around a point
- Vision calculation: optionally auto-reveal cells within N squares of each player token
- Hidden monsters only visible to GM until their cell is revealed

**SignalR messages:** `FogOfWarUpdated` (already designed)

**Angular:** Two map render layers — fog layer (dark overlay) renders on top of base map. Revealed cells cut through the fog. GM view shows everything. Player view respects fog. GM has brush tool to paint reveals. Smooth fade animation on reveal feels good here.

---

### Slice 3.9 — Hidden Rolls & GM Dice
**Server logic:**
- GM can roll dice server-side with result visible only to GM client
- Hidden roll result stored in Redis briefly, only broadcast to GM group
- GM can choose to reveal the result to the full group after seeing it
- Useful for secret perception checks, monster attack rolls the GM wants to delay revealing

**SignalR messages:** `HiddenRollResult` (server → GM group only), `RollRevealed` (GM reveals to full group)

**Angular:** GM panel shows hidden roll results in a separate section. "Reveal to players" button per roll. Players see a "GM rolled dice" indicator in the feed without the result until revealed.

---

### Slice 3.10 — NPC & Monster Control
**Server logic:**
- GM can act on behalf of any monster or NPC combatant on their turn
- Same action pipeline as player actions (attack, move, spell) but actor is GM's connection
- GM can control multiple combatants per round (for monsters that act simultaneously)
- Quick-roll buttons for common monster actions from `monster_definitions.actions JSONB`

**Angular:** When it's a monster's turn, GM sees a control panel for that monster — its actions listed, HP shown, movement range displayed. Clicking an action pre-fills the targeting UI. Distinct visual treatment so GM always knows which tokens they own vs. player tokens.

---

### Slice 3.11 — HP Override & Manual Adjustments
**Server logic:**
- GM can set any combatant's HP to an arbitrary value with an optional reason string
- Logged to `game_events` with `event_type: 'gm_override'` and the reason
- Temporary HP can be granted to any combatant
- GM can add/remove conditions manually outside of spell effects

**SignalR messages:** `GmHpOverride`, `TemporaryHpGranted`

**Angular:** Right-click context menu on any token in GM view — "Edit HP", "Add Condition", "Grant Temp HP". Inline edit field rather than a modal for speed. Changes reflected immediately across all clients.

---

## Group 5 — Inventory & Items
*Largely independent. Can be built any time after Phase 2.*

### Slice 3.12 — Inventory Management
**Postgres:** `character_inventory` and `item_definitions` already exist.

**Redis:** Add `equippedItems JSONB` to `character:live` — the subset of inventory that's currently equipped and affecting stats.

**Server logic:**
- Equip/unequip items updates `character:live` equipped state
- Equipping armor recalculates AC, broadcast to group
- Equipping weapons makes them available in attack declarations
- Quantity tracking for consumables (arrows, potions)

**SignalR messages:** `ItemEquipped`, `ItemUnequipped`, `ItemQuantityChanged`

**Angular:** Inventory panel per character — list of items with equip toggles. Equipped items shown in a paperdoll or slot layout. Weight tracking if using encumbrance rules.

---

### Slice 3.13 — Item Effects in Combat
Extends 3.12. Items that do something during combat rather than just existing.

**Server logic:**
- Consumable used → quantity decremented, effect applied (healing potion restores HP, applies to same pipeline as spells)
- Weapon properties factored into attack rolls (finesse, thrown, reach affect targeting range)
- Magical item bonuses factored into attack/damage calculations server-side
- Attunement tracking if using that rule

**Angular:** "Use item" button in combat for consumables. Visual feedback when item effect resolves (same feed entry pattern as spells).

---

## Suggested Sequencing

```
Month 1     3.1 Death Saves → 3.2 Saving Throws → 3.3 Single Target Spells
Month 2     3.4 Multi-Target → 3.5 AoE → 3.6 Core Conditions
Month 3     3.7 Condition Interactions → 3.8 Fog of War → 3.9 Hidden Rolls
Month 4     3.10 NPC Control → 3.11 HP Override → 3.12 Inventory
Month 5     3.13 Item Effects → polish, playtesting, hardening
```

Groups 4 and 5 are largely independent so if you want a change of pace mid-project you can slot GM Tools or Inventory work between the spell/condition slices without breaking anything.

Each slice should take roughly one to two focused Claude Code sessions to implement fully — narrow scope, clear contracts already defined, and the architecture is already in place to receive them.