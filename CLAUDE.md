# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

### Full Stack (Docker)
```bash
docker-compose up --build        # Start all services
docker-compose up                # Start without rebuilding
docker-compose down              # Stop all services
```

### Client (Angular 19)
```bash
cd client
npm ci --legacy-peer-deps        # Install deps (required flag for NGXS peer dep mismatch)
npm start                        # Dev server on http://localhost:4200
npm run build                    # Production build → dist/client/browser/
npm test                         # Karma/Jasmine unit tests
```

### Server (.NET 8)
```bash
cd server
dotnet build
dotnet run --project src/RpgGame.Api
dotnet test                      # Run tests (no test projects yet)

# EF migrations (from server/src/RpgGame.Infrastructure/)
dotnet ef migrations add <Name> --startup-project ../RpgGame.Api
dotnet ef database update --startup-project ../RpgGame.Api
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
- Group naming: `session:{joinCode}`

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
Loaded from `.env` at project root:
- `DB_USER`, `DB_PASSWORD` — PostgreSQL credentials
- `JWT_SECRET` — must be ≥32 chars for HMAC-SHA256

### Port Mapping
| Service | External | Internal |
|---------|----------|----------|
| Client (Nginx) | 4200 | 80 |
| API (.NET) | 5000 | 8080 |
| PostgreSQL | 5432 | 5432 |
| Redis | 6379 | 6379 |

### EF Migrations
Migrations live in `RpgGame.Infrastructure/Data/Migrations/`. They run automatically via `db.Database.MigrateAsync()` on API startup — no manual `dotnet ef database update` needed in Docker. 5 migrations through `AddGameEvents`.
