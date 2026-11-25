import { AbilityDto, PlannedActionEntry } from '../../contracts/hub-contracts.generated';
import {
  DEFAULT_AP_COSTS, abilityUnavailableReason, apSpent, canAfford, canAffordEntry, canTargetWithAbility, describeQueue, entry,
  abilityTakesTargets, maxTargets, moveCellsUsed, projectedPosition, targetShapeError,
  abilityCellError, abilityTakesCell, estimateMoveToTarget, walkQueue, blockOf, entryMoves, skipsRoundReason,
} from './plan-queue';
import { makeAbility } from '../../testing/builders';

const move = (x: number, y: number): PlannedActionEntry => entry('move', { targetCell: { x, y } });
const melee = (id: string): PlannedActionEntry => entry('melee_attack', { targetCombatantId: id });
const defend: PlannedActionEntry = entry('defend');
const ability = (abilityId: string, target: string): PlannedActionEntry =>
  entry('ability', { abilityId, targetCombatantId: target });

const powerStrike: AbilityDto = makeAbility({ id: 'power_strike', name: 'Power Strike', apCost: 3, cooldown: 2, tags: ['melee'] });
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

describe('multi-target abilities (3B.3)', () => {
  const twin: AbilityDto = makeAbility({ id: 'twin_shot', name: 'Twin Shot', apCost: 3, cooldown: 1,
    targeting: { kind: 'combatants', range: 8, count: 2 } });
  const cleave: AbilityDto = makeAbility({ id: 'cleave', name: 'Cleave', apCost: 3, cooldown: 2, targeting: { kind: 'auto' } });
  const many = (abilityId: string, ...ids: string[]) => entry('ability', { abilityId, targetCombatantIds: ids });

  it('mirrors the server: 1..count distinct picked targets for "combatants", none for "auto"', () => {
    expect(targetShapeError(twin, many('twin_shot', 'a', 'b'))).toBeNull();
    expect(targetShapeError(twin, many('twin_shot', 'a'))).toBeNull();
    expect(targetShapeError(twin, many('twin_shot'))).toBe('Twin Shot needs between 1 and 2 targets.');
    expect(targetShapeError(twin, many('twin_shot', 'a', 'b', 'c'))).toBe('Twin Shot can target at most 2.');
    expect(targetShapeError(twin, many('twin_shot', 'a', 'a'))).toBe("Twin Shot can't target the same combatant twice.");
    expect(targetShapeError(cleave, entry('ability', { abilityId: 'cleave' }))).toBeNull();
    expect(targetShapeError(cleave, ability('cleave', 'a'))).toBe("Cleave doesn't take a target.");
  });

  it('knows which abilities take picked targets, and how many', () => {
    expect([abilityTakesTargets(twin), maxTargets(twin)]).toEqual([true, 2]);
    expect([abilityTakesTargets(cleave), maxTargets(cleave)]).toEqual([false, 0]);
  });

  it('describes every picked target', () => {
    const names: Record<string, string> = { a: 'Goblin 1', b: 'Goblin 2' };
    expect(describeQueue([many('twin_shot', 'b', 'a')], id => names[id], () => 'Twin Shot')).toBe('Twin Shot on Goblin 2, Goblin 1');
    expect(describeQueue([entry('ability', { abilityId: 'cleave' })], id => names[id], () => 'Cleave')).toBe('Cleave');
  });
  describe('movement abilities (3B.3)', () => {
    const dash: AbilityDto = makeAbility({ id: 'dash', name: 'Dash', apCost: 1, cooldown: 2, tags: ['movement'],
      targeting: { kind: 'self', filter: 'ally', range: 0 }, move: { to: 'cell', maxCells: 3, usesMovementBudget: false } });
    const charge: AbilityDto = makeAbility({ id: 'charge', name: 'Charge', apCost: 3, cooldown: 2, tags: ['melee'],
      targeting: { range: 6 }, move: { to: 'target', maxCells: 6, usesMovementBudget: true } });
    const dashTo = (x: number, y: number) => entry('ability', { abilityId: 'dash', targetCell: { x, y } });

    it('moves the planner with Dash without using the movement budget', () => {
      const walk = walkQueue({ x: 0, y: 0 }, [move(6, 0), dashTo(9, 0)], [dash], 6);
      expect(walk).toEqual({ position: { x: 9, y: 0 }, cells: 6 });
      expect(projectedPosition({ x: 0, y: 0 }, [dashTo(3, 0)], [dash])).toEqual({ x: 3, y: 0 });
    });

    it('estimates a Charge toward the target, capped at the budget left', () => {
      const positions = { g: { x: 12, y: 0 } };
      const queue = [move(4, 0), ability('charge', 'g')];
      expect(walkQueue({ x: 0, y: 0 }, queue, [charge], 6, positions)).toEqual({ position: { x: 6, y: 0 }, cells: 6 });
      expect(moveCellsUsed({ x: 0, y: 0 }, [ability('charge', 'g')], [charge], 20, { g: { x: 5, y: 0 } })).toBe(4);
      expect(estimateMoveToTarget({ x: 5, y: 6 }, { x: 5, y: 5 }, 6)).toEqual({ end: { x: 5, y: 6 }, cells: 0 });
    });

    it('checks a Dash cell like the server (maxCells from where the moves end, on the grid)', () => {
      expect(abilityCellError(dash, { x: 6, y: 0 }, { x: 9, y: 0 }, 6, 6, 20, 20)).toBeNull();
      expect(abilityCellError(dash, { x: 6, y: 0 }, { x: 10, y: 0 }, 6, 6, 20, 20)).toBe('Dash moves at most 3 squares.');
      expect(abilityCellError(dash, { x: 0, y: 0 }, { x: -1, y: 0 }, 0, 6, 20, 20)).toBe('Target cell is out of bounds (0–19, 0–19).');
      const budgeted: AbilityDto = { ...dash, move: { to: 'cell', maxCells: 3, usesMovementBudget: true } };
      expect(abilityCellError(budgeted, { x: 5, y: 0 }, { x: 7, y: 0 }, 5, 6, 20, 20)).toContain('exceed maximum range');
    });

    it('knows Dash takes a cell and checks the target shape', () => {
      expect(abilityTakesCell(dash)).toBeTrue();
      expect(abilityTakesCell(charge)).toBeFalse();
      expect(abilityTakesTargets(dash)).toBeFalse();
      expect(targetShapeError(dash, dashTo(1, 0))).toBeNull();
      // A missing cell is reported with the move (validatePlan), not as a target-shape error.
      expect(targetShapeError(dash, entry('ability', { abilityId: 'dash' }))).toBeNull();
      expect(targetShapeError(charge, entry('ability', { abilityId: 'charge', targetCombatantId: 'g', targetCell: { x: 1, y: 0 } })))
        .toBe("Charge doesn't take a target cell.");
    });

    it('describes a Dash with its cell', () => {
      expect(describeQueue([dashTo(3, 4)], () => undefined, () => 'Dash')).toBe('Dash to (3, 4)');
    });
  });
});

describe('conditions at planning (3B.4 PR 3)', () => {
  const stunned = { kind: 'all', conditionName: 'Stunned' };
  const rooted = { kind: 'move', conditionName: 'Rooted' };
  const dash: AbilityDto = makeAbility({ id: 'dash', name: 'Dash', apCost: 1, cooldown: 2, move: { to: 'cell', maxCells: 3, usesMovementBudget: false } });

  it('says why a combatant skips the round: stunned, or no AP', () => {
    expect(skipsRoundReason({ characterName: 'Brakka', maxAp: 4, blocks: [stunned] })).toBe("Brakka is Stunned and can't act this round.");
    expect(skipsRoundReason({ characterName: 'Brakka', maxAp: 0, blocks: [] })).toBe("Brakka has no AP this round and can't act.");
    expect(skipsRoundReason({ characterName: 'Brakka', maxAp: 3, blocks: [rooted] })).toBeNull();
  });

  it('knows which entries move their user', () => {
    expect(entryMoves(move(1, 0), [])).toBeTrue();
    expect(entryMoves(entry('ability', { abilityId: 'dash', targetCell: { x: 1, y: 0 } }), [dash])).toBeTrue();
    expect(entryMoves(melee('g'), [])).toBeFalse();
    expect(blockOf([rooted], 'move')).toEqual(rooted);
    expect(blockOf([rooted], 'all')).toBeUndefined();
  });

  it('marks an ability that moves as unavailable while Rooted', () => {
    expect(abilityUnavailableReason(dash, [], 4, DEFAULT_AP_COSTS, [dash], {}, [rooted])).toBe("Rooted: can't move");
    expect(abilityUnavailableReason(powerStrike, [], 4, DEFAULT_AP_COSTS, [powerStrike], {}, [rooted])).toBeNull();
  });
});
