import { AttackResult, CombatantQueueDto, HealResult, ResolutionPayload, TickResult } from '../../../contracts/hub-contracts.generated';
import { buildLanes } from './round-lanes';

const q = (id: string, maxAp: number, ...actions: [string, number, string?][]): CombatantQueueDto => ({
  combatantId: id, combatantName: id, maxAp,
  actions: actions.map(([type, apCost, target]) => ({
    type, apCost,
    targetCombatantId: type.endsWith('attack') || type === 'stabilize' ? target ?? null : null,
    targetCell: type === 'move' ? { x: 1, y: 0 } : null,
    abilityId: null,
    targetCombatantIds: null,
  })),
});

const attack = (attackerId: string, targetId: string, extra: Partial<AttackResult> = {}): AttackResult => ({
  attackerId, attackerName: attackerId, targetId, targetName: targetId, attackType: 'melee_attack',
  hit: true, damage: 4, targetNewHp: 6, targetDefeated: false, wasted: false, feedText: '',
  roll: { d20: 13, accuracy: 3, targetDodge: 12 }, wastedReason: null, targetStatus: 'standing', finishingBlow: false, abilityId: null, ...extra,
});

const tick = (tickIndex: number, parts: Partial<TickResult> = {}): TickResult =>
  ({ tickIndex, movements: [], attacks: [], effects: [], abilityUses: [], heals: [], ...parts });

const payload = (queues: CombatantQueueDto[], ...ticks: TickResult[]): ResolutionPayload =>
  ({ queues, ticks, newRoundNumber: 2, defeatedCombatantIds: [], updatedCombatants: [], endOfRound: [], revivedCombatantIds: [] });

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
        movements: [{ combatantId: 'A', combatantName: 'A', path: [{ x: 0, y: 0 }, { x: 1, y: 0 }], finalPosition: { x: 1, y: 0 }, wasBlocked: false, blockedByCombatantId: null }],
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
        movements: [{ combatantId: 'A', combatantName: 'A', path: [{ x: 0, y: 0 }], finalPosition: { x: 0, y: 0 }, wasBlocked: true, blockedByCombatantId: 'C' }],
        attacks: [attack('B', 'C', { wasted: true, hit: false, roll: null, wastedReason: 'out_of_melee_reach' })],
        effects: [{ combatantId: 'C', effectType: 'defend', description: '', targetCombatantId: null, wastedReason: null }],
      }),
    ));

    expect(lanes.map(l => l.slots[0].outcome)).toEqual(['blocked', 'wasted', 'defended']);
    expect(lanes[0].slots[0].detail).toBe('A is blocked by C after 0 cells, stopping at (0, 0).');
    expect(lanes[1].slots[0].detail).toBe("B's melee attack on C is wasted (out of reach).");
    expect(lanes[2].slots[0].detail).toBe('C defends (no effect yet).');
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
    const row = (id: string) => ({ id, userId: '', characterName: id, currentHp: 0, maxHp: 10, isDefeated: true, combatantType: 'player',
      isAiControlled: false, profileId: '', profileName: '', movementRange: 6, maxAp: 4, status: 'downed', bleedoutRoundsLeft: 1, abilities: [], cooldowns: {} });
    const lanes = buildLanes({
      ...payload(
        [q('K', 4, ['stabilize', 2, 'A']), q('M', 4, ['stabilize', 2, 'B'])],
        tick(0, { effects: [
          { combatantId: 'K', effectType: 'stabilize', description: '', targetCombatantId: 'A', wastedReason: null },
          { combatantId: 'M', effectType: 'stabilize', description: '', targetCombatantId: 'B', wastedReason: 'target_not_downed' },
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
    const queue: CombatantQueueDto = { combatantId: 'B', combatantName: 'B', maxAp: 4, actions: [
      { type: 'ability', apCost: 3, targetCombatantId: 'G', targetCell: null, abilityId: 'power_strike', targetCombatantIds: null },
    ] };
    const use = { combatantId: 'B', combatantName: 'B', abilityId: 'power_strike', abilityName: 'Power Strike',
      targetIds: ['G'], wastedReason: null, feedText: '' };
    const [lane] = buildLanes(payload([queue],
      tick(0, { attacks: [attack('B', 'G', { abilityId: 'power_strike', damage: 9 })], abilityUses: [use] })));
    const s0 = lane.slots[0];
    expect([s0.kind, s0.icon, s0.outcome, s0.label]).toEqual(['ability', '✦', 'hit', 'Power Strike on G (3 AP)']);
    expect(s0.detail).toContain('Power Strike: B → G');
  });
});

describe('buildLanes: multi-target & heals (3B.3)', () => {
  const use = (abilityId: string, abilityName: string, wastedReason: string | null = null) =>
    ({ combatantId: 'M', combatantName: 'M', abilityId, abilityName, targetIds: [], wastedReason, feedText: '' });

  it('reports a heal and names every picked target', () => {
    const queue: CombatantQueueDto = { combatantId: 'M', combatantName: 'M', maxAp: 4, actions: [
      { type: 'ability', apCost: 2, targetCombatantId: 'H', targetCell: null, abilityId: 'mend', targetCombatantIds: null },
    ] };
    const heal: HealResult = { healerId: 'M', healerName: 'M', targetId: 'H', targetName: 'H', abilityId: 'mend', amount: 6,
      targetNewHp: 6, revived: true, targetStatus: 'standing', wastedReason: null, feedText: '' };
    const [lane] = buildLanes(payload([queue], tick(0, { abilityUses: [use('mend', 'Mend')], heals: [heal] })));
    expect([lane.slots[0].outcome, lane.slots[0].label]).toEqual(['healed', 'Mend on H (2 AP)']);
    expect(lane.slots[0].detail).toBe('Mend: M → H: healed 6 (H now 6 HP, back on their feet).');

    const twin: CombatantQueueDto = { combatantId: 'M', combatantName: 'M', maxAp: 4, actions: [
      { type: 'ability', apCost: 3, targetCombatantId: null, targetCell: null, abilityId: 'twin_shot', targetCombatantIds: ['A', 'B'] },
    ] };
    const [lane2] = buildLanes(payload([twin], tick(0, { abilityUses: [use('twin_shot', 'Twin Shot')], attacks: [
      attack('M', 'A', { abilityId: 'twin_shot' }), attack('M', 'B', { abilityId: 'twin_shot', hit: false }),
    ] })));
    expect([lane2.slots[0].outcome, lane2.slots[0].label]).toEqual(['hit', 'Twin Shot on A, B (3 AP)']);
  });

  it('says so when an auto ability found nobody', () => {
    const queue: CombatantQueueDto = { combatantId: 'M', combatantName: 'M', maxAp: 4, actions: [
      { type: 'ability', apCost: 3, targetCombatantId: null, targetCell: null, abilityId: 'cleave', targetCombatantIds: null },
    ] };
    const [lane] = buildLanes(payload([queue], tick(0, { abilityUses: [use('cleave', 'Cleave', 'no_targets')] })));
    expect([lane.slots[0].outcome, lane.slots[0].detail]).toEqual(['wasted', "M uses Cleave, but there's nobody in reach."]);
  });
});
