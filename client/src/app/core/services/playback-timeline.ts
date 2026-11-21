import { MovementResult, PositionDto, ResolutionPayload, TickResult } from '../../contracts/hub-contracts.generated';

/**
 * Turns a round's resolution (ticks from the server) into a timeline of beats for the client to
 * play back. Pure: no Angular, no timers. ResolutionPlaybackService plays the beats in order,
 * applying each one and then waiting its durationMs.
 *
 * To add a new kind of animation, give PlaybackBeat a new cue field, fill it here, and render it
 * from the service's state.
 */

/** Grey: the attacker missed. Green: the attacker hit. Red: the combatant was hit. */
export type AuraKind = 'miss' | 'hit' | 'hurt';

export interface AuraCue {
  combatantId: string;
  kind: AuraKind;
}

export interface FloatCue {
  /** The combatant the text floats above. */
  combatantId: string;
  text: string;
  kind: 'damage' | 'miss';
}

export type PlaybackPhase = 'movement' | 'attacks' | 'defeats' | 'idle';

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
  /** Combatants shown as defeated from this beat on. */
  defeated?: string[];
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
  /** Hold for a tick with nothing to show (defend, idle, only wasted attacks). */
  idleMs: number;
  /** Hold while defeated tokens fade. */
  defeatMs: number;
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
  idleMs: 400,
  defeatMs: 600,
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
 * The beats of a round, in order. Each tick plays as: movement steps → attacks → defeats.
 * Attacks within a tick are simultaneous (the rules net their damage together), so they share
 * one beat; the next tick's actions only start after it.
 */
export function buildTimeline(payload: ResolutionPayload, timing: PlaybackTiming = PLAYBACK_TIMING): PlaybackBeat[] {
  const beats: PlaybackBeat[] = [];
  const severalTicks = payload.ticks.length > 1;
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

    // Attacks: all of this tick's attacks land together.
    pendingFeed.push(...tick.attacks.map(a => a.feedText));
    const landed = tick.attacks.filter(a => !a.wasted);
    if (landed.length > 0) {
      const auras: AuraCue[] = [];
      const addAura = (combatantId: string, kind: AuraKind) => {
        if (!auras.some(a => a.combatantId === combatantId && a.kind === kind)) auras.push({ combatantId, kind });
      };
      const floats: FloatCue[] = [];
      const hp: Record<string, number> = {};
      for (const a of landed) {
        addAura(a.attackerId, a.hit ? 'hit' : 'miss');
        if (a.hit) addAura(a.targetId, 'hurt');
        floats.push(a.hit
          ? { combatantId: a.targetId, text: `-${a.damage}`, kind: 'damage' }
          : { combatantId: a.targetId, text: 'MISS', kind: 'miss' });
        hp[a.targetId] = a.targetNewHp;
      }
      beats.push({ tick: t, phase: 'attacks', durationMs: timing.attackMs, auras, floats, hp, feed: takeFeed() });
    }

    const defeated = [...new Set(tick.attacks.filter(a => a.targetDefeated).map(a => a.targetId))];
    if (defeated.length > 0)
      beats.push({ tick: t, phase: 'defeats', durationMs: timing.defeatMs, defeated, feed: takeFeed() });

    // Nothing visible happened this tick (defends, idles, wasted attacks): a short beat for it.
    if (beats.length === beatsBefore)
      beats.push({ tick: t, phase: 'idle', durationMs: timing.idleMs, feed: takeFeed() });
  }

  if (pendingFeed.length > 0)
    beats.push({ tick: Math.max(0, payload.ticks.length - 1), phase: 'idle', durationMs: timing.idleMs, feed: takeFeed() });
  return beats;
}
