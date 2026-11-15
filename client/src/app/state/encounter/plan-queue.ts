import { PlannedActionEntry } from '../../contracts/hub-contracts.generated';
import { Position } from '../map/map.state.model';

/**
 * Action-point helpers for planning a queue of actions (roadmap 3B.2). They mirror the server's
 * PlanValidator so the UI only offers plans the server will accept:
 *  - the queue's total AP cost must fit the combatant's maxAp;
 *  - all of a round's moves share the combatant's movementRange cells, each move measured from
 *    where the previous queued move ends.
 */

/** AP per action type, used until the server's PlanningPhaseStarted delivers its own table. */
export const DEFAULT_AP_COSTS: Readonly<Record<string, number>> = {
  move: 1,
  melee_attack: 2,
  ranged_attack: 2,
  defend: 1,
};

/** AP budget for a combatant whose server DTO doesn't carry one. */
export const DEFAULT_MAX_AP = 4;

export function apCost(type: string, costs: Readonly<Record<string, number>>): number {
  return costs[type] ?? DEFAULT_AP_COSTS[type] ?? 0;
}

export function apSpent(queue: readonly PlannedActionEntry[], costs: Readonly<Record<string, number>>): number {
  return queue.reduce((sum, a) => sum + apCost(a.type, costs), 0);
}

/** Whether appending an action of this type keeps the queue within the AP budget. */
export function canAfford(
  queue: readonly PlannedActionEntry[], type: string, maxAp: number, costs: Readonly<Record<string, number>>,
): boolean {
  return apSpent(queue, costs) + apCost(type, costs) <= maxAp;
}

export function chebyshev(a: Position, b: Position): number {
  return Math.max(Math.abs(a.x - b.x), Math.abs(a.y - b.y));
}

/** Where the combatant would stand after its queued moves (ignoring anyone who might block it). */
export function projectedPosition(start: Position | undefined, queue: readonly PlannedActionEntry[]): Position | undefined {
  let pos = start;
  for (const a of queue)
    if (a.type === 'move' && a.targetCell) pos = a.targetCell;
  return pos;
}

/** Cells the queued moves cover, each measured from where the previous one ends. */
export function moveCellsUsed(start: Position | undefined, queue: readonly PlannedActionEntry[]): number {
  let pos = start;
  let cells = 0;
  for (const a of queue) {
    if (a.type !== 'move' || !a.targetCell) continue;
    if (pos) cells += chebyshev(pos, a.targetCell);
    pos = a.targetCell;
  }
  return cells;
}

/** Text for one queued action, e.g. "Move to (3, 4)" or "Melee Attack on Goblin". */
export function describeAction(action: PlannedActionEntry, nameOf: (id: string) => string | undefined): string {
  if (action.type === 'move' && action.targetCell)
    return `Move to (${action.targetCell.x}, ${action.targetCell.y})`;
  const target = action.targetCombatantId ? nameOf(action.targetCombatantId) : undefined;
  if (action.type === 'melee_attack') return target ? `Melee Attack on ${target}` : 'Melee Attack';
  if (action.type === 'ranged_attack') return target ? `Ranged Attack on ${target}` : 'Ranged Attack';
  if (action.type === 'defend') return 'Defend';
  return action.type;
}

/** A whole queue as "1. Move to (3, 4) → 2. Melee Attack on Goblin" (no number for a single action). */
export function describeQueue(queue: readonly PlannedActionEntry[], nameOf: (id: string) => string | undefined): string {
  if (queue.length === 1) return describeAction(queue[0], nameOf);
  return queue.map((a, i) => `${i + 1}. ${describeAction(a, nameOf)}`).join(' → ');
}
