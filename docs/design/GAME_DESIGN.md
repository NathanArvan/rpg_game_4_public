# Game Design — Vision & Philosophy

> **Status:** Living document. This captures the design vision, player experience goals, and core pillars of the game. It is *not* the mechanical reference — for locked implementation decisions, see:
> - `DESIGN_SPIKE.md` — conflict resolution model, damage pipeline, placeholder stats
> - `TICK_RESOLUTION.md` — tick-based resolution loop, action queues, path-based movement, reactive actions
> - `MAGIC_SYSTEM.md` — ley points, resonance, environmental magic pipeline

---

## The Problem with Tabletop Combat

Tabletop RPG combat has two fundamental UX problems that no amount of good game mastering can fully solve. Both stem from the same root cause: the medium itself.

**Waiting.** In a traditional initiative system, one person acts while everyone else watches. A 5-player game with 4 enemies means 9 turns per round. Each player spends roughly 90% of combat doing nothing. A fight that takes 45 minutes of real time contains maybe 5 minutes of actual decision-making per player. The rest is waiting, checking phones, and losing the thread of what's happening. The longer the fight, the worse it gets — and the fights that *should* be the most exciting (boss encounters, multi-phase battles, large-scale skirmishes) are the ones where you wait the longest.

**Mental overhead.** A human GM is simultaneously tracking enemy positions, hit points, conditions, ability cooldowns, environmental effects, NPC motivations, and rules arbitration — all in their head or across scattered notes. Players are doing their own version of this: remembering which abilities are on cooldown, what conditions are active, which squares are difficult terrain, who's concentrating on what spell. The complexity ceiling of tabletop combat isn't set by what would be fun. It's set by what a table of humans can keep track of without the game grinding to a halt. Interesting mechanics get cut or simplified not because they aren't good ideas, but because adding one more thing to track breaks the experience.

These problems aren't bugs in any particular tabletop system. They're inherent limits of the medium: physical game pieces, a single shared map that everyone can see, and one human brain trying to run the world. This game is built from the ground up as a digital RPG specifically to break through those limits.

---

## Design Identity

This is a **tactics-first digital RPG** set in a fantasy world where combat revolves around simultaneous planning, environmental magic, and adaptive enemies. Social and exploration systems are planned for later horizons, built on the same digital-first principles.

The mechanical layer is the product. The game has its own rule system — it is not a digitization of an existing tabletop RPG. Three pillars define the design, and each one exists because the digital platform makes it possible:

**1. Simultaneous planning, simultaneous resolution.** All players plan their turns at the same time. When everyone locks in, all actions — player and enemy — resolve together. No initiative order. No waiting. This directly solves the waiting problem: everyone is engaged every round, and combat that takes 45 minutes in a sequential system can resolve in 10.

**2. Per-player hidden information.** Each player sees only what their character can perceive. The map, enemy positions, and even ally status can differ between screens. The server is the omniscient narrator; each client gets a filtered view. A physical tabletop can't do this — everyone sees the same board. The digital platform makes real fog of war, real ambushes, and real scouting possible for the first time.

**3. The world reacts.** Enemies adapt their tactics, the magical landscape shifts, and the environment changes in ways that would be impossibly complex for a human GM to track in real time. The server handles all of it — hundreds of state changes per round, tracked perfectly, resolved instantly. This directly solves the mental overhead problem: the complexity ceiling is set by what's fun, not by what humans can keep in their heads.

---

## Simultaneous Turn Resolution

### How It Works

Combat flows in rounds. Each round has two phases:

```
┌─────────────────────────────────────────┐
│  PLANNING PHASE                         │
│  All players queue actions at once.     │
│  GM queues enemy actions.               │
│  A timer (configurable) counts down.    │
│  Players see planning indicators for    │
│  allies but not exact action choices.   │
└─────────────────┬───────────────────────┘
                  │ All players lock in
                  ▼
┌─────────────────────────────────────────┐
│  RESOLUTION PHASE                       │
│  Server resolves all actions using a    │
│  tick-based loop.                       │
│  Results animate/display for everyone.  │
│  New round begins.                      │
└─────────────────────────────────────────┘
```

During planning, each player queues an ordered list of actions funded by their Action Point (AP) budget. A character with 3 AP might queue Move, Move, Melee Attack — or Ranged Attack, Defend — or any combination that fits the budget. Everyone plans simultaneously. A countdown timer prevents stalling; if a player doesn't lock in, they default to Defend.

Resolution uses a **tick-based loop**. Each tick processes one action from each combatant's queue, in a fixed phase order: Movement → Attacks → Effects. Within each phase, all actions of that type resolve simultaneously. A character who queued three actions generates three ticks; a character who queued one action idles after the first tick. The mechanical details of the tick model, targeting rules, and edge cases are defined in `TICK_RESOLUTION.md`.

### Why Simultaneous Resolution Changes Everything

The obvious benefit is pacing — everyone plans at the same time, so no one waits. But the deeper impact is on the kind of game this creates.

**Anticipation replaces reaction.** In a sequential system, you see what the enemy does and then respond. Here, you have to *predict* what allies and enemies will do and plan around your predictions. Coordination becomes a skill. Miscommunication becomes a risk. The tension of locking in your plan and watching it collide with everyone else's is something sequential systems can't produce.

**Mutual kills are possible.** Two combatants can defeat each other in the same round. All damage within a tick is computed simultaneously — HP is snapshot, all deltas are calculated, then all deltas are applied at once. A character at 10 HP who takes 15 damage and receives 8 healing in the same tick ends at 3 HP, not dead. The netting model means a well-timed heal can save someone from lethal damage without needing to act "before" the attack. These rules are locked in `DESIGN_SPIKE.md`.

**Wasted actions are a real cost.** If you target an enemy and an ally kills them first, your attack is wasted. Bad prediction costs you. This creates an incentive to spread damage, coordinate targets, and read the battlefield — rather than everyone dogpiling the obvious target.

### Resolution Playback

After resolution computes, the results play back as an animated sequence that all players watch together. The server has already calculated everything; the client is rendering it. This playback should be fast (target: 3–5 seconds for a typical round), clear enough that every player understands what happened to their actions, and reviewable after the fact. The combat log matters more here than in a sequential game, because players didn't watch actions resolve one at a time.

---

## Per-Player Hidden Information

In a physical tabletop game, everyone at the table sees the same map. The digital platform removes that constraint. Each player's client renders a personalized view of the game state based on what their character can perceive.

### What Each Player Sees

Each character has a vision range. Cells outside that range are fogged. Characters with different perception capabilities see different amounts of the map. A rogue scouting ahead might see enemies that the rest of the party can't — those enemies don't appear on other players' screens until they enter someone else's vision or the scout shares the information.

Whether ally information auto-shares (everyone has a telepathic link) or requires explicit communication (spending actions or abilities to warn allies) is a campaign-level setting the GM configures. Environmental hazards and traps work the same way: a character with high perception might see a trap that others walk right past. The trap exists for everyone, but only the perceiving character's client renders the warning.

### Why This Matters

Information asymmetry isn't atmosphere — it creates real tactical decisions that don't exist when everyone sees the same board.

**The scout role becomes mechanical, not just narrative.** A character who moves ahead to reveal the map is doing something mechanically valuable. The party gains information it literally cannot access otherwise.

**Communication is a resource.** If information doesn't auto-share, players need to spend actions or use abilities to warn allies. During the planning phase, you might know something your allies don't — and deciding whether to spend AP telling them or spend AP fighting is a real choice.

**Ambushes work properly.** On a physical tabletop, an ambush is hard to pull off because all players can see the GM placing miniatures. Here, enemies can be genuinely hidden until they attack or are spotted. The moment of discovery is real.

**Friendly fire from imperfect information.** If you target an area you can't fully see, you might hit an ally who moved there during simultaneous resolution. This is a natural consequence of imperfect information and simultaneous action — and it's exactly the kind of consequence that creates stories.

### GM Perspective

The GM always sees the full, unfiltered map — all enemies, all traps, all environmental state. The GM's view is the source of truth. The GM can also inspect what each individual player currently perceives, which helps when narrating or making rulings.

---

## Environmental Magic

Magic in this game is an external force. It exists in the world, not inside the character. The battlefield is a magical landscape, and characters have abilities that interact with that landscape. The environment amplifies, restricts, enables, or transforms what those abilities can do.

Two systems express this idea mechanically:

**Ley Points** are discrete cells on the encounter grid charged with raw magical energy. Standing on or near a ley point amplifies magical abilities — bonus damage, reduced cooldowns, access to powerful effects that can only be cast while channeling. Ley points are visible to all players (the magical landscape is public information, unlike enemy positions). They're a tactical objective: a resource to fight over, like high ground or cover. The GM places them during encounter design, and they can be stable, drifting, surging, or volatile.

**Resonance** describes the elemental and metaphysical energies present in the environment. A volcanic cavern has high flame resonance. A consecrated temple thrums with holy energy. A desecrated graveyard radiates death. Abilities tagged with matching resonance types perform better in environments that feed them — a fire ability hits harder in a flame-rich environment, a holy healing spell is significantly more powerful in a shrine. Resonance profiles can vary within a map (the river running through the stone dungeon creates a band of water resonance) and change dynamically as the environment changes (fire spreading raises flame resonance; mass death raises death resonance in the affected area).

Ley points govern *how much* magical energy is available. Resonance governs *what kind*. Their intersection — a flame-attuned ley point in a volcanic forge, an unholy ley point in a graveyard — is what makes each encounter's magical landscape unique.

The question of how individual characters *connect* to environmental magic (pacts, physical channeling, cognitive pattern-reading) is intentionally deferred until the environmental system is playable and tested. The full mechanical design is in `MAGIC_SYSTEM.md`.

---

## Adaptive Encounters

Enemies are not static stat blocks following predetermined behavior. The server runs enemy decision-making using a rule engine that considers the current state of the encounter. This is where the digital platform's advantage over a human GM is most dramatic: tracking five enemy behaviors while managing rules, narration, and pacing is exhausting for a person. The server does it perfectly every round.

### Enemy Behavior Tiers

Not every enemy needs to be smart. Complexity matches the narrative fiction of the creature:

**Tier 1 — Instinctive.** Attacks the nearest threat. Flees when HP is low. No coordination. Beasts, undead, mindless creatures.

**Tier 2 — Tactical.** Focuses fire on wounded targets. Prioritizes squishy characters. Uses terrain for cover. Retreats to regroup. Prefers positioning on ley points when available, and prioritizes denying ley points to enemies. Trained soldiers, intelligent monsters.

**Tier 3 — Strategic.** Coordinates with other enemies. Assigns holders to ley points. Flanks while drawing attention. Reserves abilities for optimal moments. Adapts to player patterns — if the party always focuses the same target, Tier 3 enemies protect it. If a player opens every round with the same ability, enemies begin countering it. Adapts positioning when ley points drift. Times powerful abilities to coincide with ley point surges. Bosses, rival adventuring parties, military commanders.

### Dynamic Environments

The encounter map is not a static backdrop. Environmental elements change during combat, and the server tracks all of it without any mental overhead for the GM or players.

Walls can be broken, bridges can collapse, cover can be destroyed. Fire can spread to adjacent cells each round. Flooding can raise water levels. Levers, doors, and pressure plates can be activated by either side to reshape the battlefield. These physical changes also shift the magical landscape: destroyed stone lowers stone resonance, spreading fire raises flame resonance, mass death raises death resonance in the area.

Reinforcement waves can be triggered by encounter state (round count, boss HP thresholds, player positioning). Random environmental events — volcanic eruptions, shifting tides, collapsing terrain — can be seeded deterministically for replayability. Health potions, ammunition, and other resources can spawn at designated points during combat, creating incentives for map control.

---

## Character Design Philosophy

Characters are defined by what they can do, not by a class label. The system supports character building through composable abilities rather than rigid class hierarchies.

### Stats

Six core stats, each with a clear mechanical identity tied to the game's systems:

| Stat | Role |
|------|------|
| **Power** | Melee damage, physical force |
| **Speed** | Movement range, tiebreakers in simultaneous resolution, dodge |
| **Precision** | Ranged accuracy, critical hits, perception |
| **Resilience** | Max HP, condition resistance |
| **Focus** | Vision range, spell/ability resource pool, concentration |
| **Influence** | Information sharing range, team buff radius, NPC interactions |

Every stat has mechanical weight. Speed matters because simultaneous resolution uses it as a tiebreaker for contested cells. Focus matters because per-player vision makes perception range a tactical asset. Influence matters because information sharing is a real resource when hidden information is in play.

### Abilities Over Classes

Rather than choosing a class that grants a fixed progression, characters acquire abilities — discrete mechanical packages that define what they can do. Abilities might be organized into trees, purchased with advancement points, or unlocked through narrative milestones. The progression system is a later design decision. The key principle is that a character's mechanical identity is the sum of their chosen abilities, not a label.

Ability choice directly affects the tick resolution system: which phase your actions fall into, how much AP they cost, and how they interact with ley points and resonance.

### Resource Management

Every powerful ability has a cost, creating round-to-round decision tension:

**Action Points (AP)** are a per-round budget. Moving costs AP. Attacking costs AP. Abilities cost AP. Players allocate limited AP across movement, offense, defense, and utility each round by queuing an ordered list of actions. The tick model means the *order* of your actions matters — Move then Attack plays out differently than Attack then Move.

**Cooldowns** prevent ability spam. They're tracked server-side and displayed during planning. The server handles this perfectly; no one needs to remember what round they last used an ability.

**Consumable resources** — spell slots, ammunition, potions — deplete across the encounter or session, forcing strategic thinking beyond the current round.

---

## What the Digital Platform Makes Possible

Every major design choice in this game traces back to the same thesis: a digital RPG should do things that a physical tabletop cannot, rather than simulating one.

| Design choice | Why it needs a digital platform |
|---|---|
| Simultaneous resolution with tick-based ordering | Resolving 9+ combatants' interleaved actions with path-based movement, netting HP deltas, and checking reactive triggers at each cell transition is instant for a server. A human would need minutes per round with a calculator. |
| Per-player hidden information | Requires separate screens with server-computed vision filtering. Impossible with a shared physical map. |
| Environmental magic (ley points + resonance) | Tracking ley point volatility, resonance zones, channeling states, dynamic resonance shifts from environmental changes, and per-ability resonance scaling — all updating every tick — would bury a human GM. |
| Adaptive enemy AI | Pattern recognition across rounds, coordinated multi-enemy tactics, and ley-point-aware positioning require state tracking that no GM can sustain while also running the game. |
| Dynamic environments | Fire spread, terrain destruction, hazard escalation, volatile ley point eruptions, and the cascading resonance changes they produce are tracked automatically. The GM sets the initial conditions; the server handles the physics. |
| AP-funded action queues | Validating AP budgets, enforcing action ordering, and resolving queues of varying length across all combatants in a tick loop is bookkeeping that would make tabletop combat unbearable. Here it's invisible. |

The mental overhead doesn't disappear — it moves from the humans to the server. Players think about tactics, not bookkeeping. The GM thinks about encounter design and narrative, not state tracking. The complexity ceiling is set by what's fun to play, not what's feasible to manage.

---

## Future Horizons

This document and the locked decision documents describe **Horizon 1: Single Encounter** — tactical combat played to completion. Two further horizons are planned:

**Horizon 2 — Connected Sessions.** Multiple encounters with persistent characters. Experience, items, and progression carry between sessions. Social encounters and exploration systems are introduced, built on the same digital-first principles (server-managed state, per-player information filtering, simultaneous action where appropriate).

**Horizon 3 — Campaign Play.** Long-running narrative arcs, world state persistence, crafting, downtime activities, and the full campaign layer. The internal magic source system (how characters personally connect to environmental magic) is designed after Horizon 1 playtesting validates the external magic layer.

The roadmap for Horizon 1 is in `ROADMAP_STRATEGIC.md`.
