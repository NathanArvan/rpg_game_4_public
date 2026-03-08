# Code Quality — Actionable Refactoring Checklist

> **Status:** Phase 2 complete. All items below are self-contained tasks ordered by priority. Each includes enough context for a Sonnet-class agent to complete it in one session.

---

## Tier 1 — Fix Bugs & Unlock Testing

### 1.1 Fix `EndTurn` all-defeated bug

**Priority:** Critical — active correctness bug
**Files:** `server/src/RpgGame.Api/Hubs/GameHub.cs`
**Estimated scope:** ~10 lines changed

**Problem:** The `EndTurn` method (lines 505–550) loops `Combatants.Count` iterations to find the next non-defeated combatant. If every combatant is defeated, the loop exhausts without breaking and silently selects a defeated combatant as the new turn holder. The encounter becomes stuck.

**Current code (lines 534–539):**
```csharp
for (int attempt = 0; attempt < activeEncounter.Combatants.Count; attempt++)
{
    nextIndex = (nextIndex + 1) % activeEncounter.Combatants.Count;
    var isDefeated = await redisDb.SetContainsAsync(defeatedKey, activeEncounter.Combatants[nextIndex].Id);
    if (!isDefeated) break;
}
```

**Fix:** Add a boolean `foundAlive = false` before the loop. Set it to `true` inside the `if (!isDefeated)` block before `break`. After the loop, check `if (!foundAlive)` — if true, broadcast an encounter-ended event to the group and return early instead of advancing the turn. You may need to add:
- A new `EncounterEndedPayload` record in `server/src/RpgGame.Api/Contracts/Hub/`
- A `SendAsync("EncounterEnded", payload)` broadcast
- Update the `Encounter` entity status to `Ended` in the DB
- Clean up the Redis active encounter key

**Acceptance criteria:**
- When all combatants are defeated, `EndTurn` does NOT assign a defeated combatant as the active turn holder
- The encounter transitions to an ended state
- The group receives notification that the encounter ended
- Existing behavior is unchanged when at least one combatant is alive

---

### 1.2 Extract `ICurrentUserAccessor`

**Priority:** High — eliminates 7 duplicate code blocks
**Files to modify:** `CharactersController.cs`, `CampaignsController.cs`, `Program.cs`
**Files to create:** `server/src/RpgGame.Api/Services/ICurrentUserAccessor.cs`, `server/src/RpgGame.Api/Services/HttpCurrentUserAccessor.cs`
**Estimated scope:** 3 new files, 2 files modified

**Problem:** The following 3-line pattern appears 7 times across two controllers:
```csharp
var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
    ?? User.FindFirst("sub")?.Value;
if (userId is null) return Unauthorized();
```

Locations:
- `CharactersController.cs` lines 27–29, 62–64, 77–79, 91–93, 119–121
- `CampaignsController.cs` lines 28–30, 58–60

**Implementation:**

1. Create interface:
```csharp
// server/src/RpgGame.Api/Services/ICurrentUserAccessor.cs
public interface ICurrentUserAccessor
{
    string? UserId { get; }
}
```

2. Create implementation:
```csharp
// server/src/RpgGame.Api/Services/HttpCurrentUserAccessor.cs
public class HttpCurrentUserAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentUserAccessor
{
    public string? UserId =>
        httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value;
}
```

3. Register in `Program.cs` (add near the other service registrations):
```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor, HttpCurrentUserAccessor>();
```

4. Inject `ICurrentUserAccessor` into both controllers via constructor. Replace all 7 occurrences of the 3-line pattern with:
```csharp
var userId = _user.UserId;
if (userId is null) return Unauthorized();
```

**Acceptance criteria:**
- All 7 `FindFirst` patterns replaced with `_user.UserId`
- `ICurrentUserAccessor` is registered in DI
- `dotnet build` succeeds
- No behavioral changes to any endpoint

---

### 1.3 Extract `IRedisGameState`

**Priority:** High — prerequisite for any server unit test
**Files to modify:** `GameHub.cs`, `Program.cs`
**Files to create:** `server/src/RpgGame.Api/Services/IRedisGameState.cs`, `server/src/RpgGame.Api/Services/RedisGameState.cs`
**Estimated scope:** 2 new files, 2 files modified heavily

**Problem:** `IConnectionMultiplexer` is injected directly into `GameHub` and `_redis.GetDatabase()` is called inline throughout all five hub methods (~18 Redis operations). There is no abstraction, making it impossible to swap in an in-memory implementation for tests.

**All Redis operations currently in GameHub:**

| Operation | Redis Key Pattern | Method | Used in |
|-----------|------------------|--------|---------|
| Read all players | `session:{sessionId}:players` | `HashGetAllAsync` | JoinSession, StartEncounter |
| Write player entry | `session:{sessionId}:players` | `HashSetAsync` | JoinSession |
| Write active encounter | `session:{sessionId}:active_encounter` | `StringSetAsync` | StartEncounter, EndTurn |
| Read active encounter | `session:{sessionId}:active_encounter` | `StringGetAsync` | DeclareAttack, MoveToken, EndTurn |
| Write map | `encounter:map:{encounterId}` | `StringSetAsync` | StartEncounter, MoveToken |
| Read map | `encounter:map:{encounterId}` | `StringGetAsync` | MoveToken |
| Seed character live state | `character:live:{characterId}` | `HashSetAsync` | StartEncounter |
| Decrement HP | `character:live:{characterId}` | `HashDecrementAsync` | DeclareAttack |
| Set HP to 0 | `character:live:{characterId}` | `HashSetAsync` | DeclareAttack |
| Check defeated | `encounter:{encounterId}:defeated` | `SetContainsAsync` | DeclareAttack, EndTurn |
| Add defeated | `encounter:{encounterId}:defeated` | `SetAddAsync` | DeclareAttack |

**Implementation:**

1. Create `IRedisGameState` with one method per logical operation (not one per Redis call). Group by domain concept:
```csharp
public interface IRedisGameState
{
    // Player presence
    Task<Dictionary<string, PlayerEntry>> GetPlayersAsync(Guid sessionId);
    Task SetPlayerAsync(Guid sessionId, string odUserId, PlayerEntry entry);

    // Active encounter
    Task<ActiveEncounterEntry?> GetActiveEncounterAsync(Guid sessionId);
    Task SetActiveEncounterAsync(Guid sessionId, ActiveEncounterEntry encounter);

    // Map
    Task<MapEntry?> GetMapAsync(Guid encounterId);
    Task SetMapAsync(Guid encounterId, MapEntry map);

    // Character live state
    Task SeedCharacterAsync(Guid characterId, int currentHp, string characterClass);
    Task<long> DecrementHpAsync(Guid characterId, int amount);
    Task SetHpAsync(Guid characterId, int hp);

    // Defeated tracking
    Task<bool> IsDefeatedAsync(Guid encounterId, string combatantId);
    Task AddDefeatedAsync(Guid encounterId, string combatantId);
}
```

2. Create `RedisGameState` that implements the interface using `IConnectionMultiplexer`. Move the existing Redis calls from GameHub into this class.

3. Register in `Program.cs`:
```csharp
builder.Services.AddScoped<IRedisGameState, RedisGameState>();
```

4. Replace `IConnectionMultiplexer` injection in `GameHub` with `IRedisGameState`. Replace all inline Redis calls with the new interface methods.

**Note:** The private record types `PlayerEntry`, `ActiveEncounterEntry`, `Position`, `MapEntry`, `CombatantRef` currently defined inside `GameHub` will need to move to a shared location (e.g., a `Models/` folder or into the interface file) so both `GameHub` and `RedisGameState` can reference them.

**Acceptance criteria:**
- `GameHub` no longer references `IConnectionMultiplexer` or `IDatabase`
- All Redis operations go through `IRedisGameState`
- `dotnet build` succeeds
- No behavioral changes (full game loop still works)

---

### 1.4 Extract `IEncounterService` / `ICombatService`

**Priority:** High — primary unlock for server unit tests
**Depends on:** 1.3 (IRedisGameState must exist first)
**Files to modify:** `GameHub.cs`, `Program.cs`
**Files to create:** Service interfaces and implementations in `server/src/RpgGame.Api/Services/`
**Estimated scope:** 4–6 new files, GameHub reduced from ~574 lines to ~150

**Problem:** All business logic (~400 lines) lives in `GameHub`. The hub does validation, DB queries, Redis mutations, damage calculation, initiative rolling, and broadcasting — all in the same methods.

**Implementation:** Extract into two services:

**`IEncounterService`** — handles encounter lifecycle:
```csharp
public interface IEncounterService
{
    Task<EncounterStartResult> StartEncounterAsync(Guid sessionId, string gmUserId);
    Task<MoveResult> MoveTokenAsync(Guid sessionId, string userId, string combatantId, int targetX, int targetY);
    Task<EndTurnResult> EndTurnAsync(Guid sessionId, string userId);
}
```

**`ICombatService`** — handles attack resolution:
```csharp
public interface ICombatService
{
    Task<AttackResult> DeclareAttackAsync(Guid sessionId, string userId, string targetCombatantId);
}
```

Each method should:
1. Perform all validation (turn check, bounds check, defeated check)
2. Execute DB queries and Redis mutations
3. Return a result DTO that tells the hub what to broadcast

The hub methods become thin orchestrators:
```csharp
public async Task StartEncounter(string joinCode)
{
    var session = await ValidateJoinCode(joinCode);
    var result = await _encounterService.StartEncounterAsync(session.Id, Context.UserIdentifier!);
    await Clients.Group($"session:{session.Id}").SendAsync("EncounterStarted", result.Payload);
}
```

Move the static helper methods (`RollD20`, `RollDamage`, `DexModifier`) into the service implementations.

**Acceptance criteria:**
- `GameHub` methods are each ≤20 lines: validate → call service → broadcast
- All business logic lives in service classes
- Services are registered in DI
- `dotnet build` succeeds
- No behavioral changes

---

## Tier 2 — Contracts & Type Safety

### 2.1 Finish the shared contracts folder

**Priority:** Medium — builds on the `/contracts` folder already started
**Files to modify:** `client/src/app/contracts/hub-contracts.ts`
**Files to create or update:** `/contracts/hub-events.json` or similar
**Estimated scope:** varies by approach

**Problem:** The `/contracts` folder currently only contains `openapi.json`. REST API types are auto-generated in `client/src/app/contracts/api-contracts.generated.ts` (working). But `client/src/app/contracts/hub-contracts.ts` (72 lines) is hand-maintained with a comment saying it must be manually synced to C# DTOs in `server/src/RpgGame.Api/Contracts/Hub/`.

**Current hub-contracts.ts interfaces:**
- `PlayerPresenceChangedPayload` — userId, displayName, characterId, isOnline
- `CombatantDto` — id, userId, characterId, characterName, initiative, initiativeRoll, dexModifier, currentHp, maxHp
- `PositionDto` — x, y
- `MapDto` — gridWidth, gridHeight, positions, fogOfWar
- `EncounterStartedPayload` — encounterId, activeTurnCombatantId, combatants[], map
- `TokenMovedPayload` — combatantId, x, y
- `TurnAdvancedPayload` — activeTurnCombatantId
- `AttackResolvedPayload` — attackerId, attackerName, targetId, targetName, damage, targetNewHp, feedText
- `CombatantDefeatedPayload` — combatantId
- `ActionRejectedPayload` — reason

**C# hub DTOs live in:** `server/src/RpgGame.Api/Contracts/Hub/` (9 record files matching the interfaces above)

**Recommended approach (Option A — lowest effort):** Use `swagger-typescript-api` or `openapi-typescript` to also cover hub payloads by adding a dummy controller that exposes the hub payload types in the OpenAPI spec, OR simply add a build script that uses reflection to generate TypeScript from the C# records.

**Alternative (Option B — pragmatic):** Create a `/contracts/hub-payloads.schema.json` as the single source of truth for hub payload shapes. Generate both C# records and TS interfaces from it.

**Acceptance criteria:**
- Hub payload types are generated (not hand-maintained) in at least one direction
- Adding a new field to a hub payload doesn't require manual edits in two places

---

### 2.2 Replace magic string event names with constants

**Priority:** Medium — eliminates silent typo failures
**Files to modify:** `game-hub.service.ts`, `GameHub.cs`
**Files to create:** Constants file(s)
**Estimated scope:** ~30 lines changed across 2–3 files

**Problem:** All 7 SignalR event names are bare string literals on both sides. A typo compiles and runs but silently fails at runtime.

**Server event names (in `GameHub.cs`):**
```csharp
await Clients.Group(groupName).SendAsync("PlayerPresenceChanged", ...);
await Clients.Group(groupName).SendAsync("EncounterStarted", ...);
await Clients.Group(groupName).SendAsync("TokenMoved", ...);
await Clients.Group(groupName).SendAsync("TurnAdvanced", ...);
await Clients.Caller.SendAsync("ActionRejected", ...);
await Clients.Group(groupName).SendAsync("AttackResolved", ...);
await Clients.Group(groupName).SendAsync("CombatantDefeated", ...);
```

**Client event names (in `game-hub.service.ts`):**
```typescript
this.connection.on('PlayerPresenceChanged', ...);   // line 33
this.connection.on('EncounterStarted', ...);         // line 38
this.connection.on('TokenMoved', ...);               // line 57
this.connection.on('TurnAdvanced', ...);             // line 62
this.connection.on('ActionRejected', ...);           // line 67
this.connection.on('AttackResolved', ...);           // line 72
this.connection.on('CombatantDefeated', ...);        // line 80
```

**Hub method names (client → server, in `game-hub.service.ts`):**
```typescript
this.connection.invoke('JoinSession', ...)       // line 99
this.connection.invoke('StartEncounter', ...)     // line 103
this.connection.invoke('MoveToken', ...)          // line 107
this.connection.invoke('EndTurn', ...)            // line 111
this.connection.invoke('DeclareAttack', ...)      // line 115
```

**Implementation:**

1. **Server:** Create a static class:
```csharp
// server/src/RpgGame.Api/Hubs/HubEvents.cs
public static class HubEvents
{
    public const string PlayerPresenceChanged = nameof(PlayerPresenceChanged);
    public const string EncounterStarted = nameof(EncounterStarted);
    public const string TokenMoved = nameof(TokenMoved);
    public const string TurnAdvanced = nameof(TurnAdvanced);
    public const string ActionRejected = nameof(ActionRejected);
    public const string AttackResolved = nameof(AttackResolved);
    public const string CombatantDefeated = nameof(CombatantDefeated);
}
```
Replace all `SendAsync("EventName", ...)` calls in `GameHub.cs` with `SendAsync(HubEvents.EventName, ...)`.

2. **Client:** Create a constants file:
```typescript
// client/src/app/contracts/hub-events.ts
export const HubEvents = {
  PlayerPresenceChanged: 'PlayerPresenceChanged',
  EncounterStarted: 'EncounterStarted',
  TokenMoved: 'TokenMoved',
  TurnAdvanced: 'TurnAdvanced',
  ActionRejected: 'ActionRejected',
  AttackResolved: 'AttackResolved',
  CombatantDefeated: 'CombatantDefeated',
} as const;

export const HubMethods = {
  JoinSession: 'JoinSession',
  StartEncounter: 'StartEncounter',
  MoveToken: 'MoveToken',
  EndTurn: 'EndTurn',
  DeclareAttack: 'DeclareAttack',
} as const;
```
Replace all string literals in `game-hub.service.ts` with references to these constants.

**Acceptance criteria:**
- Zero bare string event/method names remain in `GameHub.cs` or `game-hub.service.ts`
- `dotnet build` and `npm run build` succeed
- No behavioral changes

---

### 2.3 Fix `CharacterId` string/Guid type mismatch

**Priority:** Medium — 4 inline `Guid.TryParse` calls
**Files to modify:** `GameHub.cs`
**Estimated scope:** ~20 lines changed

**Problem:** `characterId` arrives as `string?` at the SignalR boundary and is parsed via `Guid.TryParse` in 4 locations:
- `JoinSession` (line 59)
- `StartEncounter` (line 157, inside player loop)
- `DeclareAttack` (line 333, encounter ID)
- `DeclareAttack` (line 340, target ID)

**Implementation:** Create a private helper method in GameHub:
```csharp
private static Guid ParseGuidOrThrow(string? value, string paramName)
{
    if (!Guid.TryParse(value, out var guid))
        throw new HubException($"Invalid {paramName}.");
    return guid;
}
```
Replace all 4 inline `Guid.TryParse` blocks with calls to this helper.

**Note:** If item 1.4 (service extraction) is done first, this helper should live in the service layer instead.

**Acceptance criteria:**
- No inline `Guid.TryParse` remains in hub methods
- Invalid GUIDs still produce `HubException` (same behavior)
- `dotnet build` succeeds

---

## Tier 3 — Data Integrity & Performance

### 3.1 Fix N+1 queries

**Priority:** Medium — performance bug under load
**Files to modify:** `GameHub.cs` (or service classes if 1.4 is done first)
**Estimated scope:** ~30 lines changed

**Problem 1 — `StartEncounter` (line 160):**
```csharp
// Inside foreach over playerEntries:
var character = await _db.Characters.FirstOrDefaultAsync(c => c.Id == charGuid);
```
One DB round-trip per player. With 6 players, that's 6 queries.

**Fix:** Before the loop, collect all character IDs, then batch:
```csharp
var charIds = playerEntries
    .Select(e => /* parse characterId from entry */)
    .Where(id => id != Guid.Empty)
    .ToList();
var characters = await _db.Characters
    .Where(c => charIds.Contains(c.Id))
    .ToDictionaryAsync(c => c.Id);
```
Then inside the loop, use `characters.TryGetValue(charGuid, out var character)`.

**Problem 2 — `DeclareAttack` (lines 340–354):**
```csharp
var attackerDbCombatant = await _db.EncounterCombatants
    .FirstOrDefaultAsync(c => c.EncounterId == encounterGuid && c.UserId == userId);
var attackerChar = attackerDbCombatant is not null
    ? await _db.Characters.FirstOrDefaultAsync(c => c.Id == attackerDbCombatant.CharacterId)
    : null;
var targetDbCombatant = await _db.EncounterCombatants
    .FirstOrDefaultAsync(c => c.Id == targetCombatantGuid);
var targetChar = targetDbCombatant is not null
    ? await _db.Characters.FirstOrDefaultAsync(c => c.Id == targetDbCombatant.CharacterId)
    : null;
```
4 sequential queries.

**Fix:** Use a single query with Include or join:
```csharp
var combatants = await _db.EncounterCombatants
    .Where(c => c.EncounterId == encounterGuid &&
                (c.UserId == userId || c.Id == targetCombatantGuid))
    .ToListAsync();
var attacker = combatants.FirstOrDefault(c => c.UserId == userId);
var target = combatants.FirstOrDefault(c => c.Id == targetCombatantGuid);
```
If you need the Character entities too, add `.Include(c => c.Character)` or load them in a second batch query.

**Acceptance criteria:**
- `StartEncounter` makes exactly 1 character query regardless of player count
- `DeclareAttack` makes at most 2 queries (down from 4)
- `dotnet build` succeeds
- No behavioral changes

---

### 3.2 Wrap Redis+DB operations in transaction scope

**Priority:** Medium — data consistency bug
**Files to modify:** `GameHub.cs` `DeclareAttack` method (or `CombatService` if 1.4 is done)
**Estimated scope:** ~15 lines changed

**Problem:** In `DeclareAttack`, HP is decremented in Redis (line 368) before the `GameEvent` is persisted with `SaveChangesAsync` (line 401). If the DB write fails, Redis shows reduced HP with no corresponding event log entry.

**Implementation:** Reorder operations so DB writes commit first:
1. Calculate damage
2. Write `GameEvent` to DB and call `SaveChangesAsync`
3. Only then decrement HP in Redis and add to defeated set

If the DB write fails, Redis is never mutated and state stays consistent. Wrap the Redis mutations in a try-catch; if Redis fails after DB success, log a warning (the event log is the source of truth and can be replayed).

**Acceptance criteria:**
- DB write happens before Redis mutation in `DeclareAttack`
- If `SaveChangesAsync` throws, Redis HP is not decremented
- `dotnet build` succeeds
- No behavioral changes in the happy path

---

### 3.3 Move credentials out of `AppDbContextFactory`

**Priority:** Medium — security hygiene
**File to modify:** `server/src/RpgGame.Infrastructure/Data/AppDbContextFactory.cs`
**Estimated scope:** ~5 lines changed

**Current code (line 11):**
```csharp
.UseNpgsql("Host=localhost;Port=5432;Database=rpg_game;Username=rpguser;Password=changeme")
```

**Implementation:** Read from environment variable with fallback:
```csharp
var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
    ?? "Host=localhost;Port=5432;Database=rpg_game;Username=rpguser;Password=changeme";
return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(connectionString)
    .Options);
```

This keeps `dotnet ef migrations add` working locally (falls back to default) while allowing CI/production to override via environment variable. The hardcoded fallback is acceptable for a design-time factory that only runs during development migration tooling.

**Acceptance criteria:**
- `AppDbContextFactory` reads connection string from env var first
- `dotnet ef migrations add TestMigration --startup-project ../RpgGame.Api` still works
- Delete the test migration after verifying

---

## Tier 4 — Client Refactors

### 4.1 Split `LobbyComponent`

**Priority:** Medium — God Component with 6 concerns
**File to modify:** `client/src/app/features/lobby/lobby.component.ts` and its template/styles
**Files to create:** 3 new components
**Estimated scope:** ~200 lines moved, ~80 lines new glue code

**Problem:** `lobby.component.ts` (281 lines) handles auth, player join, GM flow, and encounter display in one class.

**Split plan:**

1. **`AuthPanelComponent`** — extracted from template lines 30–39 (the `*ngIf="!auth.isLoggedIn()"` block)
   - Owns: register form, login form, quick-register button
   - Injects: `AuthService`, `DevService`
   - Emits: `(authenticated)` event when login/register succeeds

2. **`PlayerJoinComponent`** — extracted from template lines 50–62 (the `*ngIf="activeFlow === 'player'"` block)
   - Owns: join code input, character select dropdown
   - Injects: `GameHubService`, `CharacterService`, `Store`
   - Emits: `(joined)` event

3. **`GmFlowComponent`** — extracted from template lines 65–95 (the `*ngIf="activeFlow === 'gm'"` block)
   - Owns: campaign name input, create campaign button, start session button, start encounter button, step progression state
   - Injects: `CampaignService`, `GameHubService`, `Store`

4. **`LobbyComponent`** becomes a thin shell (~50 lines) that composes the three child components and the encounter display section.

Generate all components with `ng generate component features/lobby/auth-panel`, etc. Move the relevant template sections, styles, and TypeScript methods into each new component.

**Acceptance criteria:**
- `LobbyComponent` is ≤80 lines
- Each extracted component handles exactly one concern
- `npm run build` succeeds
- Auth → join/GM → encounter flow still works end-to-end

---

### 4.2 Remove `store.snapshot()` from `GridComponent`

**Priority:** Medium — stale state anti-pattern
**File to modify:** `client/src/app/features/encounter/grid/grid.component.ts`
**Estimated scope:** ~20 lines changed

**Problem:** Lines 185–187 use `store.snapshot()` to synchronously read `activeTurnCombatantId`:
```typescript
const state = this.store.snapshot();
const activeTurnId = state.encounter?.activeTurnCombatantId;
if (myCombatant.id !== activeTurnId) return;
```

The component also keeps private sync copies (lines 117–119) updated via subscriptions (lines 163–165):
```typescript
private combatants: Combatant[] = [];
private positions: Record<string, Position> = {};
private pendingMove: Position | null = null;
```

**Implementation:**
1. Add `activeTurnCombatantId` to the private sync variables (alongside the existing `combatants`, `positions`, `pendingMove`)
2. Add a subscription: `this.store.select(EncounterState.activeTurnCombatantId).subscribe(id => this.activeTurnId = id);`
3. Replace the `store.snapshot()` call with `this.activeTurnId`
4. Remove the `store.snapshot()` import if no longer used

**Ideal approach (if time allows):** Replace all private sync copies with a single combined observable using `combineLatest`, but the minimal fix above is sufficient.

**Acceptance criteria:**
- `store.snapshot()` is not called anywhere in `GridComponent`
- Turn validation still works correctly
- `npm run build` succeeds

---

### 4.3 Remove `fogOfWar: never[]` dead code

**Priority:** Low — 2-minute cleanup
**Files to modify:** `client/src/app/state/map/map.state.model.ts`, `client/src/app/state/map/map.state.ts`
**Estimated scope:** ~3 lines removed

**Problem:** `fogOfWar: never[]` can only ever be an empty array. No selector reads it, no action writes to it.

**Implementation:** Remove the `fogOfWar` field from `MapStateModel` and any initializer that sets it to `[]`. If `MapDto` in `hub-contracts.ts` includes `fogOfWar`, leave it there (the server sends it) — just don't store it in NGXS state.

**Acceptance criteria:**
- `fogOfWar` removed from `MapStateModel`
- `npm run build` succeeds

---

### 4.4 Fix `DevService` noun-splice bug

**Priority:** Low — cosmetic bug
**File to modify:** `client/src/app/core/services/dev.service.ts`
**Estimated scope:** 3 lines changed

**Current code (line 35):**
```typescript
name: this.pick(this.fantasyNames) + ' ' + this.pick(this.nouns).charAt(0).toUpperCase() + this.pick(this.nouns).slice(1),
```

`this.pick(this.nouns)` is called independently for the first character and the remaining characters, producing a surname spliced from two different nouns.

**Fix:**
```typescript
const surname = this.pick(this.nouns);
name: this.pick(this.fantasyNames) + ' ' + surname.charAt(0).toUpperCase() + surname.slice(1),
```

**Acceptance criteria:**
- Generated character surnames are coherent single words
- `npm run build` succeeds

---

## Tier 5 — Add Tests

### 5.1 Create server test project

**Priority:** High (but depends on Tier 1 refactors for most value)
**Files to create:** `server/tests/RpgGame.Api.Tests/RpgGame.Api.Tests.csproj` and test files
**Estimated scope:** New project + initial test files

**Implementation:**
```bash
cd server
mkdir -p tests/RpgGame.Api.Tests
cd tests/RpgGame.Api.Tests
dotnet new xunit
dotnet add reference ../../src/RpgGame.Api/RpgGame.Api.csproj
dotnet add package Moq
cd ../..
dotnet sln add tests/RpgGame.Api.Tests/RpgGame.Api.Tests.csproj
```

**Phase A tests (pure logic, no infra — possible after 1.4):**
- `CombatServiceTests` — `RollDamage("fighter")` returns 1–8; `RollDamage("rogue")` returns 1–6; `RollDamage("wizard")` returns 1–4
- `CombatServiceTests` — Chebyshev distance ≤6 accepted, >6 rejected
- `CombatServiceTests` — Initiative = d20 roll + dex modifier, always in valid range

**Phase B tests (mocked deps — possible after 1.3 + 1.4):**
- `EncounterServiceTests` — `StartEncounter` rejects non-GM user
- `EncounterServiceTests` — `EndTurn` with all defeated does not select defeated combatant
- `CombatServiceTests` — `DeclareAttack` rejects attack when not caller's turn
- `EncounterServiceTests` — `MoveToken` rejects Chebyshev > 6

**Acceptance criteria:**
- `dotnet test` runs and all tests pass
- Tests use Moq for `IRedisGameState` and `AppDbContext` (use in-memory EF provider)

---

### 5.2 Client NGXS state tests

**Priority:** High — easiest, highest value client tests; no refactors needed
**Files to create:** One `.spec.ts` file per state slice
**Estimated scope:** 4 new test files, ~40 tests total

**Implementation:** Use `NgxsTestingModule` (no HTTP or component dependencies):

```typescript
// Example: encounter.state.spec.ts
beforeEach(() => {
  TestBed.configureTestingModule({
    imports: [NgxsModule.forRoot([EncounterState])]
  });
  store = TestBed.inject(Store);
});

it('should sort combatants by initiative on EncounterStarted', () => {
  store.dispatch(new EncounterStarted({ ... }));
  const combatants = store.selectSnapshot(EncounterState.combatants);
  expect(combatants[0].initiative).toBeGreaterThanOrEqual(combatants[1].initiative);
});
```

**Test cases per state:**

**EncounterState:**
- `EncounterStarted` sets encounterId, activeTurnCombatantId, and sorts combatants by initiative descending
- `AttackResolved` decrements `currentHp` on the correct combatant
- `CombatantDefeated` sets `isDefeated: true` on the correct combatant
- `TurnAdvanced` updates `activeTurnCombatantId`
- `ClearEncounter` resets to defaults

**MapState:**
- `MapInitialized` sets gridWidth, gridHeight, positions
- `TokenMoved` updates position for correct combatant ID
- `SetPendingMove` stores pending position
- `ClearPendingMove` clears it
- `TokenMoved` clears `pendingMove` if it matches

**SessionState:**
- `PlayerPresenceChanged` with online=true adds/updates player
- `PlayerPresenceChanged` with online=false removes player
- `SetGmSession` sets isGm, campaignId, etc.

**FeedState:**
- `AddFeedEntry` prepends entry and increments `unreadCount`
- `MarkRead` resets `unreadCount` to 0
- `ClearFeed` empties entries

**Acceptance criteria:**
- `npm test` runs and all state tests pass
- Each state slice has at least 3 tests covering its key actions

---

## Reference: Code Smell Inventory

This section preserves the original audit for context. Items marked with a task number reference the actionable item above.

### Server

| Severity | Issue | Task |
|----------|-------|------|
| Critical | EndTurn all-defeated bug | 1.1 |
| Critical | Magic numbers/strings scattered | 2.2, 2.3 |
| Critical | Credentials in `AppDbContextFactory` | 3.3 |
| High | GameHub God methods (574 lines) | 1.4 |
| High | No service layer | 1.4 |
| High | No repository pattern | — (defer) |
| High | Redis access not abstracted | 1.3 |
| High | Duplicate userId extraction ×7 | 1.2 |
| Medium | N+1 queries | 3.1 |
| Medium | No Redis+DB transaction | 3.2 |
| Medium | CharacterId string/Guid mismatch | 2.3 |

### Client

| Severity | Issue | Task |
|----------|-------|------|
| Critical | LobbyComponent God Component | 4.1 |
| High | GameHubService SRP violation | — (defer) |
| High | GridComponent store.snapshot() | 4.2 |
| High | No error handling on hub invocations | — (defer) |
| Medium | Magic string event names | 2.2 |
| Medium | Magic string API endpoints | — (defer) |
| Medium | AuthService fragile JWT parsing | — (defer) |
| Medium | fogOfWar dead code | 4.3 |
| Medium | DevService noun-splice bug | 4.4 |
