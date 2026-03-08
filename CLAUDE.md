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
- **RpgGame.Domain** — Entities only (no EF, no Identity). `GameSession` lives here.
- **RpgGame.Infrastructure** — EF `AppDbContext` (IdentityDbContext<AppUser>), `AppUser`, migrations. Depends on Domain.
- **RpgGame.Api** — Controllers, SignalR Hubs, `Program.cs`. Depends on both layers.

`AppUser` intentionally lives in Infrastructure (not Domain) because it extends `IdentityUser`.

### Auth Flow
1. Client POSTs to `/auth/register` or `/auth/token` → receives JWT
2. JWT stored in `localStorage` by `AuthService`
3. SignalR connection passes JWT via query string: `?access_token=<token>` (required for WebSocket handshake)
4. `Program.cs` JwtBearer `OnMessageReceived` reads from query string

### Real-Time (SignalR + Redis)
- Hub at `/hubs/game`, requires `[Authorize]`
- Redis serves dual purpose: SignalR backplane AND live state (`session:{id}:players` hash sets)
- Group naming convention: `session:{sessionId}`
- Client event from server: `PlayerPresenceChanged`
- Client invocation to server: `JoinSession(sessionId, displayName)`

### Client State (NGXS 21)
Single state slice at `state/session/`:
- `SessionState` manages: `sessionId`, `status` (disconnected/connecting/connected), `connectedPlayers[]`
- `GameHubService` dispatches `PlayerPresenceChanged` action when SignalR fires
- Redux DevTools plugin enabled

### Nginx Proxy (client/nginx.conf)
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
Migrations live in `RpgGame.Infrastructure/Data/Migrations/`. They run automatically via `db.Database.MigrateAsync()` on API startup — no manual `dotnet ef database update` needed in Docker.
