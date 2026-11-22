import { AbilityDto, PlannedActionEntry } from '../../contracts/hub-contracts.generated';
import { Position } from '../map/map.state.model';

/**
 * Action-point helpers for planning a queue of actions (roadmap 3B.2). They mirror the server's
 * PlanValidator so the UI only offers plans the server will accept:
 *  - the queue's total AP cost must fit the combatant's maxAp;
 *  - all of a round's moves share the combatant's movementRange cells, each move measured from
 *    where the previous queued move ends;
 *  - an ability (3B.3) costs its own AP, can't be queued while on cooldown, and one with a
 *    cooldown can be queued at most once per round.
 */

/** A queue entry with every optional field empty, so callers only give what their action uses. */
export function entry(type: string, fields: Partial<Omit<PlannedActionEntry, 'type'>> = {}): PlannedActionEntry {
  return { type, targetCell: null, targetCombatantId: null, abilityId: null, targetCombatantIds: null, ...fields };
}

/** AP per action type, used until the server's PlanningPhaseStarted delivers its own table. */
export const DEFAULT_AP_COSTS: Readonly<Record<string, number>> = {
  move: 1,
  melee_attack: 2,
  ranged_attack: 2,
  defend: 1,
  stabilize: 2,
};

/** AP budget for a combatant whose server DTO doesn't carry one. */
export const DEFAULT_MAX_AP = 4;

export function apCost(type: string, costs: Readonly<Record<string, number>>): number {
  return costs[type] ?? DEFAULT_AP_COSTS[type] ?? 0;
}

/** AP one queued entry costs: basic actions from the cost table, abilities from their definition. */
export function entryCost(
  action: PlannedActionEntry, costs: Readonly<Record<string, number>>, abilities: readonly AbilityDto[] = [],
): number {
  if (action.type === 'ability') return abilities.find(a => a.id === action.abilityId)?.apCost ?? 0;
  return apCost(action.type, costs);
}

export function apSpent(
  queue: readonly PlannedActionEntry[], costs: Readonly<Record<string, number>>, abilities: readonly AbilityDto[] = [],
): number {
  return queue.reduce((sum, a) => sum + entryCost(a, costs, abilities), 0);
}

/** Whether appending a basic action of this type keeps the queue within the AP budget. */
export function canAfford(
  queue: readonly PlannedActionEntry[], type: string, maxAp: number, costs: Readonly<Record<string, number>>,
  abilities: readonly AbilityDto[] = [],
): boolean {
  return apSpent(queue, costs, abilities) + apCost(type, costs) <= maxAp;
}

/** Whether appending this entry keeps the queue within the AP budget. */
export function canAffordEntry(
  queue: readonly PlannedActionEntry[], action: PlannedActionEntry, maxAp: number,
  costs: Readonly<Record<string, number>>, abilities: readonly AbilityDto[] = [],
): boolean {
  return apSpent(queue, costs, abilities) + entryCost(action, costs, abilities) <= maxAp;
}

/**
 * Why the ability can't be added to the queue right now, or null if it can (3B.3; mirrors the
 * server's PlanValidator): on cooldown, already queued (abilities with a cooldown are once per
 * round), or not enough AP left.
 */
export function abilityUnavailableReason(
  ability: AbilityDto, queue: readonly PlannedActionEntry[], maxAp: number,
  costs: Readonly<Record<string, number>>, abilities: readonly AbilityDto[], cooldowns: Readonly<Record<string, number>>,
): string | null {
  const left = cooldowns[ability.id] ?? 0;
  if (left > 0) return `On cooldown for ${left} more ${left === 1 ? 'round' : 'rounds'}`;
  if (ability.cooldown > 0 && queue.some(a => a.type === 'ability' && a.abilityId === ability.id))
    return 'Already queued this round';
  if (apSpent(queue, costs, abilities) + ability.apCost > maxAp) return 'Not enough AP left';
  return null;
}

/** The ability's targets are picked by the player ("combatant", "combatants"), not found when it resolves. */
export function abilityTakesTargets(ability: AbilityDto): boolean {
  return ability.targeting.kind === 'combatant' || ability.targeting.kind === 'combatants';
}

/** Most targets the player picks for the ability: 1, or its count for "combatants"; 0 if none are picked. */
export function maxTargets(ability: AbilityDto): number {
  if (ability.targeting.kind === 'combatants') return ability.targeting.count ?? 1;
  return ability.targeting.kind === 'combatant' ? 1 : 0;
}

/**
 * Why an ability entry has the wrong target shape for its targeting kind, or null (3B.3; mirrors
 * the server's PlanValidator): one target for "combatant", 1..count distinct ones for
 * "combatants", none for "auto" and "self".
 */
export function targetShapeError(ability: AbilityDto, action: PlannedActionEntry): string | null {
  const hasOne = !!action.targetCombatantId;
  const listed = action.targetCombatantIds?.length ?? 0;
  const count = ability.targeting.count ?? 1;
  switch (ability.targeting.kind) {
    case 'combatant':
      return !hasOne || listed > 0 ? `${ability.name} needs one target.` : null;
    case 'self':
    case 'auto':
      return hasOne || listed > 0 ? `${ability.name} doesn't take a target.` : null;
    case 'combatants':
      if (hasOne || listed === 0) return `${ability.name} needs between 1 and ${count} targets.`;
      if (listed > count) return `${ability.name} can target at most ${count}.`;
      if (new Set(action.targetCombatantIds).size !== listed) return `${ability.name} can't target the same combatant twice.`;
      return null;
    default:
      return `${ability.name} targets a cell, which isn't supported yet.`;
  }
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

/** Text for one queued action, e.g. "Move to (3, 4)", "Melee Attack on Goblin" or "Power Strike on Goblin". */
export function describeAction(
  action: PlannedActionEntry, nameOf: (id: string) => string | undefined,
  abilityNameOf: (id: string) => string | undefined = () => undefined,
): string {
  if (action.type === 'move' && action.targetCell)
    return `Move to (${action.targetCell.x}, ${action.targetCell.y})`;
  const target = action.targetCombatantId ? nameOf(action.targetCombatantId) : undefined;
  if (action.type === 'melee_attack') return target ? `Melee Attack on ${target}` : 'Melee Attack';
  if (action.type === 'ranged_attack') return target ? `Ranged Attack on ${target}` : 'Ranged Attack';
  if (action.type === 'defend') return 'Defend';
  if (action.type === 'stabilize') return target ? `Stabilize ${target}` : 'Stabilize';
  if (action.type === 'ability') {
    const name = (action.abilityId && abilityNameOf(action.abilityId)) || action.abilityId || 'Ability';
    const targets = action.targetCombatantIds?.length
      ? action.targetCombatantIds.map(id => nameOf(id) ?? '?').join(', ')
      : target;
    return targets ? `${name} on ${targets}` : name;
  }
  return action.type;
}

/** A whole queue as "1. Move to (3, 4) → 2. Melee Attack on Goblin" (no number for a single action). */
export function describeQueue(
  queue: readonly PlannedActionEntry[], nameOf: (id: string) => string | undefined,
  abilityNameOf: (id: string) => string | undefined = () => undefined,
): string {
  if (queue.length === 1) return describeAction(queue[0], nameOf, abilityNameOf);
  return queue.map((a, i) => `${i + 1}. ${describeAction(a, nameOf, abilityNameOf)}`).join(' → ');
}

/** The bits of a combatant that decide whether an ability can target it. */
interface TargetFacts {
  id: string;
  combatantType: string;
  status: string;
}

/**
 * Whether the caster may plan this ability on the target (3B.3; mirrors the server's planning
 * check): the target's side fits the ability's filter (sides by combatant type), it isn't dead,
 * and a "downed_ally" ability needs a downed target. Range is checked when the round resolves,
 * since either of them may move first.
 */
export function canTargetWithAbility(caster: TargetFacts, target: TargetFacts, ability: AbilityDto): boolean {
  const isSelf = caster.id === target.id;
  const sameSide = caster.combatantType === target.combatantType;
  const t = ability.targeting;
  const sideOk = t.filter === 'enemy' ? !sameSide
    : t.filter === 'ally' || t.filter === 'downed_ally' ? sameSide && (!isSelf || t.includeSelf)
    : !isSelf || t.includeSelf;
  if (!sideOk || target.status === 'dead') return false;
  return t.filter !== 'downed_ally' || target.status === 'downed';
}
