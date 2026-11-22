# Testing Roadmap

> **Purpose:** Planned improvements to the test suite, so it keeps up with the detail abilities are adding and with the next slices (3B.4 conditions, 3B.6 reactions, 3M maps, 3C hidden information). Hand an item to Claude Code alongside `CLAUDE.md`. Each item says what's wrong today, what to build, when it should land relative to the roadmap, and how to know it's done.
>
> **Status (2026-11-22):** items 1–5 are done (multi-round scenarios, shared ability library and the event log; real-Redis and hub integration tests; client test-data builders; shared client/server plan-validation cases). Items 6–7 are open.

---

## Where the suite stands

| Layer | Size | Time | Good at | Weak at |
|---|---|---|---|---|
| Rules: unit tests + JSON scenarios | ~196 tests, 50 scenarios | ~1 s | Exact dice, readable failures, scenarios without C# | (fixed by items 1–2: several rounds, repeated ability blocks, new checks needing C#) |
| Api services (xUnit + Moq + EF InMemory) | ~222 tests | ~8 s | Characterization tests pin feed text, roll order and write order | Redis and the hub are mocked (covered by the integration layer) |
| Api integration (Testcontainers Postgres + Redis, `WebApplicationFactory` + `HubConnection`) | 37 tests | ~5 s + ~20 s container start | Real Redis keys and formats, whole rounds over the wire with exact rolls, wire JSON vs the client's TS contracts | One user per test so far (no per-player assertions yet) |
| Client unit tests (Karma) | ~183 specs | ~40 s with compile | Pure modules (`plan-queue`, `playback-timeline`, `round-lanes`, `feed-format`); contract fixtures come from shared builders | `game-hub.service` has no spec |
| E2E (Playwright) | 7 tests | ~1.3 min local, ~3.5 min CI | Proves the stack works together | Random dice, tied to preset layouts, awkward to run locally |

Item 3 closed most of the gap between the mocked service tests and E2E (real Redis code, the hub, JSON on the wire). Items 6 and 7 close the rest.

---

## Done

### 1. Multi-round scenarios ✅

A scenario can list `"rounds"`, each with its own `plans`, `dice` and `expect`; each round starts from the state the previous one left (HP, positions, statuses, bleedout, cooldowns). Needed for 3B.4 durations. See `server/tests/RpgGame.Rules.Tests/Scenarios/README.md`.

### 2. Shared ability library and the event log ✅

- `Scenarios/library/abilities.json` holds test versions of the shipped abilities; a scenario's own `abilities` block overrides an entry. The 26 scenarios that pasted the same definitions now use the library. The library is owned by the tests: retuning the server's `abilities.json` doesn't move scenario numbers.
- Every round's outcome is written as an event log ("t1 Bruiser cleave G1: hit 5", "t2 Mira mend Hero: +6, revived", "end Hero bleeds out"). `expect.events` checks lines in order, and failures print the whole log. A new kind of outcome (a condition applied, a push, a reaction firing) needs one line in `ScenarioTests.EventLog` instead of a new `expect` key, and ordered checks are ready for 3B.6.

### 3. Integration tests: real Redis and the hub ✅

`server/tests/RpgGame.Api.IntegrationTests` (needs Docker; Testcontainers starts `postgres:17-alpine` and `redis:7-alpine`). CI runs it as its own step in `build-server`.

- **Redis** (`Redis/`): `RedisGameState` against a real Redis, one class per area (session: players, active encounter, map, pending monsters; planning: plans, lock-ins; combatant: HP, defeated/status/revive, cooldowns). Each test round-trips a read/write pair and pins the key name, Redis type and stored format from the key table in `CLAUDE.md`. `ReviveAsync` is checked to clear both the defeated set and the status entry.
- **Hub** (`Hub/`): `ApiFactory` runs the real `Program` (migrations included) with `TestDice` (scripted per test), no playback delay, and its own presets (`Presets/it-*.json`, so retuning shipped presets doesn't move the tests). `HubClient` registers a user over REST, connects over WebSockets with the JWT in the query string like the browser, records every broadcast as raw JSON and waits for them. Cases: a Quick Battle round with exact rolls through to the encounter ending (Redis cleared, Postgres `Ended`); an ability rejected over the wire and not stored; a hero downed, healed back up, then planning and locking in the next round.
- **Wire shape** (`ContractShape`): every broadcast a hub test receives is checked against `client/src/app/contracts/hub-contracts.generated.ts`: no field the client doesn't declare, no declared field missing, null only where the client allows it, through nested contracts, arrays and maps.
- A renamed Redis key or payload field fails tests in a few seconds (checked by breaking `encounter:{id}:status` and `ResolutionPayload.defeatedCombatantIds`).

For 3C, add a second `HubClient` in the same session and assert what each one receives.

### 4. Client test-data builders ✅

`client/src/app/testing/builders.ts`: `makePayload`, `makeTick`, `makeMove`, `makeAttack`, `makeHeal`, `makeEffect`, `makeAbilityUse`, `makeBleedout`, `makeQueue`, `makeQueuedAction`, `makePlanningCombatant`, `makeCombatantDto`, `makeCombatant` (the NGXS model), `makeAbility` / `makeTargeting`. Each returns a complete object with neutral defaults (names default to ids) and takes `Partial<T>` overrides. Every spec that built resolution payloads, combatants or abilities by hand now uses them (spec-local helpers such as `attack(a, b, hit)` stay, built on the shared builders), and so does the `/preview` sandbox's mock data. Checked by adding a required field to `AttackResult` and `PlanningCombatantDto`: the only compile errors were in `builders.ts`, for both the spec and app builds.

### 5. Shared plan-validation cases (client ⇄ server) ✅

`contracts/plan-validation-cases.json`: a small ability library (only what validation reads) and 59 cases. Each case has an optional profile (AP, movement range), loadout, cooldowns, start cell (or none), positions for moves to a target, a queue in compact text (`"move 3,0"`, `"ability twin_shot [a,b]"`, `"ability dash @9,0"`), and the expected result (`{ ap, cells }`, or `{ error }` with the server's exact message, the first rule broken). Covered: AP, shared movement, grid bounds, loadout, cooldown, once per round, target shapes, Dash / budget cell moves, the Charge estimate, and which rule is reported first.

- Server: `PlanValidatorTests.SharedCase` (a theory, one row per case; the file is linked into `RpgGame.Rules.Tests`).
- Client: `validatePlan` in `plan-queue.ts` walks the queue in the server's order using the existing helpers (`cooldownBlock`, `targetShapeError`, `entryCost`, `walkQueue`, `abilityCellError`) and returns the server's messages; `plan-validation-cases.spec.ts` runs every case through it.
- On its first run the table caught two disagreements, now fixed on the client: a Dash with no cell was reported before an AP overrun (the server checks AP first), and `abilityCellError`'s out-of-bounds message differed from the server's.

A new plan rule is a row (or a few) in the table, plus the rule on each side.

---

## Open

### 6. Seeded dice for E2E

**Problem.** E2E uses `RandomDice`, so it can only assert that something happened ("a Cleave line exists"), never the outcome. Tests that need a hit, a kill or a revive need lucky rolls or very forgiving presets.

**Build.** A setting such as `Dice:Seed` (or `Dice:Script` for a fixed list) that registers `SeededDice` / `ScriptedDice` instead of `RandomDice`, honoured only in `Development` and a new `E2E` environment, never in Production. `docker-compose.override.yml` and the CI e2e job set it. Optionally a per-preset script for tests that need an exact sequence.

**When.** As soon as an E2E test needs an exact outcome (a revive E2E would).

**Done when.** An E2E test can assert "Goblin 1 goes from 10 to 2" and pass every run.

### 7. `game-hub.service` spec

**Problem.** The service that wires SignalR events into NGXS has no spec. Its most delicate part is the list of events held back during playback (`PlanningPhaseStarted`, `PlayerLockedIn`, `AllLockedIn`, `CombatantDefeated`, `EncounterEnded`, `MonsterAdded`). The 3B.3 sprint doc names it as a risk: a new broadcast that isn't held shows up before its playback finishes.

**Build.** A spec with a fake `HubConnection` (capture the `on(...)` handlers): each event dispatches the right action; held events wait while `ResolutionPlaybackService.playing()` is true and replay in order afterwards; `ResolutionCompleted` goes to playback.

**When.** Before 3B.6 (reactions may add broadcasts) and before 3C.

**Done when.** Adding a broadcast without deciding whether it's held fails a spec.

---

## Not planned

- **Coverage percentage targets.** None of the gaps above would show up as low coverage; they're about real infrastructure and cross-boundary agreement.
- **More E2E tests by default.** Prefer the integration tests (item 3) and seeded dice (item 6) for anything that needs exact outcomes; keep E2E as the smoke test of each slice.

## Suggested order

| When | Items |
|---|---|
| Before 3B.6 | 7 (hub service spec) |
| Before 3C | 6 (seeded dice); per-player assertions on the item 3 hub harness |
