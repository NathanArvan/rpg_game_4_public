import { AttackResult, CombatantQueueDto, HealResult, MovementResult, QueuedActionDto, ResolutionPayload, TickResult } from '../../../contracts/hub-contracts.generated';
import {
  makeAbilityUse, makeAttack, makeConditionResult, makeEffect, makeHeal, makeMove, makePayload, makePlanningCombatant, makeQueue, makeQueuedAction, makeRoll, makeTick,
} from '../../../testing/builders';
import { buildLanes } from './round-lanes';

const q = (id: string, maxAp: number, ...actions: [string, number, string?][]): CombatantQueueDto => makeQueue({
  combatantId: id, maxAp,
  actions: actions.map(([type, apCost, target]) => makeQueuedAction({
    type, apCost,
    targetCombatantId: type.endsWith('attack') || type === 'stabilize' ? target ?? null : null,
    targetCell: type === 'move' ? { x: 1, y: 0 } : null,
  })),
});

/** A queue of one ability. */
const abilityQueue = (id: string, maxAp: number, apCost: number, abilityId: string, extra: Partial<QueuedActionDto> = {}): CombatantQueueDto =>
  makeQueue({ combatantId: id, maxAp, actions: [makeQueuedAction({ type: 'ability', apCost, abilityId, ...extra })] });

const attack = (attackerId: string, targetId: string, extra: Partial<AttackResult> = {}): AttackResult => makeAttack({
  attackerId, targetId, hit: true, damage: 4, targetNewHp: 6, roll: makeRoll({ d20: 13, accuracy: 3, targetDodge: 12 }), ...extra,
});

const tick = (tickIndex: number, parts: Partial<TickResult> = {}): TickResult => makeTick({ tickIndex, ...parts });

const payload = (queues: CombatantQueueDto[], ...ticks: TickResult[]): ResolutionPayload => makePayload({ queues, ticks });

describe('buildLanes', () => {
  it('puts action i of every queue in column i: both first attacks, then both second attacks', () => {
    // Two 4-AP combatants trade two melee attacks each.
    const lanes = buildLanes(payload(
      [q('A', 4, ['melee_attack', 2, 'B'], ['melee_attack', 2, 'B']), q('B', 4, ['melee_attack', 2, 'A'], ['melee_attack', 2, 'A'])],
      tick(0, { attacks: [attack('A', 'B'), attack('B', 'A', { hit: false, damage: 0, targetNewHp: 10 })] }),
      tick(1, { attacks: [attack('A', 'B', { targetNewHp: 2 }), attack('B', 'A')] }),
    ));

    expect(lanes.map(l => l.slots.map(s => `${s.kind}:${s.outcome}`))).toEqual([
      ['melee_attack:hit', 'melee_attack:hit'],
      ['melee_attack:miss', 'melee_attack:hit'],
    ]);
    expect(lanes[0].apSpent).toBe(4);
    expect(lanes[0].slots[0].label).toBe('Melee attack on B (2 AP)');
    expect(lanes[0].slots[0].detail).toBe('A → B: rolled 13 + 3 = 16 vs dodge 12, hit for 4 (B now 6 HP).');
    expect(lanes[1].slots[0].detail).toBe('B → A: rolled 13 + 3 = 16 vs dodge 12, miss.');
  });

  it('shows a cheap action and an expensive one each taking one tick', () => {
    // A: move (1) + melee (2); B: melee (2) + melee (2). A's attack lands in tick 2, alongside B's second.
    const lanes = buildLanes(payload(
      [q('A', 4, ['move', 1], ['melee_attack', 2, 'B']), q('B', 4, ['melee_attack', 2, 'A'], ['melee_attack', 2, 'A'])],
      tick(0, {
        movements: [makeMove({ combatantId: 'A', path: [{ x: 0, y: 0 }, { x: 1, y: 0 }] })],
        attacks: [attack('B', 'A')],
      }),
      tick(1, { attacks: [attack('A', 'B'), attack('B', 'A')] }),
    ));

    expect(lanes[0].slots.map(s => [s.kind, s.apCost])).toEqual([['move', 1], ['melee_attack', 2]]);
    expect(lanes[0].slots[0].detail).toBe('A moves 1 cell to (1, 0).');
    expect(lanes[0].apSpent).toBe(3);
  });

  it('marks blocked moves, wasted attacks and defends', () => {
    const lanes = buildLanes(payload(
      [q('A', 4, ['move', 1]), q('B', 4, ['melee_attack', 2, 'C']), q('C', 4, ['defend', 1])],
      tick(0, {
        movements: [makeMove({ combatantId: 'A', path: [{ x: 0, y: 0 }], wasBlocked: true, blockedByCombatantId: 'C' })],
        attacks: [attack('B', 'C', { wasted: true, hit: false, roll: null, wastedReason: 'out_of_melee_reach' })],
        effects: [makeEffect({ combatantId: 'C', effectType: 'defend' })],
        conditions: [makeConditionResult({ sourceId: 'C', targetId: 'C', conditionId: 'guarded', conditionName: 'Guarded', abilityId: null, harmful: false, roundsLeft: 0 })],
      }),
    ));

    expect(lanes.map(l => l.slots[0].outcome)).toEqual(['blocked', 'wasted', 'defended']);
    expect(lanes[0].slots[0].detail).toBe('A is blocked by C after 0 cells, stopping at (0, 0).');
    expect(lanes[1].slots[0].detail).toBe("B's melee attack on C is wasted (out of reach).");
    expect(lanes[2].slots[0].detail).toBe('C defends: Guarded (+3 Dodge) until the end of the round.');
  });

  it('shows a used-up queue as idle and the actions of a defeated combatant as lost', () => {
    const lanes = buildLanes(payload(
      [q('A', 4, ['melee_attack', 2, 'B']), q('B', 4, ['melee_attack', 2, 'A'], ['melee_attack', 2, 'A'])],
      tick(0, { attacks: [attack('A', 'B', { targetNewHp: 0, targetDefeated: true, targetStatus: 'dead' }), attack('B', 'A')] }),
      tick(1),
    ));

    expect(lanes[0].slots[1]).toEqual(jasmine.objectContaining({ kind: 'idle', apCost: 0 }));
    // B still struck in tick 1 (attacks in a tick are simultaneous), but its tick-2 attack is lost.
    expect(lanes[1].slots.map(s => s.kind)).toEqual(['melee_attack', 'lost']);
    expect(lanes[1].slots[1].detail).toBe('B went down, so this melee attack on A never happens.');
    expect(lanes[0].slots[0].detail).toBe('A → B: rolled 13 + 3 = 16 vs dodge 12, hit for 4 (B is defeated).');
  });

  it('orders lanes like the tracker', () => {
    const lanes = buildLanes(payload([q('A', 4, ['defend', 1]), q('B', 4, ['defend', 1]), q('X', 4, ['defend', 1])], tick(0)), ['B', 'A']);
    expect(lanes.map(l => l.combatantId)).toEqual(['B', 'A', 'X']);
  });

  it('shows stabilizes, saved or wasted (3B.5)', () => {
    // A and B have no queue (downed combatants can't plan): their names come from the end-of-round rows.
    const row = (id: string) => makePlanningCombatant({ id, currentHp: 0, isDefeated: true, status: 'downed', bleedoutRoundsLeft: 1 });
    const lanes = buildLanes({
      ...payload(
        [q('K', 4, ['stabilize', 2, 'A']), q('M', 4, ['stabilize', 2, 'B'])],
        tick(0, { effects: [
          makeEffect({ combatantId: 'K', effectType: 'stabilize', targetCombatantId: 'A' }),
          makeEffect({ combatantId: 'M', effectType: 'stabilize', targetCombatantId: 'B', wastedReason: 'target_not_downed' }),
        ] }),
      ),
      updatedCombatants: [row('A'), row('B')],
    });
    expect(lanes.map(l => [l.slots[0].kind, l.slots[0].outcome, l.slots[0].icon])).toEqual([
      ['stabilize', 'stabilized', '✚'], ['stabilize', 'wasted', '✚'],
    ]);
    expect(lanes[1].slots[0].detail).toBe("M's stabilize on B is wasted (not bleeding out).");
  });

  it('names ability slots and reports their attacks (3B.3)', () => {
    const queue = abilityQueue('B', 4, 3, 'power_strike', { targetCombatantId: 'G' });
    const use = makeAbilityUse({ combatantId: 'B', abilityId: 'power_strike', abilityName: 'Power Strike', targetIds: ['G'] });
    const [lane] = buildLanes(payload([queue],
      tick(0, { attacks: [attack('B', 'G', { abilityId: 'power_strike', damage: 9 })], abilityUses: [use] })));
    const s0 = lane.slots[0];
    expect([s0.kind, s0.icon, s0.outcome, s0.label]).toEqual(['ability', '✦', 'hit', 'Power Strike on G (3 AP)']);
    expect(s0.detail).toContain('Power Strike: B → G');
  });
});

describe('buildLanes: multi-target & heals (3B.3)', () => {
  const use = (abilityId: string, abilityName: string, wastedReason: string | null = null) =>
    makeAbilityUse({ combatantId: 'M', abilityId, abilityName, wastedReason });

  it('reports a heal and names every picked target', () => {
    const queue = abilityQueue('M', 4, 2, 'mend', { targetCombatantId: 'H' });
    const heal: HealResult = makeHeal({ healerId: 'M', targetId: 'H', amount: 6, targetNewHp: 6, revived: true });
    const [lane] = buildLanes(payload([queue], tick(0, { abilityUses: [use('mend', 'Mend')], heals: [heal] })));
    expect([lane.slots[0].outcome, lane.slots[0].label]).toEqual(['healed', 'Mend on H (2 AP)']);
    expect(lane.slots[0].detail).toBe('Mend: M → H: healed 6 (H now 6 HP, back on their feet).');

    const twin = abilityQueue('M', 4, 3, 'twin_shot', { targetCombatantIds: ['A', 'B'] });
    const [lane2] = buildLanes(payload([twin], tick(0, { abilityUses: [use('twin_shot', 'Twin Shot')], attacks: [
      attack('M', 'A', { abilityId: 'twin_shot' }), attack('M', 'B', { abilityId: 'twin_shot', hit: false }),
    ] })));
    expect([lane2.slots[0].outcome, lane2.slots[0].label]).toEqual(['hit', 'Twin Shot on A, B (3 AP)']);
  });

  it('says so when an auto ability found nobody', () => {
    const queue = abilityQueue('M', 4, 3, 'cleave');
    const [lane] = buildLanes(payload([queue], tick(0, { abilityUses: [use('cleave', 'Cleave', 'no_targets')] })));
    expect([lane.slots[0].outcome, lane.slots[0].detail]).toEqual(['wasted', "M uses Cleave, but there's nobody in reach."]);
  });
});

describe('buildLanes: movement abilities (3B.3)', () => {
  const run = (abilityId: string, path: [number, number][], extra: Partial<MovementResult> = {}): MovementResult =>
    makeMove({ combatantId: 'K', path: path.map(([x, y]) => ({ x, y })), abilityId, ...extra });
  const use = (abilityId: string, abilityName: string) => makeAbilityUse({ combatantId: 'K', abilityId, abilityName });

  it('labels a Dash with its cell and reports the move', () => {
    const queue = abilityQueue('K', 5, 1, 'dash', { targetCell: { x: 3, y: 0 } });
    const [lane] = buildLanes(payload([queue],
      tick(0, { movements: [run('dash', [[0, 0], [1, 0], [2, 0], [3, 0]])], abilityUses: [use('dash', 'Dash')] })));
    const s0 = lane.slots[0];
    expect([s0.outcome, s0.label]).toEqual(['moved', 'Dash to (3, 0) (1 AP)']);
    expect(s0.detail).toBe('Dash: K moves 3 cells to (3, 0).');
  });

  it('reports a Charge run before its strike, with the move bonus', () => {
    const queue = abilityQueue('K', 5, 3, 'charge', { targetCombatantId: 'G' });
    const [lane] = buildLanes(payload([queue], tick(0, {
      movements: [run('charge', [[5, 9], [5, 8], [5, 7], [5, 6]])],
      attacks: [attack('K', 'G', { abilityId: 'charge', damage: 7, moveBonus: 3, targetNewHp: 13 })],
      abilityUses: [use('charge', 'Charge')],
    })));
    expect(lane.slots[0].detail).toBe(
      'Charge: K moves 3 cells to (5, 6). K → G: rolled 13 + 3 = 16 vs dodge 12, hit for 7, incl. +3 from moving (G now 13 HP).');
  });

  it('describes conditions an ability applied, and how conditions changed a roll and its damage (3B.4)', () => {
    const lanes = buildLanes(payload(
      [abilityQueue('K', 4, 1, 'mark', { targetCombatantId: 'G' }), q('F', 4, ['melee_attack', 2, 'G'])],
      tick(0, {
        abilityUses: [makeAbilityUse({ combatantId: 'K', abilityId: 'mark', abilityName: 'Mark', targetIds: ['G'] })],
        attacks: [attack('F', 'G', {
          hit: true, damage: 6, targetNewHp: 14, conditionDamage: 2,
          roll: makeRoll({ d20: 8, accuracy: 2, targetDodge: 7, conditionAccuracy: 2, conditionDodge: -3 }),
        })],
        conditions: [
          makeConditionResult({ sourceId: 'K', targetId: 'G', targetName: 'G', conditionName: 'Marked', roundsLeft: 1 }),
          makeConditionResult({ sourceId: 'K', targetId: 'G', targetName: 'G', conditionId: 'exposed', conditionName: 'Exposed', result: 'refreshed', roundsLeft: 2 }),
        ],
      }),
    ));

    expect(lanes[0].slots[0].outcome).toBe('hit');
    expect(lanes[0].slots[0].detail).toBe('Mark: G is Marked until end of next round. G is still Exposed, now for 2 more rounds.');
    expect(lanes[1].slots[0].detail).toBe(
      'F → G: rolled 8 + 2 (+2 from conditions) = 10 vs dodge 7 (-3 from conditions), hit for 6, incl. +2 from conditions (G now 14 HP).');
  });
});
