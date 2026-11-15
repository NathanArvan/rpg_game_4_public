import { PlannedActionEntry } from '../../contracts/hub-contracts.generated';
import {
  DEFAULT_AP_COSTS, apSpent, canAfford, describeQueue, moveCellsUsed, projectedPosition,
} from './plan-queue';

const move = (x: number, y: number): PlannedActionEntry => ({ type: 'move', targetCell: { x, y }, targetCombatantId: null });
const melee = (id: string): PlannedActionEntry => ({ type: 'melee_attack', targetCell: null, targetCombatantId: id });
const defend: PlannedActionEntry = { type: 'defend', targetCell: null, targetCombatantId: null };

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
});
