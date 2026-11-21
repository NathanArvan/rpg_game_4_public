import { AttackResult, ResolutionPayload } from '../../../contracts/hub-contracts.generated';
import { wastedLabel } from '../../../core/services/playback-timeline';

/**
 * The round as lanes: one row per combatant, one column per tick. Column i holds every
 * combatant's i-th queued action. They all resolve together in tick i, whatever their AP cost.
 * Pure, so it's easy to test; RoundTimelineComponent renders it.
 */

export type SlotKind = 'move' | 'melee_attack' | 'ranged_attack' | 'defend' | 'idle' | 'lost';

/** What happened. 'none' = nothing to report (idle, lost). */
export type SlotOutcome = 'moved' | 'stayed' | 'blocked' | 'hit' | 'miss' | 'wasted' | 'defended' | 'none';

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
  melee_attack: '⚔',
  ranged_attack: '🏹',
  defend: '🛡',
  idle: '·',
  lost: '✕',
};

const ACTION_NAMES: Record<string, string> = {
  move: 'Move',
  melee_attack: 'Melee attack',
  ranged_attack: 'Ranged attack',
  defend: 'Defend',
};

/**
 * @param order combatant ids in display order (the tracker's); queues for other ids go last.
 */
export function buildLanes(payload: ResolutionPayload, order: string[] = []): Lane[] {
  const names = new Map<string, string>();
  for (const q of payload.queues) names.set(q.combatantId, q.combatantName);
  for (const tick of payload.ticks) {
    for (const m of tick.movements) names.set(m.combatantId, m.combatantName);
    for (const a of tick.attacks) {
      names.set(a.attackerId, a.attackerName);
      names.set(a.targetId, a.targetName);
    }
  }
  const nameOf = (id: string | null) => (id && names.get(id)) || 'someone';

  // The tick each combatant was defeated in: its later actions never happen.
  const defeatedIn = new Map<string, number>();
  payload.ticks.forEach((tick, i) => {
    for (const a of tick.attacks)
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
    const slots: LaneSlot[] = payload.ticks.map((tick, i) => {
      const action = q.actions[i];
      const isDown = downAt !== undefined && i > downAt;

      if (!action) {
        return isDown
          ? slot(i, 'lost', 0, 'none', 'Defeated', `${name} is down.`)
          : slot(i, 'idle', 0, 'none', 'Nothing queued', `${name} has no action left (queue used up).`);
      }

      const what = actionLabel(action.type, action.targetCombatantId ? nameOf(action.targetCombatantId) : null, action.targetCell);
      const label = `${what} (${action.apCost} AP)`;
      const kind = action.type as SlotKind;
      const lost = () => slot(i, 'lost', action.apCost, 'none', label,
        `${name} was defeated, so this ${lowerFirst(what)} never happens.`);
      if (isDown) return lost();

      if (kind === 'move') {
        const m = tick.movements.find(r => r.combatantId === q.combatantId);
        if (!m) return lost();
        const cells = m.path.length - 1;
        const { x, y } = m.finalPosition;
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

      // defend
      const defended = tick.effects.some(e => e.combatantId === q.combatantId && e.effectType === 'defend');
      if (!defended) return lost();
      return slot(i, kind, action.apCost, 'defended', label, `${name} defends (no effect yet).`);
    });

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
  return { tick, kind, apCost, icon: ICONS[kind], outcome, label, detail };
}

function actionLabel(type: string, targetName: string | null, cell: { x: number; y: number } | null): string {
  const action = ACTION_NAMES[type] ?? type;
  if (type === 'move' && cell) return `${action} to (${cell.x}, ${cell.y})`;
  return targetName ? `${action} on ${targetName}` : action;
}

/** "Zara Forge → Goblin 1: rolled 13 + 3 = 16 vs dodge 12, hit for 5 (Goblin 1 now 12 HP)." */
export function attackDetail(name: string, a: AttackResult): string {
  const roll = a.roll ? `rolled ${a.roll.d20} + ${a.roll.accuracy} = ${a.roll.d20 + a.roll.accuracy} vs dodge ${a.roll.targetDodge}, ` : '';
  if (!a.hit) return `${name} → ${a.targetName}: ${roll}miss.`;
  const after = a.targetDefeated ? `${a.targetName} is defeated` : `${a.targetName} now ${Math.max(0, a.targetNewHp)} HP`;
  return `${name} → ${a.targetName}: ${roll}hit for ${a.damage} (${after}).`;
}
