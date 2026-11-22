import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { Actions, ActionStatus, provideStore } from '@ngxs/store';
import { BROADCAST_TIMING, GameHubService, HUB_CONNECTION_FACTORY, HubEventName } from './game-hub.service';
import { ResolutionPlaybackService } from './resolution-playback.service';
import { HubEvents, PlannedActionArray } from '../../contracts/hub-contracts.generated';
import { SessionState } from '../../state/session/session.state';
import { EncounterState } from '../../state/encounter/encounter.state';
import { MapState } from '../../state/map/map.state';
import { FeedState } from '../../state/feed/feed.state';
import { PlayerPresenceChanged } from '../../state/session/session.actions';
import {
  AllLockedIn, CombatantDefeated, EncounterEnded, EncounterStarted, MonsterAdded, PlanningPhaseStarted, PlayerLockedIn,
} from '../../state/encounter/encounter.actions';
import { MapInitialized } from '../../state/map/map.actions';
import { AddFeedBatch } from '../../state/feed/feed.actions';
import { BATTLE_START_LINE } from '../../features/encounter/feed/feed-format';
import { makeAttack, makeCombatantDto, makePayload, makePlanningCombatant, makeTick } from '../../testing/builders';

/** Stands in for the SignalR connection: keeps the registered handlers so a spec can play broadcasts. */
class FakeConnection {
  readonly handlers = new Map<string, (payload: unknown) => void>();
  state = HubConnectionState.Disconnected;
  readonly start = jasmine.createSpy('start').and.callFake(async () => { this.state = HubConnectionState.Connected; });
  readonly invoke = jasmine.createSpy('invoke').and.resolveTo(undefined);

  on(name: string, handler: (payload: unknown) => void): void { this.handlers.set(name, handler); }
  onclose(): void {}
  onreconnecting(): void {}
  onreconnected(): void {}

  /** The server broadcasts `name`. */
  emit(name: string, payload: unknown): void {
    const handler = this.handlers.get(name);
    if (!handler) throw new Error(`no handler registered for ${name}`);
    handler(payload);
  }
}

// One payload per broadcast, and the actions each one should dispatch (in order).
const hero = makePlanningCombatant({ id: 'hero' });
const payloads = {
  presence: { userId: 'u1', displayName: 'Ann', characterId: null, isOnline: true },
  started: {
    encounterId: 'enc-1',
    combatants: [makeCombatantDto({ id: 'hero' })],
    map: { gridWidth: 20, gridHeight: 20, positions: { hero: { x: 1, y: 2 } }, fogOfWar: [] },
  },
  planning: { roundNumber: 2, planningTimerDurationSeconds: 0, combatants: [hero], apCosts: { move: 1 } },
  lockedIn: { characterName: 'Hero' },
  rejected: { reason: 'Not enough AP' },
  defeated: { combatantId: 'goblin' },
  ended: { encounterId: 'enc-1', winningSide: 'party', finalRoundNumber: 3 },
  monster: { monster: makePlanningCombatant({ id: 'm1', combatantType: 'monster' }), spawnPosition: { x: 0, y: 19 } },
};

describe('GameHubService', () => {
  let hub: GameHubService;
  let connection: FakeConnection;
  let playback: ResolutionPlaybackService;
  let dispatched: object[];

  beforeEach(() => {
    connection = new FakeConnection();
    TestBed.configureTestingModule({
      providers: [
        provideStore([SessionState, EncounterState, MapState, FeedState]),
        { provide: HUB_CONNECTION_FACTORY, useValue: () => connection as unknown as HubConnection },
      ],
    });
    dispatched = [];
    TestBed.inject(Actions).subscribe(ctx => { if (ctx.status === ActionStatus.Dispatched) dispatched.push(ctx.action); });
    hub = TestBed.inject(GameHubService);
    playback = TestBed.inject(ResolutionPlaybackService);
  });

  /** Dispatched actions of these classes, in order (playback dispatches others too). */
  const only = (...types: (abstract new (...args: never[]) => object)[]) =>
    dispatched.filter(a => types.some(t => a instanceof t));

  describe('which broadcasts wait for playback', () => {
    // Change this table only on purpose: a broadcast applied mid-playback jumps the screen ahead of the animation.
    const expected: typeof BROADCAST_TIMING = {
      PlayerPresenceChanged: 'now',
      EncounterStarted: 'now',
      ActionRejected: 'now',
      ResolutionCompleted: 'starts_playback',
      PlanningPhaseStarted: 'after_playback',
      PlayerLockedIn: 'after_playback',
      AllLockedIn: 'after_playback',
      CombatantDefeated: 'after_playback',
      EncounterEnded: 'after_playback',
      MonsterAdded: 'after_playback',
    };

    it('decides for every broadcast in the hub contracts', () => {
      expect(Object.keys(BROADCAST_TIMING).sort()).toEqual(Object.keys(HubEvents.Events).sort());
      expect({ ...BROADCAST_TIMING }).toEqual(expected);
    });

    it('registers a handler for every broadcast', () => {
      expect([...connection.handlers.keys()].sort()).toEqual(Object.values(HubEvents.Events).sort());
    });
  });

  describe('with nothing playing, each broadcast dispatches its actions at once', () => {
    const cases: [HubEventName, unknown, () => object[]][] = [
      ['PlayerPresenceChanged', payloads.presence, () => [new PlayerPresenceChanged(payloads.presence)]],
      ['EncounterStarted', payloads.started, () => [
        new EncounterStarted('enc-1', payloads.started.combatants),
        new AddFeedBatch([BATTLE_START_LINE]),
        new MapInitialized('enc-1', 20, 20, payloads.started.map.positions),
      ]],
      ['PlanningPhaseStarted', payloads.planning, () => [new PlanningPhaseStarted(2, 0, [hero], { move: 1 })]],
      ['PlayerLockedIn', payloads.lockedIn, () => [new PlayerLockedIn('Hero')]],
      ['AllLockedIn', {}, () => [new AllLockedIn()]],
      ['ActionRejected', payloads.rejected, () => [new AddFeedBatch(['⚠ Not enough AP'])]],
      ['CombatantDefeated', payloads.defeated, () => [new CombatantDefeated('goblin')]],
      ['EncounterEnded', payloads.ended, () => [
        new EncounterEnded('party', 3),
        new AddFeedBatch(['— Encounter over after round 3: the party wins —']),
      ]],
      ['MonsterAdded', payloads.monster, () => [new MonsterAdded(payloads.monster)]],
    ];

    for (const [event, payload, actions] of cases) {
      it(event, () => {
        connection.emit(HubEvents.Events[event], payload);
        expect(dispatched).toEqual(actions());
      });
    }

    it('names the winner in the end-of-encounter line', () => {
      connection.emit(HubEvents.Events.EncounterEnded, { ...payloads.ended, winningSide: 'enemy' });
      connection.emit(HubEvents.Events.EncounterEnded, { ...payloads.ended, winningSide: 'draw' });
      expect(only(AddFeedBatch)).toEqual([
        new AddFeedBatch(['— Encounter over after round 3: the enemies win —']),
        new AddFeedBatch(['— Encounter over after round 3: draw —']),
      ]);
    });
  });

  it('hands ResolutionCompleted to playback', () => {
    spyOn(playback, 'receiveResolution').and.callThrough();
    const resolution = makePayload({ ticks: [makeTick()] });

    connection.emit(HubEvents.Events.ResolutionCompleted, resolution);

    expect(playback.receiveResolution).toHaveBeenCalledWith(resolution);
    expect(playback.playing()).toBeTrue();
  });

  describe('during playback', () => {
    // One tick with an attack: a few seconds of playback.
    const resolution = makePayload({ ticks: [makeTick({ attacks: [makeAttack({ attackerId: 'hero', targetId: 'goblin', hit: true, damage: 3 })] })] });
    const held = [PlanningPhaseStarted, PlayerLockedIn, AllLockedIn, CombatantDefeated, EncounterEnded, MonsterAdded];

    it('holds the waiting broadcasts and replays them in arrival order once it ends', fakeAsync(() => {
      connection.emit(HubEvents.Events.ResolutionCompleted, resolution);
      expect(playback.playing()).toBeTrue();

      connection.emit(HubEvents.Events.CombatantDefeated, payloads.defeated);
      connection.emit(HubEvents.Events.MonsterAdded, payloads.monster);
      connection.emit(HubEvents.Events.EncounterEnded, payloads.ended);
      connection.emit(HubEvents.Events.PlanningPhaseStarted, payloads.planning);
      connection.emit(HubEvents.Events.PlayerLockedIn, payloads.lockedIn);
      connection.emit(HubEvents.Events.AllLockedIn, {});
      expect(only(...held)).toEqual([]);

      tick(60_000);

      expect(playback.playing()).toBeFalse();
      expect(only(...held)).toEqual([
        new CombatantDefeated('goblin'),
        new MonsterAdded(payloads.monster),
        new EncounterEnded('party', 3),
        new PlanningPhaseStarted(2, 0, [hero], { move: 1 }),
        new PlayerLockedIn('Hero'),
        new AllLockedIn(),
      ]);
    }));

    it('applies presence and rejected plans at once', fakeAsync(() => {
      connection.emit(HubEvents.Events.ResolutionCompleted, resolution);

      connection.emit(HubEvents.Events.PlayerPresenceChanged, payloads.presence);
      connection.emit(HubEvents.Events.ActionRejected, payloads.rejected);

      expect(playback.playing()).toBeTrue();
      expect(only(PlayerPresenceChanged)).toEqual([new PlayerPresenceChanged(payloads.presence)]);
      expect(dispatched).toContain(new AddFeedBatch(['⚠ Not enough AP']));
      tick(60_000);
    }));

    it('queues a second resolution behind the first, and what arrived between them in order', fakeAsync(() => {
      connection.emit(HubEvents.Events.ResolutionCompleted, resolution);
      connection.emit(HubEvents.Events.PlanningPhaseStarted, payloads.planning);
      spyOn(playback, 'receiveResolution').and.callThrough();
      connection.emit(HubEvents.Events.ResolutionCompleted, resolution);
      connection.emit(HubEvents.Events.PlayerLockedIn, payloads.lockedIn);

      tick(60_000);

      // Round 2 opened before the second resolution played; the lock-in after it waited for it too.
      expect(only(PlanningPhaseStarted, PlayerLockedIn)).toEqual([
        new PlanningPhaseStarted(2, 0, [hero], { move: 1 }),
        new PlayerLockedIn('Hero'),
      ]);
      expect(playback.playing()).toBeFalse();
    }));
  });

  describe('hub methods', () => {
    const queue: PlannedActionArray = { actions: [] };
    const calls: [string, () => Promise<unknown>, string, unknown[]][] = [
      ['joinSession', () => hub.joinSession('ABC123', 'Ann', 'ch1'), 'JoinSession', ['ABC123', 'Ann', 'ch1']],
      ['startEncounter', () => hub.startEncounter('ABC123'), 'StartEncounter', ['ABC123']],
      ['planAction', () => hub.planAction('ABC123', queue, 'c1'), 'PlanAction', ['ABC123', queue, 'c1']],
      ['getQuickBattlePresets', () => hub.getQuickBattlePresets(), 'GetQuickBattlePresets', []],
      ['startQuickBattle', () => hub.startQuickBattle('skirmish'), 'StartQuickBattle', ['skirmish']],
      ['lockIn', () => hub.lockIn('ABC123'), 'LockIn', ['ABC123']],
      ['forceResolve', () => hub.forceResolve('ABC123'), 'ForceResolve', ['ABC123']],
      ['addMonster', () => hub.addMonster('ABC123', 'Goblin', true), 'AddMonster', ['ABC123', 'Goblin', true, null]],
      ['planEnemyAction', () => hub.planEnemyAction('ABC123', 'm1', queue), 'PlanEnemyAction', ['ABC123', 'm1', queue]],
      ['lockInEnemies', () => hub.lockInEnemies('ABC123'), 'LockInEnemies', ['ABC123']],
    ];

    for (const [name, call, method, args] of calls) {
      it(`${name} invokes ${method}`, async () => {
        await call();
        expect(connection.invoke).toHaveBeenCalledWith(method, ...args);
      });
    }

    it('connects only when disconnected', async () => {
      await hub.connect();
      await hub.connect();
      expect(connection.start).toHaveBeenCalledTimes(1);
      expect(hub.isConnected()).toBeTrue();
    });
  });
});
