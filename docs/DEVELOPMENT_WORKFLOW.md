# Development Workflow

Guidance for local development on this repository. Optimized for fast iteration on vertical slices that cut through Angular → SignalR contracts → .NET API → Redis/Postgres.

## Core Principle

The stack has two kinds of services:

- **Infrastructure** (Postgres, Redis) — stable, never edited. Run in containers.
- **App code** (API, Client) — edited constantly. Run on the host when actively developing.

Do not run containers for the code you are actively editing. Every code change in a container means either a rebuild, a volume-mounted hot reload, or a restart — all slower and heavier than running on the host.

Compose is the integration test environment, not the inner dev loop.

---

## Workflow by Task

### Server-side work (hub methods, services, EF entities, migrations)

Run infra in containers, API on the host with hot reload.

```bash
docker compose up postgres redis
```

In another terminal:

```bash
cd server
dotnet watch --project src/RpgGame.Api
```

`dotnet watch` gives sub-second hot reload on C# changes. The host-side API connects to `localhost:5432` and `localhost:6379` — both ports are exposed by the base compose file.

Saves ~300–500MB of memory vs running the API container.

### Client-side work (Angular components, NGXS state, templates)

Run infra + API in containers, Angular dev server on the host.

```bash
docker compose up postgres redis api
```

In another terminal:

```bash
cd client
npm start
```

The Angular dev server at `http://localhost:4200` proxies `/api/` and `/hubs/` to the containerized API. You get live reload, source maps, and the error overlay — all degraded or absent when serving a production build through nginx.

### End-to-end smoke test

When a slice is ready to exercise across all layers the way it will ship:

```bash
docker compose up --build
```

This runs the production-built Angular served by nginx, API in whatever mode the compose stack dictates, real nginx proxy rules. Use this with `smoke-test.md` for the 3-tab scenario.

### Contract-boundary work (SignalR events, new DTOs)

A vertical slice touches contracts on both sides: C# record on the server, TypeScript interface on the client. The iteration loop is usually: add the record, regenerate or hand-sync the TS, wire up the hub broadcast, wire up the Angular handler, test.

If both sides are in containers, every iteration rebuilds one or both images. Keep both on the host:

```bash
# Terminal 1
docker compose up postgres redis

# Terminal 2
cd server && dotnet watch --project src/RpgGame.Api

# Terminal 3
cd client && npm start
```

After changes to C# hub DTOs, run `npm run contracts:hub` from the `client/` directory to regenerate `hub-contracts.generated.ts`.

---

## Useful Side Terminals

Keep logs streaming so you can see what the infra is doing:

```bash
docker compose logs -f postgres redis
```

Useful when debugging why (for example) `StartEncounter` did not seed the map the way you expected — you see the Postgres queries and Redis commands as they happen.

---

## Memory Footprint

Local development uses `docker-compose.override.yml` to apply memory limits and GC tuning that should not touch production. Key settings:

- **API**: Workstation GC (`DOTNET_gcServer=0`), aggressive heap return (`GCConserveMemory=9`, `GCRetainVM=0`), `mem_limit: 512m`. The cgroup limit also makes .NET self-throttle its heap.
- **Postgres**: `shared_buffers=64MB`, `max_connections=20`, `mem_limit: 256m`. Defaults assume a real server; local smoke tests use a handful of rows.
- **Redis**: `maxmemory 64mb`, persistence disabled (`--save ""`, `--appendonly no`), volume removed. Redis is reseeded from Postgres on `StartEncounter` anyway.
- **Client (nginx)**: `mem_limit: 64m`.

Expected idle footprint for the full stack: ~400–600MB resident. Verify with `docker stats`.

Skipping containers (per workflow above) drops this further.

---

## Quick Reference

| Task | Run in containers | Run on host |
|---|---|---|
| Server slice work | `postgres`, `redis` | `dotnet watch` |
| Client slice work | `postgres`, `redis`, `api` | `npm start` |
| Contract boundary | `postgres`, `redis` | `dotnet watch` + `npm start` |
| End-to-end smoke test | all four | — |
| EF migration work | `postgres` | `dotnet ef` commands |

---

## Anti-patterns

- Running `docker compose up --build` on every code change. Rebuilds are for integration checkpoints, not iteration.
- Editing C# inside the API container via volume mount and hoping hot reload works. Use `dotnet watch` on the host instead.
- Running `npm start` and the client container simultaneously — port conflict on 4200 and wasted memory.
- Leaving the Redis volume mounted locally. Persistence is disabled and state is reseeded from Postgres, so the volume just takes up disk.
