# Smoke Test

How to check that the client ↔ SignalR ↔ API ↔ Redis/Postgres loop still works after a change.

## Fastest manual check — Quick Battle

Log in (⚡ Quick Register), pick a preset in the **⚡ Quick Battle** box and press **Start Quick Battle**. You control the whole party against AI enemies; no session, join code or characters needed. Click one of your tokens (or its tracker row) to choose who you're planning for, then **Lock In All**. The Quick Battle panel has Force Resolve, Restart and Leave. **Training Dummy** ends on the first hit, which makes it the quickest way to see a full battle through to the result screen. Presets live in `server/src/RpgGame.Api/QuickBattle/Presets/`.

**Abilities** (3B.3): the Bruiser starts next to a goblin. Click **Power Strike** in the action bar (the grid shows its reach and outlines legal targets), click the goblin, lock in. The feed reads "Bruiser's Power Strike hits …", the name floats over the Bruiser during playback, and next round the button shows ⏳ 2 and is disabled (the tracker shows the cooldown too). The Ogre uses Power Strike on you when it's ready. Esc cancels an armed ability. **Cleave** (Bruiser) shows its area and a **Use Cleave** button; it hits every enemy next to the Bruiser when it resolves, one roll each. **Twin Shot** (Scout) takes up to two picked enemies (numbered rings, **Done (1/2)** to fire at one). **Mend** (Skirmisher): with it armed, click the hurt Scout (starts on 5 HP) or yourself; playback shows a green +n, and a Downed hero healed with Mend gets back up ("UP") and plans again next round. The goblin Shaman mends its hurt allies. **Dash** (Scout, Skirmisher): walk your whole range, then arm Dash; the amber cells (up to 3 more, on top of your movement) are where it can go; click one, then shoot. **Charge** (Skirmisher): arm it and hover an enemy up to 6 cells away to see the expected run; click it, and in playback the Skirmisher runs next to it and strikes in the same tick ("+3 from moving" if it ran 2+ cells). The Ogre charges a hero in round 1.

**Conditions** (3B.4): click the Scout and arm **Mark**; its tooltip says what it applies ("Applies Marked … until end of next round"). Click Goblin 1, Lock In All. The two heroes who weren't planned defend, so the feed opens with "Bruiser is Guarded until the end of the round" (blue dot over the token). In the effects step Goblin 1 gets a purple aura with "Marked" and "Exposed" floating over it, the tracker row gets chips ("Marked · next round"), and a later hit on it reads "(+2 from conditions)". Next round the chips read "this round" and Guarded is gone; after that round they expire ("Goblin 1 is no longer Marked"). **Battle Cry** (Bruiser) gives itself and allies within 3 cells Emboldened (+2 damage); **Stoneskin** (Skirmisher) halves damage it takes from the next tick on. The goblin Shaman shouts Battle Cry. The round timeline's tick details show which part of a roll came from conditions. **Ongoing damage** (3B.4 PR 2): the Scout's **Firebolt** sets its target Burning on a hit; at the end of that round (and the next) an orange "🔥-n" floats over it, the HP drops and the feed reads "Ogre takes 4 damage from Burning (HP: 19 → 15)", before any bleedout lines. Goblin 2 opens with **Envenom** (Poisoned: 2 damage at the end of each of three rounds, "☠-2"). The Ogre (a Brute) sometimes shrugs off a harmful condition: "RESISTED" floats over it and the feed says "Ogre resists Burning". A burn that drops someone shows DOWN / fades them at the end of the round, and can end the fight.

## Automated (preferred) — Playwright E2E

`e2e/tests/quick-battle.spec.ts` is the quickest end-to-end check (one browser, a whole battle to victory, three-character planning, bleedout and Stabilize, an ability with its cooldown, Cleave + Mend, and Dash then shoot). `e2e/tests/enemy-ai.spec.ts` covers an AI monster. `e2e/tests/combat-loop.spec.ts` plays a real session with two isolated browser contexts (GM + player). It runs in CI on every PR (`e2e` job in `.github/workflows/ci.yml`) and takes ~10 seconds locally.

```bash
docker compose up -d --build          # anything serving the app on :4200 works
cd e2e
npm ci
npx playwright install chromium       # first time only
npm test                              # headless
npm run test:headed                   # watch both browsers drive the game
npm run test:ui                       # Playwright UI mode: step through, time-travel the DOM
```

Set `E2E_BASE_URL` to target something other than `http://localhost:4200` (for example the Angular dev server when the API runs on the host).

**Seeded dice.** `e2e/tests/seeded-dice.spec.ts` asserts exact rolls, so the API must run with `Dice:Seed=1`. `docker-compose.override.yml` (local `docker compose up`) and `docker-compose.e2e.yml` (CI: `docker compose -f docker-compose.yml -f docker-compose.e2e.yml up`) set it. For an API run on the host, set `Dice__Seed=1` (with `ASPNETCORE_ENVIRONMENT=Development`). The API refuses to start with a seed outside the Development and E2E environments.

**What it covers**

| Step | Asserts |
|---|---|
| GM quick-registers → creates campaign → starts session → connects → adds "Goblin" | REST auth/campaign/session endpoints, `JoinSession`, `AddMonster` staging |
| Player quick-registers → quick-creates a character → joins with the code | Character CRUD, `JoinSession`, `PlayerPresenceChanged` reaches the GM |
| GM starts encounter | `EncounterStarted` + `PlanningPhaseStarted` on both screens; player at (0,0), monster at (0,19) |
| Round 1: player plans a move to (3,3), locks in; GM locks in enemies | `PlanAction`, `LockIn`, `PlayerLockedIn` shown to GM, `LockInEnemies` triggers resolution |
| Resolution | `ResolutionCompleted` feed entry + token at (3,3) on **both** screens, round 2 planning begins |
| Round 2: player plans a ranged attack on the Goblin | Attack line appears in both feeds (hit/miss is random, so only the line is asserted), round 3 begins |

On failure, Playwright keeps a trace, screenshots, and video in `e2e/test-results/`; open the trace with `npx playwright show-trace <path>/trace.zip`. In CI they are uploaded as the `playwright-report` artifact along with container logs.

**Extending it:** helpers live in `e2e/tests/support/actors.ts` (`newActor`, `gmOpenSession`, `playerJoinSession`, `cell(page, x, y)`, `tokenAt`, …). Grid cells carry `data-x` / `data-y` attributes for targeting. Add a new spec per slice (e.g. `ap-budget.spec.ts` for 3B.2) rather than growing one giant test.

## Manual — when you want to eyeball it

1. `docker compose up --build`, open `http://localhost:4200` in two windows (one normal, one private so they don't share `sessionStorage`).
2. **Window A (GM):** ⚡ Quick Register → **GM** tab → name a campaign → Create Campaign → Start Session → note the join code → Connect to Session → add a monster.
3. **Window B (player):** ⚡ Quick Register → My Characters → ⚡ Quick Create Random → Back to Lobby → enter join code + display name, pick the character → Join Session. Window A should list the player under Connected Players.
4. **A:** Start Encounter. Both windows switch to the combat layout showing "Planning — Round 1".
5. **B:** click a green in-range cell (move) or the monster token (attack) → Lock In. **A** sees the ✓ in the tracker → Lock In Enemies (or ⚡ Force Resolve).
6. Both windows animate the resolution, add a "Round 1 Resolution" block to the Combat Log, and move on to Round 2.

For UI-only work with no server at all, use `http://localhost:4200/preview` (`EncounterPreviewComponent`), which seeds mock state and lets you toggle GM/player and planning/resolving.

## Known gaps

- Encounters never end: `EncounterEnded` is not broadcast yet, so rounds continue after one side is wiped out.
- The planning timer is 300s (`GameConstants.PlanningTimerSeconds`); timer-expiry resolution is not covered by E2E.
