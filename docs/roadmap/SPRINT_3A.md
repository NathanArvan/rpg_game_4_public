# Sprint — Phase 3A: Simultaneous Turn Foundation

> **Purpose:** This is the implementation guide for Phase 3A. Hand this to Claude Code alongside `CLAUDE.md`. For the high-level plan and data contracts, see `ROADMAP_STRATEGIC.md`. For locked design decisions, see `DESIGN_SPIKE.md` and `TICK_RESOLUTION.md`.
>
> **Scope:** Slices 3A.1, 3A.2, and 3A.3. These three slices form a single focused sprint — the game is unplayable between 3A.1 and 3A.2, so they ship together.
>
> **What changes:** The existing `EncounterService`, `CombatService`, `EncounterState`, and `GridComponent` will be heavily reworked. Expect to gut and rebuild rather than incrementally modify — the mental model is fundamentally different. The sequential encounter code (`EndTurn`, `TurnAdvanced`, `activeTurnCombatantId`) is dead after this phase. Remove it cleanly.

---

## Existing Code Being Replaced

These are the current components that 3A reworks or removes. Reference `CLAUDE.md` for full details.

**Server services (replaced):**
- `IEncounterService / EncounterService` — currently handles `StartEncounter` (initiative rolls), `MoveToken`, `EndTurn`. StartEncounter will be rewritten. MoveToken is removed (planning replaces it). EndTurn is removed entirely.
- `ICombatService / CombatService` — currently handles `DeclareAttack` with per-turn validation. Attack logic moves into `ResolutionService`.
- `CombatDice` — `RollD20`, `RollDamage`, `DamageDiceString`. Reused by the new resolution engine.

**Hub methods (removed):**
- `EndTurn`, `MoveToken`, `DeclareAttack` — replaced by `PlanAction`, `LockIn`, `ForceResolve`.

**Hub broadcasts (removed):**
- `TurnAdvanced`, `TokenMoved`, `AttackResolved` — replaced by `PlanningPhaseStarted`, `PlayerLockedIn`, `AllLockedIn`, `ResolutionCompleted`, `RoundStarted`.

**NGXS state (reworked):**
- `EncounterState` — remove `activeTurnCombatantId`, `isMyTurn` logic. Rebuild around round phases.
- `MapState` — `pendingMove` concept replaced by `myPlannedActions`.

**Angular components (reworked):**
- `GridComponent` — remove click-to-move-on-your-turn, click-enemy-to-attack, End Turn button.
- `EncounterTrackerComponent` — remove initiative order column.

**Hub contracts (removed/replaced):**
- `TurnAdvancedPayload`, `TokenMovedPayload`, `AttackResolvedPayload` — replaced by new payload types below.
- `CombatantDto` — remove `initiative`, `initiativeRoll`, `dexModifier` fields.

---

## Slice 3A.1 — Planning Phase & Lock-In

*Delivers: All players enter a planning phase simultaneously. Each player plans one action submitted as an action array of length 1. Players lock in when ready. A countdown timer enforces a deadline. The GM can force-start resolution. No actions resolve yet.*

### Postgres Migration

No new tables. Modify existing entities:

- **`EncounterCombatant`** — remove `Initiative`, `InitiativeRoll`, `DexModifier` columns.
- **`Encounter`** — add `RoundNumber INT NOT NULL DEFAULT 1`.

### Redis Operations

**Rework `session:{sessionId}:active_encounter` (String, JSON):**

Remove fields: `activeTurnCombatantId`, `initiativeOrder`.

Add fields:
```json
{
  "encounterId": "...",
  "roundPhase": "planning",
  "roundNumber": 1,
  "planningTimerExpiresAt": "2025-01-01T00:00:30Z",
  "combatants": [...]
}
```

`roundPhase` values: `planning` | `resolving` | `between_rounds`.

**New key `encounter:{encounterId}:planned_actions` (Hash):**

One field per combatant ID. Value is JSON action array:
```json
{
  "actions": [
    {
      "type": "move",
      "targetCell": { "x": 5, "y": 8 }
    }
  ]
}
```

Action types: `move`, `melee_attack`, `ranged_attack`, `defend`.
- `targetCell` present when `type == "move"`.
- `targetCombatantId` present when `type` is an attack.
- `type == "defend"` has neither field.
- Array length is enforced as exactly 1 in 3A. This constraint is removed in 3B.2.

Written during planning, consumed during resolution, cleared between rounds.

**New key `encounter:{encounterId}:locked_in` (Set):**

Combatant IDs that have committed their plan. Resolution triggers when set size equals the number of living combatants (or timer expires).

### Server — New `IPlanningService` / `PlanningService`

Scoped service. Register in DI.

```csharp
public interface IPlanningService
{
    Task<PlanActionResult> PlanActionAsync(Guid sessionId, string userId, PlannedActionArray action);
    Task<LockInResult> LockInAsync(Guid sessionId, string userId);
    Task<ForceResolveResult> ForceResolveAsync(Guid sessionId, string gmUserId);
}
```

**`PlanAction`:**
1. Validate the caller owns the combatant (userId matches).
2. Validate the action array has exactly 1 entry (3A constraint).
3. Validate the action is legal:
   - Move: target cell in bounds (0–19), Chebyshev distance ≤ 6 from current position.
   - Melee attack: target combatant exists and is alive.
   - Ranged attack: target combatant exists and is alive.
   - Defend: always valid.
4. Write to Redis `encounter:{encounterId}:planned_actions` hash, keyed by combatant ID.
5. Return success. The hub broadcasts nothing on plan — plans are private until lock-in.

Note: melee adjacency is NOT validated at planning time. The player commits to a melee attack on a target, and adjacency is checked at resolution time (after movement). This is a core design principle — you plan based on prediction, not certainty.

**`LockIn`:**
1. Validate the caller has a planned action in Redis (can't lock in without planning).
2. Add combatant ID to `encounter:{encounterId}:locked_in` set.
3. Check if all living combatants are locked in (set size == living count).
4. Return `LockInResult` with `allLockedIn: bool` so the hub knows whether to trigger resolution.

**`ForceResolve` (GM only):**
1. Validate caller is GM.
2. For any combatant not in the locked_in set and not in planned_actions: write default action `[{ "type": "defend" }]`.
3. Add all missing combatants to locked_in set.
4. Return result indicating resolution should begin.

### Server — Rework `EncounterService.StartEncounter`

Instead of rolling initiative and assigning turn order:
1. Create `Encounter` and `EncounterCombatant` rows (same as now, minus initiative fields).
2. Seed Redis: set `roundPhase: "planning"`, `roundNumber: 1`, compute `planningTimerExpiresAt` (now + 30 seconds).
3. Return `EncounterStartedPayload` (updated — no `activeTurnCombatantId`, no initiative data).
4. The hub broadcasts `PlanningPhaseStarted` after `EncounterStarted`.

### Server — Planning Timer

A server-side timer that fires when `planningTimerExpiresAt` is reached.

Options (pick one during implementation):
- **`IHostedService` with a timer loop** that polls Redis for active encounters approaching their deadline.
- **Delayed task** scheduled via `Task.Delay` when the planning phase starts, cancelled if all lock in early.
- **Hangfire/similar** if already in the stack (it's not — so probably not worth adding for this).

When the timer fires:
1. Call `ForceResolve` logic — assign default defend actions to any combatant not locked in.
2. Trigger resolution (in 3A.1 this is a no-op transition since resolution doesn't exist yet; in 3A.2 it calls `ResolutionService`).

### SignalR — Hub Methods

**New methods:**

```csharp
// Player plans an action for their combatant
public async Task PlanAction(string joinCode, PlannedActionArray plannedAction)
{
    var session = await ValidateJoinCode(joinCode);
    var result = await _planningService.PlanActionAsync(session.Id, Context.UserIdentifier!, plannedAction);
    // No broadcast — plans are private. Caller gets confirmation via return/ack.
}

// Player locks in their plan
public async Task LockIn(string joinCode)
{
    var session = await ValidateJoinCode(joinCode);
    var result = await _planningService.LockInAsync(session.Id, Context.UserIdentifier!);
    await Clients.Group($"session:{session.Id}")
        .SendAsync("PlayerLockedIn", new PlayerLockedInPayload(result.CharacterName));
    if (result.AllLockedIn)
    {
        await Clients.Group($"session:{session.Id}")
            .SendAsync("AllLockedIn", new AllLockedInPayload());
        // In 3A.2: trigger resolution here
    }
}

// GM forces resolution to begin
public async Task ForceResolve(string joinCode)
{
    var session = await ValidateJoinCode(joinCode);
    var result = await _planningService.ForceResolveAsync(session.Id, Context.UserIdentifier!);
    await Clients.Group($"session:{session.Id}")
        .SendAsync("AllLockedIn", new AllLockedInPayload());
    // In 3A.2: trigger resolution here
}
```

**Remove:** `EndTurn`, `MoveToken`, `DeclareAttack` hub methods. (DeclareAttack logic moves to ResolutionService in 3A.2.)

**New broadcasts:**

| Broadcast | Payload | When |
|---|---|---|
| `PlanningPhaseStarted` | `{ roundNumber, planningTimerDurationSeconds, combatants[] }` | After encounter starts and after each resolution completes |
| `PlayerLockedIn` | `{ characterName }` | When any player locks in. No action details revealed. |
| `AllLockedIn` | `{}` | When all combatants are locked in or GM forces resolve |

**Remove broadcasts:** `TurnAdvanced`, `TokenMoved`, `AttackResolved`.

### SignalR — New Hub Contract DTOs

Create in `server/src/RpgGame.Api/Contracts/Hub/`:

```csharp
public record PlanningPhaseStartedPayload(
    int RoundNumber,
    int PlanningTimerDurationSeconds,
    IReadOnlyList<PlanningCombatantDto> Combatants);

public record PlanningCombatantDto(
    string Id,
    string CharacterName,
    int CurrentHp,
    int MaxHp,
    bool IsDefeated);

public record PlayerLockedInPayload(string CharacterName);

public record AllLockedInPayload();

// PlannedActionArray is the server-side representation of the client's planned action
public record PlannedActionEntry(
    string Type,                    // "move" | "melee_attack" | "ranged_attack" | "defend"
    PositionDto? TargetCell,
    string? TargetCombatantId);

public record PlannedActionArray(IReadOnlyList<PlannedActionEntry> Actions);
```

Update `CombatantDto`: remove `Initiative`, `InitiativeRoll`, `DexModifier`.

Update `EncounterStartedPayload`: remove `ActiveTurnCombatantId`.

### NGXS — State Changes

**`EncounterState` (rework):**

Remove: `activeTurnCombatantId`, any `isMyTurn` derived logic.

Add:
```typescript
interface EncounterStateModel {
  encounterId: string | null;
  active: boolean;
  roundPhase: 'planning' | 'resolving' | 'between_rounds' | null;
  roundNumber: number;
  combatants: PlanningCombatantDto[];
  myPlannedActions: PlannedActionEntry[];  // action array, length 1 in 3A
  lockedInCombatantIds: string[];
  planningTimerExpiresAt: string | null;   // ISO timestamp
}
```

New selectors:
```typescript
@Selector() static isPlanning(state): boolean
@Selector() static isResolving(state): boolean
@Selector() static amILockedIn(state): boolean     // my combatant ID in lockedInCombatantIds
@Selector() static allLockedIn(state): boolean
@Selector() static planningTimeRemaining(state): number  // seconds, derived from planningTimerExpiresAt
@Selector() static myPlannedActions(state): PlannedActionEntry[]
```

New actions:
```typescript
class PlanningPhaseStarted { payload: PlanningPhaseStartedPayload }
class PlayerLockedIn { payload: PlayerLockedInPayload }
class AllLockedIn {}
class SetMyPlannedAction { payload: PlannedActionEntry[] }
class ClearMyPlannedAction {}
```

Remove actions: `TurnAdvanced`, `SetPendingMove`.

### Client — Hub Contracts

Update `client/src/app/contracts/hub-contracts.ts`:

Add:
```typescript
export interface PlanningPhaseStartedPayload {
  roundNumber: number;
  planningTimerDurationSeconds: number;
  combatants: PlanningCombatantDto[];
}

export interface PlanningCombatantDto {
  id: string;
  characterName: string;
  currentHp: number;
  maxHp: number;
  isDefeated: boolean;
}

export interface PlayerLockedInPayload {
  characterName: string;
}

export interface AllLockedInPayload {}

export interface PlannedActionEntry {
  type: 'move' | 'melee_attack' | 'ranged_attack' | 'defend';
  targetCell?: { x: number; y: number };
  targetCombatantId?: string;
}

export interface PlannedActionArray {
  actions: PlannedActionEntry[];
}
```

Remove: `TurnAdvancedPayload`, `TokenMovedPayload`, `AttackResolvedPayload`.

Update `CombatantDto`: remove `initiative`, `initiativeRoll`, `dexModifier`.

### Client — `GameHubService` Updates

Add invoke methods:
```typescript
planAction(joinCode: string, action: PlannedActionArray): Promise<void>
lockIn(joinCode: string): Promise<void>
forceResolve(joinCode: string): Promise<void>
```

Add event listeners:
```typescript
this.connection.on('PlanningPhaseStarted', (payload) => this.store.dispatch(new PlanningPhaseStarted(payload)));
this.connection.on('PlayerLockedIn', (payload) => this.store.dispatch(new PlayerLockedIn(payload)));
this.connection.on('AllLockedIn', () => this.store.dispatch(new AllLockedIn()));
```

Remove event listeners: `TurnAdvanced`, `TokenMoved`, `AttackResolved`.

### Client — `GridComponent` Rework

Remove:
- Click-to-move-on-your-turn logic (the `isMyTurn$` gating).
- Click-enemy-to-attack logic.
- End Turn button.
- `pendingMove` preview (the blue highlighted cell).

Add:
- All players see their movement range simultaneously during planning phase (highlighted cells showing legal move destinations).
- Click empty cell within range → sets planned move action. Renders as a **ghost token** (translucent copy of the player's token at the target cell). Clicking again on a different cell changes the plan. Clicking the ghost token cancels the plan.
- Click enemy token → sets planned attack action (melee or ranged — determine by current adjacency as a UI hint, but the actual melee/ranged distinction can be explicit via a context menu or default to melee if adjacent, ranged if not).
- Click "Defend" button → sets planned defend action.
- Only one action can be active at a time (3A constraint). Selecting a new action replaces the previous one.
- Lock-in button (prominent, below the grid or in the action bar). Disabled until an action is planned. After clicking, the plan is committed and the UI shows a "Locked in — waiting for others" state. The ghost token solidifies or gets a checkmark.
- Planning timer countdown display (seconds remaining). Visual urgency as time runs low (color change, pulse).

### Client — `EncounterTrackerComponent` Rework

Remove: Initiative order column, initiative values.

Show:
- Round number and phase indicator (`Planning Round 3`, `Resolving...`).
- Combatant list with HP bars (same as before, minus initiative).
- Lock-in status per combatant: checkmark icon when locked in, spinner/clock when still planning.
- The GM sees all combatant lock-in status. Players see only that "Player X has locked in" (no action details).

### Two-Tab Test (3A.1)

1. Open three browser tabs. Quick-register a user on each. GM tab creates campaign + session.
2. Player tabs join via join code with characters.
3. GM starts encounter.
4. **Verify:** All tabs simultaneously show the planning UI. No initiative order. Round 1 indicated. Timer counting down from 30.
5. Player 1 tab: click an empty cell → ghost token appears at target. Click a different cell → ghost token moves.
6. Player 1 tab: click Lock In → "Locked in" state shown. Other tabs see "Player 1 has locked in."
7. Player 2 tab: plan an attack on an enemy (click enemy token). Lock in.
8. **Verify:** Since no resolution exists yet, the system transitions to `AllLockedIn` state. (In 3A.2 this will trigger resolution.)
9. **Timer test:** Start a new round. Don't lock in. Wait 30 seconds. Verify default defend action is assigned and the system proceeds to AllLockedIn.
10. **Force resolve test:** Start a new round. Only one player locks in. GM clicks "Force Resolve." Verify all unlocked players get defend and system proceeds.

---

## Slice 3A.2 — Resolution Engine & Playback

*Delivers: After all players lock in (or timer expires), the server resolves all planned actions simultaneously and broadcasts the results. All clients play back the resolution. A new round begins automatically.*

### Server — New `IResolutionService` / `ResolutionService`

Scoped service. The core of the new system. Register in DI.

```csharp
public interface IResolutionService
{
    Task<ResolutionPayload> ResolveRoundAsync(Guid encounterId);
}
```

**Architecture: tick loop from day one.** Even though 3A has exactly 1 tick per round, the code is structured as `for tick in 0..max_ticks` with phase ordering inside it. See `TICK_RESOLUTION.md` for the full rationale.

**Resolution loop (implement this):**

```
ResolveRound(encounterId):
    1. Read all planned actions from Redis (encounter:{encounterId}:planned_actions)
    2. Read current positions from Redis (encounter:map:{encounterId})
    3. Read current HP from Redis (character:live:{encounterId})

    max_ticks = max(len(combatant.action_queue) for each combatant)
    // In 3A, max_ticks is always 1.

    for tick in 0..max_ticks:
        actions_this_tick = gather action at queue position [tick] for each living combatant

        // --- Phase 1: Movement ---
        movements = actions where type == "move"
        For each movement, compute path:
            - Shortest Chebyshev path from current position to target cell
            - Path is computed server-side, deterministic (prefer cardinal over diagonal, break ties north-first-then-east)
        Process paths in lockstep (all movers take step 1 simultaneously, then step 2, etc.):
            - At each step, check for contested cells (two combatants claiming the same cell)
            - Speed tiebreaker for contested cells (higher Speed claims it; tied = neither moves to contested cell, both stop one short)
            - [Extension point: reactive trigger checks at each cell transition — empty in 3A]
        Update positions in working state
        Recompute adjacency

        // --- Phase 2: Attacks ---
        attacks = actions where type in ("melee_attack", "ranged_attack")
        [Extension point: interrupt-timing reaction checks — empty in 3A]
        Validate attacks against CURRENT positions (post-movement):
            - Melee: attacker must be adjacent (Chebyshev ≤ 1) to target. If not, attack WASTED.
            - Ranged: unlimited range in 3A. Always valid if target is alive.
            - If target was defeated in this tick (by an earlier phase — not possible in 3A since movement can't kill, but build for it): attack WASTED.
        Snapshot HP for all combatants
        For each valid attack, compute hit check:
            hitRoll = roll(1, 20) + attacker.accuracyBonus    // +0 in 3A
            if hitRoll < target.dodgeThreshold:                // 10 in 3A
                result = MISS
            else:
                damage = rollClassDice(attacker.class)         // Fighter 1d8, Rogue 1d6, Wizard 1d4
                weaponBonus = 0                                // placeholder
                abilityModifier = 0                            // placeholder
                totalDamage = damage + weaponBonus + abilityModifier
                result = HIT(totalDamage)
        Collect all (combatantId, hpDelta) pairs
        Net deltas per combatant (sum all incoming damage)
        Apply all HP changes in one batch
        Check defeats AFTER all damage applied (HP ≤ 0 = defeated)
        Mutual kills are possible — two combatants can defeat each other
        Void future actions for defeated combatants (relevant when max_ticks > 1)

        // --- Phase 3: Effects ---
        [Extension point: response-timing reaction checks — empty in 3A]
        Resolve defend actions — no-op in 3A
        [Extension point: conditions tick, environment effects — empty in 3A]

        Record tick results for the resolution payload

    // --- End of Round ---
    Write all state deltas to Redis:
        - Update positions in encounter:map:{encounterId}
        - Update HP in character:live:{encounterId}
        - Add defeated combatants to encounter:defeated:{encounterId}
        - Clear encounter:{encounterId}:planned_actions
        - Clear encounter:{encounterId}:locked_in
        - Increment roundNumber
        - Set roundPhase back to "planning"
        - Set new planningTimerExpiresAt
    Write GameEvent batch to Postgres (one event per resolved action) in a single transaction
    DB write commits BEFORE Redis mutations (per CODE_QUALITY.md item 3.2)
    Return ResolutionPayload for broadcast
```

**Atomicity:** Resolution must be atomic from the clients' perspective. No partial updates leak during computation. Compute everything in memory, then write all Redis mutations, then broadcast.

### Resolution Payload Structure

The payload drives client-side playback animation. It is structured as a list of tick results, each containing phase results:

```csharp
public record ResolutionPayload(
    IReadOnlyList<TickResult> Ticks,
    int NewRoundNumber,
    IReadOnlyList<string> DefeatedCombatantIds);

public record TickResult(
    int TickIndex,
    IReadOnlyList<MovementResult> Movements,
    IReadOnlyList<AttackResult> Attacks,
    IReadOnlyList<EffectResult> Effects);    // empty in 3A

public record MovementResult(
    string CombatantId,
    string CombatantName,
    IReadOnlyList<PositionDto> Path,         // full path for animation
    PositionDto FinalPosition,
    bool WasBlocked,                          // true if movement conflict stopped them
    string? BlockedByCombatantId);

public record AttackResult(
    string AttackerId,
    string AttackerName,
    string TargetId,
    string TargetName,
    string AttackType,                        // "melee_attack" | "ranged_attack"
    bool Hit,
    int Damage,                               // 0 if miss
    int TargetNewHp,
    bool TargetDefeated,
    bool Wasted,                              // true if target was dead or melee out of range
    string FeedText);                         // human-readable summary for combat log

public record EffectResult(
    string CombatantId,
    string EffectType,                        // "defend" in 3A
    string Description);
```

### Resolution Payload — Feed Text Generation

Each `AttackResult` includes a `FeedText` string for the combat log. Examples:
- Hit: `"Thorin strikes Goblin A for 6 damage (HP: 12 → 6)"`
- Miss (melee): `"Thorin swings at Goblin A and misses"`
- Miss (ranged): `"Elena shoots at Goblin B and misses"`
- Wasted (melee out of range): `"Thorin's melee attack on Goblin A fails — target out of reach"`
- Kill: `"Thorin strikes Goblin A for 8 damage — defeated!"`
- Mutual kill: both combatants get their own feed entries, both show as defeated

### Server — Wiring Resolution into the Hub

After `AllLockedIn` (either via last player locking in or GM force-resolve):

```csharp
// In the LockIn or ForceResolve hub method, after confirming all locked in:
var resolution = await _resolutionService.ResolveRoundAsync(encounterId);

await Clients.Group($"session:{session.Id}")
    .SendAsync("ResolutionCompleted", resolution);

// After a brief delay (or immediately — let client control playback timing):
await Clients.Group($"session:{session.Id}")
    .SendAsync("PlanningPhaseStarted", new PlanningPhaseStartedPayload(
        resolution.NewRoundNumber,
        30,  // timer seconds
        updatedCombatants));
```

### SignalR — New Broadcasts (3A.2)

| Broadcast | Payload | When |
|---|---|---|
| `ResolutionCompleted` | `ResolutionPayload` (tick results, defeats) | After resolution engine completes |
| `RoundStarted` | `PlanningPhaseStartedPayload` | After resolution, signals next planning phase |

### NGXS — State Changes (3A.2)

New action:
```typescript
class ResolutionCompleted { payload: ResolutionPayload }
```

Handler applies all state deltas from the resolution payload:
- `MapState.positions` — updated from final positions in movement results.
- `EncounterState.combatants` — HP updated from attack results. Defeated flags set.
- `FeedState.entries` — new entries generated from all attack results' `feedText`.
- `EncounterState.roundPhase` — transitions to `'resolving'` during playback, then to `'planning'` when `PlanningPhaseStarted` arrives.
- `EncounterState.resolutionTicks` — stored for playback rendering.

Add to `EncounterState`:
```typescript
resolutionTicks: TickResult[];    // populated on ResolutionCompleted, cleared on next PlanningPhaseStarted
```

### Client — Resolution Playback Component

New component (or mode within `GridComponent`) that animates the resolution:

**Playback sequence per tick:**
1. **Movement phase (~1–2 seconds):** Tokens slide along their paths simultaneously. Blocked movements show a brief "bump" animation at the collision point. Ghost tokens from planning phase are cleared.
2. **Attack phase (~1–2 seconds):** For hits: damage numbers float above the target token (red, "-6"). For misses: "MISS" floats above target. For wasted attacks: brief "X" indicator. Attacks animate simultaneously — multiple damage numbers can appear at once.
3. **Defeat phase:** Defeated combatant tokens fade out or grey out with a visual cue. If mutual kill: both fade simultaneously.

**After all ticks play back:** Brief pause (~0.5 seconds), then auto-transition to the next planning phase. Planning timer starts. Grid returns to planning mode.

**Playback controls:**
- "Skip" button — if all connected players click Skip, playback jumps to final state instantly. Not required for 3A (nice-to-have).

**Target: 3–5 seconds total playback** for a typical round with 1 tick.

### Client — Feed Updates

The combat log (`FeedComponent`) receives batch entries from the resolution. Instead of individual actions appearing one at a time (the old per-turn model), a round's results appear as a group:

```
— Round 1 Resolution —
Thorin moves to (5, 8)
Elena moves to (3, 4)
Thorin strikes Goblin A for 6 damage (HP: 12 → 6)
Elena's ranged attack on Goblin B misses
Goblin A strikes Thorin for 4 damage (HP: 20 → 16)
```

Feed entries are generated client-side from the `ResolutionPayload` using the `feedText` field on each result.

### Two-Tab Test (3A.2)

Builds on the 3A.1 test. Now resolution actually works:

1. Start encounter with two players and two enemies (GM controls enemies manually for now — 3A.3 adds proper enemy planning, but for this test GM can plan enemy actions via the same `PlanAction` hub method with a combatant ID override, or enemies default to defend).
2. Player 1 plans a move. Player 2 plans a ranged attack on an enemy.
3. Both lock in.
4. **Verify resolution playback:** Player 1's token slides to new position. Player 2's ranged attack resolves — either hit (damage number appears, HP updates) or miss (MISS indicator).
5. **Verify both tabs see identical playback simultaneously.**
6. **Verify new planning phase begins:** Round 2 indicated, timer counting down, previous plans cleared.
7. **Movement conflict test:** Both players plan to move to the same cell. Lock in. Verify Speed tiebreaker — higher Speed claims the cell, other stops short. If tied, both stop short.
8. **Melee wasted test:** Player plans melee attack on enemy. Enemy is not adjacent and doesn't move closer. Verify attack is wasted ("target out of reach" in feed).
9. **Mutual kill test:** Set up two low-HP combatants attacking each other. Verify both can be defeated in the same resolution. Both deaths show in the feed and on the grid.
10. **Feed batch test:** Verify all round results appear as a group in the combat log, not one at a time.

---

## Slice 3A.3 — GM Enemy Planning

*Delivers: During the planning phase, the GM plans actions for all enemy combatants. Enemies participate in the same resolution cycle as players.*

### Redis

Enemy planned actions use the same `encounter:{encounterId}:planned_actions` hash as player actions, keyed by monster combatant ID. Same action array schema.

### Server — Extend `PlanningService`

**`PlanEnemyAction`:**
```csharp
Task<PlanActionResult> PlanEnemyActionAsync(
    Guid sessionId, string gmUserId, string combatantId, PlannedActionArray action);
```

1. Validate caller is GM (check `CampaignMember.Role == Gm`).
2. Validate combatant exists and `combatantType == "monster"`.
3. Validate action array (same rules as player actions).
4. Write to Redis planned_actions hash keyed by the monster combatant ID.

**`LockInEnemies`:**
```csharp
Task<LockInResult> LockInEnemiesAsync(Guid sessionId, string gmUserId);
```

1. Validate caller is GM.
2. For any monster combatant without a planned action: assign default `[{ "type": "defend" }]`.
3. Add all monster combatant IDs to the locked_in set as a single batch.
4. The GM counts as **one lock-in covering all enemies**, not one per enemy.
5. Check if all combatants (players + monsters) are now locked in → return `allLockedIn`.

**Default enemy action on timeout:** Same as players — `[{ "type": "defend" }]`.

### Server — Lock-In Logic Update

The existing `LockIn` (player) and the new `LockInEnemies` (GM) both check the same condition: is the `locked_in` set size equal to the number of living combatants? The trigger-resolution logic is shared.

Living combatant count includes both player and monster combatants minus those in the defeated set.

### SignalR — New Hub Methods (3A.3)

```csharp
// GM plans an action for a specific enemy
public async Task PlanEnemyAction(string joinCode, string combatantId, PlannedActionArray plannedAction)
{
    var session = await ValidateJoinCode(joinCode);
    await _planningService.PlanEnemyActionAsync(session.Id, Context.UserIdentifier!, combatantId, plannedAction);
    // No broadcast — enemy plans are hidden from players
}

// GM locks in all enemy actions at once
public async Task LockInEnemies(string joinCode)
{
    var session = await ValidateJoinCode(joinCode);
    var result = await _planningService.LockInEnemiesAsync(session.Id, Context.UserIdentifier!);
    await Clients.Group($"session:{session.Id}")
        .SendAsync("PlayerLockedIn", new PlayerLockedInPayload("GM"));
    if (result.AllLockedIn)
    {
        await Clients.Group($"session:{session.Id}")
            .SendAsync("AllLockedIn", new AllLockedInPayload());
        // Trigger resolution
        var resolution = await _resolutionService.ResolveRoundAsync(result.EncounterId);
        await Clients.Group($"session:{session.Id}")
            .SendAsync("ResolutionCompleted", resolution);
        await Clients.Group($"session:{session.Id}")
            .SendAsync("PlanningPhaseStarted", /* next round payload */);
    }
}
```

### Client — GM Enemy Planning UI

During the planning phase, the GM tab shows a **control panel** (sidebar or overlay) listing all living enemy combatants:

For each enemy:
- Name and current HP.
- Simplified action picker:
  - **Move:** Click enemy in the list, then click a destination cell on the grid. Ghost token appears for the enemy (different visual from player ghosts — maybe red-tinted translucent).
  - **Melee Attack:** Click enemy, then click a target (player token).
  - **Ranged Attack:** Click enemy, then click a target.
  - **Defend:** Click defend button next to the enemy.
- Current planned action shown next to each enemy name (e.g., "→ Move to (5,8)" or "⚔ Attack Thorin" or "🛡 Defend").
- "Clear" button per enemy to remove planned action.

**Lock In Enemies** button: locks in all enemy actions at once. Disabled until at least one enemy has a planned action (enemies without plans get default defend). After lock-in, the enemy panel shows "Locked in" state.

**Player view:** No change. Players don't see the enemy planning panel. Players don't see enemy ghost tokens or planned actions. The `PlayerLockedIn` broadcast shows "GM has locked in" to indicate enemies are committed.

### Two-Tab Test (3A.3)

Full end-to-end combat round with players AND enemies:

1. Three tabs: GM, Player 1, Player 2. Start encounter with 2 players and 2 enemies.
2. **Planning phase:** Player tabs plan their actions. GM tab plans enemy actions via the enemy control panel.
3. GM locks in enemies. Player tabs see "GM has locked in."
4. Players lock in.
5. **Resolution:** All actions (players + enemies) resolve together. Tokens move, attacks resolve (hits/misses/damage), feed fills with all results.
6. **Verify:** Enemy movement and attacks are included in the resolution playback on all tabs. Enemy damage is applied to player characters. Player damage is applied to enemies.
7. **Verify:** New planning phase begins. GM can plan different enemy actions for round 2.
8. **Default test:** Start a round. GM doesn't plan anything for enemies. Timer expires. Verify all enemies default to defend. Resolution proceeds with player actions only (enemies stand still).

---

## Code Removal Checklist

After 3A is complete, confirm the following are gone:

**Server:**
- [ ] `EndTurn` hub method
- [ ] `MoveToken` hub method (movement is now planned, not executed directly)
- [ ] `DeclareAttack` hub method (attacks are resolved by ResolutionService)
- [ ] `TurnAdvanced` broadcast
- [ ] `TokenMoved` broadcast
- [ ] `AttackResolved` broadcast
- [ ] `TurnAdvancedPayload` DTO
- [ ] `TokenMovedPayload` DTO
- [ ] `AttackResolvedPayload` DTO
- [ ] Initiative rolling logic in `StartEncounter`
- [ ] `Initiative`, `InitiativeRoll`, `DexModifier` on `EncounterCombatant` entity
- [ ] `activeTurnCombatantId` in Redis active encounter JSON
- [ ] `initiativeOrder` in Redis active encounter JSON

**Client:**
- [ ] `TurnAdvanced` event listener and NGXS action
- [ ] `TokenMoved` event listener and NGXS action
- [ ] `AttackResolved` event listener and NGXS action
- [ ] `activeTurnCombatantId` from `EncounterState`
- [ ] `isMyTurn` selector / observable
- [ ] `SetPendingMove` action and `pendingMove` from `MapState`
- [ ] End Turn button in `GridComponent`
- [ ] Initiative column in `EncounterTrackerComponent`
- [ ] `endTurn()`, `moveToken()`, `declareAttack()` from `GameHubService`

**Tests:**
- [ ] Rewrite NGXS encounter state tests (they reference `activeTurnCombatantId` and `TurnAdvanced`)
- [ ] Rewrite smoke test (`smoke-test.md`) — the "Playing a Turn" section assumes sequential turns

---

## Placeholder Values (3A only)

These values are hardcoded for 3A and will be replaced by stat-derived values in 3B.1:

| Value | 3A Placeholder | Replaced In |
|---|---|---|
| Movement range | 6 cells (Chebyshev) | 3B.1: `2 + Speed` |
| Accuracy bonus | +0 | 3B.1: `Power` (melee) or `Precision` (ranged) |
| Dodge threshold | 10 | 3B.1: `8 + Speed` |
| Damage | Class dice (Fighter 1d8, Rogue 1d6, Wizard 1d4) | 3B.1: `1d4 + Power/Precision` |
| Max HP | `MaxHp` from character entity | 3B.1: `10 + (Resilience × 5)` |
| Speed (tiebreaker) | Parse from DexModifier or use 0 | 3B.1: Speed stat directly |
| Planning timer | 30 seconds | Stays, GM-configurable later |
| Grid size | 20×20 | Stays |
| Ranged attack range | Unlimited | 3B: tied to Precision |
| Action array length | 1 (enforced) | 3B.2: variable, funded by AP |
