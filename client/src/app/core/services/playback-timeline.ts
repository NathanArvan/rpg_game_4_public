import {
  ActiveConditionDto, ConditionDto, ConditionResult, MovementResult, PositionDto, ResolutionPayload, TickResult,
} from '../../contracts/hub-contracts.generated';
import { CombatantStatus, toStatus } from '../../state/encounter/encounter.model';

/**
 * Turns a round's resolution (ticks from the server) into a timeline of beats for the client to
 * play back. Pure: no Angular, no timers. ResolutionPlaybackService plays the beats in order,
 * applying each one and then waiting its durationMs.
 *
 * To add a new kind of animation, give PlaybackBeat a new cue field, fill it here, and render it
 * from the service's state.
 */

/**
 * Grey: the attacker missed. Green: the attacker hit. Red: the combatant was hit. Teal: stabilized
 * (3B.5). Soft green: healed or back on their feet (3B.3). Blue / purple: got a beneficial / harmful
 * condition (3B.4).
 */
export type AuraKind = 'miss' | 'hit' | 'hurt' | 'stabilize' | 'heal' | 'buff' | 'debuff';

/** A combatant's status as shown (3B.5). */
export interface StatusView {
  status: CombatantStatus;
  bleedoutRoundsLeft: number;
}

export interface AuraCue {
  combatantId: string;
  kind: AuraKind;
}

export interface FloatCue {
  /** The combatant the text floats above. */
  combatantId: string;
  text: string;
  /**
   * damage: red; miss: grey; wasted: grey italic, above the attacker; status: down / stabilized /
   * bleeding (3B.5); ability: the ability's name above its user (3B.3); heal: green +n, and UP for
   * someone back on their feet (3B.3); buff / debuff: a condition's name as it lands (3B.4).
   */
  kind: 'damage' | 'miss' | 'wasted' | 'status' | 'ability' | 'heal' | 'buff' | 'debuff';
}

/** Short words for why an attack was wasted (the server's wastedReason). */
export function wastedLabel(reason: string | null): string {
  switch (reason) {
    case 'target_defeated': return 'target down';
    case 'out_of_melee_reach': return 'out of reach';
    case 'invalid_target': return 'no target';
    case 'target_not_downed': return 'not bleeding out';
    case 'out_of_range': return 'out of range';
    case 'on_cooldown': return 'on cooldown';
    case 'not_in_loadout': return 'no such ability';
    case 'invalid_target_type': return 'wrong target';
    case 'no_targets': return 'no targets';
    case 'missed': return 'missed';
    default: return 'wasted';
  }
}

export type PlaybackPhase = 'movement' | 'attacks' | 'defeats' | 'revives' | 'effects' | 'bleedout' | 'expiries' | 'idle';

export interface PlaybackBeat {
  /** 0-based tick this beat belongs to. */
  tick: number;
  phase: PlaybackPhase;
  /** How long to hold this beat before the next one starts. */
  durationMs: number;
  /** Positions that change at the start of this beat (movement steps). */
  positions?: Record<string, PositionDto>;
  /** How long tokens take to glide to their new positions. */
  moveMs?: number;
  auras?: AuraCue[];
  floats?: FloatCue[];
  /** HP revealed at this beat. */
  hp?: Record<string, number>;
  /** Statuses that change at this beat (going down, dying, stabilized, bleeding out) (3B.5). */
  statuses?: Record<string, StatusView>;
  /** Combatants whose conditions change at this beat, with their whole list as shown from now on (3B.4). */
  conditions?: Record<string, ActiveConditionDto[]>;
  /** Feed lines added at this beat. */
  feed?: string[];
}

export interface PlaybackTiming {
  /** A tick's movement takes about this long, whatever the path length... */
  moveTotalMs: number;
  /** ...but each cell takes at least minStepMs and at most maxStepMs. */
  minStepMs: number;
  maxStepMs: number;
  /** Hold after a tick's attacks, so auras and numbers can be read. */
  attackMs: number;
  /** Hold for a tick whose only attacks were wasted (long enough to read the label). */
  wastedMs: number;
  /** Hold for a tick with nothing to show (defend, idle, only wasted attacks). */
  idleMs: number;
  /** Hold while defeated tokens fade. */
  defeatMs: number;
  /** Hold while revived tokens get back up (3B.3). */
  reviveMs: number;
  /** Hold after a tick's stabilizes (3B.5). */
  effectMs: number;
  /** Hold for the end-of-round bleedout step (3B.5). */
  bleedoutMs: number;
  /** Hold for the end-of-round step where conditions run out (3B.4). */
  expiryMs: number;
  /** How long an aura glows. */
  auraMs: number;
  /** How long floating damage / MISS text lingers (it may outlast its beat). */
  floatMs: number;
}

export const PLAYBACK_TIMING: PlaybackTiming = {
  moveTotalMs: 1200,
  minStepMs: 150,
  maxStepMs: 300,
  attackMs: 1500,
  wastedMs: 900,
  idleMs: 400,
  defeatMs: 600,
  reviveMs: 900,
  effectMs: 1200,
  bleedoutMs: 1500,
  expiryMs: 900,
  auraMs: 1000,
  floatMs: 2500,
};

/**
 * Feed line for one movement result. A one-cell path that wasn't blocked means the combatant chose
 * to stay put. A move made by an ability (Dash, Charge) names it: "Scout moves to (9, 5) with Dash".
 */
export function movementFeedText(movement: MovementResult, abilityName?: string): string {
  const { x, y } = movement.finalPosition;
  if (abilityName) {
    if (movement.path.length <= 1 && !movement.wasBlocked) return `${movement.combatantName} stays at (${x}, ${y}) (${abilityName})`;
    return `${movement.combatantName} moves to (${x}, ${y}) with ${abilityName}${movement.wasBlocked ? ' (blocked)' : ''}`;
  }
  if (movement.path.length <= 1 && !movement.wasBlocked) {
    return `${movement.combatantName} stays put at (${x}, ${y})`;
  }
  return `${movement.combatantName} moves to (${x}, ${y})${movement.wasBlocked ? ' (blocked)' : ''}`;
}

/**
 * Where everyone stood when the round started. A mover's first path cell is its starting cell
 * (paths include the origin), so this holds whatever `positions` currently says about movers.
 * Everyone else keeps their position from `positions`.
 */
export function positionsBeforeResolution(
  positions: Record<string, PositionDto>, ticks: TickResult[]): Record<string, PositionDto> {
  const start = { ...positions };
  const seen = new Set<string>();
  for (const tick of ticks) {
    for (const m of tick.movements) {
      if (seen.has(m.combatantId)) continue;
      seen.add(m.combatantId);
      start[m.combatantId] = m.path[0] ?? m.finalPosition;
    }
  }
  return start;
}

/** Where everyone stands after the round: each mover's last final position. */
export function positionsAfterResolution(
  positions: Record<string, PositionDto>, ticks: TickResult[]): Record<string, PositionDto> {
  const end = { ...positions };
  for (const tick of ticks)
    for (const m of tick.movements)
      end[m.combatantId] = m.finalPosition;
  return end;
}

/**
 * Follows everyone's conditions through a round (3B.4), so chips can change as playback reaches each
 * event: starts from the conditions before the round, adds or refreshes them as they land, clears
 * them when someone falls, and ends on the server's lists after the round.
 */
class ConditionTracker {
  private readonly lists = new Map<string, ActiveConditionDto[]>();
  private readonly definitions = new Map<string, ConditionDto>();

  constructor(before: Record<string, ActiveConditionDto[]>, payload: ResolutionPayload) {
    for (const [id, list] of Object.entries(before)) this.lists.set(id, list);
    const learn = (c: ConditionDto) => { if (!this.definitions.has(c.id)) this.definitions.set(c.id, c); };
    for (const list of Object.values(before)) list.forEach(a => learn(a.condition));
    for (const u of payload.updatedCombatants ?? []) {
      (u.conditions ?? []).forEach(a => learn(a.condition));
      for (const ability of u.abilities ?? []) (ability.appliesConditions ?? []).forEach(a => learn(a.condition));
    }
  }

  /** Applies a condition result; returns the target's new list, or null if nothing changed. */
  apply(r: ConditionResult): ActiveConditionDto[] | null {
    if (r.result !== 'applied' && r.result !== 'refreshed') return null;
    const condition = this.definitions.get(r.conditionId) ?? {
      id: r.conditionId, name: r.conditionName, description: '', harmful: r.harmful, tags: [],
      modifiers: { accuracy: 0, dodge: 0, attackKind: 'any', damageDealt: 0, damageTaken: null, movement: 0, ap: 0, periodic: null, blocks: [] },
    };
    const entry: ActiveConditionDto = { condition, roundsLeft: r.roundsLeft, sourceId: r.sourceId };
    const list = [...(this.lists.get(r.targetId) ?? [])];
    const i = list.findIndex(a => a.condition.id === r.conditionId);
    if (i >= 0) list[i] = entry; else list.push(entry);
    this.lists.set(r.targetId, list);
    return list;
  }

  /** Someone fell: their conditions are gone (3B.4). Returns true if they had any. */
  clear(combatantId: string): boolean {
    const had = (this.lists.get(combatantId) ?? []).length > 0;
    this.lists.set(combatantId, []);
    return had;
  }
}

/**
 * The beats of a round, in order. Each tick plays as: movement steps → attacks and heals →
 * defeats → revives → effects (stabilize, conditions); after the last tick, the end-of-round bleedout
 * step (3B.5) and conditions running out (3B.4). Defend's Guarded shows with the tick's first beat.
 * Attacks and heals within a tick are simultaneous (the rules net them together), so they share
 * one beat; the next tick's actions only start after it.
 *
 * @param conditionsBefore everyone's conditions as the round started, so chips can follow playback (3B.4).
 */
export function buildTimeline(
  payload: ResolutionPayload,
  timing: PlaybackTiming = PLAYBACK_TIMING,
  conditionsBefore: Record<string, ActiveConditionDto[]> = {},
): PlaybackBeat[] {
  const beats: PlaybackBeat[] = [];
  const tracker = new ConditionTracker(conditionsBefore, payload);
  const severalTicks = payload.ticks.length > 1;
  // Bleedout rounds for anyone downed this round come from the server's end-of-round rows.
  const finalBleedout = new Map((payload.updatedCombatants ?? []).map(c => [c.id, c.bleedoutRoundsLeft ?? 0]));
  // Feed lines wait for the next beat, so they appear when the thing they describe happens.
  let pendingFeed: string[] = [`— Round ${payload.newRoundNumber - 1} Resolution —`];
  const takeFeed = (): string[] | undefined => {
    if (pendingFeed.length === 0) return undefined;
    const feed = pendingFeed;
    pendingFeed = [];
    return feed;
  };

  for (const tick of payload.ticks) {
    const t = tick.tickIndex;
    const beatsBefore = beats.length;
    if (severalTicks) pendingFeed.push(`Tick ${t + 1}`);

    // Defend's Guarded (3B.4) lands at the start of the tick: shown with the tick's first beat.
    const tickConditions = tick.conditions ?? [];
    const guards = tickConditions.filter(c => c.abilityId === null);
    const guardChanges: Record<string, ActiveConditionDto[]> = {};
    for (const g of guards) {
      const list = tracker.apply(g);
      if (list) guardChanges[g.targetId] = list;
    }
    pendingFeed.push(...guards.map(g => g.feedText));

    // Movement: one beat per cell, every mover stepping in lockstep (as the rules resolve it).
    const uses = tick.abilityUses ?? [];
    const abilityNameOf = (m: MovementResult) => m.abilityId
      ? uses.find(u => u.combatantId === m.combatantId && u.abilityId === m.abilityId)?.abilityName ?? m.abilityId
      : undefined;
    pendingFeed.push(...tick.movements.map(m => movementFeedText(m, abilityNameOf(m))));
    const steps = Math.max(0, ...tick.movements.map(m => m.path.length - 1));
    // An ability that moves its user (Dash, Charge) shows its name as the move starts.
    const movedByAbility = new Set(tick.movements.filter(m => m.abilityId && m.path.length > 1).map(m => m.combatantId));
    if (steps > 0) {
      const stepMs = Math.min(timing.maxStepMs, Math.max(timing.minStepMs, Math.floor(timing.moveTotalMs / steps)));
      for (let step = 1; step <= steps; step++) {
        const positions: Record<string, PositionDto> = {};
        for (const m of tick.movements)
          if (step < m.path.length) positions[m.combatantId] = m.path[step];
        const beat: PlaybackBeat = { tick: t, phase: 'movement', durationMs: stepMs, moveMs: stepMs, positions, feed: takeFeed() };
        if (step === 1 && movedByAbility.size > 0)
          beat.floats = uses.filter(u => !u.wastedReason && movedByAbility.has(u.combatantId))
            .map(u => ({ combatantId: u.combatantId, text: u.abilityName, kind: 'ability' as const }));
        beats.push(beat);
      }
    }

    // Abilities used this tick (3B.3): the name floats over the user as its attacks land (unless it
    // already showed as the user moved). A refused one (e.g. still on cooldown) made no attacks, so
    // it gets its own feed line and label.
    const abilityFloats: FloatCue[] = [];
    for (const use of uses) {
      if (!use.wastedReason && movedByAbility.has(use.combatantId)) continue;
      if (use.wastedReason) {
        pendingFeed.push(use.feedText);
        abilityFloats.push({ combatantId: use.combatantId, text: wastedLabel(use.wastedReason), kind: 'wasted' });
      } else {
        abilityFloats.push({ combatantId: use.combatantId, text: use.abilityName, kind: 'ability' });
      }
    }

    // Attacks and heals: all of this tick's land together (3B.3: heals are netted with the damage).
    const heals = tick.heals ?? [];
    pendingFeed.push(...tick.attacks.map(a => a.feedText), ...heals.map(h => h.feedText));
    if (tick.attacks.length > 0 || heals.length > 0 || abilityFloats.length > 0) {
      const auras: AuraCue[] = [];
      const addAura = (combatantId: string, kind: AuraKind) => {
        if (!auras.some(a => a.combatantId === combatantId && a.kind === kind)) auras.push({ combatantId, kind });
      };
      const floats: FloatCue[] = [...abilityFloats];
      const hp: Record<string, number> = {};
      for (const a of tick.attacks) {
        if (a.wasted) {
          // Nothing was rolled: say why above the attacker.
          floats.push({ combatantId: a.attackerId, text: wastedLabel(a.wastedReason), kind: 'wasted' });
          continue;
        }
        addAura(a.attackerId, a.hit ? 'hit' : 'miss');
        if (a.hit) addAura(a.targetId, 'hurt');
        if (!a.hit) floats.push({ combatantId: a.targetId, text: 'MISS', kind: 'miss' });
        else if (a.finishingBlow) floats.push({ combatantId: a.targetId, text: 'finished', kind: 'damage' });
        else floats.push({ combatantId: a.targetId, text: `-${a.damage}`, kind: 'damage' });
        hp[a.targetId] = a.targetNewHp;
      }
      for (const h of heals) {
        if (h.wastedReason) {
          floats.push({ combatantId: h.healerId, text: wastedLabel(h.wastedReason), kind: 'wasted' });
          continue;
        }
        addAura(h.targetId, 'heal');
        floats.push({ combatantId: h.targetId, text: `+${h.amount}`, kind: 'heal' });
        hp[h.targetId] = h.targetNewHp;
      }
      const anyLanded = tick.attacks.some(a => !a.wasted) || heals.some(h => !h.wastedReason);
      beats.push({
        tick: t, phase: 'attacks', durationMs: anyLanded ? timing.attackMs : timing.wastedMs,
        auras, floats, hp, feed: takeFeed(),
      });
    }

    // Who fell this tick: Downed (bleeding out) or dead.
    const fell: Record<string, StatusView> = {};
    const downFloats: FloatCue[] = [];
    const clearedConditions: Record<string, ActiveConditionDto[]> = {};
    for (const a of tick.attacks.filter(a => a.targetDefeated)) {
      const status = toStatus(a.targetStatus, true);
      fell[a.targetId] = { status, bleedoutRoundsLeft: status === 'downed' ? finalBleedout.get(a.targetId) ?? 0 : 0 };
      if (status === 'downed') downFloats.push({ combatantId: a.targetId, text: 'DOWN', kind: 'damage' });
      if (tracker.clear(a.targetId)) clearedConditions[a.targetId] = []; // falling clears conditions (3B.4)
    }
    if (Object.keys(fell).length > 0) {
      const beat: PlaybackBeat = { tick: t, phase: 'defeats', durationMs: timing.defeatMs, statuses: fell, floats: downFloats, feed: takeFeed() };
      if (Object.keys(clearedConditions).length > 0) beat.conditions = clearedConditions;
      beats.push(beat);
    }

    // Back on their feet (3B.3): a heal revived someone who was down.
    const revived = heals.filter(h => h.revived && !h.wastedReason);
    if (revived.length > 0) {
      const statuses: Record<string, StatusView> = {};
      for (const h of revived) statuses[h.targetId] = { status: 'standing', bleedoutRoundsLeft: 0 };
      beats.push({
        tick: t, phase: 'revives', durationMs: timing.reviveMs, statuses,
        auras: revived.map(h => ({ combatantId: h.targetId, kind: 'heal' as const })),
        floats: revived.map(h => ({ combatantId: h.targetId, text: 'UP', kind: 'heal' as const })),
        feed: takeFeed(),
      });
    }

    // Effects: stabilizes, then conditions from abilities (3B.4). Defend's Guarded showed at the start.
    const stabilizes = tick.effects.filter(e => e.effectType === 'stabilize');
    const abilityConditions = tickConditions.filter(c => c.abilityId !== null);
    if (stabilizes.length > 0 || abilityConditions.length > 0) {
      pendingFeed.push(...stabilizes.map(e => e.description), ...abilityConditions.map(c => c.feedText));
      const statuses: Record<string, StatusView> = {};
      const auras: AuraCue[] = [];
      const floats: FloatCue[] = [];
      const conditions: Record<string, ActiveConditionDto[]> = {};
      for (const c of abilityConditions) {
        const list = tracker.apply(c);
        if (list) {
          conditions[c.targetId] = list;
          const kind = c.harmful ? 'debuff' as const : 'buff' as const;
          if (!auras.some(a => a.combatantId === c.targetId && a.kind === kind)) auras.push({ combatantId: c.targetId, kind });
          floats.push({ combatantId: c.targetId, text: c.conditionName, kind });
        } else if (c.result === 'resisted') {
          floats.push({ combatantId: c.targetId, text: 'RESISTED', kind: 'miss' });
        } else if (c.wastedReason && c.wastedReason !== 'missed') {
          // A miss already showed MISS; anything else says why above whoever used it.
          floats.push({ combatantId: c.sourceId, text: wastedLabel(c.wastedReason), kind: 'wasted' });
        }
      }
      for (const e of stabilizes) {
        if (e.wastedReason || !e.targetCombatantId) {
          floats.push({ combatantId: e.combatantId, text: wastedLabel(e.wastedReason), kind: 'wasted' });
          continue;
        }
        statuses[e.targetCombatantId] = { status: 'stable', bleedoutRoundsLeft: 0 };
        auras.push({ combatantId: e.targetCombatantId, kind: 'stabilize' });
        floats.push({ combatantId: e.targetCombatantId, text: 'stabilized', kind: 'status' });
      }
      const beat: PlaybackBeat = { tick: t, phase: 'effects', durationMs: timing.effectMs, statuses, auras, floats, feed: takeFeed() };
      if (Object.keys(conditions).length > 0) beat.conditions = conditions;
      beats.push(beat);
    }

    // Nothing visible happened this tick (defends, idles): a short beat for it.
    if (beats.length === beatsBefore)
      beats.push({ tick: t, phase: 'idle', durationMs: timing.idleMs, feed: takeFeed() });

    // Guarded shows as the tick starts: on its first beat, with a short cue over the defender.
    if (guards.length > 0) {
      const first = beats[beatsBefore];
      first.conditions = { ...guardChanges, ...first.conditions };
      first.auras = [...(first.auras ?? []), ...Object.keys(guardChanges).map(id => ({ combatantId: id, kind: 'buff' as const }))];
      first.floats = [...(first.floats ?? []), ...guards.map(g => ({ combatantId: g.targetId, text: g.conditionName, kind: 'buff' as const }))];
    }
  }

  const lastTick = Math.max(0, payload.ticks.length - 1);

  // End of round: bleedout countdowns tick, and anyone at 0 dies (3B.5).
  const bleeding = payload.endOfRound ?? [];
  if (bleeding.length > 0) {
    pendingFeed.push('End of round', ...bleeding.map(b => b.feedText));
    const statuses: Record<string, StatusView> = {};
    const floats: FloatCue[] = [];
    for (const b of bleeding) {
      statuses[b.combatantId] = b.died ? { status: 'dead', bleedoutRoundsLeft: 0 } : { status: 'downed', bleedoutRoundsLeft: b.roundsLeft };
      floats.push(b.died
        ? { combatantId: b.combatantId, text: 'bled out', kind: 'damage' }
        : { combatantId: b.combatantId, text: `bleeding: ${b.roundsLeft}`, kind: 'status' });
    }
    beats.push({ tick: lastTick, phase: 'bleedout', durationMs: timing.bleedoutMs, statuses, floats, feed: takeFeed() });
  }

  // End of round: conditions that ran out go (3B.4); chips end on the server's lists.
  const expired = payload.expiredConditions ?? [];
  if (expired.length > 0) {
    if (bleeding.length === 0) pendingFeed.push('End of round');
    pendingFeed.push(...expired.map(x => x.feedText));
    const conditions: Record<string, ActiveConditionDto[]> = {};
    for (const u of payload.updatedCombatants ?? [])
      if (expired.some(x => x.combatantId === u.id)) conditions[u.id] = u.conditions ?? [];
    beats.push({ tick: lastTick, phase: 'expiries', durationMs: timing.expiryMs, conditions, feed: takeFeed() });
  }

  if (pendingFeed.length > 0)
    beats.push({ tick: lastTick, phase: 'idle', durationMs: timing.idleMs, feed: takeFeed() });
  return beats;
}
