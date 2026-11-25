import {
  AttackResult, ConditionResult, HealResult, MovementResult, ReactionResult, ResolutionPayload,
} from '../../../contracts/hub-contracts.generated';
import { wastedLabel } from '../../../core/services/playback-timeline';
import { describeDuration } from '../condition-chips/condition-format';

/**
 * The round as lanes: one row per combatant, one column per tick. Column i holds every
 * combatant's i-th queued action. They all resolve together in tick i, whatever their AP cost.
 * Pure, so it's easy to test; RoundTimelineComponent renders it.
 */

export type SlotKind = 'move' | 'disengage' | 'melee_attack' | 'ranged_attack' | 'defend' | 'stabilize' | 'ability' | 'idle' | 'lost';

/** What happened. 'none' = nothing to report (idle, lost). */
export type SlotOutcome =
  'moved' | 'stayed' | 'blocked' | 'hit' | 'miss' | 'wasted' | 'defended' | 'stabilized' | 'healed' | 'stopped' | 'watching' | 'none';

export interface LaneSlot {
  tick: number;
  kind: SlotKind;
  /** 0 for idle / lost slots. */
  apCost: number;
  icon: string;
  outcome: SlotOutcome;
  /** What was planned, safe to show before it plays ("Melee attack on Goblin (2 AP)"). */
  label: string;
  /** What happened, with the roll for attacks. */
  detail: string;
  /** This combatant's move drew a reaction this tick (an opportunity attack, 3B.6): shown as ⚔ on the cell. */
  provoked: boolean;
  /**
   * A prepared reaction is watching in this tick (3B.6): 'start' on the tick it was queued for (👁), 'on' for the later
   * ticks it keeps watching (a faint bar to the end of the round, or until it fires).
   */
  watching: 'start' | 'on' | null;
  /** The combatant's prepared reaction fired this tick (3B.6): shown as ⚡ on the cell. */
  reacted: boolean;
}

export interface Lane {
  combatantId: string;
  name: string;
  apSpent: number;
  maxAp: number;
  slots: LaneSlot[];
}

const ICONS: Record<SlotKind, string> = {
  move: '➜',
  disengage: '🏃',
  melee_attack: '⚔',
  ranged_attack: '🏹',
  defend: '🛡',
  stabilize: '✚',
  ability: '✦',
  idle: '·',
  lost: '✕',
};

const ACTION_NAMES: Record<string, string> = {
  move: 'Move',
  disengage: 'Disengage',
  melee_attack: 'Melee attack',
  ranged_attack: 'Ranged attack',
  defend: 'Defend',
  stabilize: 'Stabilize',
};

/**
 * @param order combatant ids in display order (the tracker's); queues for other ids go last.
 */
export function buildLanes(payload: ResolutionPayload, order: string[] = []): Lane[] {
  // Everyone's name, including combatants with no queue (e.g. a downed ally being stabilized).
  const names = new Map<string, string>();
  for (const c of payload.updatedCombatants ?? []) names.set(c.id, c.characterName);
  for (const q of payload.queues) names.set(q.combatantId, q.combatantName);
  for (const tick of payload.ticks) {
    for (const m of tick.movements) names.set(m.combatantId, m.combatantName);
    for (const a of tick.attacks) {
      names.set(a.attackerId, a.attackerName);
      names.set(a.targetId, a.targetName);
    }
    for (const h of tick.heals ?? []) {
      names.set(h.healerId, h.healerName);
      names.set(h.targetId, h.targetName);
    }
  }
  const nameOf = (id: string | null) => (id && names.get(id)) || 'someone';
  // Ability names (3B.3), from the activations the server reported.
  const abilityNames = new Map<string, string>();
  for (const c of payload.updatedCombatants ?? [])
    for (const a of c.abilities ?? []) abilityNames.set(a.id, a.name);
  for (const tick of payload.ticks) {
    for (const u of tick.abilityUses ?? []) abilityNames.set(u.abilityId, u.abilityName);
    for (const w of tick.watching ?? []) abilityNames.set(w.abilityId, w.abilityName);
  }

  // The tick each combatant fell in (went down or died): its later actions never happen. Reactions (3B.6) can drop
  // someone mid-move, before the tick's attacks.
  const defeatedIn = new Map<string, number>();
  payload.ticks.forEach((tick, i) => {
    const reactionAttacks = (tick.reactions ?? []).flatMap(r => r.attack ? [r.attack] : []);
    for (const a of [...reactionAttacks, ...tick.attacks])
      if (a.targetDefeated && !defeatedIn.has(a.targetId)) defeatedIn.set(a.targetId, i);
  });

  const rank = (id: string) => {
    const i = order.indexOf(id);
    return i === -1 ? order.length : i;
  };
  const queues = [...payload.queues].sort((a, b) => rank(a.combatantId) - rank(b.combatantId));

  return queues.map(q => {
    const name = q.combatantName;
    const downAt = defeatedIn.get(q.combatantId);
    const slots: LaneSlot[] = withWatching(payload, q.combatantId,
      payload.ticks.map((tick, i) => withReactions(tick.reactions ?? [], q.combatantId, buildSlot(tick, i))));
    function buildSlot(tick: ResolutionPayload['ticks'][number], i: number): LaneSlot {
      const action = q.actions[i];
      const isDown = downAt !== undefined && i > downAt;

      if (!action) {
        return isDown
          ? slot(i, 'lost', 0, 'none', 'Defeated', `${name} is down.`)
          : slot(i, 'idle', 0, 'none', 'Nothing queued', `${name} has no action left (queue used up).`);
      }

      const targetIds = action.targetCombatantIds?.length ? action.targetCombatantIds
        : action.targetCombatantId ? [action.targetCombatantId] : [];
      const what = actionLabel(action.type, targetIds.length ? targetIds.map(nameOf).join(', ') : null, action.targetCell,
        action.abilityId ? abilityNames.get(action.abilityId) ?? action.abilityId : null);
      const label = `${what} (${action.apCost} AP)`;
      const kind = action.type as SlotKind;
      const lost = () => slot(i, 'lost', action.apCost, 'none', label,
        `${name} went down, so this ${lowerFirst(what)} never happens.`);
      if (isDown) return lost();

      // A condition stopped it (3B.4 PR 3): Stunned, or a Rooted move.
      const stopped = (tick.blocked ?? []).find(b => b.combatantId === q.combatantId);
      if (stopped) return slot(i, kind, action.apCost, 'stopped', label, `${stopped.feedText}: this ${lowerFirst(what)} never happens.`);

      if (kind === 'move' || kind === 'disengage') {
        const m = tick.movements.find(r => r.combatantId === q.combatantId);
        if (!m) return lost();
        const cells = m.path.length - 1;
        const { x, y } = m.finalPosition;
        if (m.stopReason === 'fell')
          return slot(i, kind, action.apCost, 'stopped', label, `${name} falls at (${x}, ${y}) after ${cells} ${cells === 1 ? 'cell' : 'cells'}.`);
        if (m.wasBlocked) {
          const by = m.blockedByCombatantId ? ` by ${nameOf(m.blockedByCombatantId)}` : '';
          return slot(i, kind, action.apCost, 'blocked', label,
            `${name} is blocked${by} after ${cells} ${cells === 1 ? 'cell' : 'cells'}, stopping at (${x}, ${y}).`);
        }
        if (cells === 0) return slot(i, kind, action.apCost, 'stayed', label, `${name} stays put at (${x}, ${y}).`);
        return slot(i, kind, action.apCost, 'moved', label, `${name} moves ${cells} ${cells === 1 ? 'cell' : 'cells'} to (${x}, ${y}).`);
      }

      if (kind === 'melee_attack' || kind === 'ranged_attack') {
        const a = tick.attacks.find(r => r.attackerId === q.combatantId);
        if (!a) return lost();
        if (a.wasted) {
          return slot(i, kind, action.apCost, 'wasted', label,
            `${name}'s ${lowerFirst(what)} is wasted (${wastedLabel(a.wastedReason)}).`);
        }
        return slot(i, kind, action.apCost, a.hit ? 'hit' : 'miss', label, attackDetail(name, a));
      }

      if (kind === 'ability') {
        // A prepared reaction (3B.6) starts watching instead of resolving; withWatching adds whether it fired.
        const watch = (tick.watching ?? []).find(w => w.combatantId === q.combatantId);
        if (watch) return { ...slot(i, kind, action.apCost, 'watching', label,
          `${name} prepares ${watch.abilityName}: watching until it fires or the round ends.`), icon: '👁' };
        const use = (tick.abilityUses ?? []).find(u => u.combatantId === q.combatantId);
        if (!use) return lost();
        if (use.wastedReason === 'no_targets') return slot(i, kind, action.apCost, 'wasted', label,
          `${name} uses ${use.abilityName}, but there's nobody in reach.`);
        if (use.wastedReason) return slot(i, kind, action.apCost, 'wasted', label,
          `${name} can't use ${use.abilityName} (${wastedLabel(use.wastedReason)}).`);
        // An ability that moves its user (Dash, Charge, 3B.3) says where it went first.
        const move = tick.movements.find(r => r.combatantId === q.combatantId && r.abilityId === use.abilityId);
        const moved = move ? abilityMoveDetail(name, move, nameOf) + ' ' : '';
        const heals = (tick.heals ?? []).filter(h => h.healerId === q.combatantId && h.abilityId === use.abilityId);
        if (heals.length > 0) {
          const outcome = heals.every(h => h.wastedReason) ? 'wasted' : 'healed';
          return slot(i, kind, action.apCost, outcome, label, `${use.abilityName}: ` + heals.map(h => healDetail(name, h)).join(' '));
        }
        const hits = tick.attacks.filter(a => a.attackerId === q.combatantId && a.abilityId === use.abilityId);
        // Conditions it applied (3B.4), after its attacks.
        const conditions = (tick.conditions ?? []).filter(c => c.sourceId === q.combatantId && c.abilityId === use.abilityId);
        const conditionText = conditions.length > 0 ? ' ' + conditions.map(conditionDetail).join(' ') : '';
        if (hits.length === 0 && conditions.length > 0) {
          const outcome = conditions.some(c => c.result === 'applied' || c.result === 'refreshed') ? 'hit' : 'wasted';
          return slot(i, kind, action.apCost, outcome, label, `${use.abilityName}: ${moved}${conditionText.trim()}`);
        }
        if (hits.length === 0 && move) return slot(i, kind, action.apCost, move.wasBlocked ? 'blocked' : 'moved', label,
          `${use.abilityName}: ${moved.trim()}`);
        if (hits.length === 0) return slot(i, kind, action.apCost, 'none', label, `${name} uses ${use.abilityName}.`);
        if (hits.every(a => a.wasted)) return slot(i, kind, action.apCost, 'wasted', label,
          `${moved}${name}'s ${use.abilityName} is wasted (${wastedLabel(hits[0].wastedReason)}).`);
        const outcome = hits.some(a => !a.wasted && a.hit) ? 'hit' : 'miss';
        return slot(i, kind, action.apCost, outcome, label,
          `${use.abilityName}: ${moved}` + hits.map(a => a.wasted
            ? `${a.targetName}: wasted (${wastedLabel(a.wastedReason)}).`
            : attackDetail(name, a)).join(' ') + conditionText);
      }

      if (kind === 'stabilize') {
        const e = tick.effects.find(r => r.combatantId === q.combatantId && r.effectType === 'stabilize');
        if (!e) return lost();
        if (e.wastedReason) return slot(i, kind, action.apCost, 'wasted', label,
          `${name}'s stabilize on ${nameOf(e.targetCombatantId)} is wasted (${wastedLabel(e.wastedReason)}).`);
        return slot(i, kind, action.apCost, 'stabilized', label,
          `${name} stabilizes ${nameOf(e.targetCombatantId)}: no longer bleeding out.`);
      }

      // defend
      const defended = tick.effects.some(e => e.combatantId === q.combatantId && e.effectType === 'defend');
      if (!defended) return lost();
      // Defend grants Guarded (3B.4) from the start of its tick.
      const guard = (tick.conditions ?? []).find(c => c.targetId === q.combatantId && c.abilityId === null);
      return slot(i, kind, action.apCost, 'defended', label, guard
        ? `${name} defends: ${guard.conditionName} (+3 Dodge) until the end of the round.`
        : `${name} defends.`);
    }

    return {
      combatantId: q.combatantId,
      name,
      apSpent: q.actions.reduce((sum, a) => sum + a.apCost, 0),
      maxAp: q.maxAp,
      slots,
    };
  });
}

/** "Melee attack on Goblin" → "melee attack on Goblin" (names keep their capitals). */
function lowerFirst(text: string): string {
  return text.charAt(0).toLowerCase() + text.slice(1);
}

function slot(tick: number, kind: SlotKind, apCost: number, outcome: SlotOutcome, label: string, detail: string): LaneSlot {
  return { tick, kind, apCost, icon: ICONS[kind], outcome, label, detail, provoked: false, watching: null, reacted: false };
}

/**
 * Follows the combatant's prepared reaction (3B.6) from the tick it started watching: every tick until it fires (⚡,
 * with the reaction's roll in the detail) or the round ends (and it goes unused) is marked as watching.
 */
function withWatching(payload: ResolutionPayload, combatantId: string, slots: LaneSlot[]): LaneSlot[] {
  const start = payload.ticks.findIndex(t => (t.watching ?? []).some(w => w.combatantId === combatantId));
  if (start < 0) return slots;
  const watch = payload.ticks[start].watching!.find(w => w.combatantId === combatantId)!;
  const unused = (payload.unusedReactions ?? []).some(u => u.combatantId === combatantId);
  let end = slots.length - 1;
  return slots.map((s, i) => {
    if (i < start || i > end || s.kind === 'lost') return s;
    const fired = (payload.ticks[i].reactions ?? []).filter(r => r.reactorId === combatantId && r.kind === 'prepared');
    const next: LaneSlot = { ...s, watching: i === start ? 'start' : 'on' };
    if (fired.length > 0) {
      end = i;
      next.reacted = true;
      next.detail = `${s.detail} ⚡ ` + fired.map(r => r.attack
        ? `${watch.abilityName} (step ${r.step}): ${attackDetail(r.reactorName, r.attack)}`
        : `${watch.abilityName} fires (step ${r.step}).`).join(' ');
    } else if (i > start && s.kind === 'idle') {
      next.detail = `${s.detail.replace(/ \(queue used up\)\.$/, '.')} 👁 Still watching with ${watch.abilityName}.`;
    }
    if (i === slots.length - 1 && unused) next.detail = `${next.detail} ${watch.abilityName} goes unused.`;
    return next;
  });
}

/** Marks a slot whose combatant drew reactions this tick (3B.6) and adds their lines, with the rolls, to its detail. */
function withReactions(reactions: ReactionResult[], combatantId: string, s: LaneSlot): LaneSlot {
  const mine = reactions.filter(r => r.triggerId === combatantId);
  if (mine.length === 0) return s;
  const lines = mine.map(r => {
    const what = r.kind === 'opportunity' ? 'Opportunity attack' : r.abilityName ?? 'Reaction';
    return r.attack ? `${what} (step ${r.step}): ${attackDetail(r.reactorName, r.attack)}` : `${what} (step ${r.step}) by ${r.reactorName}.`;
  });
  return { ...s, provoked: true, detail: `${s.detail} ⚔ ${lines.join(' ')}` };
}

function actionLabel(
  type: string, targetName: string | null, cell: { x: number; y: number } | null, abilityName: string | null = null,
): string {
  const action = type === 'ability' ? abilityName ?? 'Ability' : ACTION_NAMES[type] ?? type;
  if ((type === 'move' || type === 'disengage' || type === 'ability') && cell) return `${action} to (${cell.x}, ${cell.y})`;
  return targetName ? `${action} on ${targetName}` : action;
}

/** "Mira → Brakka: healed 6 (Brakka now 6 HP, back on their feet)." (3B.3) */
export function healDetail(name: string, h: HealResult): string {
  if (h.wastedReason) return `${name} → ${h.targetName}: wasted (${wastedLabel(h.wastedReason)}).`;
  if (h.targetStatus === 'dead') return `${name} → ${h.targetName}: healed ${h.amount}, but too late.`;
  const up = h.revived ? ', back on their feet' : '';
  return `${name} → ${h.targetName}: healed ${h.amount} (${h.targetName} now ${h.targetNewHp} HP${up}).`;
}

/** "Kestrel runs 3 cells to (5, 6)." / "… is blocked by Goblin after 1 cell, at (5, 7)." for an ability's move (3B.3). */
function abilityMoveDetail(name: string, m: MovementResult, nameOf: (id: string) => string): string {
  const cells = m.path.length - 1;
  const { x, y } = m.finalPosition;
  const n = `${cells} ${cells === 1 ? 'cell' : 'cells'}`;
  if (m.wasBlocked) {
    const by = m.blockedByCombatantId ? ` by ${nameOf(m.blockedByCombatantId)}` : '';
    return `${name} is blocked${by} after ${n}, at (${x}, ${y}).`;
  }
  return cells === 0 ? `${name} doesn't need to move.` : `${name} moves ${n} to (${x}, ${y}).`;
}

/** "Goblin 1 is Marked until end of next round." / "Goblin 1 resists Burning." / "Dazzled: missed." (3B.4) */
export function conditionDetail(c: ConditionResult): string {
  switch (c.result) {
    case 'applied': return `${c.targetName} is ${c.conditionName} ${describeDuration(c.roundsLeft)}.`;
    case 'refreshed': return `${c.targetName} is still ${c.conditionName}, now ${describeDuration(c.roundsLeft)}.`;
    case 'resisted': return `${c.targetName} resists ${c.conditionName}.`;
    default: return `${c.conditionName} on ${c.targetName}: ${wastedLabel(c.wastedReason)}.`;
  }
}

const fromConditions = (n: number) => n === 0 ? '' : ` (${n > 0 ? '+' : ''}${n} from conditions)`;

/**
 * "Zara Forge → Goblin 1: rolled 13 + 3 = 16 vs dodge 12, hit for 5 (Goblin 1 now 12 HP)." Parts that
 * came from conditions are called out (3B.4): "rolled 8 + 2 (+2 from conditions) = 10 vs dodge 13 (+3 from conditions)".
 */
export function attackDetail(name: string, a: AttackResult): string {
  const r = a.roll;
  const roll = r
    ? `rolled ${r.d20} + ${r.accuracy}${fromConditions(r.conditionAccuracy ?? 0)} = ${r.d20 + r.accuracy} vs dodge ${r.targetDodge}${fromConditions(r.conditionDodge ?? 0)}, `
    : '';
  if (!a.hit) return `${name} → ${a.targetName}: ${roll}miss.`;
  if (a.finishingBlow) return `${name} → ${a.targetName}: ${roll}hit, finishing them off.`;
  const after = !a.targetDefeated ? `${a.targetName} now ${Math.max(0, a.targetNewHp)} HP`
    : a.targetStatus === 'downed' ? `${a.targetName} goes down` : `${a.targetName} is defeated`;
  const condition = a.conditionDamage ?? 0;
  const bonus = (a.moveBonus > 0 ? `, incl. +${a.moveBonus} from moving` : '')
    + (condition !== 0 ? `, incl. ${condition > 0 ? '+' : ''}${condition} from conditions` : '');
  return `${name} → ${a.targetName}: ${roll}hit for ${a.damage}${bonus} (${after}).`;
}
