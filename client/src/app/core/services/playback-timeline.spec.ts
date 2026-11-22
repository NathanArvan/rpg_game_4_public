import { AttackResult, HealResult, MovementResult, ResolutionPayload, TickResult } from '../../contracts/hub-contracts.generated';
import { PLAYBACK_TIMING, buildTimeline, positionsAfterResolution, positionsBeforeResolution } from './playback-timeline';

const move = (id: string, path: [number, number][], wasBlocked = false): MovementResult => ({
  combatantId: id, combatantName: id, path: path.map(([x, y]) => ({ x, y })),
  finalPosition: { x: path[path.length - 1][0], y: path[path.length - 1][1] }, wasBlocked, blockedByCombatantId: null, abilityId: null,
});

const attack = (attackerId: string, targetId: string, hit: boolean, damage = 0, targetNewHp = 10, extra: Partial<AttackResult> = {}): AttackResult => ({
  attackerId, attackerName: attackerId, targetId, targetName: targetId, attackType: 'melee_attack', hit, damage,
  targetNewHp, targetDefeated: false, wasted: false, feedText: `${attackerId} → ${targetId}`, roll: null, wastedReason: null, targetStatus: 'standing', finishingBlow: false, abilityId: null, moveBonus: 0, ...extra,
});

const tick = (tickIndex: number, parts: Partial<TickResult> = {}): TickResult =>
  ({ tickIndex, movements: [], attacks: [], effects: [], abilityUses: [], heals: [], ...parts });

const payload = (...ticks: TickResult[]): ResolutionPayload =>
  ({ ticks, newRoundNumber: 2, defeatedCombatantIds: [], updatedCombatants: [], queues: [], endOfRound: [], revivedCombatantIds: [] });

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
    const beats = buildTimeline(payload(tick(0, { attacks: [attack('a', 'b', true, 9, 0, { targetDefeated: true, targetStatus: 'dead' })] })));
    expect(beats.map(b => b.phase)).toEqual(['attacks', 'defeats']);
    expect(beats[1].statuses).toEqual({ b: { status: 'dead', bleedoutRoundsLeft: 0 } });
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

  describe('bleedout (3B.5)', () => {
    const downedRow = { id: 'hero', userId: 'me', characterName: 'hero', currentHp: 0, maxHp: 10, isDefeated: true, combatantType: 'player',
      isAiControlled: false, profileId: 'p', profileName: 'P', movementRange: 6, maxAp: 4, status: 'downed', bleedoutRoundsLeft: 2, abilities: [], cooldowns: {} };

    it('shows a hero going down: damage, then DOWN with the rounds left', () => {
      const beats = buildTimeline({
        ...payload(tick(0, { attacks: [attack('ogre', 'hero', true, 6, 0, { targetDefeated: true, targetStatus: 'downed' })] })),
        updatedCombatants: [downedRow],
      });
      expect(beats.map(b => b.phase)).toEqual(['attacks', 'defeats']);
      expect(beats[0].floats).toEqual([{ combatantId: 'hero', text: '-6', kind: 'damage' }]);
      expect(beats[1].statuses).toEqual({ hero: { status: 'downed', bleedoutRoundsLeft: 2 } });
      expect(beats[1].floats).toEqual([{ combatantId: 'hero', text: 'DOWN', kind: 'damage' }]);
    });

    it('labels a finishing blow instead of showing 0 damage', () => {
      const [beat] = buildTimeline(payload(tick(0, {
        attacks: [attack('ogre', 'hero', true, 0, 0, { targetDefeated: true, targetStatus: 'dead', finishingBlow: true, abilityId: null, moveBonus: 0 })],
      })));
      expect(beat.floats).toEqual([{ combatantId: 'hero', text: 'finished', kind: 'damage' }]);
    });

    it('plays stabilizes after the tick\'s attacks, with a teal aura on the saved ally', () => {
      const beats = buildTimeline(payload(tick(0, { effects: [
        { combatantId: 'knight', effectType: 'stabilize', description: 'knight stabilizes hero', targetCombatantId: 'hero', wastedReason: null },
        { combatantId: 'medic', effectType: 'stabilize', description: "medic's stabilize on x fails", targetCombatantId: 'x', wastedReason: 'out_of_melee_reach' },
        { combatantId: 'scout', effectType: 'defend', description: 'scout holds position', targetCombatantId: null, wastedReason: null },
      ] })));
      const effects = beats.find(b => b.phase === 'effects')!;
      expect(effects.statuses).toEqual({ hero: { status: 'stable', bleedoutRoundsLeft: 0 } });
      expect(effects.auras).toEqual([{ combatantId: 'hero', kind: 'stabilize' }]);
      expect(effects.floats).toEqual([
        { combatantId: 'hero', text: 'stabilized', kind: 'status' },
        { combatantId: 'medic', text: 'out of reach', kind: 'wasted' },
      ]);
      expect(effects.feed).toEqual(['— Round 1 Resolution —', 'knight stabilizes hero', "medic's stabilize on x fails"]);
    });

    it('ends the round with the bleedout countdown', () => {
      const beats = buildTimeline({
        ...payload(tick(0)),
        endOfRound: [
          { combatantId: 'a', combatantName: 'a', roundsLeft: 1, died: false, feedText: 'a is bleeding out — 1 round left' },
          { combatantId: 'b', combatantName: 'b', roundsLeft: 0, died: true, feedText: 'b bleeds out' },
        ],
      });
      const last = beats[beats.length - 1];
      expect(last.phase).toBe('bleedout');
      expect(last.statuses).toEqual({ a: { status: 'downed', bleedoutRoundsLeft: 1 }, b: { status: 'dead', bleedoutRoundsLeft: 0 } });
      expect(last.floats).toEqual([
        { combatantId: 'a', text: 'bleeding: 1', kind: 'status' },
        { combatantId: 'b', text: 'bled out', kind: 'damage' },
      ]);
      expect(last.feed).toEqual(['End of round', 'a is bleeding out — 1 round left', 'b bleeds out']);
    });
  });

  it('floats the ability name over its user as its attack lands (3B.3)', () => {
    const use = { combatantId: 'hero', combatantName: 'hero', abilityId: 'power_strike', abilityName: 'Power Strike',
      targetIds: ['ogre'], wastedReason: null, feedText: 'hero uses Power Strike on ogre' };
    const beats = buildTimeline(payload(tick(0, {
      attacks: [attack('hero', 'ogre', true, 9, 11, { abilityId: 'power_strike', feedText: "hero's Power Strike hits ogre" })],
      abilityUses: [use],
    })));
    expect(beats[0].phase).toBe('attacks');
    expect(beats[0].floats).toEqual([
      { combatantId: 'hero', text: 'Power Strike', kind: 'ability' },
      { combatantId: 'ogre', text: '-9', kind: 'damage' },
    ]);
    // The attack line names the ability; the "uses" line isn't repeated.
    expect(beats[0].feed).toEqual(['— Round 1 Resolution —', "hero's Power Strike hits ogre"]);
  });

  it('shows a refused ability (on cooldown) with its own feed line and label (3B.3)', () => {
    const use = { combatantId: 'hero', combatantName: 'hero', abilityId: 'power_strike', abilityName: 'Power Strike',
      targetIds: ['ogre'], wastedReason: 'on_cooldown', feedText: "hero can't use Power Strike — still on cooldown" };
    const beats = buildTimeline(payload(tick(0, { abilityUses: [use] })));
    expect(beats[0].floats).toEqual([{ combatantId: 'hero', text: 'on cooldown', kind: 'wasted' }]);
    expect(beats[0].feed).toContain("hero can't use Power Strike — still on cooldown");
  });
});

describe('playback timeline: movement abilities (3B.3)', () => {
  const use = (abilityId: string, abilityName: string, targetIds: string[] = []) =>
    ({ combatantId: 'hero', combatantName: 'hero', abilityId, abilityName, targetIds, wastedReason: null, feedText: '' });

  it('shows a Charge as the run starts, names it in the move line, then lands the strike', () => {
    const run = { ...move('hero', [[5, 9], [5, 8], [5, 7], [5, 6]]), abilityId: 'charge' };
    const beats = buildTimeline(payload(tick(0, {
      movements: [run],
      attacks: [attack('hero', 'ogre', true, 7, 13, { abilityId: 'charge', moveBonus: 3, feedText: "hero's Charge hits ogre" })],
      abilityUses: [use('charge', 'Charge', ['ogre'])],
    })));
    expect(beats.map(b => b.phase)).toEqual(['movement', 'movement', 'movement', 'attacks']);
    expect(beats[0].floats).toEqual([{ combatantId: 'hero', text: 'Charge', kind: 'ability' }]);
    expect(beats[0].feed).toContain('hero moves to (5, 6) with Charge');
    expect(beats[3].floats).toEqual([{ combatantId: 'ogre', text: '-7', kind: 'damage' }]);
  });

  it('plays a Dash as movement only, with no attacks beat', () => {
    const dash = { ...move('hero', [[0, 0], [1, 0], [2, 0], [3, 0]]), abilityId: 'dash' };
    const beats = buildTimeline(payload(tick(0, { movements: [dash], abilityUses: [use('dash', 'Dash')] })));
    expect(beats.every(b => b.phase === 'movement')).toBeTrue();
    expect(beats[0].floats).toEqual([{ combatantId: 'hero', text: 'Dash', kind: 'ability' }]);
  });
});

describe('playback timeline: heals (3B.3)', () => {
  const heal = (healerId: string, targetId: string, extra: Partial<HealResult> = {}): HealResult => ({
    healerId, healerName: healerId, targetId, targetName: targetId, abilityId: 'mend', amount: 6, targetNewHp: 6,
    revived: false, targetStatus: 'standing', wastedReason: null, feedText: `${healerId} heals ${targetId}`, ...extra,
  });

  it('plays heals in the attacks beat: a green aura, +n and the netted HP', () => {
    const beats = buildTimeline(payload(tick(0, { heals: [heal('mira', 'hero', { targetNewHp: 9 })] })));
    const attacks = beats.find(b => b.phase === 'attacks')!;
    expect(attacks.auras).toContain({ combatantId: 'hero', kind: 'heal' });
    expect(attacks.floats).toContain({ combatantId: 'hero', text: '+6', kind: 'heal' });
    expect(attacks.hp).toEqual({ hero: 9 });
    expect(attacks.durationMs).toBe(PLAYBACK_TIMING.attackMs);
    expect(attacks.feed).toContain('mira heals hero');
    expect(beats.some(b => b.phase === 'revives')).toBeFalse();
  });

  it('gets a revived combatant back up in its own beat, after the attacks', () => {
    const beats = buildTimeline(payload(tick(0, { heals: [heal('mira', 'hero', { revived: true })] })));
    const phases = beats.map(b => b.phase);
    expect(phases.indexOf('revives')).toBeGreaterThan(phases.indexOf('attacks'));
    const revive = beats.find(b => b.phase === 'revives')!;
    expect(revive.statuses).toEqual({ hero: { status: 'standing', bleedoutRoundsLeft: 0 } });
    expect(revive.floats).toEqual([{ combatantId: 'hero', text: 'UP', kind: 'heal' }]);
  });

  it('labels a wasted heal above the healer, and changes no HP', () => {
    const beats = buildTimeline(payload(tick(0, { heals: [heal('mira', 'hero', { amount: 0, wastedReason: 'out_of_range' })] })));
    const attacks = beats.find(b => b.phase === 'attacks')!;
    expect(attacks.floats).toEqual([{ combatantId: 'mira', text: 'out of range', kind: 'wasted' }]);
    expect(attacks.hp).toEqual({});
    expect(attacks.durationMs).toBe(PLAYBACK_TIMING.wastedMs);
  });
});
