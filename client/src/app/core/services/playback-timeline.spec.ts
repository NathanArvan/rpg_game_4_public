import { AttackResult, HealResult, MovementResult, ResolutionPayload, TickResult } from '../../contracts/hub-contracts.generated';
import {
  makeAbilityCondition, makeAbility, makeAbilityUse, makeActiveCondition, makeAttack, makeBleedout, makeBlocked, makeConditionResult, makeEffect,
  makeExpiredCondition, makeHeal, makeMove, makePayload, makePeriodic, makePlanningCombatant, makeReaction, makeTick, makeUnusedReaction, makeWatching,
} from '../../testing/builders';
import { PLAYBACK_TIMING, buildTimeline, periodicIcon, positionsAfterResolution, positionsBeforeResolution } from './playback-timeline';

const move = (id: string, path: [number, number][], wasBlocked = false): MovementResult =>
  makeMove({ combatantId: id, path: path.map(([x, y]) => ({ x, y })), wasBlocked });

const attack = (attackerId: string, targetId: string, hit: boolean, damage = 0, targetNewHp = 10, extra: Partial<AttackResult> = {}): AttackResult =>
  makeAttack({ attackerId, targetId, hit, damage, targetNewHp, feedText: `${attackerId} → ${targetId}`, ...extra });

const tick = (tickIndex: number, parts: Partial<TickResult> = {}): TickResult => makeTick({ tickIndex, ...parts });

const payload = (...ticks: TickResult[]): ResolutionPayload => makePayload({ ticks });

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
    const downedRow = makePlanningCombatant({ id: 'hero', currentHp: 0, isDefeated: true, status: 'downed', bleedoutRoundsLeft: 2 });

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
        attacks: [attack('ogre', 'hero', true, 0, 0, { targetDefeated: true, targetStatus: 'dead', finishingBlow: true })],
      })));
      expect(beat.floats).toEqual([{ combatantId: 'hero', text: 'finished', kind: 'damage' }]);
    });

    it('plays stabilizes after the tick\'s attacks, with a teal aura on the saved ally', () => {
      const beats = buildTimeline(payload(tick(0, { effects: [
        makeEffect({ combatantId: 'knight', effectType: 'stabilize', description: 'knight stabilizes hero', targetCombatantId: 'hero' }),
        makeEffect({ combatantId: 'medic', effectType: 'stabilize', description: "medic's stabilize on x fails", targetCombatantId: 'x', wastedReason: 'out_of_melee_reach' }),
        makeEffect({ combatantId: 'scout', effectType: 'defend', description: 'scout holds position' }),
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
          makeBleedout({ combatantId: 'a', roundsLeft: 1, feedText: 'a is bleeding out — 1 round left' }),
          makeBleedout({ combatantId: 'b', roundsLeft: 0, died: true, feedText: 'b bleeds out' }),
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
    const use = makeAbilityUse({ combatantId: 'hero', abilityId: 'power_strike', abilityName: 'Power Strike',
      targetIds: ['ogre'], feedText: 'hero uses Power Strike on ogre' });
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
    const use = makeAbilityUse({ combatantId: 'hero', abilityId: 'power_strike', abilityName: 'Power Strike',
      targetIds: ['ogre'], wastedReason: 'on_cooldown', feedText: "hero can't use Power Strike — still on cooldown" });
    const beats = buildTimeline(payload(tick(0, { abilityUses: [use] })));
    expect(beats[0].floats).toEqual([{ combatantId: 'hero', text: 'on cooldown', kind: 'wasted' }]);
    expect(beats[0].feed).toContain("hero can't use Power Strike — still on cooldown");
  });
});

describe('playback timeline: movement abilities (3B.3)', () => {
  const use = (abilityId: string, abilityName: string, targetIds: string[] = []) =>
    makeAbilityUse({ combatantId: 'hero', abilityId, abilityName, targetIds });

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
  const heal = (healerId: string, targetId: string, extra: Partial<HealResult> = {}): HealResult =>
    makeHeal({ healerId, targetId, amount: 6, targetNewHp: 6, feedText: `${healerId} heals ${targetId}`, ...extra });

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

describe('playback timeline: conditions (3B.4)', () => {
  const guard = makeConditionResult({
    sourceId: 'g', targetId: 'g', conditionId: 'guarded', conditionName: 'Guarded', abilityId: null, harmful: false, roundsLeft: 0,
    feedText: 'g is Guarded until the end of the round',
  });
  const marked = makeConditionResult({ sourceId: 'k', targetId: 'g', conditionName: 'Marked', feedText: "k's Mark: g is Marked until the end of next round" });

  it('shows Guarded with the first beat of its tick, before anything else happens', () => {
    const beats = buildTimeline(payload(tick(0, {
      movements: [move('k', [[0, 0], [1, 0]])],
      effects: [makeEffect({ combatantId: 'g', effectType: 'defend' })],
      conditions: [guard],
    })));

    const first = beats[0];
    expect(first.phase).toBe('movement');
    expect(first.conditions!['g'].map(c => c.condition.id)).toEqual(['guarded']);
    expect(first.floats).toContain({ combatantId: 'g', text: 'Guarded', kind: 'buff' });
    expect(first.auras).toContain({ combatantId: 'g', kind: 'buff' });
    expect(first.feed).toContain('g is Guarded until the end of the round');
  });

  it('shows a blocked action on the first beat of its tick: ⛔ and the feed line (3B.4 PR 3)', () => {
    const beats = buildTimeline(payload(
      tick(0, { attacks: [attack('a', 'g', true, 2)] }),
      tick(1, { blocked: [makeBlocked({ combatantId: 'g', feedText: 'g is Stunned and can\'t act' })] }),
    ));

    const first = beats.find(b => b.tick === 1)!;
    expect(first.phase).toBe('idle');
    expect(first.floats).toContain({ combatantId: 'g', text: '⛔ STUNNED', kind: 'debuff' });
    expect(first.auras).toContain({ combatantId: 'g', kind: 'debuff' });
    expect(first.feed).toEqual(['Tick 2', 'g is Stunned and can\'t act']);
  });

  it('plays ability conditions in the effects beat, with the definition from the caster\'s loadout', () => {
    const kestrel = makePlanningCombatant({
      id: 'k', abilities: [makeAbility({ id: 'mark', appliesConditions: [makeAbilityCondition({ condition: { id: 'marked', name: 'Marked', description: 'Takes 2 more.' } })] })],
    });
    const p = makePayload({
      ticks: [tick(0, { abilityUses: [makeAbilityUse({ combatantId: 'k', abilityId: 'mark', abilityName: 'Mark' })], conditions: [marked] })],
      updatedCombatants: [kestrel],
    });

    const effects = buildTimeline(p).find(b => b.phase === 'effects')!;
    const shown = effects.conditions!['g'];
    expect(shown.map(c => [c.condition.id, c.condition.description, c.roundsLeft, c.sourceId])).toEqual([['marked', 'Takes 2 more.', 1, 'k']]);
    expect(effects.floats).toEqual([{ combatantId: 'g', text: 'Marked', kind: 'debuff' }]);
    expect(effects.auras).toEqual([{ combatantId: 'g', kind: 'debuff' }]);
    expect(effects.feed).toContain("k's Mark: g is Marked until the end of next round");
  });

  it('keeps conditions from before the round and refreshes in place', () => {
    const before = { g: [makeActiveCondition({ condition: { id: 'blessed', name: 'Blessed', harmful: false }, roundsLeft: 1 }), makeActiveCondition({ roundsLeft: 0 })] };
    const refreshed = makeConditionResult({ targetId: 'g', result: 'refreshed', roundsLeft: 2 });

    const effects = buildTimeline(payload(tick(0, { conditions: [refreshed] })), PLAYBACK_TIMING, before).find(b => b.phase === 'effects')!;

    expect(effects.conditions!['g'].map(c => [c.condition.id, c.roundsLeft])).toEqual([['blessed', 1], ['marked', 2]]);
  });

  it('a miss shows no extra label; another waste says why above the user; a resist floats RESISTED', () => {
    const effects = buildTimeline(payload(tick(0, { conditions: [
      makeConditionResult({ result: 'wasted', wastedReason: 'missed', roundsLeft: 0 }),
      makeConditionResult({ sourceId: 'k', result: 'wasted', wastedReason: 'out_of_range', roundsLeft: 0 }),
      makeConditionResult({ targetId: 'z', result: 'resisted', roundsLeft: 0 }),
    ] }))).find(b => b.phase === 'effects')!;

    expect(effects.floats).toEqual([
      { combatantId: 'k', text: 'out of range', kind: 'wasted' },
      { combatantId: 'z', text: 'RESISTED', kind: 'miss' },
    ]);
    expect(effects.conditions).toBeUndefined();
  });

  it('clears the conditions of someone who falls', () => {
    const before = { b: [makeActiveCondition()] };
    const beats = buildTimeline(payload(tick(0, {
      attacks: [attack('a', 'b', true, 9, 0, { targetDefeated: true, targetStatus: 'dead' })],
    })), PLAYBACK_TIMING, before);

    expect(beats.find(b => b.phase === 'defeats')!.conditions).toEqual({ b: [] });
  });

  it('ends with an expiries beat that drops what ran out and lands on the server\'s lists', () => {
    const p = makePayload({
      ticks: [tick(0)],
      expiredConditions: [makeExpiredCondition({ combatantId: 'g', conditionName: 'Marked', feedText: 'g is no longer Marked' })],
      updatedCombatants: [makePlanningCombatant({ id: 'g', conditions: [makeActiveCondition({ condition: { id: 'blessed' }, roundsLeft: 0 })] })],
    });

    const beats = buildTimeline(p);
    const last = beats.find(b => b.phase === 'expiries')!;
    expect(last.feed).toEqual(['End of round', 'g is no longer Marked']);
    expect(last.conditions!['g'].map(c => c.condition.id)).toEqual(['blessed']);
    expect(last.durationMs).toBe(PLAYBACK_TIMING.expiryMs);
  });

  describe('periodic effects at the end of the round (3B.4 PR 2)', () => {
    it('plays periodic damage and healing first, then bleedout, then expiries', () => {
      const p = makePayload({
        ticks: [tick(0)],
        periodic: [
          makePeriodic({ combatantId: 'g', amount: 3, newHp: 4, feedText: 'g takes 3 damage from Burning (HP: 7 → 4)' }),
          makePeriodic({ combatantId: 'm', conditionId: 'regenerating', conditionName: 'Regenerating', harmful: false, tags: [],
            kind: 'heal', amount: 2, newHp: 9, feedText: 'm heals 2 from Regenerating (HP: 7 → 9)' }),
        ],
        endOfRound: [makeBleedout({ combatantId: 'h', roundsLeft: 1, feedText: 'h is bleeding out — 1 round left' })],
        expiredConditions: [makeExpiredCondition({ combatantId: 'g', feedText: 'g is no longer Burning' })],
      });

      const beats = buildTimeline(p);
      expect(beats.map(b => b.phase).slice(-3)).toEqual(['periodic', 'bleedout', 'expiries']);
      const periodic = beats.find(b => b.phase === 'periodic')!;
      expect(periodic.floats).toEqual([
        { combatantId: 'g', text: '🔥-3', kind: 'periodic' },
        { combatantId: 'm', text: '+2', kind: 'heal' },
      ]);
      expect(periodic.auras).toEqual([{ combatantId: 'g', kind: 'hurt' }, { combatantId: 'm', kind: 'heal' }]);
      expect(periodic.hp).toEqual({ g: 4, m: 9 });
      expect(periodic.durationMs).toBe(PLAYBACK_TIMING.periodicMs);
      // One "End of round" heading, before the first end-of-round line.
      expect(periodic.feed!.slice(-3)).toEqual(['End of round', 'g takes 3 damage from Burning (HP: 7 → 4)', 'm heals 2 from Regenerating (HP: 7 → 9)']);
      expect(beats.find(b => b.phase === 'bleedout')!.feed).toEqual(['h is bleeding out — 1 round left']);
      expect(beats.find(b => b.phase === 'expiries')!.feed).toEqual(['g is no longer Burning']);
    });

    it('drops anyone it downs or kills in a defeats beat, clearing their conditions', () => {
      const before = { hero: [makeActiveCondition({ condition: { id: 'burning' } })], g: [makeActiveCondition({ condition: { id: 'poisoned' } })] };
      const p = makePayload({
        ticks: [tick(0)],
        updatedCombatants: [makePlanningCombatant({ id: 'hero', currentHp: 0, isDefeated: true, status: 'downed', bleedoutRoundsLeft: 2 })],
        periodic: [
          makePeriodic({ combatantId: 'hero', newHp: 0, status: 'downed', fell: true }),
          makePeriodic({ combatantId: 'g', conditionId: 'poisoned', tags: ['poison'], amount: 2, newHp: 0, status: 'dead', fell: true }),
        ],
      });

      const beats = buildTimeline(p, PLAYBACK_TIMING, before);
      const periodicAt = beats.findIndex(b => b.phase === 'periodic');
      expect(beats[periodicAt].floats![1].text).toBe('☠-2');
      const fall = beats[periodicAt + 1];
      expect(fall.phase).toBe('defeats');
      expect(fall.statuses).toEqual({ hero: { status: 'downed', bleedoutRoundsLeft: 2 }, g: { status: 'dead', bleedoutRoundsLeft: 0 } });
      expect(fall.floats).toEqual([{ combatantId: 'hero', text: 'DOWN', kind: 'damage' }]);
      expect(fall.conditions).toEqual({ hero: [], g: [] });
    });

    it('icons follow the tags', () => {
      expect(periodicIcon(['flame'])).toBe('🔥');
      expect(periodicIcon(['poison'])).toBe('☠');
      expect(periodicIcon([])).toBe('');
    });
  });

  describe('reactions (3B.6)', () => {
    it('shows an opportunity attack in the movement beat of the step it fired on, and holds that beat to read it', () => {
      const beats = buildTimeline(payload(tick(0, {
        movements: [move('a', [[5, 5], [4, 5], [3, 5]])],
        reactions: [makeReaction({ reactorId: 'g', triggerId: 'a', step: 1 })],
      }), ), PLAYBACK_TIMING);

      expect(beats.map(b => b.phase)).toEqual(['movement', 'movement']);
      const [first, second] = beats;
      expect(first.auras).toEqual([{ combatantId: 'g', kind: 'lunge' }, { combatantId: 'a', kind: 'hurt' }]);
      expect(first.floats).toEqual([
        { combatantId: 'g', text: 'OPPORTUNITY', kind: 'reaction' },
        { combatantId: 'a', text: '⚔ -3', kind: 'damage' },
      ]);
      expect(first.hp).toEqual({ a: 7 });
      expect(first.durationMs).toBe(PLAYBACK_TIMING.reactionMs);
      expect(first.feed).toEqual(['— Round 1 Resolution —', 'a moves to (3, 5)', "g's opportunity attack on a hits for 3 damage (HP: 10 → 7)"]);
      expect(second.auras).toBeUndefined();
      expect(second.durationMs).toBe(second.moveMs!);
    });

    it('shows MISS over the mover for a miss', () => {
      const [beat] = buildTimeline(payload(tick(0, {
        movements: [move('a', [[5, 5], [4, 5]])],
        reactions: [makeReaction({ reactorId: 'g', triggerId: 'a', attack: makeAttack({ attackerId: 'g', targetId: 'a', hit: false }) })],
      })));
      expect(beat.floats).toContain({ combatantId: 'a', text: 'MISS', kind: 'miss' });
      expect(beat.hp).toBeUndefined();
    });

    it('stops a mover dropped mid-path where it fell, with DOWN and "falls at" after the reaction line', () => {
      const fell = makeMove({ combatantId: 'a', path: [{ x: 5, y: 5 }, { x: 4, y: 5 }], stopReason: 'fell' });
      const beats = buildTimeline(makePayload({
        ticks: [tick(0, {
          movements: [fell, move('b', [[0, 0], [1, 0], [2, 0]])],
          reactions: [makeReaction({
            reactorId: 'g', triggerId: 'a', feedText: 'g hits a — goes down!',
            attack: makeAttack({ attackerId: 'g', targetId: 'a', hit: true, damage: 5, targetNewHp: 0, targetDefeated: true, targetStatus: 'downed' }),
          })],
        })],
        updatedCombatants: [makePlanningCombatant({ id: 'a', status: 'downed', bleedoutRoundsLeft: 2, isDefeated: true })],
      }));

      expect(beats[0].positions).toEqual({ a: { x: 4, y: 5 }, b: { x: 1, y: 0 } });
      expect(beats[0].statuses).toEqual({ a: { status: 'downed', bleedoutRoundsLeft: 2 } });
      expect(beats[0].floats).toContain({ combatantId: 'a', text: 'DOWN', kind: 'damage' });
      expect(beats[0].feed).toEqual(['— Round 1 Resolution —', 'b moves to (2, 0)', 'g hits a — goes down!', 'a falls at (4, 5)']);
      expect(beats[1].positions).toEqual({ b: { x: 2, y: 0 } }); // the fallen mover takes no more steps
    });

    it('says "disengages to" for a Disengage', () => {
      const [beat] = buildTimeline(payload(tick(0, {
        movements: [makeMove({ combatantId: 'a', path: [{ x: 5, y: 5 }, { x: 4, y: 5 }], disengaging: true })],
      })));
      expect(beat.feed).toContain('a disengages to (4, 5)');
    });
  });

  describe('prepared reactions (3B.6 PR 2)', () => {
    it('puts the 👁 badge on with the first beat of the watcher\'s tick, with the "prepares" line', () => {
      const [beat] = buildTimeline(payload(tick(0, { watching: [makeWatching({ combatantId: 'w' })] })));
      expect(beat.phase).toBe('idle');
      expect(beat.watching).toEqual({ w: 'Brace' });
      expect(beat.floats).toContain({ combatantId: 'w', text: '👁 Brace', kind: 'status' });
      expect(beat.feed).toContain('w prepares Brace');
    });

    it('labels a prepared reaction with its name and takes the badge off when it fires', () => {
      const beats = buildTimeline(payload(
        tick(0, { watching: [makeWatching({ combatantId: 'w' })] }),
        tick(1, {
          movements: [move('g', [[8, 5], [7, 5], [6, 5]])],
          reactions: [makeReaction({ reactorId: 'w', triggerId: 'g', kind: 'prepared', abilityId: 'brace', abilityName: 'Brace', step: 2,
            feedText: "w's Brace hits g for 6 damage (HP: 10 → 4)" })],
        }),
      ));
      const fired = beats.find(b => b.tick === 1 && b.floats?.some(f => f.kind === 'reaction'))!;
      expect(fired.floats).toContain({ combatantId: 'w', text: '⚡ BRACE', kind: 'reaction' });
      expect(fired.watching).toEqual({ w: null });
      expect(fired.feed).toContain("w's Brace hits g for 6 damage (HP: 10 → 4)");
    });

    it('ends with an unused note that takes the badge off', () => {
      const beats = buildTimeline(makePayload({
        ticks: [tick(0, { watching: [makeWatching({ combatantId: 'w', abilityName: 'Overwatch' })] })],
        unusedReactions: [makeUnusedReaction({ combatantId: 'w', abilityName: 'Overwatch' })],
      }));
      const last = beats[beats.length - 1];
      expect(last.phase).toBe('unused');
      expect(last.watching).toEqual({ w: null });
      expect(last.floats).toEqual([{ combatantId: 'w', text: '👁 unused', kind: 'status' }]);
      expect(last.feed).toEqual(['End of round', "w's Overwatch goes unused"]);
    });
  });
});
