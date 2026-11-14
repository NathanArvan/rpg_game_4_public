# RPG Game

A multiplayer RPG app where all players act at the same time. Instead of waiting through a turn order, everyone plans their moves simultaneously, then the server resolves everything at once. A GM controls enemies and manages the session, while players each see the battle from their own character's perspective.

Built for the browser. Real-time sync across all connected players via WebSockets.

## Tech Stack

- **Server:** .NET 10, SignalR, PostgreSQL, Redis
- **Client:** Angular 19, NGXS state management
- **Infra:** Docker Compose, deployed to DigitalOcean via GitHub Actions

## What Works Today

- User registration and login (JWT auth)
- Campaign and session management — GM creates a game, players join with a 6-character code
- Character creation and management
- Live combat encounters on a 20×20 grid — move tokens, declare attacks, track HP
- Real-time sync across browser tabs via SignalR + Redis
- Combat feed with timestamped events

The current encounter system uses sequential turns (initiative order). The next major milestone replaces this with simultaneous planning and resolution — the core design goal of the project.

## Running Locally

### Prerequisites

- [Docker](https://www.docker.com/) and Docker Compose

### Quick Start

1. Clone the repo
2. Copy `.env.example` to `.env` and fill in the values:
   - `DB_USER` / `DB_PASSWORD` — PostgreSQL credentials
   - `JWT_SECRET` — any string, at least 32 characters
3. Start everything:
   ```bash
   docker-compose up --build
   ```
4. Open `http://localhost:4200`

That's it. The database migrations run automatically on startup. PostgreSQL, Redis, the .NET API, and the Angular client all come up together.

To stop: `docker-compose down`

### Running Without Docker

If you'd rather run the pieces individually:

**Server** (.NET 10 SDK required):
```bash
cd server
dotnet build
dotnet run --project src/RpgGame.Api
```
You'll need PostgreSQL and Redis running locally and a connection string configured.

**Client** (Node.js required):
```bash
cd client
npm ci --legacy-peer-deps
npm start
```
Dev server runs on `http://localhost:4200`.

### Running Tests

```bash
cd client
npm test
```

Tests cover the NGXS state slices (encounter, feed, map, session). There is no server test project yet.

### Generating Types

TypeScript types for both the REST API and SignalR hub payloads are auto-generated. Don't edit the generated files by hand — regenerate them when you change server contracts.

**REST API types** — generated from the OpenAPI spec:
```bash
# 1. Rebuild the server and regenerate the OpenAPI spec
cd server
dotnet build
dotnet swagger tofile --output ../contracts/openapi.json src/RpgGame.Api/bin/Debug/net10.0/RpgGame.Api.dll v1

# 2. Regenerate the TypeScript types
cd ../client
npm run codegen
```

**SignalR hub types** — generated from the C# records in `server/src/RpgGame.Api/Contracts/Hub/`:
```bash
cd client
npm run contracts:hub
```

Run `contracts:hub` any time you add or change a record in the Hub contracts folder.

## Project Structure

```
server/
  src/
    RpgGame.Domain/          # Entities (no framework dependencies)
    RpgGame.Infrastructure/  # EF Core, database, Identity
    RpgGame.Api/             # Controllers, SignalR hub, startup
client/
  src/
    app/
      features/              # Lobby, character management
      state/                 # NGXS state slices
      services/              # API and SignalR services
```

## Docs

Design documents and roadmaps live in the repo if you want to dig deeper into where the project is headed:

- `docs/design/GAME_DESIGN.md` — Core rules and design philosophy
- `docs/roadmap/ROADMAP_STRATEGIC.md` — High-level plan and phase sequencing
- `CLAUDE.md` — Developer reference (architecture, commands, conventions)


## Roadmap

The project is organized into three horizons, each representing a product-level milestone.

### Horizon 1 — Single Encounter

A GM and players can join a session, create characters, build an encounter, and play it to completion with the full simultaneous resolution system. This is the core tactical combat product. All major implementation work lives here:


### Horizon 2 — Connected Sessions

Multiple encounters with persistent characters. Experience, items, and progression carry between sessions. Social encounters and exploration systems are introduced, built on the same digital-first principles.

### Horizon 3 — Campaign Play

Long-running narrative arcs, world state persistence, crafting, downtime activities, faction reputation, and full campaign management tools.
