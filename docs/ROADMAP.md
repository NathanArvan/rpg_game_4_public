# Roadmap

Work is organized as vertical slices — each slice touches every layer it needs
(Postgres → Redis → SignalR → NGXS → Angular) before moving on.

Status: ✅ Done · 🔄 In Progress · ⬜ Not Started

---

## Phase 1 — Walking Skeleton

Goal: All five technologies talking to each other with real data. No game logic.

### ✅ Walking Skeleton
Delivers: Register/login, join a session by string ID, see connected players update in real time across browser tabs.

- [x] Postgres: Identity tables + `Sessions` table (EF migration)
- [x] Redis: `session:{id}:players` hash for live presence; SignalR backplane
- [x] SignalR Hub: `JoinSession` → `PlayerPresenceChanged` broadcast
- [x] NGXS: `SessionState` (sessionId, status, connectedPlayers[])
- [x] Angular: LobbyComponent — register/login form + player list

---

## Phase 2 — Core Game Loop

Goal: The minimum feature set that constitutes a playable session.
Each slice depends on the previous.

### ⬜ Slice 1: Campaign Foundation
Delivers: GM creates a campaign and starts a game session within it; players join via session code; GM role is enforced server-side; all presence is tied to real DB records.

Schema separation: `campaigns` is the long-lived container (ruleset, members); `game_sessions` is one play session within a campaign. A campaign can have many sessions over time.

- [ ] Postgres: `campaigns`, `campaign_members`, `game_sessions` migration
- [ ] Redis: update presence key to `session:{game_session_id}:players`; store sessionId from DB (not arbitrary string)
- [ ] SignalR: `CreateCampaign`, `StartSession` Hub methods; update `JoinSession` to look up `game_sessions` record and validate `campaign_members` role
- [ ] NGXS: add `isGm`, `campaignId`, `gameSessionId` to `SessionState`; `SessionStarted` action
- [ ] Angular: split LobbyComponent — GM flow (create campaign → start session → share code) vs. player flow (enter code → join)

### ✅ Slice 2: Character Sheet
Delivers: Players can create a character and view their own character sheet.

- [x] Postgres: `characters` table (name, race, class, level, base_stats JSONB, max_hp)
- [x] Redis: no new keys yet
- [x] SignalR: not needed — pure HTTP REST for character CRUD
- [x] NGXS: new `MyCharacterState` slice (characterId, name, currentHp, maxHp, base stats)
- [x] Angular: `/character` route + character sheet component

### ⬜ Slice 3: Encounter Setup
Delivers: GM starts an encounter; all players roll initiative; initiative tracker renders and updates live.

- [ ] Postgres: `encounters` + `encounter_combatants` migration
- [ ] Redis: `encounter:live:{encounter_id}` key (status, round, initiativeOrder, activeTurnCombatantId)
- [ ] SignalR: `StartEncounter`, `RollInitiative` → `EncounterStarted`, `InitiativeRolled` broadcasts
- [ ] NGXS: new `EncounterState` slice (encounterId, status, round, initiativeOrder, activeTurnCombatantId)
- [ ] Angular: initiative tracker component; GM-only "Start Encounter" control

### ⬜ Slice 4: Movement
Delivers: Player moves token on grid; all clients see the move in real time. First test of optimistic UI.

- [ ] Postgres: no new tables (position is ephemeral during encounter)
- [ ] Redis: `encounter:map:{encounter_id}` key (grid dims, positions by combatantId, fog, AoE effects)
- [ ] SignalR: `MoveToken` → `TokenMoved` broadcast (with movement validation server-side)
- [ ] NGXS: new `MapState` slice (gridWidth/Height, positions, fogOfWar, pendingMovePath for local optimistic preview)
- [ ] Angular: grid component (CSS Grid); token rendering; optimistic move + rollback on `ActionRejected`

### ✅ Slice 5: Basic Attack
Delivers: Player declares attack → dice roll → HP updates → feed entry appears for all clients. Full four-layer exercise under real game logic.

- [x] Postgres: `game_events` table (append-only log, written async after each resolved action)
- [x] Redis: `character:live:{character_id}` (currentHp, conditions); `monster:live:{encounter_id}:{combatant_id}` (currentHp, isDefeated)
- [x] SignalR: `DeclareAttack` → `AttackResolved`, `CombatantDefeated` broadcasts; `ActionRejected` for invalid attempts
- [x] NGXS: update `EncounterState` monster HP; new `FeedState` slice (entries[], unreadCount)
- [x] Angular: attack action buttons on active turn; feed/log panel showing roll results

---

## Phase 3 — Feature Depth

Define slice details after Phase 2 is complete. Rough shape:

- Spells — cast spell, slot tracking, multi-target
- AoE effects — visual overlay on map, area targeting
- Conditions — poisoned/stunned/etc., duration tracking, condition interactions
- Saving throws — save vs. DC mechanic
- Fog of war — GM reveal cells, vision ranges
- Inventory — equip items, item stat effects
- GM tools — HP override, hidden rolls, NPC control
- Death saves — unconscious state, stabilization

---

## Phase 4 — Hardening

- Reconnection resilience — drop and rejoin mid-combat without losing state
- Conflict resolution — Redis distributed lock (Redlock) on encounter during turn processing
- Redis persistence — RDB snapshot interval config in docker-compose
- Load testing — simulate 5–10 concurrent WebSocket connections under combat load
- Auth audit — confirm all GM-only Hub methods are enforced server-side

---

## Design Decisions Log

| Decision | Choice | Rationale |
|---|---|---|
| Map rendering | CSS Grid (for now) | Simplest; fine for ≤20×20 grids; defer canvas/PixiJS until performance need is real |
| Shared type contracts | Manual (keep in sync) | NSwag/TypeGen adds tooling overhead; revisit in Phase 3 |
| Redis persistence | RDB snapshots (60s interval) | Acceptable data loss window for a game session |
| Migration tool | EF Core migrations | Native .NET, auto-runs on API startup in Docker |
| SignalR backplane | Redis (already in stack) | Required for multi-container safety; configured from day one |
