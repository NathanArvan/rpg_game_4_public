import { AttackResult, MovementResult, ResolutionPayload, TickResult } from '../../contracts/hub-contracts.generated';
import { PLAYBACK_TIMING, buildTimeline, positionsAfterResolution, positionsBeforeResolution } from './playback-timeline';

const move = (id: string, path: [number, number][], wasBlocked = false): MovementResult => ({
  combatantId: id, combatantName: id, path: path.map(([x, y]) => ({ x, y })),
  finalPosition: { x: path[path.length - 1][0], y: path[path.length - 1][1] }, wasBlocked, blockedByCombatantId: null,
});

const attack = (attackerId: string, targetId: string, hit: boolean, damage = 0, targetNewHp = 10, extra: Partial<AttackResult> = {}): AttackResult => ({
  attackerId, attackerName: attackerId, targetId, targetName: targetId, attackType: 'melee_attack', hit, damage,
  targetNewHp, targetDefeated: false, wasted: false, feedText: `${attackerId} → ${targetId}`, roll: null, wastedReason: null, ...extra,
});

const tick = (tickIndex: number, parts: Partial<TickResult> = {}): TickResult =>
  ({ tickIndex, movements: [], attacks: [], effects: [], ...parts });

const payload = (...ticks: TickResult[]): ResolutionPayload =>
  ({ ticks, newRoundNumber: 2, defeatedCombatantIds: [], updatedCombatants: [], queues: [] });

describe('playback timeline', () => {
  describe('positions', () => {
    const ticks = [
      tick(0, { movements: [move('a', [[0, 0], [1, 0]])] }),
      tick(1, { movements: [move('a', [[1, 0], [2, 0]])] }),
    ];

    it('starts each mover at the first cell of its first path, and leaves everyone else where they are', () => {
      // Even if the positions passed in are already the final ones.
      expect(positionsBeforeResolution({ a: { x: 2, y: 0 }, b: { x: 5, y: 5 } }, ticks))
        .toEqual({ a: { x: 0, y: 0 }, b: { x: 5, y: 5 } });
    });

    it("ends each mover at its last move's final position", () => {
      expect(positionsAfterResolution({ a: { x: 0, y: 0 }, b: { x: 5, y: 5 } }, ticks))
        .toEqual({ a: { x: 2, y: 0 }, b: { x: 5, y: 5 } });
    });
  });

  it('plays movement one cell per beat, every mover stepping together', () => {
    const beats = buildTimeline(payload(tick(0, {
      movements: [move('a', [[0, 0], [1, 0], [2, 0]]), move('b', [[5, 5], [5, 4]]), move('c', [[9, 9]], true)],
    })));

    expect(beats.map(b => b.phase)).toEqual(['movement', 'movement']);
    expect(beats[0].positions).toEqual({ a: { x: 1, y: 0 }, b: { x: 5, y: 4 } });
    expect(beats[1].positions).toEqual({ a: { x: 2, y: 0 } });
    expect(beats[0].moveMs).toBe(PLAYBACK_TIMING.maxStepMs);       // 1200ms / 2 steps, capped
    expect(beats[0].durationMs).toBe(beats[0].moveMs!);
    expect(beats[0].feed).toEqual(['— Round 1 Resolution —', 'a moves to (2, 0)', 'b moves to (5, 4)', 'c moves to (9, 9) (blocked)']);
  });

  it('shows a hit as green on the attacker and red on the target, and a miss as grey on the attacker', () => {
    const [beat] = buildTimeline(payload(tick(0, {
      attacks: [attack('a', 'b', true, 4, 6), attack('b', 'a', false, 0, 10)],
    })));

    expect(beat.phase).toBe('attacks');
    expect(beat.durationMs).toBe(PLAYBACK_TIMING.attackMs);
    expect(beat.auras).toEqual([
      { combatantId: 'a', kind: 'hit' },
      { combatantId: 'b', kind: 'hurt' },
      { combatantId: 'b', kind: 'miss' },
    ]);
    expect(beat.floats).toEqual([
      { combatantId: 'b', text: '-4', kind: 'damage' },
      { combatantId: 'a', text: 'MISS', kind: 'miss' },
    ]);
    expect(beat.hp).toEqual({ b: 6, a: 10 });
  });

  it('resolves each tick before the next: both first attacks, then both second attacks', () => {
    const beats = buildTimeline(payload(
      tick(0, { attacks: [attack('a', 'b', true, 3, 7), attack('b', 'a', true, 2, 8)] }),
      tick(1, { attacks: [attack('a', 'b', true, 3, 4), attack('b', 'a', false)] }),
    ));

    expect(beats.map(b => [b.tick, b.phase])).toEqual([[0, 'attacks'], [1, 'attacks']]);
    expect(beats[0].hp).toEqual({ b: 7, a: 8 });
    expect(beats[1].hp).toEqual({ b: 4, a: 10 });
    expect(beats[0].feed).toEqual(['— Round 1 Resolution —', 'Tick 1', 'a → b', 'b → a']);
    expect(beats[1].feed).toEqual(['Tick 2', 'a → b', 'b → a']);
  });

  it('fades defeated targets after the attack that defeated them', () => {
    const beats = buildTimeline(payload(tick(0, { attacks: [attack('a', 'b', true, 9, 0, { targetDefeated: true })] })));
    expect(beats.map(b => b.phase)).toEqual(['attacks', 'defeats']);
    expect(beats[1].defeated).toEqual(['b']);
  });

  it('labels a wasted attack above the attacker, with no aura or HP change', () => {
    const beats = buildTimeline(payload(
      tick(0, { attacks: [attack('a', 'b', false, 0, 10, { wasted: true, wastedReason: 'out_of_melee_reach', feedText: 'wasted' })] }),
      tick(1),
    ));
    expect(beats.map(b => [b.tick, b.phase])).toEqual([[0, 'attacks'], [1, 'idle']]);
    expect(beats[0].durationMs).toBe(PLAYBACK_TIMING.wastedMs);
    expect(beats[0].floats).toEqual([{ combatantId: 'a', text: 'out of reach', kind: 'wasted' }]);
    expect(beats[0].auras).toEqual([]);
    expect(beats[0].hp).toEqual({});
    expect(beats[0].feed).toEqual(['— Round 1 Resolution —', 'Tick 1', 'wasted']);
    expect(beats[1].durationMs).toBe(PLAYBACK_TIMING.idleMs);  // a quiet tick: a short beat
  });
});
