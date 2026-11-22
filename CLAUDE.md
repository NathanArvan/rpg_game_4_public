# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Current Status

Phase 3A (simultaneous planning → tick-based resolution → GM enemy planning) is **complete and merged**. The old sequential initiative model (`MoveToken` / `DeclareAttack` / `EndTurn`) is gone. **3B.0 Manual Testing Spike** is done: encounters end, monsters can be AI-controlled, and **⚡ Quick Battle** (lobby) lets one user command a party against AI enemies with no setup. Use Quick Battle to try out every change by hand. **3B.1 Combat Profiles** is done: every combatant carries hand-authored combat values (HP, movement range, Speed, melee/ranged accuracy, dodge, damage die) from a preset in `server/src/RpgGame.Api/CombatProfiles/profiles.json`; D&D class dice and Dex are no longer used, and core stats and progression stay deferred until after Phase 3C. **3B.2 Action Points** is done: each round a combatant queues several actions paid from an AP budget (`MaxAp` from its profile; move 1, melee 2, ranged 2, defend 1), all its moves share its movement range, and the round resolves one tick per queue position. **3B.5 Defeat & Incapacitation** is done: at 0 HP a party combatant goes **Downed** and bleeds out after 2 rounds unless an adjacent ally uses **Stabilize** (2 AP); monsters die at 0 HP (`docs/design/DEFEAT_AND_INCAPACITATION.md`; try the **Rescue** Quick Battle). Next roadmap slice: **3B.3 Abilities & Cooldowns**. Execution order is 3B.0 → 3B.1 → 3B.2 → 3B.5 → 3B.3 → 3B.4 → 3B.6 (prepared reactions & opportunity attacks) → **3M (maps & terrain: large maps + pan/zoom, walls & buildings, ley points & resonance (3M.5, after 3M.2), terrain types, elevation)** → 3C. See `docs/roadmap/ROADMAP_STRATEGIC.md`.

## Documentation

### Design
- `docs/design/GAME_DESIGN.md` — Core rules & design philosophy; tactics-first, setting-agnostic system. Start here for any design question.
- `docs/design/DESIGN_SPIKE.md` — Locked decisions for Phase 3A: conflict resolution model, mutual kills, HP netting, damage pipeline, placeholder stats.
- `docs/design/TICK_RESOLUTION.md` — Tick-based resolution loop, path-based movement, action queue schema, opportunity attacks. Supersedes the single-pass phase model in DESIGN_SPIKE.
- `docs/design/DEFEAT_AND_INCAPACITATION.md` — Slice 3B.5 locked decisions: bleedout at 0 HP (Downed → Stabilize or die after 2 rounds; monsters die at 0 HP by default). Bleedout length and finishing blows to be revisited with character stats.
- `docs/design/ELEVATION.md` — Slice 3M.4 draft (not locked): integer height levels, slopes/cliffs derived from height difference, high-ground hit/range bonuses, line of sight over terrain.
- `docs/design/ABILITIES.md` — Slice 3B.3 locked decisions: abilities as data (targeting + effects that each resolve in a fixed phase), healing netted with damage, cooldowns, teams in the engine, and how spells, conditions, AoE, reactions and perception fit later.
- `docs/design/MAGIC_SYSTEM.md` — Environmental magic layer: ley points, resonance, magic pipeline. Defers internal character magic sources to post-playtesting.

### Workflow & Testing
- `docs/DEVELOPMENT_WORKFLOW.md` — Local dev guidance; which services to run in containers vs on the host.
- `docs/smoke-test.md` — Automated E2E (Playwright) + manual two-browser smoke test for the current planning/resolution loop.
- `docs/CODE_QUALITY.md` — Historical refactoring checklist (all items done; kept for context).

### Roadmap
- `docs/roadmap/ROADMAP_STRATEGIC.md` — High-level plan: what to build, in what order, data contracts per slice.
- `docs/roadmap/SPRINT_3B3.md` — Sprint doc for 3B.3 Abilities & Cooldowns (draft): PR plan, contracts, tests.
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

### Server (.NET 10)
```bash
cd server
dotnet build
dotnet watch --project src/RpgGame.Api   # Host API on http://localhost:5291 with hot reload
dotnet test                              # xUnit tests: RpgGame.Api.Tests + RpgGame.Rules.Tests
dotnet test tests/RpgGame.Rules.Tests    # rules engine + scenarios only (no DB/Redis, ~1s)

# EF migrations (from server/src/RpgGame.Infrastructure/)
dotnet ef migrations add <Name> --startup-project ../RpgGame.Api
dotnet ef database update --startup-project ../RpgGame.Api

# Regenerate OpenAPI spec (requires swashbuckle CLI, installed as dotnet tool)
dotnet swagger tofile --output ../contracts/openapi.json src/RpgGame.Api/bin/Debug/net10.0/RpgGame.Api.dll v1
```
Note: `dotnet build` of the API also rewrites `contracts/openapi.json` via a post-build step.

### Design Loop (game-rule changes)
Combat rules live in the pure `RpgGame.Rules` library, so a rule change doesn't need the server, a browser or containers:
1. Change the rule in `server/src/RpgGame.Rules/` (usually `RoundResolver`, `CombatProfile` or `RulesConfig`). To retune a profile instead, edit `server/src/RpgGame.Api/CombatProfiles/profiles.json`.
2. Add or adjust a scenario JSON in `server/tests/RpgGame.Rules.Tests/Scenarios/` (format: that folder's `README.md`; `"ai"` plans run the enemy AI) and/or a `RoundResolverTests` / `EnemyAiTests` case.
3. `dotnet test server/tests/RpgGame.Rules.Tests` — runs in about a second.
4. Then the full `dotnet test` (the `ResolutionServiceTests` characterization tests pin exact feed text and persistence) and E2E if the change reaches the client.

### End-to-End Tests (Playwright)
```bash
docker compose up -d --build     # or run API + client on the host; anything serving the app on :4200
cd e2e
npm ci
npx playwright install chromium  # first time only
npm test                         # E2E_BASE_URL overrides the default http://localhost:4200
```
Specs: `quick-battle.spec.ts` (one browser: play the Training Dummy preset to victory; plan three Skirmish heroes and Lock In All) is the fastest end-to-end check and the default smoke test for each slice. `combat-loop.spec.ts` drives two isolated browser contexts (GM + player) through register → campaign → session → join → add monster → start encounter → plan → lock in → resolution on both screens, with manual GM planning. `enemy-ai.spec.ts` checks an AI monster resolves a round with no GM input. Run it after any change that crosses the client/server boundary (hub methods, contracts, NGXS handlers, templates).

## Architecture

### Layers (Server)
Clean Architecture with four projects:
- **RpgGame.Domain** — Entities only (no EF, no Identity). All game entities live here.
- **RpgGame.Infrastructure** — EF `AppDbContext` (IdentityDbContext<AppUser>), `AppUser`, migrations. Depends on Domain.
- **RpgGame.Rules** — Pure combat rules engine. **No dependencies at all** (no packages, no EF/Redis/SignalR/ASP.NET/logging, no Domain). Own immutable types (`EncounterState`, `CombatantState`, `CombatProfile`, `PlannedAction`, `RoundOutcome`, …), `CombatProfile` (per-combatant values: MaxHp, MovementRange (cells per round, shared by all moves), Speed, Melee/RangedAccuracy, Dodge, BaseDamage die, MaxAp; an attack hits when d20 + the attacker's accuracy ≥ the target's Dodge), `RulesConfig` (shared constants: `MeleeReach`, the AP cost of each action type, `BleedoutRounds`), `CombatantStatus` (Standing / Downed / Stable / Dead, 3B.5; `CombatantState.IsDefeated` = not standing, plus `BleedoutRoundsLeft`; `CombatProfile.CanBeDowned`), `PlanValidator` (a queue's AP total and movement budget, used by `PlanningService` and the resolver), `IDiceRoller` (`RandomDice`, `SeededDice`, `ScriptedDice`), `RoundResolver.Resolve(state, plans, dice, config)` — the tick loop; its `RoundOutcome` also reports each attack's `AttackRoll` (d20, accuracy, target dodge), the `ResolvedPlans` (queues cut to their AP) and the end-of-round bleedout (`EndOfRound`). At 0 HP a combatant goes Downed (if `CanBeDowned`) or dies, HP stops at 0; Stabilize resolves in the Effects phase; a hit on someone already down at the start of the round finishes them off — and `EnemyAi.PlanRound(state, teamById, aiIds, config)`, the simple enemy AI, which returns an AP queue per AI combatant (melee an adjacent enemy as often as AP allows, else walk toward the nearest one, avoiding other AI walkers' paths, and melee with the AP left if the walk ends next to it). Never mutates inputs; plan (dictionary) order is meaningful (roll order, result order, contested-cell winner).
- **RpgGame.Api** — Controllers, SignalR Hubs, services, `Program.cs`. Depends on Domain, Infrastructure and Rules (never the reverse).

`AppUser` intentionally lives in Infrastructure (not Domain) because it extends `IdentityUser`.

### Domain Entities
- **Campaign** — Id, Name, CreatedAt; relations: Members[], Sessions[]
- **CampaignMember** — CampaignId, UserId, Role (Gm|Player); unique index on (CampaignId, UserId)
- **GameSession** — Id, CampaignId, JoinCode (6-char alphanumeric, unique), StartedAt
- **Character** — Id, UserId, Name, CombatProfile (preset id, default `skirmisher`), Race, Class, Level, BaseStats (JSONB: Str/Dex/Con/Int/Wis/Cha), MaxHp, CurrentHp (the D&D-style fields are unused by combat since 3B.1; removal deferred to the stats/progression decision)
- **Encounter** — Id, GameSessionId, Status (Active|Ended), StartedAt, EndedAt (nullable), RoundNumber; unique-active index on GameSessionId
- **EncounterCombatant** — Id, EncounterId, UserId, CharacterId (nullable — null for monsters), CharacterName, CombatantType (`player` | `monster`), IsAiControlled, Profile (JSONB `CombatProfileValues`: the combat profile copied in when the combatant was created, with any battle-preset overrides applied, incl. an optional `CanBeDowned`; null only on pre-3B.1 rows, read as the default profile)
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
| GET | `/api/combat-profiles` | Combat profile presets (`CombatProfileDto[]`) for the character and monster pickers |

### Auth Flow
1. Client POSTs to `/api/auth/register` or `/api/auth/token` → receives JWT
2. JWT stored in `sessionStorage` by `AuthService`
3. HTTP `AuthInterceptor` adds `Authorization: Bearer {token}` to all requests
4. SignalR connection passes JWT via `accessTokenFactory` → query string `?access_token=<token>`
5. `Program.cs` JwtBearer `OnMessageReceived` reads from query string

### Quick Battle (3B.0)
One click from the lobby: `StartQuickBattle(presetId)` creates a campaign (caller is GM), a session and an encounter from a **battle preset**, joins the caller to the session group and opens round 1. The caller controls every party combatant (`CombatantType "player"`, no `Character` row); enemies are AI-controlled monsters. The planning timer is off (`PlanningTimerEnabled = false` → `PlanningTimerDurationSeconds = 0`). The client shows the player view (`SessionState.quickBattlePresetId` set, `isGm` false) plus a Quick Battle panel (Force Resolve / Restart / Leave); the result overlay offers Play again.
- **Presets:** `server/src/RpgGame.Api/QuickBattle/Presets/*.json` (copied to the output folder). Each lists combatants with `name`, `side` (`party` | `enemy`), `profile` (a combat profile id), `at` [x, y], optional `ai` (enemies default to true), and optional overrides of single profile values: `hp`, `damage` (die), `speed`, `move`, `meleeAccuracy`, `rangedAccuracy`, `dodge`, `ap`, `downs` (goes Downed at 0 HP; default by side: party yes, enemies no). `BattlePresetCatalog` validates them at startup and stops the API with the file name if one is broken. To add a battle, drop in a new file; `order` sets its place in the menu (lowest first = default).
- **Stats:** the resolved profile (preset + overrides) is stored on `EncounterCombatant.Profile`, like every other combatant. Live HP is keyed by combatant ID when there's no character.
- **Several characters per user:** `PlanAction` takes the `combatantId` of one of your `player` combatants (required if you control more than one). `LockIn` locks all your living combatants; unplanned ones defend (at least one must have a plan). The client tracks plans per combatant and which one you're planning for (click your token or tracker row); the button reads "Lock In All (n/m planned)".

### Combat Loop (Phase 3A)
1. **Lobby** — GM creates campaign → starts session → connects; GM can `AddMonster` with a combat profile (staged in Redis until the encounter starts). Players join with a character.
2. **StartEncounter** — creates Encounter + Combatants (each gets a copy of its combat profile: the character's, or the staged monster's; everyone starts at the profile's MaxHp), places players on row 0 and monsters on row 19, broadcasts `EncounterStarted` + `PlanningPhaseStarted`, starts the planning timer (`GameConstants.PlanningTimerSeconds`, currently 300s).
3. **Planning** — each player queues actions (`move` | `melee_attack` | `ranged_attack` | `defend` | `stabilize` (a downed ally, 3B.5)) as a `PlannedActionArray` whose total AP cost fits the combatant's `MaxAp` (3B.2; costs arrive in `PlanningPhaseStarted.apCosts`), and locks in. Each `PlanAction` replaces the whole queue; an empty queue clears it. AI-controlled monsters are planned and locked in by the server as soon as the phase opens (`AiPlanningService` → `EnemyAi`). The GM plans the other monsters (`PlanEnemyAction`, which can also override an AI monster for one round) and locks in all enemies at once (`LockInEnemies`; unplanned manual monsters defend). If every monster is AI, the round resolves when the last player locks in.
4. **Resolution** — when every living combatant is locked in (or GM `ForceResolve`, or timer expiry), `ResolutionService` hands state to `RoundResolver`, which runs the tick loop (one tick per queue position; Movement → Attacks → Effects within each); `ResolutionService` then writes Redis + `GameEvent`s, and `RoundOrchestrator` broadcasts `ResolutionCompleted` (which carries `defeatedCombatantIds`, each attack's `roll` and `wastedReason`, and the resolved `queues` with each action's AP cost) plus one `CombatantDefeated` per newly defeated combatant.
5. **Next round or end** — after a playback delay (3s per tick), `RoundOrchestrator` checks `EncounterEndCheck`: if one side has no standing combatants (downed and stable count as out) (and both sides had at least one), `EncounterService.EndEncounterAsync` marks the encounter `Ended`, logs an `encounter_ended` GameEvent and deletes the session's Redis encounter state, and `EncounterEnded` (`winningSide`: `party` | `enemy` | `draw`) is broadcast. Otherwise the next `PlanningPhaseStarted` is sent and the timer restarts. Clients show a result overlay; "Return to lobby" clears encounter state, and the GM can start a new encounter in the same session.

### Real-Time (SignalR + Redis)
- Hub at `/hubs/game`, requires `[Authorize]`
- Redis serves dual purpose: SignalR backplane AND live state
- Group naming: `session:{sessionId}` (UUID, not the join code)
- Method/event names are constants in `Contracts/Hub/HubEvents.cs` (mirrored in the generated TS contracts)

**Hub methods (client → server):**

| Method | Auth | Description |
|--------|------|-------------|
| `JoinSession(joinCode, displayName, characterId?)` | Any | Validates join code, adds to Redis, broadcasts `PlayerPresenceChanged` |
| `AddMonster(joinCode, monsterName, isAiControlled, profileId)` | GM only | `profileId` null → default `grunt`, unknown → `ActionRejected`. Stages a monster pre-encounter (or spawns one mid-encounter; an AI one plans and locks in immediately); broadcasts `MonsterAdded` |
| `StartEncounter(joinCode)` | GM only | Creates encounter + combatants, seeds map/HP, starts round 1 planning |
| `PlanAction(joinCode, plannedAction, combatantId)` | Player (own combatant) | Validates (targets, AP budget, shared movement budget) and stores the action queue for one of the caller's characters, replacing the previous one; an empty queue clears it (`combatantId` may be null if they control only one) |
| `LockIn(joinCode)` | Player | Locks in all the caller's living characters (unplanned → defend); one `PlayerLockedIn` each; triggers resolution if everyone is locked in |
| `GetQuickBattlePresets()` | Any | Returns `QuickBattlePresetDto[]` (id, name, description) |
| `StartQuickBattle(presetId)` | Any | Creates campaign + session + encounter from a preset, joins the caller, opens round 1; returns `QuickBattleStartedResult` (joinCode, campaignId, gameSessionId, presetId) |
| `PlanEnemyAction(joinCode, combatantId, plannedAction)` | GM only | Stores (or, if empty, clears) the action queue for one monster |
| `LockInEnemies(joinCode)` | GM only | Locks in all living monsters (unplanned → defend) |
| `ForceResolve(joinCode)` | GM only | Resolves immediately; unplanned combatants defend |

**Server → client broadcasts:**
`PlayerPresenceChanged`, `MonsterAdded`, `EncounterStarted`, `PlanningPhaseStarted`, `PlayerLockedIn`, `AllLockedIn`, `ResolutionCompleted`, `CombatantDefeated`, `EncounterEnded`, `ActionRejected`

**Redis key patterns:**

| Key | Type | Contents |
|-----|------|----------|
| `session:{sessionId}:players` | Hash | Connected player presences |
| `session:{sessionId}:pending_monsters` | List | Monsters staged before the encounter starts, as JSON `{ name, isAiControlled, profile }` (plain-name entries read as manual; no `profile` → default enemy profile) |
| `session:{sessionId}:active_encounter` | JSON | `encounterId`, `roundPhase`, `roundNumber`, `planningTimerExpiresAt`, combatants[] (`id`, `userId`, `combatantType`, `isAiControlled`) |
| `encounter:map:{encounterId}` | JSON | 20×20 grid dims, positions by combatantId |
| `character:live:{id}` | Hash | `currentHp`, `profile` (profile id; the values live on the combatant row) — keyed by character ID for players, combatant ID for monsters |
| `encounter:{encounterId}:planned_actions` | Hash | combatantId → `PlannedActionArray` JSON |
| `encounter:{encounterId}:locked_in` | Set | Locked-in combatant IDs |
| `encounter:{encounterId}:defeated` | Set | Combatant IDs out of the fight (downed, stable or dead) |
| `encounter:{encounterId}:status` | Hash | combatantId → `downed:2` / `stable:0` / `dead:0` (status:bleedout rounds left, 3B.5). Defeated with no entry = dead |

### Client State (NGXS)
Five state slices, all registered in `app.config.ts`. Redux DevTools + Logger plugins enabled.

| Slice | Key fields |
|-------|-----------|
| `SessionState` (`state/session/`) | `campaignId`, `gameSessionId`, `joinCode`, `isGm`, `status`, `connectedPlayers[]`, `quickBattlePresetId` |
| `CharacterState` (`state/character/`) | `characters[]`, `selected`, `loading` |
| `EncounterState` (`state/encounter/`) | `encounterId`, `active`, `roundPhase`, `roundNumber`, `combatants[]` (with `movementRange`, `maxAp`), `myPlannedActions` (queue per combatant), `selectedCombatantId`, `lockedInCombatantNames`, `planningTimerExpiresAt`, `resolutionTicks`, `gmSelectedEnemyId`, `enemyPlannedActions` (queue per monster), `apCosts` |
| `MapState` (`state/map/`) | `gridWidth/Height` (20×20), `positions` (Record<combatantId, {x,y}>) |
| `FeedState` (`state/feed/`) | `entries[]`, `unreadCount` |

### Client Services (`core/services/`)
- **AuthService** — register, login, getToken, getUserId (parses JWT), isLoggedIn, logout
- **CampaignService** — createCampaign, startSession
- **CharacterService** — getMyCharacters, createCharacter, updateCharacter, deleteCharacter
- **GameHubService** — SignalR connection; invokes all hub methods; dispatches NGXS actions on received events. Hands `ResolutionCompleted` to `ResolutionPlaybackService`, and holds the encounter events that arrive mid-playback (`PlanningPhaseStarted`, `PlayerLockedIn`, `AllLockedIn`, `CombatantDefeated`, `EncounterEnded`, `MonsterAdded`) until playback ends
- **ResolutionPlaybackService** — plays a round's resolution back on every screen. Commits the final state to the store at once (it stays the source of truth), then plays the beats from `playback-timeline.ts` and exposes what's been shown so far as signals: token positions, revealed HP (`hpOf`), defeats (`isDefeated`), auras and floating text; feed lines are added beat by beat. Resets on `ClearEncounter` / `EncounterStarted`
- **playback-timeline.ts** (`core/services/`) — pure builder: `buildTimeline(payload)` turns the ticks into timed beats (per tick: one beat per movement cell → one attacks beat with auras (grey miss / green hit / red was hit), `-n` / MISS text and HP → a defeats beat (going down shows DOWN) → an effects beat for stabilizes; a short idle beat for quiet ticks; after the last tick, an end-of-round beat for the bleedout countdown). Durations are in `PLAYBACK_TIMING`. New animations = a new cue on `PlaybackBeat`, filled here and rendered from the service
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
- **IEncounterService / EncounterService** — Scoped; `StartEncounterAsync` (DB persist, map/HP seed, round 1 planning), `AddMonsterAsync`, and `EndEncounterAsync` (Ended status + GameEvent, then clears Redis encounter state)
- **IPlanningService / PlanningService** — Scoped; `PlanAction`, `LockIn`, `ForceResolve`, `PlanEnemyAction`, `LockInEnemies`; decides when all living combatants are locked in
- **IResolutionService / ResolutionService** — Scoped; `ResolveRoundAsync` is an **adapter** around `RpgGame.Rules.RoundResolver`: loads state via `EncounterStateLoader`, maps it to `EncounterState` + plans, calls the resolver with the injected `IDiceRoller`, maps the `RoundOutcome` to `ResolutionPayload`, then persists (GameEvents to DB before Redis mutations)
- **ResolutionFeedFormatter** — `internal static`; feed text for attack/effect results (presentation stays in Api). Strings are pinned byte-for-byte by tests
- **RulesWireMapping** — `internal static`; hub wire strings (`"melee_attack"`, …) ⇄ Rules enums
- **IDiceRoller** (from Rules) — registered as a `RandomDice` singleton; tests inject `ScriptedDice`
- **IRoundOrchestrator / RoundOrchestrator** — Scoped; `ResolveAndAdvanceAsync` is the single post-lock-in flow (cancel timer → `AllLockedIn` → resolve → `ResolutionCompleted` / `CombatantDefeated` → playback delay → `EncounterEnded` or the next planning phase). `StartPlanningPhaseAsync` is the single way a planning phase opens (round 1 from `StartEncounter`, later rounds from resolution): `PlanningPhaseStarted` → timer → AI planning → one `PlayerLockedIn` per AI combatant. Broadcasts via `IHubContext<GameHub>`, so the hub and the timer share it
- **IAiPlanningService / AiPlanningService** — Scoped; adapter around `EnemyAi`: loads state, plans every living AI combatant without a plan yet (a GM override wins), stores the plans and locks them in. Teams come from `CombatantType` for now
- **EncounterStateLoader** — `internal static`; builds the Rules `EncounterState` (plus persistence records) from Postgres (combatants and their stored profiles) + Redis. Shared by resolution and AI planning
- **CombatProfileCatalog** (`CombatProfiles/`) — Singleton; loads and validates `profiles.json` at startup (stops the API naming the broken profile). `Snapshot()` copies a preset into a `CombatProfileValues`; `CombatProfileMapping.ToRules` turns stored values into the Rules `CombatProfile`. Defaults: `skirmisher` (party), `grunt` (enemy)
- **CombatantDtos** — `internal static`; builds `CombatantDto` / `PlanningCombatantDto` (including `profileId`, `profileName`, `movementRange`)
- **EncounterLaunch** — `internal static`; the shared second half of starting an encounter (Redis active encounter, map, live HP, EncounterStarted/PlanningPhaseStarted payloads). Used by `EncounterService` and `QuickBattleService`
- **IQuickBattleService / QuickBattleService** — Scoped; lists presets and starts a Quick Battle (campaign, GM membership, session, encounter, combatants with preset stats, timer off)
- **BattlePresetCatalog** (`QuickBattle/`) — Singleton; loads and validates the preset files at startup (needs `CombatProfileCatalog` to resolve each combatant's profile + overrides)
- **EncounterEndCheck** — `internal static`; decides whether a round ended the encounter and who won (sides from `CombatantType` for now)
- **IPlanningTimer / PlanningTimerService** — Singleton; per-encounter countdown (`Task.Delay` + CTS); on expiry defaults unplanned combatants to defend and calls `RoundOrchestrator` in a fresh scope
- **GameActionException** — custom exception with `bool IsActionRejected`; thrown by services. Hub catches `IsActionRejected: true` and sends `ActionRejected` to caller; `IsActionRejected: false` propagates as `HubException`.

`GameHub` is a thin orchestrator — business logic lives in the scoped services above.

### Swagger / OpenAPI
- `Swashbuckle.AspNetCore` generates docs at `/swagger` in Development
- `ResponseTypeOperationFilter` reads `[ProducesResponseType]` attributes to populate response schemas
- `swashbuckle.aspnetcore.cli` installed as a local dotnet tool (`server/.config/dotnet-tools.json`) for spec generation

### Components (`client/src/app/features/`)
- **LobbyComponent** (`/`) — hosts AuthPanel, QuickBattleStart, PlayerJoin, GmFlow and the connected-player list; swaps to CombatLayout when an encounter is active
  - **QuickBattleStartComponent** — preset dropdown + Start Quick Battle (via `QuickBattleService`, which also restarts and leaves)
  - **AuthPanelComponent** — register / login / ⚡ Quick Register
  - **PlayerJoinComponent** — join code + display name + character select
  - **GmFlowComponent** — create campaign → start session → connect → add monsters (name, combat profile, AI toggle) → start encounter
- **CharacterComponent** (`/character`) — character list + CRUD form with a combat profile picker (via `CombatProfileService`); ⚡ Quick Create Random
- **CombatLayoutComponent** — three columns: CombatSidebar (EncounterTracker) | RoundBanner + Grid | Feed
- **EncounterTrackerComponent** — combatant HP table (with each combatant's profile name), lock-in checkmarks, status (DOWN (n) / STABLE / DEAD) as revealed by playback
- **RoundTimelineComponent** (in CombatSidebar) — the round as lanes: one row per combatant, one column per tick (action icon, AP cost, outcome colour), built by `round-lanes.ts` (pure) from `ResolutionPayload.queues` + ticks. Follows playback (outcomes revealed as they play, details with rolls for the current tick); afterwards stays up as "Last round" and a tick header click shows that tick's details
- **RoundBannerComponent** — round number, phase, timer, lock-in count; during playback, "Tick n of m" and the part of the tick playing (Movement / Attacks / Defeats)
- **GridComponent** — 20×20 grid; each click appends to the selected character's queue (a cell in range → move, measured from where the queued moves end; a token → melee if in reach of that spot, else ranged). The action bar shows `AP n/max`, the numbered queue, undo / clear, Defend and Lock In. AP helpers live in `state/encounter/plan-queue.ts` (mirrors `PlanValidator`). GM sees GmEnemyPlanner + ⚡ Force Resolve. Clicking a downed ally whose position the selected character's queued moves end next to queues Stabilize (3B.5); downed tokens pulse red with a bleedout badge, stable ones are teal-ringed. Tokens are drawn in a `.token-layer` over the cells (one element per combatant, tagged `data-x`/`data-y`) and glide with CSS transitions during playback; auras and floating text render there from `ResolutionPlaybackService`
- **GmEnemyPlannerComponent** — select a monster, queue actions via grid clicks or 🛡 (AP-limited), clear, Lock In Enemies
- **QuickBattlePanelComponent** — Quick Battle only: preset name, Force Resolve, Restart, Leave
- **EncounterResultComponent** — overlay when `EncounterEnded` arrives: Victory/Defeat/Draw, Return to lobby, Play again (Quick Battle)
- **FeedComponent** — timestamped combat log, oldest line first (each encounter opens with "— Battle Start —", added when `EncounterStarted` arrives); fills the right column and scrolls inside itself, following new lines while you're at the bottom, with a "Jump to latest" button (and a count of new lines) when scrolled up; unread badge. Lines stay plain text in `FeedState`; `feed-format.ts` (pure) styles them for display: round headings > tick headings > events, party names blue, enemy names red, damage orange, misses grey
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
Migrations live in `RpgGame.Infrastructure/Data/Migrations/`. They run automatically via `db.Database.MigrateAsync()` on API startup — no manual `dotnet ef database update` needed in Docker. 14 migrations through `AddCanBeDowned`.

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
- **Server (xUnit + Moq + EF InMemory)** — `server/tests/RpgGame.Api.Tests/`: `EncounterServiceTests`, `PlanningServiceTests`, `ResolutionServiceTests` (+ `ResolutionServiceCharacterizationTests`, which pin exact feed text, roll order, persistence and known quirks using `ScriptedDice`), `CombatProfileCatalogTests`, `BattlePresetCatalogTests`, `QuickBattleServiceTests`. `TestProfiles` gives tests the shipped profile catalog and a `Values(...)` builder. Redis is mocked via `IRedisGameState`.
- **Rules (xUnit only, no Moq/EF)** — `server/tests/RpgGame.Rules.Tests/`: `RoundResolverTests`, `EnemyAiTests`, `DiceTests`, `ModelTests`, and `ScenarioTests`, which runs every `Scenarios/*.json` (designer-writable round scenarios; see `Scenarios/README.md`).
- **Client (Karma/Jasmine)** — specs for all NGXS state slices (`state/*/*.state.spec.ts`) plus component smoke specs.
- **E2E (Playwright)** — `e2e/tests/`; see the command section above.
