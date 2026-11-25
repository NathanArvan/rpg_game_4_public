import {
  ActiveConditionDto, ConditionDto, ConditionResult, MovementResult, PositionDto, ReactionResult, ResolutionPayload, TickResult,
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
 * condition (3B.4). Orange lunge: struck out at a mover in a reaction (3B.6).
 */
export type AuraKind = 'miss' | 'hit' | 'hurt' | 'stabilize' | 'heal' | 'buff' | 'debuff' | 'lunge';

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
   * someone back on their feet (3B.3); buff / debuff: a condition's name as it lands (3B.4);
   * periodic: orange end-of-round damage from a condition, with an icon for its tag (3B.4 PR 2);
   * reaction: the reaction's label (OPPORTUNITY) above the reactor (3B.6).
   */
  kind: 'damage' | 'miss' | 'wasted' | 'status' | 'ability' | 'heal' | 'buff' | 'debuff' | 'periodic' | 'reaction';
}

/** A small icon for a periodic effect's tags (3B.4 PR 2): flame, poison, or none. */
export function periodicIcon(tags: readonly string[]): string {
  if (tags.includes('flame')) return '🔥';
  if (tags.includes('poison')) return '☠';
  return '';
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

export type PlaybackPhase =
  'movement' | 'attacks' | 'defeats' | 'revives' | 'effects' | 'periodic' | 'bleedout' | 'expiries' | 'unused' | 'idle';

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
  /**
   * Prepared reactions that start or stop watching at this beat (3B.6): combatant → the ability's name (shown as a 👁
   * badge on the token from now on), or null to take the badge off (it fired, or the round ended).
   */
  watching?: Record<string, string | null>;
}

export interface PlaybackTiming {
  /** A tick's movement takes about this long, whatever the path length... */
  moveTotalMs: number;
  /** ...but each cell takes at least minStepMs and at most maxStepMs. */
  minStepMs: number;
  maxStepMs: number;
  /** Hold after a tick's attacks, so auras and numbers can be read. */
  attackMs: number;
  /** Hold on a movement step where a reaction fired (3B.6), so the strike can be read before the mover walks on. */
  reactionMs: number;
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
  /** Hold for the end-of-round periodic damage and healing (3B.4 PR 2). */
  periodicMs: number;
  /** Hold for the end-of-round bleedout step (3B.5). */
  bleedoutMs: number;
  /** Hold for the end-of-round step where conditions run out (3B.4). */
  expiryMs: number;
  /** Hold for the end-of-round note on prepared reactions that never fired (3B.6). */
  unusedMs: number;
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
  reactionMs: 1100,
  wastedMs: 900,
  idleMs: 400,
  defeatMs: 600,
  reviveMs: 900,
  effectMs: 1200,
  periodicMs: 1500,
  bleedoutMs: 1500,
  expiryMs: 900,
  unusedMs: 700,
  auraMs: 1000,
  floatMs: 2500,
};

/**
 * Feed line for one movement result. A one-cell path that wasn't blocked means the combatant chose
 * to stay put. A move made by an ability (Dash, Charge) names it: "Scout moves to (9, 5) with Dash".
 * A Disengage says so ("Mira disengages to (5, 6)"), and a mover a reaction dropped "falls at (5, 7)" (3B.6).
 */
export function movementFeedText(movement: MovementResult, abilityName?: string): string {
  const { x, y } = movement.finalPosition;
  if (movement.stopReason === 'fell') return `${movement.combatantName} falls at (${x}, ${y})`;
  if (movement.disengaging && !abilityName && movement.path.length > 1)
    return `${movement.combatantName} disengages to (${x}, ${y})${movement.wasBlocked ? ' (blocked)' : ''}`;
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
 * A reaction cue inside the movement beat of the step it fired on (3B.6): an orange lunge aura and its label
 * (OPPORTUNITY, or the prepared ability's name) over the reactor, `⚔ -4` / MISS over the mover with its HP, and DOWN
 * (with the status) for a mover it dropped, whose token stops there. The beat is held long enough to read it.
 */
function addReactionCues(
  beat: PlaybackBeat, reactions: ReactionResult[], tracker: ConditionTracker, finalBleedout: Map<string, number>,
  timing: PlaybackTiming,
): void {
  const auras: AuraCue[] = [...(beat.auras ?? [])];
  const floats: FloatCue[] = [...(beat.floats ?? [])];
  const hp: Record<string, number> = { ...beat.hp };
  const statuses: Record<string, StatusView> = { ...beat.statuses };
  const conditions: Record<string, ActiveConditionDto[]> = { ...beat.conditions };
  const watching: Record<string, string | null> = { ...beat.watching };
  for (const r of reactions) {
    const label = r.kind === 'opportunity' ? 'OPPORTUNITY' : `⚡ ${(r.abilityName ?? r.abilityId ?? '').toUpperCase()}`;
    if (r.kind === 'prepared') watching[r.reactorId] = null; // it fired: no longer watching (SPRINT_3B6 X9)
    if (!auras.some(a => a.combatantId === r.reactorId)) auras.push({ combatantId: r.reactorId, kind: 'lunge' });
    floats.push({ combatantId: r.reactorId, text: label, kind: 'reaction' });
    const a = r.attack;
    if (!a) continue;
    if (a.wasted) { floats.push({ combatantId: r.reactorId, text: wastedLabel(a.wastedReason), kind: 'wasted' }); continue; }
    if (!a.hit) { floats.push({ combatantId: a.targetId, text: 'MISS', kind: 'miss' }); continue; }
    if (!auras.some(x => x.combatantId === a.targetId && x.kind === 'hurt')) auras.push({ combatantId: a.targetId, kind: 'hurt' });
    floats.push({ combatantId: a.targetId, text: `⚔ -${a.damage}`, kind: 'damage' });
    hp[a.targetId] = a.targetNewHp;
    if (a.targetDefeated && !statuses[a.targetId]) {
      const status = toStatus(a.targetStatus, true);
      statuses[a.targetId] = { status, bleedoutRoundsLeft: status === 'downed' ? finalBleedout.get(a.targetId) ?? 0 : 0 };
      if (status === 'downed') floats.push({ combatantId: a.targetId, text: 'DOWN', kind: 'damage' });
      if (tracker.clear(a.targetId)) conditions[a.targetId] = [];
    }
  }
  beat.auras = auras;
  beat.floats = floats;
  if (Object.keys(hp).length > 0) beat.hp = hp;
  if (Object.keys(statuses).length > 0) beat.statuses = statuses;
  if (Object.keys(conditions).length > 0) beat.conditions = conditions;
  if (Object.keys(watching).length > 0) beat.watching = watching;
  beat.durationMs = Math.max(beat.durationMs, timing.reactionMs);
}

/**
 * The beats of a round, in order. Each tick plays as: movement steps (with any reactions they provoked, 3B.6) → attacks and heals →
 * defeats → revives → effects (stabilize, conditions); after the last tick, the end of round:
 * periodic damage and healing (3B.4 PR 2), anyone that drops, the bleedout step (3B.5) and conditions
 * running out (3B.4). Defend's Guarded shows with the tick's first beat.
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

    // Actions a condition stopped (3B.4 PR 3): Stunned / Rooted, said as the tick starts.
    const blocked = tick.blocked ?? [];
    pendingFeed.push(...blocked.map(b => b.feedText));

    // Prepared reactions (3B.6) start watching as the tick starts, before its movement: 👁 on the token from then on.
    const watchers = tick.watching ?? [];
    pendingFeed.push(...watchers.map(w => w.feedText));

    // Movement: one beat per cell, every mover stepping in lockstep (as the rules resolve it).
    const uses = tick.abilityUses ?? [];
    const abilityNameOf = (m: MovementResult) => m.abilityId
      ? uses.find(u => u.combatantId === m.combatantId && u.abilityId === m.abilityId)?.abilityName ?? m.abilityId
      : undefined;
    // A mover a reaction dropped gets its line ("falls at") at the step it fell on, after the reaction's.
    pendingFeed.push(...tick.movements.filter(m => m.stopReason !== 'fell').map(m => movementFeedText(m, abilityNameOf(m))));
    const reactions = tick.reactions ?? [];
    const steps = Math.max(0, ...tick.movements.map(m => m.path.length - 1));
    // An ability that moves its user (Dash, Charge) shows its name as the move starts.
    const movedByAbility = new Set(tick.movements.filter(m => m.abilityId && m.path.length > 1).map(m => m.combatantId));
    if (steps > 0) {
      const stepMs = Math.min(timing.maxStepMs, Math.max(timing.minStepMs, Math.floor(timing.moveTotalMs / steps)));
      for (let step = 1; step <= steps; step++) {
        const positions: Record<string, PositionDto> = {};
        for (const m of tick.movements)
          if (step < m.path.length) positions[m.combatantId] = m.path[step];
        const stepReactions = reactions.filter(r => r.step === step);
        const fellHere = tick.movements.filter(m => m.stopReason === 'fell' && m.path.length - 1 === step);
        pendingFeed.push(...stepReactions.map(r => r.feedText), ...fellHere.map(m => movementFeedText(m)));
        const beat: PlaybackBeat = { tick: t, phase: 'movement', durationMs: stepMs, moveMs: stepMs, positions, feed: takeFeed() };
        if (step === 1 && movedByAbility.size > 0)
          beat.floats = uses.filter(u => !u.wastedReason && movedByAbility.has(u.combatantId))
            .map(u => ({ combatantId: u.combatantId, text: u.abilityName, kind: 'ability' as const }));
        if (stepReactions.length > 0) addReactionCues(beat, stepReactions, tracker, finalBleedout, timing);
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

    // A blocked action shows on the tick's first beat: ⛔ and the condition's name over the combatant.
    if (blocked.length > 0) {
      const first = beats[beatsBefore];
      const shown = [...new Map(blocked.map(b => [b.combatantId, b])).values()]; // once per combatant per tick
      first.auras = [...(first.auras ?? []), ...shown.map(b => ({ combatantId: b.combatantId, kind: 'debuff' as const }))];
      first.floats = [...(first.floats ?? []),
        ...shown.map(b => ({ combatantId: b.combatantId, text: `⛔ ${b.conditionName.toUpperCase()}`, kind: 'debuff' as const }))];
    }

    // A watcher's 👁 badge goes on with the tick's first beat (a reaction that fires on that very beat takes it off again).
    if (watchers.length > 0) {
      const first = beats[beatsBefore];
      first.watching = { ...Object.fromEntries(watchers.map(w => [w.combatantId, w.abilityName])), ...first.watching };
      first.floats = [...(first.floats ?? []),
        ...watchers.map(w => ({ combatantId: w.combatantId, text: `👁 ${w.abilityName}`, kind: 'status' as const }))];
    }

    // Guarded shows as the tick starts: on its first beat, with a short cue over the defender.
    if (guards.length > 0) {
      const first = beats[beatsBefore];
      first.conditions = { ...guardChanges, ...first.conditions };
      first.auras = [...(first.auras ?? []), ...Object.keys(guardChanges).map(id => ({ combatantId: id, kind: 'buff' as const }))];
      first.floats = [...(first.floats ?? []), ...guards.map(g => ({ combatantId: g.targetId, text: g.conditionName, kind: 'buff' as const }))];
    }
  }

  const lastTick = Math.max(0, payload.ticks.length - 1);
  let endOfRoundHeading = false;
  const endOfRound = () => {
    if (!endOfRoundHeading) pendingFeed.push('End of round');
    endOfRoundHeading = true;
  };

  // End of round, first: prepared reactions that never fired (3B.6). No refund; the badge comes off.
  const unused = payload.unusedReactions ?? [];
  if (unused.length > 0) {
    endOfRound();
    pendingFeed.push(...unused.map(u => u.feedText));
    beats.push({
      tick: lastTick, phase: 'unused', durationMs: timing.unusedMs, feed: takeFeed(),
      floats: unused.map(u => ({ combatantId: u.combatantId, text: '👁 unused', kind: 'status' as const })),
      watching: Object.fromEntries(unused.map(u => [u.combatantId, null])),
    });
  }

  // Then conditions' periodic damage and healing, netted per combatant (3B.4 PR 2).
  const periodic = payload.periodic ?? [];
  if (periodic.length > 0) {
    endOfRound();
    pendingFeed.push(...periodic.map(p => p.feedText));
    const auras: AuraCue[] = [];
    const floats: FloatCue[] = [];
    const hp: Record<string, number> = {};
    for (const p of periodic) {
      const kind: AuraKind = p.kind === 'heal' ? 'heal' : 'hurt';
      if (!auras.some(a => a.combatantId === p.combatantId && a.kind === kind)) auras.push({ combatantId: p.combatantId, kind });
      floats.push(p.kind === 'heal'
        ? { combatantId: p.combatantId, text: `+${p.amount}`, kind: 'heal' }
        : { combatantId: p.combatantId, text: `${periodicIcon(p.tags)}-${p.amount}`, kind: 'periodic' });
      hp[p.combatantId] = p.newHp;
    }
    beats.push({ tick: lastTick, phase: 'periodic', durationMs: timing.periodicMs, auras, floats, hp, feed: takeFeed() });

    // Anyone it dropped falls now, as in a tick: Downed (their bleedout starts next round) or dead.
    const fell: Record<string, StatusView> = {};
    const downFloats: FloatCue[] = [];
    const cleared: Record<string, ActiveConditionDto[]> = {};
    for (const p of periodic.filter(p => p.fell)) {
      if (fell[p.combatantId]) continue;
      const status = toStatus(p.status, true);
      fell[p.combatantId] = { status, bleedoutRoundsLeft: status === 'downed' ? finalBleedout.get(p.combatantId) ?? 0 : 0 };
      if (status === 'downed') downFloats.push({ combatantId: p.combatantId, text: 'DOWN', kind: 'damage' });
      if (tracker.clear(p.combatantId)) cleared[p.combatantId] = [];
    }
    if (Object.keys(fell).length > 0) {
      const beat: PlaybackBeat = { tick: lastTick, phase: 'defeats', durationMs: timing.defeatMs, statuses: fell, floats: downFloats };
      if (Object.keys(cleared).length > 0) beat.conditions = cleared;
      beats.push(beat);
    }
  }

  // End of round: bleedout countdowns tick, and anyone at 0 dies (3B.5).
  const bleeding = payload.endOfRound ?? [];
  if (bleeding.length > 0) {
    endOfRound();
    pendingFeed.push(...bleeding.map(b => b.feedText));
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
    endOfRound();
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
