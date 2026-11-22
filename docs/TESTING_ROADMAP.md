# Testing Roadmap

> **Purpose:** Planned improvements to the test suite, so it keeps up with the detail abilities are adding and with the next slices (3B.4 conditions, 3B.6 reactions, 3M maps, 3C hidden information). Hand an item to Claude Code alongside `CLAUDE.md`. Each item says what's wrong today, what to build, when it should land relative to the roadmap, and how to know it's done.
>
> **Status (2026-11-22):** items 1 and 2 are done (multi-round scenarios, shared ability library and the event log). Items 3–7 are open.

---

## Where the suite stands

| Layer | Size | Time | Good at | Weak at |
|---|---|---|---|---|
| Rules: unit tests + JSON scenarios | ~196 tests, 50 scenarios | ~1 s | Exact dice, readable failures, scenarios without C# | (fixed by items 1–2: several rounds, repeated ability blocks, new checks needing C#) |
| Api services (xUnit + Moq + EF InMemory) | ~222 tests | ~8 s | Characterization tests pin feed text, roll order and write order | Redis and the hub are always mocked |
| Client unit tests (Karma) | ~164 specs | ~40 s with compile | Pure modules (`plan-queue`, `playback-timeline`, `round-lanes`, `feed-format`) | `game-hub.service` has no spec; hand-written fixtures break when a contract grows |
| E2E (Playwright) | 7 tests | ~1.3 min local, ~3.5 min CI | Proves the stack works together | Random dice, tied to preset layouts, awkward to run locally |

The gap is between the mocked service tests and E2E: nothing fast checks the real Redis code, the hub, JSON on the wire, or what each client receives. Items 3, 6 and 7 close it.

---

## Done

### 1. Multi-round scenarios ✅

A scenario can list `"rounds"`, each with its own `plans`, `dice` and `expect`; each round starts from the state the previous one left (HP, positions, statuses, bleedout, cooldowns). Needed for 3B.4 durations. See `server/tests/RpgGame.Rules.Tests/Scenarios/README.md`.

### 2. Shared ability library and the event log ✅

- `Scenarios/library/abilities.json` holds test versions of the shipped abilities; a scenario's own `abilities` block overrides an entry. The 26 scenarios that pasted the same definitions now use the library. The library is owned by the tests: retuning the server's `abilities.json` doesn't move scenario numbers.
- Every round's outcome is written as an event log ("t1 Bruiser cleave G1: hit 5", "t2 Mira mend Hero: +6, revived", "end Hero bleeds out"). `expect.events` checks lines in order, and failures print the whole log. A new kind of outcome (a condition applied, a push, a reaction firing) needs one line in `ScenarioTests.EventLog` instead of a new `expect` key, and ordered checks are ready for 3B.6.

---

## Open

### 3. Integration tests: real Redis and the hub

**Problem.** Every Api test mocks `IRedisGameState` with Moq (57 setups in `PlanningServiceTests` alone). `RedisGameState` (282 lines) and `GameHub` (213 lines) have no direct tests. A wrong key name or a serialization mistake passes every unit test; only E2E could catch it, and only for paths E2E plays. The 3B.3 revive is an example: unit tests only check that `ReviveAsync` was called on the mock, and no E2E test revives anyone. Loose mocks also return defaults for anything not set up (`IsDefeatedAsync` → false), which can hide mistakes. 3C (per-player hidden information) can't be tested at all without a hub-level harness.

**Build.**
- **Redis tests:** a test class per `RedisGameState` area (encounter, planning, lock-ins, defeated/status/revive, cooldowns, HP) against a real Redis via `Testcontainers.Redis` (GitHub's ubuntu runners and the dev sandbox both have Docker). Round-trip each read/write pair, and check that `ReviveAsync` clears both the defeated set and the status entry.
- **Hub harness:** `WebApplicationFactory<Program>` plus a real `HubConnection` client. Replace `IDiceRoller` with `ScriptedDice`, use a Postgres container (startup runs `MigrateAsync`, which EF InMemory can't), and a Redis container. Program uses top-level statements, so add `public partial class Program;` (or `InternalsVisibleTo`).
- **First cases:** start a Quick Battle → `PlanAction` → `LockIn` → `ResolutionCompleted` with exact rolls; an ability rejected over the wire; a revive round followed by the revived hero planning next round; a JSON-shape check that `ResolutionCompleted` deserializes into the client contract field names.
- Keep it in a separate test project (`RpgGame.Api.IntegrationTests`) so `dotnet test tests/RpgGame.Rules.Tests` and the unit tests stay instant. CI runs it in `build-server`.

**When.** Redis tests before 3B.4 (conditions add Redis state). The hub harness before 3C (it's the only place to assert "player A doesn't receive B's hidden data").

**Done when.** A broken Redis key name or a renamed payload field fails a test in under a minute of CI, without E2E.

### 4. Client test-data builders

**Problem.** Specs build `ResolutionPayload`, `TickResult`, `AttackResult` and `CombatantQueueDto` as hand-written object literals. 3B.3 PR 2 added two fields and broke 7 files, fixed with a regex. Every slice that grows the contract (conditions, push, reactions, per-player payloads) will do it again.

**Build.** One `client/src/app/testing/builders.ts` with `makePayload`, `makeTick`, `makeAttack`, `makeHeal`, `makeQueue`, `makeCombatant`, `makeAbility` (defaults + `Partial<T>` overrides). Move the copies in `playback-timeline.spec`, `round-lanes.spec`, `round-timeline.component.spec`, `resolution-playback.service.spec`, `grid.component.spec` and `encounter.state.spec` onto it. The preview component's mock data can use it too.

**When.** The next PR that changes a hub contract (3B.3 PR 3 adds `MovementOutcome.AbilityId`). Small; can ride along.

**Done when.** Adding a required field to a resolution contract means editing one builder.

### 5. Shared plan-validation cases (client ⇄ server)

**Problem.** `state/encounter/plan-queue.ts` mirrors `PlanValidator` by convention only. Every rule needs a twin and a spec on each side, and nothing checks they agree; drift means the UI offers plans the server rejects (or blocks ones it would accept).

**Build.** A JSON table, e.g. `contracts/plan-validation-cases.json`: each case has a profile (AP, movement range, abilities), cooldowns, a start cell, a queue and the expected result (valid / error message). `PlanValidatorTests` runs it as a theory; `plan-queue.spec.ts` runs it through a client `validatePlan` that wraps the existing helpers (`canAffordEntry`, `abilityUnavailableReason`, `targetShapeError`, move budget). Start with the current rules: AP, shared movement, loadout, cooldown, once per round, target shapes.

**When.** Before or with 3B.3 PR 3: movement abilities add validator rules (budget vs off-budget moves, `to: cell` within `maxCells`).

**Done when.** A new plan rule is one table row plus an implementation on each side, and a disagreement fails CI.

### 6. Seeded dice for E2E

**Problem.** E2E uses `RandomDice`, so it can only assert that something happened ("a Cleave line exists"), never the outcome. Tests that need a hit, a kill or a revive need lucky rolls or very forgiving presets.

**Build.** A setting such as `Dice:Seed` (or `Dice:Script` for a fixed list) that registers `SeededDice` / `ScriptedDice` instead of `RandomDice`, honoured only in `Development` and a new `E2E` environment, never in Production. `docker-compose.override.yml` and the CI e2e job set it. Optionally a per-preset script for tests that need an exact sequence.

**When.** With item 3 (both swap `IDiceRoller`) or as soon as an E2E test needs an exact outcome (a revive E2E would).

**Done when.** An E2E test can assert "Goblin 1 goes from 10 to 2" and pass every run.

### 7. `game-hub.service` spec

**Problem.** The service that wires SignalR events into NGXS has no spec. Its most delicate part is the list of events held back during playback (`PlanningPhaseStarted`, `PlayerLockedIn`, `AllLockedIn`, `CombatantDefeated`, `EncounterEnded`, `MonsterAdded`). The 3B.3 sprint doc names it as a risk: a new broadcast that isn't held shows up before its playback finishes.

**Build.** A spec with a fake `HubConnection` (capture the `on(...)` handlers): each event dispatches the right action; held events wait while `ResolutionPlaybackService.playing()` is true and replay in order afterwards; `ResolutionCompleted` goes to playback.

**When.** Before 3B.6 (reactions may add broadcasts) and before 3C.

**Done when.** Adding a broadcast without deciding whether it's held fails a spec.

---

## Not planned

- **Coverage percentage targets.** None of the gaps above would show up as low coverage; they're about real infrastructure and cross-boundary agreement.
- **More E2E tests by default.** Prefer items 3 and 6 for anything that needs exact outcomes; keep E2E as the smoke test of each slice.

## Suggested order

| When | Items |
|---|---|
| With 3B.3 PR 3 | 4 (builders), 5 (shared plan cases) |
| Before 3B.4 | 3a (Redis tests) |
| Before 3B.6 | 7 (hub service spec) |
| Before 3C | 3b (hub harness), 6 (seeded dice) |
