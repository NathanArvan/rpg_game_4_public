import { TestBed } from '@angular/core/testing';
import { provideStore, Store } from '@ngxs/store';
import { EncounterState } from './encounter.state';
import { AttackResolved, ClearEncounter, CombatantDefeated, EncounterStarted, TurnAdvanced } from './encounter.actions';
import { CombatantDto } from '../../contracts/hub-contracts';

const makeCombatant = (overrides: Partial<CombatantDto> = {}): CombatantDto => ({
  id: 'c1',
  userId: 'u1',
  characterId: 'ch1',
  characterName: 'Frodo',
  initiative: 10,
  initiativeRoll: 8,
  dexModifier: 2,
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
    it('sets encounterId and activeTurnCombatantId', () => {
      const combatants = [makeCombatant()];
      store.dispatch(new EncounterStarted('enc-1', combatants, 'c1'));

      const state = store.snapshot().encounter;
      expect(state.encounterId).toBe('enc-1');
      expect(state.activeTurnCombatantId).toBe('c1');
      expect(state.active).toBeTrue();
    });

    it('maps CombatantDto to Combatant with isDefeated: false', () => {
      store.dispatch(new EncounterStarted('enc-1', [makeCombatant({ id: 'c1' })], 'c1'));

      const combatants = store.selectSnapshot(EncounterState.combatants);
      expect(combatants[0].isDefeated).toBeFalse();
    });

    it('sorts combatants by initiative descending via selector', () => {
      const combatants = [
        makeCombatant({ id: 'low', initiative: 5 }),
        makeCombatant({ id: 'high', initiative: 18 }),
        makeCombatant({ id: 'mid', initiative: 11 }),
      ];
      store.dispatch(new EncounterStarted('enc-1', combatants, 'high'));

      const sorted = store.selectSnapshot(EncounterState.combatants);
      expect(sorted[0].id).toBe('high');
      expect(sorted[1].id).toBe('mid');
      expect(sorted[2].id).toBe('low');
    });
  });

  describe('AttackResolved', () => {
    it('updates currentHp on the correct combatant', () => {
      store.dispatch(new EncounterStarted('enc-1', [
        makeCombatant({ id: 'target', currentHp: 20 }),
        makeCombatant({ id: 'other', currentHp: 15 }),
      ], 'target'));

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
      ], 'defeated'));

      store.dispatch(new CombatantDefeated('defeated'));

      const combatants = store.selectSnapshot(EncounterState.combatants);
      expect(combatants.find(c => c.id === 'defeated')!.isDefeated).toBeTrue();
      expect(combatants.find(c => c.id === 'alive')!.isDefeated).toBeFalse();
    });
  });

  describe('TurnAdvanced', () => {
    it('updates activeTurnCombatantId', () => {
      store.dispatch(new EncounterStarted('enc-1', [
        makeCombatant({ id: 'c1' }),
        makeCombatant({ id: 'c2' }),
      ], 'c1'));

      store.dispatch(new TurnAdvanced('c2'));

      expect(store.selectSnapshot(EncounterState.activeTurnCombatantId)).toBe('c2');
    });
  });

  describe('ClearEncounter', () => {
    it('resets state to defaults', () => {
      store.dispatch(new EncounterStarted('enc-1', [makeCombatant()], 'c1'));
      store.dispatch(new ClearEncounter());

      const state = store.snapshot().encounter;
      expect(state.encounterId).toBeNull();
      expect(state.combatants).toEqual([]);
      expect(state.active).toBeFalse();
      expect(state.activeTurnCombatantId).toBeNull();
    });
  });
});
