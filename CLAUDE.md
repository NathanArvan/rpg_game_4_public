# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Documentation

### Design
- `docs/design/GAME_DESIGN.md` — Core rules & design philosophy; tactics-first, setting-agnostic system. Start here for any design question.
- `docs/design/DESIGN_SPIKE.md` — Locked decisions for Phase 3A: conflict resolution model, mutual kills, HP netting, damage pipeline, placeholder stats. Authoritative for 3A implementation.
- `docs/design/TICK_RESOLUTION.md` — Tick-based resolution loop, path-based movement, action queue schema, opportunity attacks. Supersedes the single-pass phase model in DESIGN_SPIKE.
- `docs/design/MAGIC_SYSTEM.md` — Environmental magic layer: ley points, resonance, magic pipeline. Defers internal character magic sources to post-playtesting.

### Roadmap
- `docs/roadmap/ROADMAP_STRATEGIC.md` — High-level plan: what to build, in what order, 

## Commands

### Full Stack (Docker — local dev)
```bash
docker-compose up --build        # Start all services
docker-compose up                # Start without rebuilding
docker-compose down              # Stop all services
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
npm start                        # Dev server on http://localhost:4200
npm run build                    # Production build → dist/client/browser/
npm test                         # Karma/Jasmine unit tests
npm run contracts:hub            # Regenerate hub-contracts.generated.ts from C# records
```

**Angular code generation:** Always use the Angular CLI (`ng generate`) to scaffold new Angular artifacts — components, services, interceptors, guards, pipes, directives, state classes, etc. Do not write these files by hand. Run `ng generate` from the `client/` directory (e.g., `npx ng generate component foo/bar`).

### Server (.NET 8)
```bash
cd server
dotnet build
dotnet run --project src/RpgGame.Api
# No .NET test projects — tests are on the Angular side

# EF migrations (from server/src/RpgGame.Infrastructure/)
dotnet ef migrations add <Name> --startup-project ../RpgGame.Api
dotnet ef database update --startup-project ../RpgGame.Api

# Regenerate OpenAPI spec (requires swashbuckle CLI, installed as dotnet tool)
dotnet swagger tofile --output ../contracts/openapi.json src/RpgGame.Api/bin/Debug/net8.0/RpgGame.Api.dll v1
```

## Architecture

### Layers (Server)
Clean Architecture with three projects:
- **RpgGame.Domain** — Entities only (no EF, no Identity). All game entities live here.
- **RpgGame.Infrastructure** — EF `AppDbContext` (IdentityDbContext<AppUser>), `AppUser`, migrations. Depends on Domain.
- **RpgGame.Api** — Controllers, SignalR Hubs, `Program.cs`. Depends on both layers.

`AppUser` intentionally lives in Infrastructure (not Domain) because it extends `IdentityUser`.

### Domain Entities
- **Campaign** — Id, Name, CreatedAt; relations: Members[], Sessions[]
- **CampaignMember** — CampaignId, UserId, Role (Gm|Player); unique index on (CampaignId, UserId)
- **GameSession** — Id, CampaignId, JoinCode (6-char alphanumeric, unique), StartedAt
- **Character** — Id, UserId, Name, Race, Class, Level, BaseStats (JSONB: Str/Dex/Con/Int/Wis/Cha), MaxHp, CurrentHp
- **Encounter** — Id, GameSessionId, Status (Active|Ended), StartedAt; unique-active index on GameSessionId
- **EncounterCombatant** — Id, EncounterId, UserId, CharacterId, CharacterName, Initiative, InitiativeRoll, DexModifier
- **GameEvent** — Id, EncounterId, OccurredAt, EventType (string), Payload (JSONB); append-only log

### API Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| POST | `/auth/register` | Register user, returns JWT |
| POST | `/auth/token` | Login, returns JWT |
| POST | `/campaigns` | Create campaign (GM becomes first member) |
| POST | `/campaigns/{id}/sessions` | Start session → returns `{ joinCode, gameSessionId }` |
| POST | `/characters` | Create character |
| GET | `/characters/mine` | List caller's characters |
| GET | `/characters/{id}` | Get character |
| PUT | `/characters/{id}` | Update character |
| DELETE | `/characters/{id}` | Delete character |

### Auth Flow
1. Client POSTs to `/auth/register` or `/auth/token` → receives JWT
2. JWT stored in `sessionStorage` by `AuthService`
3. HTTP `AuthInterceptor` adds `Authorization: Bearer {token}` to all requests
4. SignalR connection passes JWT via `accessTokenFactory` → query string `?access_token=<token>`
5. `Program.cs` JwtBearer `OnMessageReceived` reads from query string

### Real-Time (SignalR + Redis)
- Hub at `/hubs/game`, requires `[Authorize]`
- Redis serves dual purpose: SignalR backplane AND live state
- Group naming: `session:{sessionId}` (UUID, not the join code)

**Hub methods (client → server):**

| Method | Auth | Description |
|--------|------|-------------|
| `JoinSession(joinCode, displayName, characterId?)` | Any | Validates join code, adds to Redis, broadcasts `PlayerPresenceChanged` |
| `StartEncounter(joinCode)` | GM only | Rolls initiatives (1d20 + dexMod), creates Encounter + Combatants, initialises map |
| `MoveToken(joinCode, combatantId, targetX, targetY)` | Own turn | Chebyshev distance ≤ 6, bounds 0–19; broadcasts `TokenMoved` |
| `DeclareAttack(joinCode, targetCombatantId)` | Own turn | Rolls damage by class (1d8/1d6/1d4), decrements HP in Redis, logs GameEvent; broadcasts `AttackResolved`, `CombatantDefeated` |
| `EndTurn(joinCode)` | Own turn | Advances to next non-defeated combatant; broadcasts `TurnAdvanced` |

**Server → client broadcasts:**
`PlayerPresenceChanged`, `EncounterStarted`, `TokenMoved`, `TurnAdvanced`, `AttackResolved`, `CombatantDefeated`, `ActionRejected`

**Redis key patterns:**

| Key | Type | Contents |
|-----|------|----------|
| `session:{sessionId}:players` | Hash | Connected player presences |
| `session:{sessionId}:active_encounter` | JSON | `encounterId`, `activeTurnCombatantId`, combatants[] |
| `encounter:map:{encounterId}` | JSON | 20×20 grid dims, positions by combatantId, fogOfWar |
| `character:live:{characterId}` | Hash | `currentHp`, `class` (seeded at encounter start) |
| `encounter:{encounterId}:defeated` | Set | Defeated combatant IDs |

### Client State (NGXS 21)
Five state slices, all registered in `app.config.ts`. Redux DevTools + Logger plugins enabled.

| Slice | Key fields |
|-------|-----------|
| `SessionState` (`state/session/`) | `campaignId`, `gameSessionId`, `joinCode`, `isGm`, `status`, `connectedPlayers[]` |
| `CharacterState` (`state/character/`) | `characters[]`, `selected`, `loading` |
| `EncounterState` (`state/encounter/`) | `encounterId`, `combatants[]`, `active`, `activeTurnCombatantId` |
| `MapState` (`state/map/`) | `gridWidth/Height` (20×20), `positions` (Record<combatantId, {x,y}>), `pendingMove` |
| `FeedState` (`state/feed/`) | `entries[]`, `unreadCount` |

### Client Services (`core/services/`)
- **AuthService** — register, login, getToken, getUserId (parses JWT), isLoggedIn, logout
- **CampaignService** — createCampaign, startSession
- **CharacterService** — getMyCharacters, createCharacter, updateCharacter, deleteCharacter
- **GameHubService** — SignalR connection; invokes all hub methods; dispatches NGXS actions on received events
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
Run `contracts:hub` after adding or changing any record in `server/src/RpgGame.Api/Contracts/Hub/` (excluding `EndTurnResult.cs` and `ServiceResults.cs`).


### Server Services (`RpgGame.Api/Services/`)
- **ICurrentUserAccessor / HttpCurrentUserAccessor** — abstracts `HttpContext.User` claims into `UserId`/`UserName`; injected into controllers instead of accessing `User` directly
- **IRedisGameState / RedisGameState** — Singleton; encapsulates all Redis reads/writes (players hash, active encounter JSON, map JSON, character live HP hash, defeated set). All hub logic goes through this interface rather than touching `IConnectionMultiplexer` directly.
- **ISessionService / SessionService** — Scoped; handles `JoinSession` logic (DB lookup, character validation, campaign member auto-join, Redis player upsert)
- **IEncounterService / EncounterService** — Scoped; handles `StartEncounter` (initiative rolls via `CombatDice`, DB persist, Redis seed), `MoveToken` (bounds + Chebyshev validation), `EndTurn` (advance turn or end encounter). Returns sealed `EndTurnResult` union (`TurnAdvancedResult` | `EncounterEndedResult`).
- **ICombatService / CombatService** — Scoped; handles `DeclareAttack` (turn validation, damage roll via `CombatDice`, Redis HP decrement, DB GameEvent log, defeated check)
- **CombatDice** — `internal static` helper; `DexModifier`, `RollD20`, `RollDamage`, `DamageDiceString`
- **GameActionException** — custom exception with `bool IsActionRejected`; thrown by services. Hub catches `IsActionRejected: true` and sends `ActionRejected` to caller; `IsActionRejected: false` propagates as `HubException`.

`GameHub` is a thin orchestrator (≤20 lines per method) — all business logic lives in the three scoped services above.

### Swagger / OpenAPI
- `Swashbuckle.AspNetCore` generates docs at `/swagger` in Development
- `ResponseTypeOperationFilter` reads `[ProducesResponseType]` attributes to populate response schemas
- `swashbuckle.aspnetcore.cli` installed as a local dotnet tool (`server/.config/dotnet-tools.json`) for spec generation

### Components
- **LobbyComponent** (`/`) — Auth (register/login/quick-register), GM flow (create campaign → start session → start encounter), Player flow (join by code + character select), player list, hosts EncounterTrackerComponent + GridComponent + FeedComponent
- **CharacterComponent** (`/character`) — Character list + CRUD form; Quick Create Random button
- **EncounterTrackerComponent** — Initiative order table; HP; defeated state (strikethrough)
- **GridComponent** — 20×20 CSS grid; click empty cell → MoveToken; click enemy → DeclareAttack; End Turn button; pending-move preview; active/defeated token styles
- **FeedComponent** — Timestamped combat log; unread badge; click to mark read

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

**Production (`docker-compose.prod.yml`) — bound to 127.0.0.1 (behind reverse proxy):**
| Service | External | Internal |
|---------|----------|----------|
| Client (Nginx) | 4201 | 80 |
| API (.NET) | 5001 | 8080 |
| PostgreSQL | 5433 | 5432 |
| Redis | 6379 | 6379 |

### EF Migrations
Migrations live in `RpgGame.Infrastructure/Data/Migrations/`. They run automatically via `db.Database.MigrateAsync()` on API startup — no manual `dotnet ef database update` needed in Docker. 5 migrations through `AddGameEvents`.

### Branching Convention

```
feat/<short-description>     e.g. feat/death-saves
refactor/<short-description> e.g. refactor/extract-redis-game-state
fix/<short-description>      e.g. fix/initiative-roll-bug
```

All branches target `master`. Branch protection requires CI to pass and a PR before merging (no direct pushes).

### CI/CD (GitHub Actions)
`.github/workflows/deploy.yml` triggers on push to `master`:
1. **build-and-push** — builds API and client Docker images, pushes to GHCR (`ghcr.io/nathanarvan/rpg-api:latest`, `ghcr.io/nathanarvan/rpg-client:latest`)
2. **deploy** — SSHs into the DigitalOcean droplet (`/opt/rpg/`), writes `.env` from GitHub Secrets, pulls latest images, runs `docker compose -f docker-compose.prod.yml up -d`

**Required GitHub Secrets:** `DROPLET_HOST`, `DROPLET_SSH_KEY`, `DB_USER`, `DB_PASSWORD`, `JWT_SECRET`, `GHCR_PAT`

### Unit Tests (Angular)
Karma/Jasmine specs covering all four NGXS state slices:
- `state/encounter/encounter.state.spec.ts`
- `state/feed/feed.state.spec.ts`
- `state/map/map.state.spec.ts`
- `state/session/session.state.spec.ts`

Run with `npm test` from the `client/` directory.
