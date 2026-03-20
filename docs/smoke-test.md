# 3-Tab Smoke Test

## Prerequisites
App is running (`docker-compose up` or dev server).

---

## Setup — All 3 Tabs

**All tabs:** Navigate to `http://localhost:4200` (`LobbyComponent`).
Click **⚡ Quick Register** on each tab.

> `quickRegister()` calls `DevService.randomUser()` → `POST /auth/register` → JWT stored in `sessionStorage` by `AuthService`.

---

## Tab 1 — GM Flow

1. Click the **GM** tab (`activeFlow = 'gm'`).
2. Enter a campaign name → click **Create Campaign**.
   > `CampaignService.createCampaign()` → `POST /campaigns`; stores `gmCampaignId`.
3. Click **Start Session**.
   > `CampaignService.startSession()` → `POST /campaigns/{id}/sessions`; displays the 6-char join code in the big `<div class="join-code">` box. Dispatches `SetGmSession` to `SessionState`.
4. Click **Connect to Session**.
   > `GameHubService.connect()` + `hub.joinSession(joinCode, 'GM', null)` → SignalR `JoinSession` on the hub. Adds GM to `session:{sessionId}:players` Redis hash. Broadcasts `PlayerPresenceChanged` → `SessionState.connectedPlayers` updates. `gmConnected = true`, **Start Encounter** button appears (disabled until players join).

---

## Tabs 2 & 3 — Player Flow (do each tab in sequence)

1. Click **My Characters** link → navigates to `/character` (`CharacterComponent`).
2. Click **⚡ Quick Create Random**.
   > `DevService.randomCharacter()` → `CharacterService.createCharacter()` → `POST /characters`. Dispatches `SetMyCharacter` + `LoadMyCharacters` to `CharacterState`.
3. Click **← Back to Lobby**.
4. On the **Player** tab, enter the join code from Tab 1, a display name, and select the character from the dropdown.
5. Click **Join Session**.
   > Dispatches `JoinSession` action to `SessionState`. `GameHubService.connect()` + `hub.joinSession(joinCode, displayName, characterId)` → SignalR `JoinSession` hub method. Server validates join code via DB, upserts player into Redis. Broadcasts `PlayerPresenceChanged` to all tabs → connected players list updates everywhere.

> After both players join, Tab 1's **Start Encounter** button becomes enabled.

---

## Tab 1 — Start Encounter

Click **Start Encounter**.
> `hub.startEncounter(joinCode)` → SignalR `StartEncounter` (GM-only).
> Server (`EncounterService`): rolls initiative (1d20 + DEX modifier via `CombatDice`), creates `Encounter` + `EncounterCombatant` rows in DB, seeds Redis (`session:{sessionId}:active_encounter`, `encounter:map:{encounterId}`, `character:live:{characterId}` hashes). Broadcasts `EncounterStarted` to all tabs.
> All tabs: `EncounterTrackerComponent` (initiative order + HP), `GridComponent` (20×20 map), and `FeedComponent` (combat log) appear.

---

## Playing a Turn

**On whichever tab has the active turn** (highest initiative first, shown highlighted in `EncounterTrackerComponent`):

The grid shows **"Your turn!"** with a green **End Turn** button (`isMyTurn$` observable resolves `true`). Other tabs show **"Waiting for [name]..."**.

**To move:** Click an empty cell on the grid.
> `GridComponent.onCellClick()` → empty cell → dispatches `SetPendingMove` (highlights cell blue) + `hub.moveToken(joinCode, combatantId, x, y)`.
> Server (`EncounterService.MoveToken`): validates Chebyshev distance ≤ 6, bounds 0–19, updates map JSON in Redis. Broadcasts `TokenMoved` → `MapState.positions` updates on all tabs.

**To attack:** Click an enemy token (red/dark, `enemy-token` class).
> `hub.declareAttack(joinCode, targetCombatantId)`.
> Server (`CombatService.DeclareAttack`): validates it's your turn, rolls damage by class (Fighter: 1d8, Rogue: 1d6, others: 1d4), decrements HP in `character:live:{id}` Redis hash, appends `GameEvent` to DB. Broadcasts `AttackResolved` (HP update on all tabs) and `CombatantDefeated` if HP ≤ 0 (token greys out, strikethrough in tracker).

**To end turn:** Click **End Turn**.
> `hub.endTurn(joinCode)`.
> Server (`EncounterService.EndTurn`): advances `activeTurnCombatantId` to next non-defeated combatant, or ends encounter if all defeated. Broadcasts `TurnAdvanced` (or `EncounterEnded`) → active turn indicator shifts to the next tab.

**Repeat** with the other player's tab until the encounter resolves.

---

## What This Tests End-to-End

| Layer | What's exercised |
|-------|-----------------|
| `AuthService` + `POST /auth/register` | Quick Register on all tabs |
| `CampaignService` + REST endpoints | Campaign create, session start |
| `CharacterService` + `GET\|POST /characters` | Character CRUD, `CharacterState` |
| `GameHubService` + SignalR `/hubs/game` | Connect, all hub methods |
| `SessionState` | Player presence, join code, GM flag |
| `EncounterState` | Combatant list, active turn, defeated |
| `MapState` | Token positions, pending move |
| `FeedState` | Combat log entries |
| Redis (via `RedisGameState`) | Players hash, encounter JSON, map JSON, HP hash, defeated set |
| DB (via `EncounterService`, `CombatService`) | Encounter + Combatant rows, GameEvent log |
| Real-time broadcast | All SignalR broadcasts reflected across all 3 tabs simultaneously |
