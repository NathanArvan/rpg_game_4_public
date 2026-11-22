import { AbilityDto, PlannedActionEntry } from '../../contracts/hub-contracts.generated';
import {
  DEFAULT_AP_COSTS, abilityUnavailableReason, apSpent, canAfford, canAffordEntry, canTargetWithAbility, describeQueue, entry,
  moveCellsUsed, projectedPosition,
} from './plan-queue';

const move = (x: number, y: number): PlannedActionEntry => entry('move', { targetCell: { x, y } });
const melee = (id: string): PlannedActionEntry => entry('melee_attack', { targetCombatantId: id });
const defend: PlannedActionEntry = entry('defend');
const ability = (abilityId: string, target: string): PlannedActionEntry =>
  entry('ability', { abilityId, targetCombatantId: target });

const powerStrike: AbilityDto = {
  id: 'power_strike', name: 'Power Strike', description: '', apCost: 3, cooldown: 2, tags: ['melee'],
  targeting: { kind: 'combatant', filter: 'enemy', range: 1, count: null, includeSelf: false },
};
const jab: AbilityDto = { ...powerStrike, id: 'jab', name: 'Jab', apCost: 1, cooldown: 0 };

describe('plan-queue', () => {
  it('adds up AP with the default costs (move 1, attacks 2, defend 1)', () => {
    expect(apSpent([move(1, 0), melee('g'), defend], DEFAULT_AP_COSTS)).toBe(4);
  });

  it('uses the costs the server sent', () => {
    expect(apSpent([melee('g')], { melee_attack: 3 })).toBe(3);
  });

  it('checks whether one more action fits the budget', () => {
    expect(canAfford([melee('g')], 'melee_attack', 4, DEFAULT_AP_COSTS)).toBeTrue();
    expect(canAfford([melee('g'), defend], 'melee_attack', 4, DEFAULT_AP_COSTS)).toBeFalse();
  });

  it('projects the position after the queued moves', () => {
    expect(projectedPosition({ x: 0, y: 0 }, [move(3, 0), melee('g'), move(3, 2)])).toEqual({ x: 3, y: 2 });
    expect(projectedPosition({ x: 1, y: 1 }, [defend])).toEqual({ x: 1, y: 1 });
  });

  it('measures each move from where the last one ends', () => {
    expect(moveCellsUsed({ x: 0, y: 0 }, [move(3, 0), move(3, 3)])).toBe(6);
    expect(moveCellsUsed({ x: 0, y: 0 }, [move(2, 0), move(0, 0)])).toBe(4);
  });

  it('describes a queue with step numbers, and a single action without', () => {
    const nameOf = (id: string) => (id === 'g' ? 'Goblin' : undefined);
    expect(describeQueue([melee('g')], nameOf)).toBe('Melee Attack on Goblin');
    expect(describeQueue([move(2, 3), melee('g')], nameOf)).toBe('1. Move to (2, 3) → 2. Melee Attack on Goblin');
  });

  // ----- Abilities (3B.3) -----

  it('prices abilities from their definition', () => {
    expect(apSpent([ability('power_strike', 'g'), move(1, 0)], DEFAULT_AP_COSTS, [powerStrike])).toBe(4);
    expect(canAffordEntry([move(1, 0)], ability('power_strike', 'g'), 4, DEFAULT_AP_COSTS, [powerStrike])).toBeTrue();
    expect(canAffordEntry([melee('g')], ability('power_strike', 'g'), 4, DEFAULT_AP_COSTS, [powerStrike])).toBeFalse();
  });

  it('counts an unknown ability as free (the server rejects it)', () => {
    expect(apSpent([ability('nope', 'g')], DEFAULT_AP_COSTS, [])).toBe(0);
  });

  it('says why an ability is unavailable, mirroring the server', () => {
    const all = [powerStrike, jab];
    expect(abilityUnavailableReason(powerStrike, [], 4, DEFAULT_AP_COSTS, all, {})).toBeNull();
    expect(abilityUnavailableReason(powerStrike, [], 4, DEFAULT_AP_COSTS, all, { power_strike: 1 }))
      .toBe('On cooldown for 1 more round');
    expect(abilityUnavailableReason(powerStrike, [], 4, DEFAULT_AP_COSTS, all, { power_strike: 2 }))
      .toBe('On cooldown for 2 more rounds');
    expect(abilityUnavailableReason(powerStrike, [ability('power_strike', 'g')], 10, DEFAULT_AP_COSTS, all, {}))
      .toBe('Already queued this round');
    expect(abilityUnavailableReason(powerStrike, [melee('g')], 4, DEFAULT_AP_COSTS, all, {})).toBe('Not enough AP left');
    // No cooldown: can repeat while AP lasts.
    expect(abilityUnavailableReason(jab, [ability('jab', 'g')], 4, DEFAULT_AP_COSTS, all, {})).toBeNull();
  });

  it('describes ability entries by name', () => {
    const nameOf = (id: string) => (id === 'g' ? 'Goblin' : undefined);
    const abilityNameOf = (id: string) => (id === 'power_strike' ? 'Power Strike' : undefined);
    expect(describeQueue([ability('power_strike', 'g')], nameOf, abilityNameOf)).toBe('Power Strike on Goblin');
    expect(describeQueue([move(1, 0), ability('power_strike', 'g')], nameOf, abilityNameOf))
      .toBe('1. Move to (1, 0) → 2. Power Strike on Goblin');
  });

  it('checks an ability target by side and status, like the server', () => {
    const hero = { id: 'h', combatantType: 'player', status: 'standing' };
    const friend = { id: 'f', combatantType: 'player', status: 'standing' };
    const goblin = { id: 'g', combatantType: 'monster', status: 'standing' };
    expect(canTargetWithAbility(hero, goblin, powerStrike)).toBeTrue();
    expect(canTargetWithAbility(hero, friend, powerStrike)).toBeFalse();
    expect(canTargetWithAbility(hero, hero, powerStrike)).toBeFalse();
    expect(canTargetWithAbility(hero, { ...goblin, status: 'dead' }, powerStrike)).toBeFalse();
    // A downed enemy can still be hit (a finishing blow).
    expect(canTargetWithAbility(hero, { ...goblin, status: 'downed' }, powerStrike)).toBeTrue();

    const mend: AbilityDto = { ...powerStrike, id: 'mend', targeting: { ...powerStrike.targeting, filter: 'ally', includeSelf: true } };
    expect(canTargetWithAbility(hero, friend, mend)).toBeTrue();
    expect(canTargetWithAbility(hero, hero, mend)).toBeTrue();
    expect(canTargetWithAbility(hero, goblin, mend)).toBeFalse();
  });
});
