import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideStore, Store } from '@ngxs/store';
import { ResolutionPlaybackService } from './resolution-playback.service';
import { PLAYBACK_TIMING } from './playback-timeline';
import { EncounterState } from '../../state/encounter/encounter.state';
import { MapState } from '../../state/map/map.state';
import { FeedState } from '../../state/feed/feed.state';
import { ClearEncounter, PlanningPhaseStarted } from '../../state/encounter/encounter.actions';
import { MapInitialized } from '../../state/map/map.actions';
import { PlanningCombatantDto, ResolutionPayload } from '../../contracts/hub-contracts.generated';
import { makeAttack, makeMove, makePayload, makePlanningCombatant, makeTick } from '../../testing/builders';

describe('ResolutionPlaybackService', () => {
  let playback: ResolutionPlaybackService;
  let store: Store;

  const row = (id: string, currentHp = 10): PlanningCombatantDto => makePlanningCombatant({ id, currentHp });

  // Hero walks one cell and then strikes; the Goblin stands still and strikes back (and dies in tick 2).
  const resolution: ResolutionPayload = makePayload({
    defeatedCombatantIds: ['goblin'],
    ticks: [
      makeTick({
        tickIndex: 0,
        movements: [makeMove({ combatantId: 'hero', path: [{ x: 0, y: 0 }, { x: 1, y: 0 }] })],
        attacks: [makeAttack({ attackerId: 'goblin', targetId: 'hero', hit: true, damage: 3, targetNewHp: 7, feedText: 'goblin hits hero' })],
      }),
      makeTick({
        tickIndex: 1,
        attacks: [makeAttack({ attackerId: 'hero', targetId: 'goblin', hit: true, damage: 10, targetNewHp: 0, targetDefeated: true, targetStatus: 'dead', feedText: 'hero kills goblin' })],
      }),
    ],
  });

  const T = PLAYBACK_TIMING;
  const heroOf = () => store.selectSnapshot(EncounterState.combatants).find(c => c.id === 'hero')!;
  const goblinOf = () => store.selectSnapshot(EncounterState.combatants).find(c => c.id === 'goblin')!;
  const feed = () => store.selectSnapshot(FeedState.entries).map(e => e.text);

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideStore([EncounterState, MapState, FeedState])] });
    playback = TestBed.inject(ResolutionPlaybackService);
    store = TestBed.inject(Store);
    store.dispatch(new PlanningPhaseStarted(1, 0, [row('hero'), row('goblin')]));
    store.dispatch(new MapInitialized('enc', 20, 20, { hero: { x: 0, y: 0 }, goblin: { x: 2, y: 0 } }));
  });

  it('commits the final state to the store at once but shows it as the round plays', fakeAsync(() => {
    playback.receiveResolution(resolution);

    expect(store.selectSnapshot(MapState.positions)['hero']).toEqual({ x: 1, y: 0 });
    expect(heroOf().currentHp).toBe(7);
    expect(goblinOf().isDefeated).toBeTrue();
    expect(playback.playing()).toBeTrue();
    expect(playback.hpOf(heroOf())).toBe(10);
    expect(playback.isDefeated(goblinOf())).toBeFalse();
    playback.reset();
  }));

  it('keeps every combatant on the board from the first beat, movers or not', fakeAsync(() => {
    playback.receiveResolution(resolution);

    // Tick 1, first beat: the Hero has taken its step; the Goblin, who only attacks, is still on its cell.
    expect(playback.positions()).toEqual({ hero: { x: 1, y: 0 }, goblin: { x: 2, y: 0 } });
    expect(playback.tick()).toBe(1);
    playback.reset();
  }));

  it('reveals HP, auras and defeats tick by tick, then hands back to the store', fakeAsync(() => {
    playback.receiveResolution(resolution);
    expect(playback.hpOf(heroOf())).toBe(10);                  // moving: the hit hasn't landed yet
    expect(feed()).toEqual(['— Round 1 Resolution —', 'Tick 1', 'hero moves to (1, 0)']);

    tick(T.maxStepMs);                                          // tick 1 attacks
    expect(playback.hpOf(heroOf())).toBe(7);
    expect(playback.auras().map(a => `${a.combatantId}:${a.kind}`)).toEqual(['goblin:hit', 'hero:hurt']);
    expect(playback.floats().map(f => f.text)).toEqual(['-3']);
    expect(playback.isDefeated(goblinOf())).toBeFalse();      // the store already says defeated

    tick(T.attackMs);                                           // tick 2 attacks
    expect(playback.tick()).toBe(2);
    expect(playback.hpOf(goblinOf())).toBe(0);
    expect(playback.isDefeated(goblinOf())).toBeFalse();

    tick(T.attackMs);                                           // tick 2 defeats
    expect(playback.isDefeated(goblinOf())).toBeTrue();
    expect(feed()).toContain('hero kills goblin');

    tick(T.defeatMs);
    expect(playback.playing()).toBeFalse();
    expect(playback.hpOf(heroOf())).toBe(7);
    tick(T.floatMs);                                            // leftover floats expire
    expect(playback.floats()).toEqual([]);
    expect(playback.auras()).toEqual([]);
  }));

  it('holds events that arrive mid-playback until it ends, in order', fakeAsync(() => {
    const ran: string[] = [];
    playback.receiveResolution(resolution);
    playback.afterPlayback(() => ran.push('planning'));
    playback.afterPlayback(() => ran.push('locked in'));
    expect(ran).toEqual([]);

    tick(T.maxStepMs + 2 * T.attackMs + T.defeatMs);
    expect(ran).toEqual(['planning', 'locked in']);

    playback.afterPlayback(() => ran.push('now'));               // idle: runs straight away
    expect(ran).toContain('now');
    tick(T.floatMs);
  }));

  it('drops the playback and anything waiting on it when the encounter is cleared', fakeAsync(() => {
    const ran: string[] = [];
    playback.receiveResolution(resolution);
    playback.afterPlayback(() => ran.push('planning'));

    store.dispatch(new ClearEncounter());
    expect(playback.playing()).toBeFalse();

    tick(10_000);
    expect(ran).toEqual([]);
    expect(feed()).toEqual(['— Round 1 Resolution —', 'Tick 1', 'hero moves to (1, 0)']);
  }));
});
