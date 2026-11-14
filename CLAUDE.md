# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Current Status

Phase 3A (simultaneous planning → tick-based resolution → GM enemy planning) is **complete and merged**. The old sequential initiative model (`MoveToken` / `DeclareAttack` / `EndTurn`) is gone. Next roadmap slice: **3B.1 Custom Stats**. See `docs/roadmap/ROADMAP_STRATEGIC.md`.

## Documentation

### Design
- `docs/design/GAME_DESIGN.md` — Core rules & design philosophy; tactics-first, setting-agnostic system. Start here for any design question.
- `docs/design/DESIGN_SPIKE.md` — Locked decisions for Phase 3A: conflict resolution model, mutual kills, HP netting, damage pipeline, placeholder stats.
- `docs/design/TICK_RESOLUTION.md` — Tick-based resolution loop, path-based movement, action queue schema, opportunity attacks. Supersedes the single-pass phase model in DESIGN_SPIKE.
- `docs/design/MAGIC_SYSTEM.md` — Environmental magic layer: ley points, resonance, magic pipeline. Defers internal character magic sources to post-playtesting.

### Workflow & Testing
- `docs/DEVELOPMENT_WORKFLOW.md` — Local dev guidance; which services to run in containers vs on the host.
- `docs/smoke-test.md` — Automated E2E (Playwright) + manual two-browser smoke test for the current planning/resolution loop.
- `docs/CODE_QUALITY.md` — Historical refactoring checklist (all items done; kept for context).

### Roadmap
- `docs/roadmap/ROADMAP_STRATEGIC.md` — High-level plan: what to build, in what order, data contracts per slice.
- `docs/roadmap/SPRINT_3A.md` — Detailed sprint doc for Phase 3A (complete; reference for how slices are specified).

## Commands

### Full Stack (Docker — local dev)
```bash
docker compose up --build        # Start all services (applies docker-compose.override.yml dev settings)
docker compose up                # Start without rebuilding
docker compose down              # Stop all services
```

### Production (Docker Compose)
```bash
# Runs on the DigitalOcean droplet at /opt/rpg/
docker compose -f docker-compose.prod.yml up -d   # Start/update
docker image prune -f                              # Clean old images
```
Images are pulled from GHCR (`ghcr.io/nathanarvan/rpg-api:latest`, `ghcr.io/nathanarvan/rpg-client:latest`).
Copy `.env.example` → `.env` and fill in secrets before first run.

### Client (Angular 19)
```bash
cd client
npm ci --legacy-peer-deps        # Install deps (required flag for NGXS peer dep mismatch)
npm start                        # Dev server on http://localhost:4200 (proxies /api + /hubs to http://localhost:5291)
npm run build                    # Production build → dist/client/browser/
npm test -- --watch=false --browsers=FirefoxHeadless   # Karma/Jasmine unit tests (only Firefox launcher is installed)
npm run contracts:hub            # Regenerate hub-contracts.generated.ts from C# records
```

**Angular code generation:** Always use the Angular CLI (`ng generate`) to scaffold new Angular artifacts — components, services, interceptors, guards, pipes, directives, state classes, etc. Do not write these files by hand. Run `ng generate` from the `client/` directory (e.g., `npx ng generate component foo/bar`).

**Component files:** All components must use separate template and style files (`templateUrl` + `styleUrl`). Never use inline `template:` or `styles:` in the `@Component` decorator.

### Server (.NET 8)
```bash
cd server
dotnet build
dotnet watch --project src/RpgGame.Api   # Host API on http://localhost:5291 with hot reload
dotnet test                              # xUnit tests in server/tests/RpgGame.Api.Tests

# EF migrations (from server/src/RpgGame.Infrastructure/)
dotnet ef migrations add <Name> --startup-project ../RpgGame.Api
dotnet ef database update --startup-project ../RpgGame.Api

# Regenerate OpenAPI spec (requires swashbuckle CLI, installed as dotnet tool)
dotnet swagger tofile --output ../contracts/openapi.json src/RpgGame.Api/bin/Debug/net8.0/RpgGame.Api.dll v1
```
Note: `dotnet build` of the API also rewrites `contracts/openapi.json` via a post-build step.

### End-to-End Tests (Playwright)
```bash
docker compose up -d --build     # or run API + client on the host; anything serving the app on :4200
cd e2e
npm ci
npx playwright install chromium  # first time only
npm test                         # E2E_BASE_URL overrides the default http://localhost:4200
```
The E2E suite drives two isolated browser contexts (GM + player) through the full loop: register → campaign → session → join → add monster → start encounter → plan → lock in → resolution on both screens. Run it after any change that crosses the client/server boundary (hub methods, contracts, NGXS handlers, templates).

## Architecture

### Layers (Server)
Clean Architecture with three projects:
- **RpgGame.Domain** — Entities only (no EF, no Identity). All game entities live here.
- **RpgGame.Infrastructure** — EF `AppDbContext` (IdentityDbContext<AppUser>), `AppUser`, migrations. Depends on Domain.
- **RpgGame.Api** — Controllers, SignalR Hubs, services, `Program.cs`. Depends on both layers.

`AppUser` intentionally lives in Infrastructure (not Domain) because it extends `IdentityUser`.

### Domain Entities
- **Campaign** — Id, Name, CreatedAt; relations: Members[], Sessions[]
- **CampaignMember** — CampaignId, UserId, Role (Gm|Player); unique index on (CampaignId, UserId)
- **GameSession** — Id, CampaignId, JoinCode (6-char alphanumeric, unique), StartedAt
- **Character** — Id, UserId, Name, Race, Class, Level, BaseStats (JSONB: Str/Dex/Con/Int/Wis/Cha), MaxHp, CurrentHp (D&D-style stats until 3B.1)
- **Encounter** — Id, GameSessionId, Status (Active|Ended), StartedAt, RoundNumber; unique-active index on GameSessionId
- **EncounterCombatant** — Id, EncounterId, UserId, CharacterId (nullable — null for monsters), CharacterName, CombatantType (`player` | `monster`)
- **GameEvent** — Id, EncounterId, OccurredAt, EventType (string), Payload (JSONB); append-only log

### API Routing Convention

All REST controllers use the `api/` prefix directly in their `[Route]` attribute (e.g. `[Route("api/auth")]`). This means the URL `/api/auth/register` is the actual route on the server — no path rewriting anywhere. The nginx proxy and the Angular dev proxy both forward `/api/` to the API without stripping the prefix. **Never use `[Route("auth")]` without the `api/` prefix — it will work in Docker (where nginx historically rewrote paths) but break the Angular dev proxy and any direct curl/Swagger calls.**

### API Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| POST | `/api/auth/register` | Register user, returns JWT |
| POST | `/api/auth/token` | Login, returns JWT |
| POST | `/api/campaigns` | Create campaign (GM becomes first member) |
| POST | `/api/campaigns/{id}/sessions` | Start session → returns `{ joinCode, gameSessionId }` |
| POST | `/api/characters` | Create character |
| GET | `/api/characters/mine` | List caller's characters |
| GET | `/api/characters/{id}` | Get character |
| PUT | `/api/characters/{id}` | Update character |
| DELETE | `/api/characters/{id}` | Delete character |

### Auth Flow
1. Client POSTs to `/api/auth/register` or `/api/auth/token` → receives JWT
2. JWT stored in `sessionStorage` by `AuthService`
3. HTTP `AuthInterceptor` adds `Authorization: Bearer {token}` to all requests
4. SignalR connection passes JWT via `accessTokenFactory` → query string `?access_token=<token>`
5. `Program.cs` JwtBearer `OnMessageReceived` reads from query string

### Combat Loop (Phase 3A)
1. **Lobby** — GM creates campaign → starts session → connects; GM can `AddMonster` (staged in Redis until the encounter starts). Players join with a character.
2. **StartEncounter** — creates Encounter + Combatants, places players on row 0 and monsters on row 19, broadcasts `EncounterStarted` + `PlanningPhaseStarted`, starts the planning timer (`GameConstants.PlanningTimerSeconds`, currently 300s).
3. **Planning** — each player plans one action (`move` | `melee_attack` | `ranged_attack` | `defend`) as a `PlannedActionArray` of length 1 and locks in. GM plans each monster (`PlanEnemyAction`) and locks in all enemies at once (`LockInEnemies`; unplanned monsters defend).
4. **Resolution** — when every living combatant is locked in (or GM `ForceResolve`, or timer expiry), `ResolutionService` runs the tick loop (Movement → Attacks → Effects), writes Redis + `GameEvent`s, broadcasts `ResolutionCompleted` (which carries `defeatedCombatantIds`), then the next `PlanningPhaseStarted`.

**Known gap:** there is no end-of-encounter yet. `EncounterEnded` and `CombatantDefeated` exist as contracts and client handlers, but the server never broadcasts them — rounds keep cycling after one side is wiped out.

### Real-Time (SignalR + Redis)
- Hub at `/hubs/game`, requires `[Authorize]`
- Redis serves dual purpose: SignalR backplane AND live state
- Group naming: `session:{sessionId}` (UUID, not the join code)
- Method/event names are constants in `Contracts/Hub/HubEvents.cs` (mirrored in the generated TS contracts)

**Hub methods (client → server):**

| Method | Auth | Description |
|--------|------|-------------|
| `JoinSession(joinCode, displayName, characterId?)` | Any | Validates join code, adds to Redis, broadcasts `PlayerPresenceChanged` |
| `AddMonster(joinCode, monsterName)` | GM only | Stages a monster pre-encounter (or spawns one mid-encounter); broadcasts `MonsterAdded` |
| `StartEncounter(joinCode)` | GM only | Creates encounter + combatants, seeds map/HP, starts round 1 planning |
| `PlanAction(joinCode, plannedAction)` | Player (own combatant) | Validates and stores the caller's planned action |
| `LockIn(joinCode)` | Player | Locks in; broadcasts `PlayerLockedIn`; triggers resolution if everyone is locked in |
| `PlanEnemyAction(joinCode, combatantId, plannedAction)` | GM only | Stores a planned action for one monster |
| `LockInEnemies(joinCode)` | GM only | Locks in all living monsters (unplanned → defend) |
| `ForceResolve(joinCode)` | GM only | Resolves immediately; unplanned combatants defend |

**Server → client broadcasts:**
`PlayerPresenceChanged`, `MonsterAdded`, `EncounterStarted`, `PlanningPhaseStarted`, `PlayerLockedIn`, `AllLockedIn`, `ResolutionCompleted`, `ActionRejected`
(`CombatantDefeated` and `EncounterEnded` are defined but currently unused by the server — see Known gap above.)

**Redis key patterns:**

| Key | Type | Contents |
|-----|------|----------|
| `session:{sessionId}:players` | Hash | Connected player presences |
| `session:{sessionId}:pending_monsters` | List | Monster names staged before the encounter starts |
| `session:{sessionId}:active_encounter` | JSON | `encounterId`, `roundPhase`, `roundNumber`, `planningTimerExpiresAt`, combatants[] |
| `encounter:map:{encounterId}` | JSON | 20×20 grid dims, positions by combatantId |
| `character:live:{id}` | Hash | `currentHp`, `class` — keyed by character ID for players, combatant ID for monsters |
| `encounter:{encounterId}:planned_actions` | Hash | combatantId → `PlannedActionArray` JSON |
| `encounter:{encounterId}:locked_in` | Set | Locked-in combatant IDs |
| `encounter:{encounterId}:defeated` | Set | Defeated combatant IDs |

### Client State (NGXS)
Five state slices, all registered in `app.config.ts`. Redux DevTools + Logger plugins enabled.

| Slice | Key fields |
|-------|-----------|
| `SessionState` (`state/session/`) | `campaignId`, `gameSessionId`, `joinCode`, `isGm`, `status`, `connectedPlayers[]` |
| `CharacterState` (`state/character/`) | `characters[]`, `selected`, `loading` |
| `EncounterState` (`state/encounter/`) | `encounterId`, `active`, `roundPhase`, `roundNumber`, `combatants[]`, `myPlannedActions`, `lockedInCombatantNames`, `planningTimerExpiresAt`, `resolutionTicks`, `gmSelectedEnemyId`, `enemyPlannedActions` |
| `MapState` (`state/map/`) | `gridWidth/Height` (20×20), `positions` (Record<combatantId, {x,y}>) |
| `FeedState` (`state/feed/`) | `entries[]`, `unreadCount` |

### Client Services (`core/services/`)
- **AuthService** — register, login, getToken, getUserId (parses JWT), isLoggedIn, logout
- **CampaignService** — createCampaign, startSession
- **CharacterService** — getMyCharacters, createCharacter, updateCharacter, deleteCharacter
- **GameHubService** — SignalR connection; invokes all hub methods; dispatches NGXS actions on received events (also turns `ResolutionCompleted` into feed entries)
- **DevService** — randomUser(), randomCharacter() helpers for testing
- **AuthInterceptor** — adds Bearer token to all HTTP requests

### Shared Contracts
- `contracts/openapi.json` — OpenAPI 3 spec generated from Swagger; source of truth for REST DTOs
- `client/src/app/contracts/api-contracts.generated.ts` — TypeScript types generated from `openapi.json` via `npm run codegen`
- `client/src/app/contracts/hub-contracts.generated.ts` — TypeScript interfaces generated from C# records in `server/src/RpgGame.Api/Contracts/Hub/` via `npm run contracts:hub` (runs `client/scripts/generate-hub-contracts.js`). Committed to repo. Do not edit by hand.

### Contract Generation
```bash
cd client
npm run codegen        # Regenerate REST types from contracts/openapi.json
npm run contracts:hub  # Regenerate SignalR hub types from C# Hub/ records
```
Run `contracts:hub` after adding or changing any record in `server/src/RpgGame.Api/Contracts/Hub/` (excluding `ServiceResults.cs`). CI fails if the committed generated file is out of date.

### Server Services (`RpgGame.Api/Services/`)
- **ICurrentUserAccessor / HttpCurrentUserAccessor** — abstracts `HttpContext.User` claims into `UserId`/`UserName`; injected into controllers instead of accessing `User` directly
- **IRedisGameState / RedisGameState** — Singleton; encapsulates all Redis reads/writes (players, pending monsters, active encounter, map, live HP, planned actions, lock-ins, defeated set). All hub logic goes through this interface rather than touching `IConnectionMultiplexer` directly.
- **ISessionService / SessionService** — Scoped; `JoinSession` (DB lookup, character validation, campaign member auto-join, Redis player upsert)
- **IEncounterService / EncounterService** — Scoped; `StartEncounterAsync` (DB persist, map/HP seed, round 1 planning) and `AddMonsterAsync`
- **IPlanningService / PlanningService** — Scoped; `PlanAction`, `LockIn`, `ForceResolve`, `PlanEnemyAction`, `LockInEnemies`; decides when all living combatants are locked in
- **IResolutionService / ResolutionService** — Scoped; `ResolveRoundAsync` — the tick loop. Loads state from DB/Redis, runs pure static phase methods (`ResolveMovementPhase`, `ResolveAttackPhase`, `ResolveEffectsPhase`), writes results back, returns `ResolutionPayload`
- **PlanningTimerService** — Singleton; per-encounter countdown (`Task.Delay` + CTS); on expiry resolves via `IHubContext<GameHub>` and starts the next round
- **CombatDice** — `internal static` helper; `DexModifier`, `RollD20`, `RollDamage`, `DamageDiceString`
- **GameActionException** — custom exception with `bool IsActionRejected`; thrown by services. Hub catches `IsActionRejected: true` and sends `ActionRejected` to caller; `IsActionRejected: false` propagates as `HubException`.

`GameHub` is a thin orchestrator — business logic lives in the scoped services above.

### Swagger / OpenAPI
- `Swashbuckle.AspNetCore` generates docs at `/swagger` in Development
- `ResponseTypeOperationFilter` reads `[ProducesResponseType]` attributes to populate response schemas
- `swashbuckle.aspnetcore.cli` installed as a local dotnet tool (`server/.config/dotnet-tools.json`) for spec generation

### Components (`client/src/app/features/`)
- **LobbyComponent** (`/`) — hosts AuthPanel, PlayerJoin, GmFlow and the connected-player list; swaps to CombatLayout when an encounter is active
  - **AuthPanelComponent** — register / login / ⚡ Quick Register
  - **PlayerJoinComponent** — join code + display name + character select
  - **GmFlowComponent** — create campaign → start session → connect → add monsters → start encounter
- **CharacterComponent** (`/character`) — character list + CRUD form; ⚡ Quick Create Random
- **CombatLayoutComponent** — three columns: CombatSidebar (EncounterTracker) | RoundBanner + Grid | Feed
- **EncounterTrackerComponent** — combatant HP table, lock-in checkmarks, defeated state
- **RoundBannerComponent** — round number, phase, timer, lock-in count
- **GridComponent** — 20×20 grid; players click a cell in range to plan a move or a token to plan an attack, Defend / Lock In bar; GM sees GmEnemyPlanner + ⚡ Force Resolve; animates resolution ticks with damage numbers
- **GmEnemyPlannerComponent** — select a monster, plan via grid click or 🛡, Lock In Enemies
- **FeedComponent** — timestamped combat log; unread badge
- **EncounterPreviewComponent** (`/preview`) — offline UI sandbox seeded with mock NGXS state (no server needed); toggle GM/player and planning/resolving

### Nginx Proxy (`client/nginx.conf`)
The Angular container proxies:
- `/api/` → `api:8080`
- `/hubs/` → `api:8080` (with WebSocket upgrade headers)
- All other routes → SPA fallback (`index.html`)

### Environment Variables
Loaded from `.env` at project root (see `.env.example`):
- `DB_USER`, `DB_PASSWORD` — PostgreSQL credentials
- `JWT_SECRET` — must be ≥32 chars for HMAC-SHA256

### Port Mapping

**Local dev (`docker-compose.yml`):**
| Service | External | Internal |
|---------|----------|----------|
| Client (Nginx) | 4200 | 80 |
| API (.NET) | 5000 | 8080 |
| PostgreSQL | 5432 | 5432 |
| Redis | 6379 | 6379 |

Host-run API (`dotnet watch`) listens on 5291; the Angular dev server proxy targets that port.

**Production (`docker-compose.prod.yml`) — bound to 127.0.0.1 (behind reverse proxy):**
| Service | External | Internal |
|---------|----------|----------|
| Client (Nginx) | 4201 | 80 |
| API (.NET) | 5001 | 8080 |
| PostgreSQL | 5433 | 5432 |
| Redis | 6379 | 6379 |

### EF Migrations
Migrations live in `RpgGame.Infrastructure/Data/Migrations/`. They run automatically via `db.Database.MigrateAsync()` on API startup — no manual `dotnet ef database update` needed in Docker. 8 migrations through `MakeCharacterIdNullable`.

### Branching Convention

```
feat/<short-description>     e.g. feat/death-saves
refactor/<short-description> e.g. refactor/extract-redis-game-state
fix/<short-description>      e.g. fix/initiative-roll-bug
```

All branches target `master`. Branch protection requires CI to pass and a PR before merging (no direct pushes). Keep PRs to one roadmap slice — the first Phase 3 attempt merged four slices at once and had to be reverted wholesale (PR #60).

### CI/CD (GitHub Actions)
`.github/workflows/ci.yml` runs on PRs to `master`:
- **build-server** — `dotnet test`
- **test-client** — contract-drift check (`npm run contracts:hub` must produce no diff) + Karma unit tests (Firefox headless)
- **e2e** — builds the full stack with Docker Compose and runs the Playwright suite in `e2e/`; uploads the HTML report on failure

`.github/workflows/deploy.yml` triggers on push to `master`:
1. **build-and-push** — builds API and client Docker images, pushes to GHCR (`ghcr.io/nathanarvan/rpg-api:latest`, `ghcr.io/nathanarvan/rpg-client:latest`)
2. **deploy** — SSHs into the DigitalOcean droplet (`/opt/rpg/`), writes `.env` from GitHub Secrets, pulls latest images, runs `docker compose -f docker-compose.prod.yml up -d`

`.github/workflows/mirror-to-public.yml` mirrors the repo to a public copy.

**Required GitHub Secrets:** `DROPLET_HOST`, `DROPLET_SSH_KEY`, `DB_USER`, `DB_PASSWORD`, `JWT_SECRET`, `GHCR_PAT`

### Tests
- **Server (xUnit + Moq + EF InMemory)** — `server/tests/RpgGame.Api.Tests/`: `CombatDiceTests`, `EncounterServiceTests`, `PlanningServiceTests`, `ResolutionServiceTests`. Redis is mocked via `IRedisGameState`.
- **Client (Karma/Jasmine)** — specs for all NGXS state slices (`state/*/*.state.spec.ts`) plus component smoke specs.
- **E2E (Playwright)** — `e2e/tests/`; see the command section above.
