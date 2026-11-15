import { TestBed } from '@angular/core/testing';
import { provideStore, Store } from '@ngxs/store';
import { EncounterState } from './encounter.state';
import {
  AllLockedIn,
  ClearEncounter,
  CombatantDefeated,
  EncounterEnded,
  EncounterStarted,
  PlayerLockedIn,
  PlanningPhaseStarted,
  ResolutionCompleted,
  SetMyPlannedAction,
  ClearMyPlannedAction,
  SelectMyCombatant,
  GmSetEnemyPlan,
} from './encounter.actions';
import { ResolutionPayload, TickResult } from '../../contracts/hub-contracts.generated';
import { CombatantDto, PlanningCombatantDto } from '../../contracts/hub-contracts.generated';

const makeCombatantDto = (overrides: Partial<CombatantDto> = {}): CombatantDto => ({
  id: 'c1',
  userId: 'u1',
  characterId: 'ch1',
  characterName: 'Frodo',
  currentHp: 20,
  maxHp: 20,
  combatantType: 'player',
  isAiControlled: false,
  profileId: 'bruiser',
  profileName: 'Bruiser',
  movementRange: 4,
  maxAp: 4,
  ...overrides,
});

const makePlanningCombatantDto = (overrides: Partial<PlanningCombatantDto> = {}): PlanningCombatantDto => ({
  id: 'c1',
  userId: 'user-1',
  characterName: 'Frodo',
  currentHp: 20,
  maxHp: 20,
  isDefeated: false,
  combatantType: 'player',
  isAiControlled: false,
  profileId: 'bruiser',
  profileName: 'Bruiser',
  movementRange: 4,
  maxAp: 4,
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

  describe('combat profile', () => {
    it('keeps profile name and movement range from every combatant payload', () => {
      store.dispatch(new EncounterStarted('enc-1', [makeCombatantDto({ id: 'c1' })]));
      expect(store.snapshot().encounter.combatants[0]).toEqual(jasmine.objectContaining({ profileName: 'Bruiser', movementRange: 4 }));

      store.dispatch(new PlanningPhaseStarted(2, 0, [makePlanningCombatantDto({ id: 'c1', profileName: 'Scout', movementRange: 8 })]));
      expect(store.snapshot().encounter.combatants[0]).toEqual(jasmine.objectContaining({ profileName: 'Scout', movementRange: 8 }));
    });
  });

  describe('action points (3B.2)', () => {
    it('keeps maxAp from every combatant payload', () => {
      store.dispatch(new EncounterStarted('enc-1', [makeCombatantDto({ id: 'c1', maxAp: 5 })]));
      expect(store.snapshot().encounter.combatants[0].maxAp).toBe(5);

      store.dispatch(new PlanningPhaseStarted(2, 0, [makePlanningCombatantDto({ id: 'c1', maxAp: 3 })]));
      expect(store.snapshot().encounter.combatants[0].maxAp).toBe(3);
    });

    it('stores the AP costs from PlanningPhaseStarted, defaulting when none are sent', () => {
      store.dispatch(new PlanningPhaseStarted(1, 0, [], { move: 2, melee_attack: 3, ranged_attack: 3, defend: 1 }));
      expect(store.selectSnapshot(EncounterState.apCosts)['move']).toBe(2);

      store.dispatch(new PlanningPhaseStarted(2, 0, []));
      expect(store.selectSnapshot(EncounterState.apCosts)['melee_attack']).toBe(2);
    });

    it('stores GM enemy plans as queues', () => {
      const queue = [
        { type: 'move', targetCell: { x: 1, y: 1 }, targetCombatantId: null },
        { type: 'melee_attack', targetCell: null, targetCombatantId: 'c1' },
      ];
      store.dispatch(new GmSetEnemyPlan('m1', queue));
      expect(store.selectSnapshot(EncounterState.enemyPlannedActions)['m1']).toEqual(queue);
    });
  });

  describe('AI flag', () => {
    it('is kept from EncounterStarted and PlanningPhaseStarted', () => {
      store.dispatch(new EncounterStarted('enc-1', [makeCombatantDto({ id: 'm1', combatantType: 'monster', isAiControlled: true })]));
      expect(store.snapshot().encounter.combatants[0].isAiControlled).toBeTrue();

      store.dispatch(new PlanningPhaseStarted(2, 30, [
        makePlanningCombatantDto({ id: 'm1', combatantType: 'monster', isAiControlled: true }),
        makePlanningCombatantDto({ id: 'c1' }),
      ]));
      const [monster, player] = store.snapshot().encounter.combatants;
      expect(monster.isAiControlled).toBeTrue();
      expect(player.isAiControlled).toBeFalse();
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
      store.dispatch(new SetMyPlannedAction('c1', [{ type: 'defend', targetCell: null, targetCombatantId: null }]));
      store.dispatch(new PlayerLockedIn('Frodo'));
      store.dispatch(new PlanningPhaseStarted(2, 30, []));

      const state = store.snapshot().encounter;
      expect(state.myPlannedActions).toEqual({});
      expect(state.lockedInCombatantNames).toEqual([]);
    });

    it('has no timer when the duration is 0 (Quick Battle)', () => {
      store.dispatch(new PlanningPhaseStarted(1, 0, []));
      expect(store.snapshot().encounter.planningTimerExpiresAt).toBeNull();
    });

    it('keeps which character is selected across rounds', () => {
      store.dispatch(new SelectMyCombatant('c2'));
      store.dispatch(new PlanningPhaseStarted(2, 0, []));
      expect(store.snapshot().encounter.selectedCombatantId).toBe('c2');
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
    it('stores planned actions per character', () => {
      const move = { type: 'move', targetCell: { x: 3, y: 4 }, targetCombatantId: null };
      const defend = { type: 'defend', targetCell: null, targetCombatantId: null };
      store.dispatch(new SetMyPlannedAction('c1', [move]));
      store.dispatch(new SetMyPlannedAction('c2', [defend]));

      expect(store.snapshot().encounter.myPlannedActions).toEqual({ c1: [move], c2: [defend] });
    });
  });

  describe('ClearMyPlannedAction', () => {
    it("clears only that character's plan", () => {
      const defend = { type: 'defend', targetCell: null, targetCombatantId: null };
      store.dispatch(new SetMyPlannedAction('c1', [defend]));
      store.dispatch(new SetMyPlannedAction('c2', [defend]));
      store.dispatch(new ClearMyPlannedAction('c1'));

      expect(store.snapshot().encounter.myPlannedActions).toEqual({ c2: [defend] });
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

  describe('ResolutionCompleted', () => {
    const makeResolutionPayload = (overrides: Partial<ResolutionPayload> = {}): ResolutionPayload => ({
      ticks: [{
        tickIndex: 0,
        movements: [],
        attacks: [{
          attackerId: 'c2',
          attackerName: 'Sam',
          targetId: 'c1',
          targetName: 'Frodo',
          attackType: 'melee_attack',
          hit: true,
          damage: 5,
          targetNewHp: 15,
          targetDefeated: false,
          wasted: false,
          feedText: 'Sam strikes Frodo for 5 damage',
        }],
        effects: [],
      }],
      newRoundNumber: 2,
      defeatedCombatantIds: [],
      updatedCombatants: [],
      ...overrides,
    });

    it('sets roundPhase to resolving', () => {
      store.dispatch(new PlanningPhaseStarted(1, 30, [makePlanningCombatantDto({ id: 'c1' })]));
      store.dispatch(new ResolutionCompleted(makeResolutionPayload()));

      expect(store.snapshot().encounter.roundPhase).toBe('resolving');
    });

    it('stores resolutionTicks', () => {
      store.dispatch(new PlanningPhaseStarted(1, 30, [makePlanningCombatantDto({ id: 'c1' })]));
      store.dispatch(new ResolutionCompleted(makeResolutionPayload()));

      expect(store.snapshot().encounter.resolutionTicks.length).toBe(1);
    });

    it('updates combatant HP from attack results', () => {
      store.dispatch(new EncounterStarted('enc-1', [makeCombatantDto({ id: 'c1', currentHp: 20 })]));
      store.dispatch(new ResolutionCompleted(makeResolutionPayload()));

      const combatants = store.selectSnapshot(EncounterState.combatants);
      expect(combatants.find(c => c.id === 'c1')!.currentHp).toBe(15);
    });

    it('marks combatant as defeated when in defeatedCombatantIds', () => {
      store.dispatch(new EncounterStarted('enc-1', [
        makeCombatantDto({ id: 'c1' }),
        makeCombatantDto({ id: 'c2' }),
      ]));
      store.dispatch(new ResolutionCompleted(makeResolutionPayload({ defeatedCombatantIds: ['c1'] })));

      const combatants = store.selectSnapshot(EncounterState.combatants);
      expect(combatants.find(c => c.id === 'c1')!.isDefeated).toBeTrue();
      expect(combatants.find(c => c.id === 'c2')!.isDefeated).toBeFalse();
    });

    it('PlanningPhaseStarted after resolution clears ticks and returns to planning', () => {
      store.dispatch(new PlanningPhaseStarted(1, 30, []));
      store.dispatch(new ResolutionCompleted(makeResolutionPayload()));
      store.dispatch(new PlanningPhaseStarted(2, 30, []));

      const state = store.snapshot().encounter;
      expect(state.roundPhase).toBe('planning');
      expect(state.resolutionTicks).toEqual([]);
    });
  });

  describe('EncounterEnded', () => {
    it('records the outcome and leaves the planning phase', () => {
      store.dispatch(new EncounterStarted('enc-1', [makeCombatantDto()]));
      store.dispatch(new PlanningPhaseStarted(3, 30, [makePlanningCombatantDto()]));
      store.dispatch(new EncounterEnded('party', 3));

      const state = store.snapshot().encounter;
      expect(state.roundPhase).toBe('ended');
      expect(state.outcome).toEqual({ winningSide: 'party', finalRoundNumber: 3 });
      expect(state.planningTimerExpiresAt).toBeNull();
      expect(state.active).toBeTrue();
      expect(store.selectSnapshot(EncounterState.isPlanning)).toBeFalse();
    });

    it('is cleared by ClearEncounter', () => {
      store.dispatch(new EncounterEnded('enemy', 2));
      store.dispatch(new ClearEncounter());
      expect(store.selectSnapshot(EncounterState.outcome)).toBeNull();
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
