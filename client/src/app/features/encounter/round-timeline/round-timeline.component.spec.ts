import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideStore, Store } from '@ngxs/store';
import { RoundTimelineComponent } from './round-timeline.component';
import { ResolutionPlaybackService } from '../../../core/services/resolution-playback.service';
import { PLAYBACK_TIMING } from '../../../core/services/playback-timeline';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { MapState } from '../../../state/map/map.state';
import { FeedState } from '../../../state/feed/feed.state';
import { ClearEncounter, PlanningPhaseStarted } from '../../../state/encounter/encounter.actions';
import { MapInitialized } from '../../../state/map/map.actions';
import { AttackResult, PlanningCombatantDto, ResolutionPayload } from '../../../contracts/hub-contracts.generated';

describe('RoundTimelineComponent', () => {
  let fixture: ComponentFixture<RoundTimelineComponent>;
  let playback: ResolutionPlaybackService;
  let store: Store;

  const row = (id: string): PlanningCombatantDto =>
    ({ id, userId: 'me', characterName: id, currentHp: 10, maxHp: 10, isDefeated: false, combatantType: 'player', isAiControlled: false,
       profileId: 'test', profileName: 'Test', movementRange: 6, maxAp: 4, status: 'standing', bleedoutRoundsLeft: 0, abilities: [], cooldowns: {} });

  const hit = (attackerId: string, targetId: string, d20: number): AttackResult => ({
    attackerId, attackerName: attackerId, targetId, targetName: targetId, attackType: 'melee_attack', hit: true, damage: 2,
    targetNewHp: 8, targetDefeated: false, wasted: false, feedText: `${attackerId} hits ${targetId}`,
    roll: { d20, accuracy: 1, targetDodge: 10 }, wastedReason: null, targetStatus: 'standing', finishingBlow: false, abilityId: null,
  });

  // Two combatants trade two melee attacks each (4 AP apiece): two ticks.
  const melee = (target: string) => ({ type: 'melee_attack', apCost: 2, targetCombatantId: target, targetCell: null, abilityId: null, targetCombatantIds: null });
  const resolution: ResolutionPayload = {
    newRoundNumber: 3, defeatedCombatantIds: [], updatedCombatants: [], endOfRound: [], revivedCombatantIds: [],
    queues: [
      { combatantId: 'goblin', combatantName: 'goblin', maxAp: 4, actions: [melee('hero'), melee('hero')] },
      { combatantId: 'hero', combatantName: 'hero', maxAp: 4, actions: [melee('goblin'), melee('goblin')] },
    ],
    ticks: [
      { tickIndex: 0, movements: [], attacks: [hit('hero', 'goblin', 11), hit('goblin', 'hero', 12)], effects: [], abilityUses: [], heals: [] },
      { tickIndex: 1, movements: [], attacks: [hit('hero', 'goblin', 13), hit('goblin', 'hero', 14)], effects: [], abilityUses: [], heals: [] },
    ],
  };

  const el = () => fixture.nativeElement as HTMLElement;
  const text = (selector: string) => Array.from(el().querySelectorAll(selector)).map(e => e.textContent!.trim());
  const render = () => fixture.detectChanges();

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RoundTimelineComponent],
      providers: [provideStore([EncounterState, MapState, FeedState])],
    }).compileComponents();

    store = TestBed.inject(Store);
    playback = TestBed.inject(ResolutionPlaybackService);
    store.dispatch(new PlanningPhaseStarted(1, 0, [row('hero'), row('goblin')]));
    store.dispatch(new MapInitialized('enc', 20, 20, { hero: { x: 0, y: 0 }, goblin: { x: 1, y: 0 } }));
    fixture = TestBed.createComponent(RoundTimelineComponent);
    render();
  });

  it('shows nothing before the first resolution', () => {
    expect(el().querySelector('.round-timeline')).toBeNull();
  });

  it('lays the round out as one row per combatant (tracker order) and one column per tick', fakeAsync(() => {
    playback.receiveResolution(resolution);
    render();

    expect(text('.tick-head')).toEqual(['T1', 'T2']);
    expect(text('.lane-name').map(t => t.split(/\s+/)[0])).toEqual(['hero', 'goblin']);
    expect(text('.lane-name .ap')).toEqual(['4/4 AP', '4/4 AP']);
    playback.reset();
  }));

  it('follows playback, revealing each tick only once its attacks land', fakeAsync(() => {
    playback.receiveResolution(resolution);
    render();

    // Tick 1 is playing (its attack beat is the first beat), tick 2 hasn't started.
    expect(el().querySelector('.tick-head.current')!.textContent!.trim()).toBe('T1');
    expect(el().querySelectorAll('.slot.outcome-hit').length).toBe(2);
    expect(el().querySelectorAll('.slot.pending').length).toBe(2);
    expect(text('.details li')).toEqual([
      'hero → goblin: rolled 11 + 1 = 12 vs dodge 10, hit for 2 (goblin now 8 HP).',
      'goblin → hero: rolled 12 + 1 = 13 vs dodge 10, hit for 2 (hero now 8 HP).',
    ]);

    tick(PLAYBACK_TIMING.attackMs);
    render();
    expect(el().querySelector('.tick-head.current')!.textContent!.trim()).toBe('T2');
    expect(el().querySelectorAll('.slot.pending').length).toBe(0);
    tick(PLAYBACK_TIMING.attackMs + PLAYBACK_TIMING.floatMs);
  }));

  it('stays up after playback for review: the last tick by default, any tick on click', fakeAsync(() => {
    playback.receiveResolution(resolution);
    tick(2 * PLAYBACK_TIMING.attackMs + PLAYBACK_TIMING.floatMs);
    render();

    expect(el().querySelector('h4')!.textContent).toContain('Last round (2)');
    expect(el().querySelector('.details-title')!.textContent).toContain('Tick 2');

    (el().querySelectorAll('.tick-head')[0] as HTMLElement).click();
    render();
    expect(el().querySelector('.details-title')!.textContent).toContain('Tick 1');
    expect(text('.details li')[0]).toContain('rolled 11 + 1');

    store.dispatch(new ClearEncounter());   // leaving the encounter clears it
    render();
    expect(el().querySelector('.round-timeline')).toBeNull();
  }));
});
