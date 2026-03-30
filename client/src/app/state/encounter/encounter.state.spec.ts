import { TestBed } from '@angular/core/testing';
import { provideStore, Store } from '@ngxs/store';
import { EncounterState } from './encounter.state';
import {
  AllLockedIn,
  ClearEncounter,
  CombatantDefeated,
  EncounterStarted,
  PlayerLockedIn,
  PlanningPhaseStarted,
  SetMyPlannedAction,
  ClearMyPlannedAction,
} from './encounter.actions';
import { CombatantDto, PlanningCombatantDto } from '../../contracts/hub-contracts.generated';

const makeCombatantDto = (overrides: Partial<CombatantDto> = {}): CombatantDto => ({
  id: 'c1',
  userId: 'u1',
  characterId: 'ch1',
  characterName: 'Frodo',
  currentHp: 20,
  maxHp: 20,
  ...overrides,
});

const makePlanningCombatantDto = (overrides: Partial<PlanningCombatantDto> = {}): PlanningCombatantDto => ({
  id: 'c1',
  userId: 'user-1',
  characterName: 'Frodo',
  currentHp: 20,
  maxHp: 20,
  isDefeated: false,
  ...overrides,
});

describe('EncounterState', () => {
  let store: Store;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideStore([EncounterState])],
    });
    store = TestBed.inject(Store);
  });

  describe('EncounterStarted', () => {
    it('sets encounterId and active', () => {
      store.dispatch(new EncounterStarted('enc-1', [makeCombatantDto()]));

      const state = store.snapshot().encounter;
      expect(state.encounterId).toBe('enc-1');
      expect(state.active).toBeTrue();
    });

    it('maps CombatantDto to Combatant with isDefeated: false', () => {
      store.dispatch(new EncounterStarted('enc-1', [makeCombatantDto({ id: 'c1' })]));

      const combatants = store.selectSnapshot(EncounterState.combatants);
      expect(combatants[0].isDefeated).toBeFalse();
    });
  });

  describe('PlanningPhaseStarted', () => {
    it('sets roundPhase to planning and roundNumber', () => {
      store.dispatch(new PlanningPhaseStarted(2, 30, [makePlanningCombatantDto()]));

      const state = store.snapshot().encounter;
      expect(state.roundPhase).toBe('planning');
      expect(state.roundNumber).toBe(2);
    });

    it('sets planningTimerExpiresAt approximately 30 seconds from now', () => {
      const before = Date.now();
      store.dispatch(new PlanningPhaseStarted(1, 30, []));
      const after = Date.now();

      const expiresAt = new Date(store.snapshot().encounter.planningTimerExpiresAt).getTime();
      expect(expiresAt).toBeGreaterThanOrEqual(before + 29000);
      expect(expiresAt).toBeLessThanOrEqual(after + 31000);
    });

    it('clears myPlannedActions and lockedInCombatantNames', () => {
      store.dispatch(new SetMyPlannedAction([{ type: 'defend', targetCell: null, targetCombatantId: null }]));
      store.dispatch(new PlayerLockedIn('Frodo'));
      store.dispatch(new PlanningPhaseStarted(2, 30, []));

      const state = store.snapshot().encounter;
      expect(state.myPlannedActions).toEqual([]);
      expect(state.lockedInCombatantNames).toEqual([]);
    });
  });

  describe('PlayerLockedIn', () => {
    it('appends character name to lockedInCombatantNames', () => {
      store.dispatch(new PlayerLockedIn('Frodo'));
      store.dispatch(new PlayerLockedIn('Sam'));

      expect(store.snapshot().encounter.lockedInCombatantNames).toEqual(['Frodo', 'Sam']);
    });
  });

  describe('AllLockedIn', () => {
    it('sets roundPhase to resolving', () => {
      store.dispatch(new PlanningPhaseStarted(1, 30, []));
      store.dispatch(new AllLockedIn());

      expect(store.snapshot().encounter.roundPhase).toBe('resolving');
    });
  });

  describe('SetMyPlannedAction', () => {
    it('stores planned actions', () => {
      const action = { type: 'move', targetCell: { x: 3, y: 4 }, targetCombatantId: null };
      store.dispatch(new SetMyPlannedAction([action]));

      expect(store.snapshot().encounter.myPlannedActions).toEqual([action]);
    });
  });

  describe('ClearMyPlannedAction', () => {
    it('clears planned actions', () => {
      store.dispatch(new SetMyPlannedAction([{ type: 'defend', targetCell: null, targetCombatantId: null }]));
      store.dispatch(new ClearMyPlannedAction());

      expect(store.snapshot().encounter.myPlannedActions).toEqual([]);
    });
  });

  describe('CombatantDefeated', () => {
    it('sets isDefeated: true on the correct combatant', () => {
      store.dispatch(new EncounterStarted('enc-1', [
        makeCombatantDto({ id: 'defeated' }),
        makeCombatantDto({ id: 'alive' }),
      ]));
      store.dispatch(new CombatantDefeated('defeated'));

      const combatants = store.selectSnapshot(EncounterState.combatants);
      expect(combatants.find(c => c.id === 'defeated')!.isDefeated).toBeTrue();
      expect(combatants.find(c => c.id === 'alive')!.isDefeated).toBeFalse();
    });
  });

  describe('ClearEncounter', () => {
    it('resets state to defaults', () => {
      store.dispatch(new EncounterStarted('enc-1', [makeCombatantDto()]));
      store.dispatch(new ClearEncounter());

      const state = store.snapshot().encounter;
      expect(state.encounterId).toBeNull();
      expect(state.combatants).toEqual([]);
      expect(state.active).toBeFalse();
      expect(state.roundPhase).toBeNull();
    });
  });

  describe('selectors', () => {
    it('isPlanning returns true when roundPhase is planning', () => {
      store.dispatch(new PlanningPhaseStarted(1, 30, []));
      expect(store.selectSnapshot(EncounterState.isPlanning)).toBeTrue();
    });

    it('isResolving returns true when roundPhase is resolving', () => {
      store.dispatch(new PlanningPhaseStarted(1, 30, []));
      store.dispatch(new AllLockedIn());
      expect(store.selectSnapshot(EncounterState.isResolving)).toBeTrue();
    });
  });
});
