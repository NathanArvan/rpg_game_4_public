import { MovementResult, PositionDto, ResolutionPayload, TickResult } from '../../contracts/hub-contracts.generated';
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
 * (3B.5). Soft green: healed or back on their feet (3B.3).
 */
export type AuraKind = 'miss' | 'hit' | 'hurt' | 'stabilize' | 'heal';

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
   * someone back on their feet (3B.3).
   */
  kind: 'damage' | 'miss' | 'wasted' | 'status' | 'ability' | 'heal';
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
    default: return 'wasted';
  }
}

export type PlaybackPhase = 'movement' | 'attacks' | 'defeats' | 'revives' | 'effects' | 'bleedout' | 'idle';

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
  auraMs: 1000,
  floatMs: 2500,
};

/** Feed line for one movement result. A one-cell path that wasn't blocked means the combatant chose to stay put. */
export function movementFeedText(movement: MovementResult): string {
  const { x, y } = movement.finalPosition;
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
 * The beats of a round, in order. Each tick plays as: movement steps → attacks and heals →
 * defeats → revives → effects (stabilize); after the last tick, the end-of-round bleedout step (3B.5).
 * Attacks and heals within a tick are simultaneous (the rules net them together), so they share
 * one beat; the next tick's actions only start after it.
 */
export function buildTimeline(payload: ResolutionPayload, timing: PlaybackTiming = PLAYBACK_TIMING): PlaybackBeat[] {
  const beats: PlaybackBeat[] = [];
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

    // Movement: one beat per cell, every mover stepping in lockstep (as the rules resolve it).
    pendingFeed.push(...tick.movements.map(movementFeedText));
    const steps = Math.max(0, ...tick.movements.map(m => m.path.length - 1));
    if (steps > 0) {
      const stepMs = Math.min(timing.maxStepMs, Math.max(timing.minStepMs, Math.floor(timing.moveTotalMs / steps)));
      for (let step = 1; step <= steps; step++) {
        const positions: Record<string, PositionDto> = {};
        for (const m of tick.movements)
          if (step < m.path.length) positions[m.combatantId] = m.path[step];
        beats.push({ tick: t, phase: 'movement', durationMs: stepMs, moveMs: stepMs, positions, feed: takeFeed() });
      }
    }

    // Abilities used this tick (3B.3): the name floats over the user as its attacks land. A refused
    // one (e.g. still on cooldown) made no attacks, so it gets its own feed line and label.
    const abilityFloats: FloatCue[] = [];
    for (const use of tick.abilityUses ?? []) {
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
    for (const a of tick.attacks.filter(a => a.targetDefeated)) {
      const status = toStatus(a.targetStatus, true);
      fell[a.targetId] = { status, bleedoutRoundsLeft: status === 'downed' ? finalBleedout.get(a.targetId) ?? 0 : 0 };
      if (status === 'downed') downFloats.push({ combatantId: a.targetId, text: 'DOWN', kind: 'damage' });
    }
    if (Object.keys(fell).length > 0)
      beats.push({ tick: t, phase: 'defeats', durationMs: timing.defeatMs, statuses: fell, floats: downFloats, feed: takeFeed() });

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

    // Effects: stabilizes (defends have nothing to show yet).
    const stabilizes = tick.effects.filter(e => e.effectType === 'stabilize');
    if (stabilizes.length > 0) {
      pendingFeed.push(...stabilizes.map(e => e.description));
      const statuses: Record<string, StatusView> = {};
      const auras: AuraCue[] = [];
      const floats: FloatCue[] = [];
      for (const e of stabilizes) {
        if (e.wastedReason || !e.targetCombatantId) {
          floats.push({ combatantId: e.combatantId, text: wastedLabel(e.wastedReason), kind: 'wasted' });
          continue;
        }
        statuses[e.targetCombatantId] = { status: 'stable', bleedoutRoundsLeft: 0 };
        auras.push({ combatantId: e.targetCombatantId, kind: 'stabilize' });
        floats.push({ combatantId: e.targetCombatantId, text: 'stabilized', kind: 'status' });
      }
      beats.push({ tick: t, phase: 'effects', durationMs: timing.effectMs, statuses, auras, floats, feed: takeFeed() });
    }

    // Nothing visible happened this tick (defends, idles): a short beat for it.
    if (beats.length === beatsBefore)
      beats.push({ tick: t, phase: 'idle', durationMs: timing.idleMs, feed: takeFeed() });
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

  if (pendingFeed.length > 0)
    beats.push({ tick: lastTick, phase: 'idle', durationMs: timing.idleMs, feed: takeFeed() });
  return beats;
}
