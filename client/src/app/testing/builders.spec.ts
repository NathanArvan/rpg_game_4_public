import { makeAbility, makeAttack, makeMove, makePayload, makePlanningCombatant, makeQueue, makeTick } from './builders';

describe('test builders', () => {
  it('names default to ids', () => {
    expect(makeAttack({ attackerId: 'hero', targetId: 'ogre' })).toEqual(jasmine.objectContaining({ attackerName: 'hero', targetName: 'ogre' }));
    expect(makePlanningCombatant({ id: 'g1' }).characterName).toBe('g1');
    expect(makeQueue({ combatantId: 'k' }).combatantName).toBe('k');
  });

  it('overrides win over defaults, names included', () => {
    expect(makeAttack({ attackerId: 'g', attackerName: 'Goblin', hit: true, damage: 4 }))
      .toEqual(jasmine.objectContaining({ attackerId: 'g', attackerName: 'Goblin', hit: true, damage: 4, wasted: false }));
  });

  it('ends a move at the last cell of its path unless told otherwise', () => {
    expect(makeMove({ path: [{ x: 0, y: 0 }, { x: 3, y: 2 }] }).finalPosition).toEqual({ x: 3, y: 2 });
    expect(makeMove({ path: [{ x: 0, y: 0 }], finalPosition: { x: 9, y: 9 } }).finalPosition).toEqual({ x: 9, y: 9 });
  });

  it('merges partial targeting over the defaults', () => {
    expect(makeAbility({ id: 'mend', targeting: { filter: 'ally', includeSelf: true } }).targeting)
      .toEqual({ kind: 'combatant', filter: 'ally', range: 1, count: null, includeSelf: true });
  });

  it('builds an empty round', () => {
    expect(makePayload({ ticks: [makeTick()] })).toEqual(jasmine.objectContaining({ newRoundNumber: 2, defeatedCombatantIds: [], queues: [] }));
  });
});
