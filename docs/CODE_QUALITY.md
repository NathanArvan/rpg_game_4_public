# Code Quality & Test Strategy

> **Status:** Phase 2 complete. This document captures current technical debt and establishes the test strategy for Phase 3 and beyond.

---

## 1. Server Code Smells (.NET)

### Critical

#### Magic numbers and strings scattered throughout
All game constants are hardcoded inline with no central configuration object.

| Value | Location |
|-------|----------|
| Grid size 20×20 | `GameHub.cs:200–201` (`const int gridWidth = 20; const int gridHeight = 20`) |
| Movement range 6 | `GameHub.cs:498` (`chebyshev > 6`) |
| CORS origin `http://localhost:4200` | `Program.cs:76` |
| Damage dice by class (d8/d6/d4) | `GameHub.cs:571–574` (switch expression) |
| Join code length 6 | Implicit in `CampaignsController` generation logic |

Adding a second map size or changing movement speed requires a grep hunt rather than a config change.

#### Credentials hardcoded in source
`AppDbContextFactory.cs:11` contains a literal connection string including the password `changeme`. This file is checked into source control and is used by EF tooling (`dotnet ef migrations add`). The password must never appear in committed code.

```
// AppDbContextFactory.cs:11
.UseNpgsql("Host=localhost;Port=5432;Database=rpg_game;Username=rpguser;Password=changeme")
```

---

### High

#### `GameHub.cs` has three God methods
All business logic lives in three methods that each violate the Single Responsibility Principle:

| Method | Lines | Responsibilities |
|--------|-------|-----------------|
| `StartEncounter` | 120–267 (~147 lines) | GM auth, player enumeration, initiative rolls, encounter DB insert, Redis state init, map seeding, broadcast |
| `DeclareAttack` | 269–424 (~155 lines) | Turn validation, defeated checks, DB lookups, damage calculation, Redis HP decrement, event log, broadcast |
| `MoveToken` | 426–517 (~91 lines) | Turn validation, bounds check, Chebyshev distance, Redis map update, broadcast |

Each method would be a candidate for extraction into a dedicated service class.

#### No service layer — all business logic in hub and controllers
There is no `IEncounterService`, `ICombatService`, or `ISessionService`. Business rules (initiative calculation, damage dice, turn order) live directly inside `GameHub`. This makes the logic impossible to unit test without a full SignalR host.

#### No repository pattern — `AppDbContext` injected directly
`AppDbContext` is constructor-injected into `GameHub`, `CharactersController`, and `CampaignsController`. There is no abstraction between hub/controller code and the ORM. Any test that exercises a hub method must provide a working `DbContext`.

#### Redis access not abstracted
`IConnectionMultiplexer` is injected directly into `GameHub` and `.GetDatabase()` is called inline throughout all five hub methods. There is no `IRedisGameState` or similar interface. Swapping Redis for an in-memory store (for tests) is not possible without modifying the hub.

#### Duplicate userId extraction — 7 occurrences
Controllers use the two-claim fallback pattern on every action method:

```csharp
// CharactersController.cs:24–25, 58–59, 72–73, 85–86, 113–114
// CampaignsController.cs:25–26, 54–55
var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
    ?? User.FindFirst("sub")?.Value;
```

This pattern is repeated 7 times across two controllers. It should live once in a base controller or extension method.

#### `EndTurn` all-defeated bug
`GameHub.cs:548–553` loops at most `Combatants.Count` iterations to find the next non-defeated combatant. If every combatant is in the defeated set, the loop exhausts its iterations without finding a live combatant, then silently selects the last-iterated (defeated) combatant as the new active turn holder. The encounter becomes stuck — the defeated combatant's client never receives a valid turn.

```csharp
// GameHub.cs:548–553
for (int attempt = 0; attempt < activeEncounter.Combatants.Count; attempt++)
{
    nextIndex = (nextIndex + 1) % activeEncounter.Combatants.Count;
    var isDefeated = await redisDb.SetContainsAsync(defeatedKey, ...);
    if (!isDefeated) break;  // never breaks when all are defeated
}
```

---

### Medium

#### N+1 queries

- **`StartEncounter` (line 160):** `_db.Characters.FirstOrDefaultAsync` called inside a `foreach` over `playerEntries`. One DB round-trip per player. Should be a single `WHERE Id IN (...)` query.
- **`DeclareAttack` (lines 347–360):** Two separate `FirstOrDefaultAsync` calls to load attacker and target combatants, each followed by another call to load the associated character. Four round-trips where one join query would suffice.

#### No transaction between Redis and PostgreSQL
In `DeclareAttack`, HP is decremented in Redis (`GameHub.cs:375`) before the `GameEvent` is persisted with `SaveChangesAsync` (`GameHub.cs:401`). If the DB write fails, Redis shows reduced HP with no corresponding event log entry — state is inconsistent and cannot be reconstructed.

#### `CharacterId` string/Guid type mismatch
The `characterId` parameter arrives as `string?` at the SignalR boundary and is parsed via `Guid.TryParse` in four locations:
- `GameHub.cs:59` — `JoinSession`
- `GameHub.cs:157` — `StartEncounter` (inside player loop)
- `GameHub.cs:333` — `DeclareAttack` (encounter ID)
- `GameHub.cs:340` — `DeclareAttack` (target ID)

A typed DTO with `[JsonConverter]` applied once at the boundary would eliminate all parse calls.

---

## 2. Client Code Smells (Angular)

### Critical

#### `LobbyComponent` is a God Component
`lobby.component.ts` (281 lines) handles six distinct concerns in a single class:

1. Register / Login / Quick Register (`lines 184–210`)
2. Player join flow — join code + character select (`lines 212–231`)
3. GM flow — campaign create → session start → connect (`lines 233–280`)
4. Host for `EncounterTrackerComponent`, `GridComponent`, and `FeedComponent`
5. Reads from three separate NGXS state slices
6. Manages six independent error strings and five local state variables for GM step progression

This should be split into at least `AuthPanelComponent`, `PlayerJoinComponent`, and `GmFlowComponent`, each focused on one concern.

---

### High

#### `GameHubService` violates SRP
`game-hub.service.ts` does all of the following in a single class:

- Manages the SignalR `HubConnection` lifecycle (connect, reconnect, disconnect)
- Registers 7 inbound event handlers (`lines 24–78`)
- Invokes 5 outbound hub methods (`lines 89–107`)
- Dispatches actions to 5 different NGXS state slices (`SessionState`, `EncounterState` ×2, `MapState` ×2, `FeedState`)

The connection lifecycle, event routing, and store dispatch are separate concerns.

#### `GridComponent` uses `store.snapshot()` anti-pattern
`grid.component.ts:185–186`:

```typescript
const state = this.store.snapshot();
const activeTurnId = state.encounter?.activeTurnCombatantId;
```

`store.snapshot()` synchronously reads the current state object, bypassing NGXS's reactive pipeline. If state updates while this executes, the snapshot is stale. The component also maintains private sync copies of observable state (`lines 117–119, 163–165`), which duplicate the store and can drift out of sync.

#### No error handling on hub invocations
In `GameHubService`, all five `connection.invoke(...)` calls (`lines 90, 94, 98, 102, 106`) return `Promise<void>` with no `.catch()`. Errors from the server (`HubException`) are silently swallowed. `GridComponent` similarly calls `this.hub.moveToken(...)` and `this.hub.declareAttack(...)` (`lines 194, 198`) without awaiting or catching.

---

### Medium

#### Magic string hub event names
All 7 SignalR event names are bare string literals in `GameHubService` (`lines 24–74`):
`'PlayerPresenceChanged'`, `'EncounterStarted'`, `'TokenMoved'`, `'TurnAdvanced'`, `'ActionRejected'`, `'AttackResolved'`, `'CombatantDefeated'`. A typo fails silently at runtime with no compile-time error.

#### Magic string API endpoints
Every HTTP call uses a hardcoded path string:
- `AuthService`: `'/api/auth/register'`, `'/api/auth/token'`
- `CharacterService`: `'/api/characters'`, `'/api/characters/mine'`, etc.

There is no `API_BASE_URL` constant or environment-based config object.

#### `AuthService.getUserId()` JWT parsing is fragile
`auth.service.ts:42–46`:

```typescript
const payload = JSON.parse(atob(this.token.split('.')[1]));
return payload.sub ?? payload.nameid ?? '';
```

- No validation that the token has three `.`-separated parts
- `atob` throws on non-base64 input (e.g., tokens with URL-safe characters)
- The `catch` block silently returns `''`, hiding misconfigured JWT issuers

#### `MapStateModel.fogOfWar` is dead code
`map.state.model.ts:10`:

```typescript
fogOfWar: never[];
```

The type `never[]` means this field can only ever be an empty array. No selector reads it, no action writes to it (other than the default initializer), and the server sends `fogOfWar: []` which is ignored. The field should be removed until fog-of-war is actually implemented.

#### `DevService.randomCharacter()` noun-split bug
`dev.service.ts:35`:

```typescript
name: this.pick(this.fantasyNames) + ' ' + this.pick(this.nouns).charAt(0).toUpperCase() + this.pick(this.nouns).slice(1)
```

`this.pick(this.nouns)` is called twice independently. The uppercase first letter comes from one randomly selected noun; the remaining letters come from a *different* randomly selected noun. The generated surname is a random splice of two unrelated words.

---

## 3. Recommended Refactors (Priority Order)

These are ordered by impact on testability and correctness. Each is a prerequisite for the test phases described in Section 4.

1. **Fix `EndTurn` all-defeated bug** — add a guard after the loop that detects no non-defeated combatant was found and ends the encounter or throws a `HubException`. Zero test infrastructure needed; purely additive.

2. **Extract `ICurrentUserAccessor`** — a single interface with a `GetUserId()` method eliminates the 7 duplicated `FindFirst` patterns in controllers. Register as `HttpContextAccessor`-backed in production; mock in tests.

3. **Extract `IRedisGameState`** — wraps all Redis read/write operations currently inline in `GameHub`. Enables swapping in a fake/in-memory implementation for hub tests without a Redis process.

4. **Extract `IEncounterService` / `ICombatService`** — move `StartEncounter`, `DeclareAttack`, `EndTurn`, and `MoveToken` business logic out of the hub into focused service classes. Hub methods become thin orchestrators: validate → call service → broadcast. This is the primary unlock for server unit tests.

5. **Wrap Redis+DB operations in a transaction scope** — use `IDbContextTransaction` or at minimum re-order operations so that DB writes commit before Redis is mutated, or implement a compensating rollback on DB failure.

6. **Fix N+1 queries** — replace per-player character fetches in `StartEncounter` with a single `WHERE Id IN (...)`, and replace the four separate DB calls in `DeclareAttack` with one or two joined queries.

7. **Split `LobbyComponent`** — extract `AuthPanelComponent` and `GmFlowComponent`. This enables component-level tests with `MockStore` and eliminates the God Component.

8. **Remove `store.snapshot()` from `GridComponent`** — replace with a combined observable that includes `activeTurnCombatantId`, consistent with the existing `isMyTurn$` observable pattern already in the file.

9. **Replace magic string event names with constants** — a `HubEvents` namespace with `const` members eliminates silent typo failures.

10. **Move credentials out of `AppDbContextFactory`** — read from environment variable or `dotnet user-secrets`. The hardcoded `changeme` string should never be committed.

---

## 4. Test Strategy

### Principles

- Test business logic, not framework wiring
- Prioritize tests that catch known real bugs (EndTurn all-defeated, HP race, attack-on-wrong-turn)
- All tests run in CI via the existing `dotnet test` and `npm test` commands
- Mock at abstraction boundaries; do not mock types you own

---

### Server — xUnit + Moq

Refactors 3 and 4 above (service extraction) are prerequisites for Phases A and B.

#### Phase A — Pure domain/logic (no infrastructure needed)

Once `CombatService` and `SessionService` are extracted, the following are pure-function tests with no DB or Redis dependency:

- `CombatService` — damage calculation by class returns value in correct dice range; Chebyshev distance validation accepts ≤ 6, rejects 7+; initiative roll is in range [1..20] + dex modifier
- `SessionService` — join code generation produces 6-char alphanumeric strings; GM role check rejects non-GM callers
- `CharacterValidator` — stat values 1–20 accepted, 0 and 21 rejected; `MaxHp > 0` enforced; `Level ≥ 1` enforced

#### Phase B — Hub integration with mocked dependencies

With `IEncounterService` and `IRedisGameState` extracted, `GameHub` methods can be tested using `Moq` and the SignalR `HubCallerContext` test helpers:

- `StartEncounter` — rejects non-GM; creates correct number of combatants; orders by initiative descending; seeds Redis map
- `DeclareAttack` — rejects wrong-turn caller; rejects attack on defeated target; decrements HP by correct dice range; writes `GameEvent` to DB
- `EndTurn` — normal advance skips to next; skips defeated combatants correctly; all-defeated scenario does not hand turn to a defeated combatant (regression for the current bug)
- `MoveToken` — rejects out-of-bounds target; rejects Chebyshev > 6; updates Redis position; broadcasts `TokenMoved`

#### Phase C — Controller tests

- `CharactersController` — CRUD actions return 403 for characters belonging to a different user; `GET /characters/{id}` returns 404 for unknown IDs; `POST /characters` with missing required fields returns 400
- `CampaignsController` — `POST /campaigns/{id}/sessions` returns a 6-char join code; duplicate active encounter returns 409

---

### Client — Karma/Jasmine (already configured)

`npm test` in `client/` runs the Karma test suite. No additional tooling is needed.

#### Phase A — NGXS State (easiest, highest value)

State classes are pure reducers — they can be tested with `NgxsTestingModule` and no component or HTTP dependency:

- `SessionState` — `PlayerPresenceChanged` upserts a new player and updates an existing one; offline payload removes player from list
- `EncounterState` — `AttackResolved` decrements `currentHp` on the correct combatant; `CombatantDefeated` sets `isDefeated: true`; `EncounterStarted` sorts combatants by initiative
- `MapState` — `TokenMoved` updates position for the correct combatant ID; `SetPendingMove` stores the position; `TokenMoved` clears `pendingMove`
- `FeedState` — `AddFeedEntry` prepends entry and increments `unreadCount`; `MarkRead` resets `unreadCount` to 0

#### Phase B — Services with mocked HTTP / HubConnection

- `AuthService.getUserId()` — parses valid JWT and returns `sub`; returns `''` on malformed token; returns `''` when token is null
- `CharacterService` — CRUD happy paths return expected shapes; 404 response propagates as observable error
- `GameHubService` — each of the 7 event handlers dispatches the correct NGXS action with the correct payload (use a fake `HubConnection` that emits synthetic events)

#### Phase C — Components with MockStore

After the `LobbyComponent` split (Refactor 7 above):

- `AuthPanelComponent` — renders register/login form when not logged in; calls `AuthService.register` on submit
- `GmFlowComponent` — shows campaign-create step initially; shows session-start step after campaign creation; shows join code after session start
- `GridComponent` — `onCellClick` on empty cell dispatches `SetPendingMove` and calls `hub.moveToken`; `onCellClick` on enemy token calls `hub.declareAttack`; `onCellClick` when it is not my turn is a no-op; `store.snapshot()` replaced with observable before this test can run cleanly

#### What NOT to test

- Angular template rendering (covered by end-to-end tests in a future phase)
- EF migration correctness (verified by DB startup in Docker)
- NGXS framework internals (the library is trusted)
- SignalR reconnection logic (library behavior, not application logic)
