# Testing Roadmap

> **Purpose:** Planned improvements to the test suite, so it keeps up with the detail abilities are adding and with the next slices (3B.4 conditions, 3B.6 reactions, 3M maps, 3C hidden information). Hand an item to Claude Code alongside `CLAUDE.md`. Each item says what's wrong today, what to build, when it should land relative to the roadmap, and how to know it's done.
>
> **Status (2026-11-22):** items 1–6 are done (multi-round scenarios, shared ability library and the event log; real-Redis and hub integration tests; client test-data builders; shared client/server plan-validation cases; seeded dice for E2E). Item 7 is open.

---

## Where the suite stands

| Layer | Size | Time | Good at | Weak at |
|---|---|---|---|---|
| Rules: unit tests + JSON scenarios | ~196 tests, 50 scenarios | ~1 s | Exact dice, readable failures, scenarios without C# | (fixed by items 1–2: several rounds, repeated ability blocks, new checks needing C#) |
| Api services (xUnit + Moq + EF InMemory) | ~222 tests | ~8 s | Characterization tests pin feed text, roll order and write order | Redis and the hub are mocked (covered by the integration layer) |
| Api integration (Testcontainers Postgres + Redis, `WebApplicationFactory` + `HubConnection`) | 37 tests | ~5 s + ~20 s container start | Real Redis keys and formats, whole rounds over the wire with exact rolls, wire JSON vs the client's TS contracts | One user per test so far (no per-player assertions yet) |
| Client unit tests (Karma) | ~183 specs | ~40 s with compile | Pure modules (`plan-queue`, `playback-timeline`, `round-lanes`, `feed-format`); contract fixtures come from shared builders | `game-hub.service` has no spec |
| E2E (Playwright) | 9 tests | ~1.5 min local, ~3.5 min CI | Proves the stack works together; exact outcomes with seeded dice | Tied to preset layouts, awkward to run locally |

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

### 6. Seeded dice for E2E ✅

- **Setting.** `Dice:Seed` (env `Dice__Seed`), read by `DiceSetup`. It's honoured only in the Development and E2E environments; anywhere else the API refuses to start. `docker-compose.override.yml` (local) and a new `docker-compose.e2e.yml` (CI: E2E environment on the production-style stack) set it to 1.
- **Per-round seeding.** `RoundSeededDice.ForRound(n)` gives each round's resolution its own `SeededDice`, so a round's rolls depend only on the seed and round number, not on what other tests or encounters rolled first.
- **Stable order (the bigger fix).** Seeding alone wasn't enough. The same plan still rolled differently from run to run, because three things followed random combatant GUIDs:
  - EF loaded an encounter's combatants ordered by Id, so the AI planned in GUID order.
  - Redis stores the plans hash as a hashtable (its values are longer than 64 bytes), so `HGETALL` returned plans in hash order of their GUID keys.
  - Auto targets (Cleave) broke distance ties by id.

  Now `EncounterCombatant.SortOrder` (new column and migration) records creation order, combatants load in it, `ResolutionService` hands plans to the resolver in that order, and auto-target ties keep the state's order. This also makes production roll order and same-tick tiebreaks repeatable, where before they were effectively random.
- **Cold-start race (found on the way).** Quick Battle's `EncounterStarted` arrives before `StartQuickBattle` returns the join code. A fast first click could plan with an empty join code (or, on Restart, the previous battle's), and the server rejected it. The lobby now shows combat only once the join code is known, and starting a battle clears the old session first.
- **Test.** `e2e/tests/seeded-dice.spec.ts` plays the Abilities preset's Cleave + Mend round and asserts exact feed lines and HP (Goblin 2 10 → 5, Ogre 28 → 21, Scout 5 → 10, Bruiser 13/24). It passed every run, including the first one after an API restart.

---

## Open

### 7. `game-hub.service` spec

**Problem.** The service that wires SignalR events into NGXS has no spec. Its most delicate part is the list of events held back during playback (`PlanningPhaseStarted`, `PlayerLockedIn`, `AllLockedIn`, `CombatantDefeated`, `EncounterEnded`, `MonsterAdded`). The 3B.3 sprint doc names it as a risk: a new broadcast that isn't held shows up before its playback finishes.

**Build.** A spec with a fake `HubConnection` (capture the `on(...)` handlers): each event dispatches the right action; held events wait while `ResolutionPlaybackService.playing()` is true and replay in order afterwards; `ResolutionCompleted` goes to playback.

**When.** Before 3B.6 (reactions may add broadcasts) and before 3C.

**Done when.** Adding a broadcast without deciding whether it's held fails a spec.

---

## Not planned

- **Coverage percentage targets.** None of the gaps above would show up as low coverage; they're about real infrastructure and cross-boundary agreement.
- **More E2E tests by default.** Prefer the integration tests (item 3) for exact outcomes; when it has to be E2E, seeded dice (item 6) make them exact; keep E2E as the smoke test of each slice.

## Suggested order

| When | Items |
|---|---|
| Before 3B.6 | 7 (hub service spec) |
| Before 3C | per-player assertions on the item 3 hub harness |
