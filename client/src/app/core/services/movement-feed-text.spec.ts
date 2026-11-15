import { MovementResult } from '../../contracts/hub-contracts.generated';
import { movementFeedText } from './game-hub.service';

describe('movementFeedText', () => {
  const base: MovementResult = {
    combatantId: 'a',
    combatantName: 'Goblin',
    path: [{ x: 0, y: 0 }, { x: 1, y: 0 }],
    finalPosition: { x: 1, y: 0 },
    wasBlocked: false,
    blockedByCombatantId: null,
  };

  it('describes a normal move', () => {
    expect(movementFeedText(base)).toBe('Goblin moves to (1, 0)');
  });

  it('marks a blocked move', () => {
    expect(movementFeedText({ ...base, path: [{ x: 0, y: 0 }], finalPosition: { x: 0, y: 0 }, wasBlocked: true, blockedByCombatantId: 'b' }))
      .toBe('Goblin moves to (0, 0) (blocked)');
  });

  it('says the combatant stays put for a move to its own cell', () => {
    expect(movementFeedText({ ...base, path: [{ x: 4, y: 4 }], finalPosition: { x: 4, y: 4 } }))
      .toBe('Goblin stays put at (4, 4)');
  });
});
