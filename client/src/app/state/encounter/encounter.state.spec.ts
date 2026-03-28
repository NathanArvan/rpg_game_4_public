import { TestBed } from '@angular/core/testing';
import { provideStore, Store } from '@ngxs/store';
import { EncounterState } from './encounter.state';
import {
  ActionPlanned,
  AllLockedIn,
  AttackResolved,
  ClearEncounter,
  ClearPlannedAction,
  CombatantDefeated,
  EncounterStarted,
  PlayerLockedIn,
} from './encounter.actions';
import { CombatantDto } from '../../contracts/hub-contracts.generated';

const makeCombatant = (overrides: Partial<CombatantDto> = {}): CombatantDto => ({
  id: 'c1',
  userId: 'u1',
  characterId: 'ch1',
  characterName: 'Frodo',
  currentHp: 20,
  maxHp: 20,
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
    it('sets encounterId, roundNumber, roundPhase, and planningTimerExpiresAt', () => {
      const combatants = [makeCombatant()];
      store.dispatch(new EncounterStarted('enc-1', combatants, 1, 'planning', 1700000000000));

      const state = store.snapshot().encounter;
      expect(state.encounterId).toBe('enc-1');
      expect(state.roundNumber).toBe(1);
      expect(state.roundPhase).toBe('planning');
      expect(state.planningTimerExpiresAt).toBe(1700000000000);
      expect(state.active).toBeTrue();
    });

    it('maps CombatantDto to Combatant with isDefeated: false', () => {
      store.dispatch(new EncounterStarted('enc-1', [makeCombatant({ id: 'c1' })], 1, 'planning', 0));

      const combatants = store.selectSnapshot(EncounterState.combatants);
      expect(combatants[0].isDefeated).toBeFalse();
    });

    it('resets lockedInCombatantIds and myPlannedAction', () => {
      store.dispatch(new EncounterStarted('enc-1', [makeCombatant()], 1, 'planning', 0));

      const state = store.snapshot().encounter;
      expect(state.lockedInCombatantIds).toEqual([]);
      expect(state.myPlannedAction).toBeNull();
    });
  });

  describe('ActionPlanned', () => {
    it('sets myPlannedAction', () => {
      store.dispatch(new EncounterStarted('enc-1', [makeCombatant()], 1, 'planning', 0));
      store.dispatch(new ActionPlanned({ actionType: 'move', targetX: 5, targetY: 3 }));

      const action = store.selectSnapshot(EncounterState.myPlannedAction);
      expect(action).toEqual({ actionType: 'move', targetX: 5, targetY: 3 });
    });
  });

  describe('ClearPlannedAction', () => {
    it('clears myPlannedAction', () => {
      store.dispatch(new EncounterStarted('enc-1', [makeCombatant()], 1, 'planning', 0));
      store.dispatch(new ActionPlanned({ actionType: 'attack', targetCombatantId: 'c2' }));
      store.dispatch(new ClearPlannedAction());

      expect(store.selectSnapshot(EncounterState.myPlannedAction)).toBeNull();
    });
  });

  describe('PlayerLockedIn', () => {
    it('adds combatantId to lockedInCombatantIds', () => {
      store.dispatch(new EncounterStarted('enc-1', [makeCombatant()], 1, 'planning', 0));
      store.dispatch(new PlayerLockedIn('c1'));

      expect(store.selectSnapshot(EncounterState.lockedInCombatantIds)).toEqual(['c1']);
    });

    it('does not duplicate combatantId', () => {
      store.dispatch(new EncounterStarted('enc-1', [makeCombatant()], 1, 'planning', 0));
      store.dispatch(new PlayerLockedIn('c1'));
      store.dispatch(new PlayerLockedIn('c1'));

      expect(store.selectSnapshot(EncounterState.lockedInCombatantIds)).toEqual(['c1']);
    });
  });

  describe('AllLockedIn', () => {
    it('transitions roundPhase to resolving', () => {
      store.dispatch(new EncounterStarted('enc-1', [makeCombatant()], 1, 'planning', 0));
      store.dispatch(new AllLockedIn());

      const state = store.snapshot().encounter;
      expect(state.roundPhase).toBe('resolving');
    });
  });

  describe('Selectors', () => {
    it('isPlanning returns true during planning phase', () => {
      store.dispatch(new EncounterStarted('enc-1', [makeCombatant()], 1, 'planning', 0));
      expect(store.selectSnapshot(EncounterState.isPlanning)).toBeTrue();
    });

    it('isResolving returns true after AllLockedIn', () => {
      store.dispatch(new EncounterStarted('enc-1', [makeCombatant()], 1, 'planning', 0));
      store.dispatch(new AllLockedIn());
      expect(store.selectSnapshot(EncounterState.isResolving)).toBeTrue();
    });
  });

  describe('AttackResolved', () => {
    it('updates currentHp on the correct combatant', () => {
      store.dispatch(new EncounterStarted('enc-1', [
        makeCombatant({ id: 'target', currentHp: 20 }),
        makeCombatant({ id: 'other', currentHp: 15 }),
      ], 1, 'planning', 0));

      store.dispatch(new AttackResolved('target', 12, 'Frodo hit for 8'));

      const combatants = store.selectSnapshot(EncounterState.combatants);
      const target = combatants.find(c => c.id === 'target')!;
      const other = combatants.find(c => c.id === 'other')!;
      expect(target.currentHp).toBe(12);
      expect(other.currentHp).toBe(15);
    });
  });

  describe('CombatantDefeated', () => {
    it('sets isDefeated: true on the correct combatant', () => {
      store.dispatch(new EncounterStarted('enc-1', [
        makeCombatant({ id: 'defeated' }),
        makeCombatant({ id: 'alive' }),
      ], 1, 'planning', 0));

      store.dispatch(new CombatantDefeated('defeated'));

      const combatants = store.selectSnapshot(EncounterState.combatants);
      expect(combatants.find(c => c.id === 'defeated')!.isDefeated).toBeTrue();
      expect(combatants.find(c => c.id === 'alive')!.isDefeated).toBeFalse();
    });
  });

  describe('ClearEncounter', () => {
    it('resets state to defaults', () => {
      store.dispatch(new EncounterStarted('enc-1', [makeCombatant()], 1, 'planning', 0));
      store.dispatch(new ClearEncounter());

      const state = store.snapshot().encounter;
      expect(state.encounterId).toBeNull();
      expect(state.combatants).toEqual([]);
      expect(state.active).toBeFalse();
      expect(state.roundPhase).toBeNull();
      expect(state.roundNumber).toBe(0);
      expect(state.myPlannedAction).toBeNull();
      expect(state.lockedInCombatantIds).toEqual([]);
      expect(state.planningTimerExpiresAt).toBeNull();
    });
  });
});
