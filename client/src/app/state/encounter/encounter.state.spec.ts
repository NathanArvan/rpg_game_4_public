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
  PlanningPhaseStarted,
  PlaybackFinished,
  PlayerLockedIn,
  ResolutionCompleted,
} from './encounter.actions';
import { CombatantDto } from '../../contracts/hub-contracts.generated';

const makeCombatant = (overrides: Partial<CombatantDto> = {}): CombatantDto => ({
  id: 'c1',
  userId: 'u1',
  characterId: 'ch1',
  characterName: 'Frodo',
  currentHp: 20,
  maxHp: 20,
  combatantType: 'player',
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

  describe('ResolutionCompleted', () => {
    it('stores steps and sets isPlayingBack to true', () => {
      store.dispatch(new EncounterStarted('enc-1', [makeCombatant()], 1, 'planning', 0));

      const steps = [{
        phase: 'movement', actorId: 'c1', actorName: 'Frodo', actionType: 'move',
        targetId: null, targetName: null, damage: null, damageDice: null,
        newHp: null, newX: 3, newY: 4, defeated: false, feedText: 'Frodo moves to (3, 4).'
      }];
      store.dispatch(new ResolutionCompleted(steps, {}, false));

      const state = store.snapshot().encounter;
      expect(state.resolutionSteps.length).toBe(1);
      expect(state.isPlayingBack).toBeTrue();
    });

    it('applies HP/defeated deltas from attack steps', () => {
      store.dispatch(new EncounterStarted('enc-1', [
        makeCombatant({ id: 'c1', currentHp: 20 }),
        makeCombatant({ id: 'c2', userId: 'u2', characterName: 'Sam', currentHp: 15 }),
      ], 1, 'planning', 0));

      const steps = [{
        phase: 'attack', actorId: 'c1', actorName: 'Frodo', actionType: 'attack',
        targetId: 'c2', targetName: 'Sam', damage: 8, damageDice: '1d8',
        newHp: 7, newX: null, newY: null, defeated: false, feedText: 'Frodo attacks Sam for 8 damage.'
      }];
      store.dispatch(new ResolutionCompleted(steps, {}, false));

      const combatants = store.selectSnapshot(EncounterState.combatants);
      expect(combatants.find(c => c.id === 'c2')!.currentHp).toBe(7);
    });

    it('marks combatant as defeated when step.defeated is true', () => {
      store.dispatch(new EncounterStarted('enc-1', [
        makeCombatant({ id: 'c1' }),
        makeCombatant({ id: 'c2', userId: 'u2', characterName: 'Sam', currentHp: 5 }),
      ], 1, 'planning', 0));

      const steps = [{
        phase: 'attack', actorId: 'c1', actorName: 'Frodo', actionType: 'attack',
        targetId: 'c2', targetName: 'Sam', damage: 10, damageDice: '1d8',
        newHp: 0, newX: null, newY: null, defeated: true, feedText: 'Sam is defeated!'
      }];
      store.dispatch(new ResolutionCompleted(steps, {}, false));

      const combatants = store.selectSnapshot(EncounterState.combatants);
      expect(combatants.find(c => c.id === 'c2')!.isDefeated).toBeTrue();
    });
  });

  describe('PlanningPhaseStarted', () => {
    it('resets to planning with new round number and timer', () => {
      store.dispatch(new EncounterStarted('enc-1', [makeCombatant()], 1, 'planning', 0));
      store.dispatch(new AllLockedIn());
      store.dispatch(new PlanningPhaseStarted('enc-1', 2, 1700000060000));

      const state = store.snapshot().encounter;
      expect(state.roundPhase).toBe('planning');
      expect(state.roundNumber).toBe(2);
      expect(state.planningTimerExpiresAt).toBe(1700000060000);
      expect(state.myPlannedAction).toBeNull();
      expect(state.lockedInCombatantIds).toEqual([]);
      expect(state.resolutionSteps).toEqual([]);
      expect(state.isPlayingBack).toBeFalse();
    });
  });

  describe('PlaybackFinished', () => {
    it('sets isPlayingBack to false', () => {
      store.dispatch(new EncounterStarted('enc-1', [makeCombatant()], 1, 'planning', 0));
      const steps = [{
        phase: 'movement', actorId: 'c1', actorName: 'Frodo', actionType: 'move',
        targetId: null, targetName: null, damage: null, damageDice: null,
        newHp: null, newX: 3, newY: 4, defeated: false, feedText: 'Frodo moves.'
      }];
      store.dispatch(new ResolutionCompleted(steps, {}, false));
      expect(store.snapshot().encounter.isPlayingBack).toBeTrue();

      store.dispatch(new PlaybackFinished());
      expect(store.snapshot().encounter.isPlayingBack).toBeFalse();
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
      expect(state.resolutionSteps).toEqual([]);
      expect(state.isPlayingBack).toBeFalse();
    });
  });
});
