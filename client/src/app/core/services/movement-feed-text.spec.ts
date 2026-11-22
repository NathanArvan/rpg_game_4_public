import { MovementResult } from '../../contracts/hub-contracts.generated';
import { movementFeedText } from './playback-timeline';

describe('movementFeedText', () => {
  const base: MovementResult = {
    combatantId: 'a',
    combatantName: 'Goblin',
    path: [{ x: 0, y: 0 }, { x: 1, y: 0 }],
    finalPosition: { x: 1, y: 0 },
    wasBlocked: false,
    blockedByCombatantId: null, abilityId: null,
  };

  it('describes a normal move', () => {
    expect(movementFeedText(base)).toBe('Goblin moves to (1, 0)');
  });

  it('marks a blocked move', () => {
    expect(movementFeedText({ ...base, path: [{ x: 0, y: 0 }], finalPosition: { x: 0, y: 0 }, wasBlocked: true, blockedByCombatantId: 'b', abilityId: null }))
      .toBe('Goblin moves to (0, 0) (blocked)');
  });

  it('says the combatant stays put for a move to its own cell', () => {
    expect(movementFeedText({ ...base, path: [{ x: 4, y: 4 }], finalPosition: { x: 4, y: 4 } }))
      .toBe('Goblin stays put at (4, 4)');
  });
  it('names the ability that made the move (3B.3)', () => {
    expect(movementFeedText({ ...base, abilityId: 'dash' }, 'Dash')).toBe('Goblin moves to (1, 0) with Dash');
    expect(movementFeedText({ ...base, path: [{ x: 1, y: 0 }], finalPosition: { x: 1, y: 0 }, abilityId: 'charge' }, 'Charge'))
      .toBe('Goblin stays at (1, 0) (Charge)');
  });
});
