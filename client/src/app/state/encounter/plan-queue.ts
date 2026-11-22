import { AbilityDto, PlannedActionEntry } from '../../contracts/hub-contracts.generated';
import { Position } from '../map/map.state.model';

/**
 * Action-point helpers for planning a queue of actions (roadmap 3B.2). They mirror the server's
 * PlanValidator so the UI only offers plans the server will accept:
 *  - the queue's total AP cost must fit the combatant's maxAp;
 *  - all of a round's moves share the combatant's movementRange cells, each move measured from
 *    where the previous queued move ends;
 *  - an ability (3B.3) costs its own AP, can't be queued while on cooldown, and one with a
 *    cooldown can be queued at most once per round;
 *  - an ability that moves (Dash, Charge) moves the planner too: a move to a cell within its
 *    maxCells of where the earlier moves end, a move to a target estimated toward it; cells of a
 *    move that uses the movement budget count toward it.
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
 * Whether the ability is blocked by its cooldown: still cooling down (`left` rounds), or already
 * queued this round (an ability with a cooldown is once per round). Null if neither.
 */
export function cooldownBlock(
  ability: AbilityDto, queue: readonly PlannedActionEntry[], cooldowns: Readonly<Record<string, number>>,
): { reason: 'cooldown'; left: number } | { reason: 'once_per_round' } | null {
  const left = cooldowns[ability.id] ?? 0;
  if (left > 0) return { reason: 'cooldown', left };
  if (ability.cooldown > 0 && queue.some(a => a.type === 'ability' && a.abilityId === ability.id)) return { reason: 'once_per_round' };
  return null;
}

/**
 * Why the ability can't be added to the queue right now, or null if it can (3B.3; mirrors the
 * server's PlanValidator): on cooldown, already queued (abilities with a cooldown are once per
 * round), or not enough AP left. Short text for the ability bar.
 */
export function abilityUnavailableReason(
  ability: AbilityDto, queue: readonly PlannedActionEntry[], maxAp: number,
  costs: Readonly<Record<string, number>>, abilities: readonly AbilityDto[], cooldowns: Readonly<Record<string, number>>,
): string | null {
  const block = cooldownBlock(ability, queue, cooldowns);
  if (block?.reason === 'cooldown') return `On cooldown for ${block.left} more ${block.left === 1 ? 'round' : 'rounds'}`;
  if (block?.reason === 'once_per_round') return 'Already queued this round';
  if (apSpent(queue, costs, abilities) + ability.apCost > maxAp) return 'Not enough AP left';
  return null;
}

/** The ability is aimed at a cell the player picks: it moves its user there (Dash). */
export function abilityTakesCell(ability: AbilityDto): boolean {
  return ability.move?.to === 'cell';
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
  // A missing cell (Dash) isn't a target-shape error: like the server, validatePlan reports it with the move, after AP.
  if (action.targetCell && !abilityTakesCell(ability)) return `${ability.name} doesn't take a target cell.`;
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

/** Melee reach (the server's RulesConfig.MeleeReach): how close a move to a target gets. */
export const MELEE_REACH = 1;

/** The shortest king-move path from `from` to `to` (diagonal steps first), origin included; mirrors the server's. */
export function chebyshevPath(from: Position, to: Position): Position[] {
  const path = [from];
  let { x, y } = from;
  while (x !== to.x || y !== to.y) {
    x += Math.sign(to.x - x);
    y += Math.sign(to.y - y);
    path.push({ x, y });
  }
  return path;
}

/**
 * Where a move to a target (Charge) is expected to end, and the cells it covers: toward the
 * target's current position until within melee reach, at most `cap` cells (mirrors the server's
 * PlanValidator estimate; the real cell is picked when the round resolves).
 */
export function estimateMoveToTarget(from: Position, target: Position, cap: number): { end: Position; cells: number } {
  const cells = Math.max(0, Math.min(cap, chebyshev(from, target) - MELEE_REACH));
  const path = chebyshevPath(from, target);
  return { end: path[Math.min(cells, path.length - 1)], cells };
}

/**
 * Walks a queue the way the server's PlanValidator does: where each move (plain or by an ability)
 * leaves the planner, and how many of the round's movement cells they use. Ability moves off the
 * budget (Dash) move the planner without using cells; a move to a target is estimated from the
 * target's current position (`positions`), capped at its maxCells and the budget left.
 */
export function walkQueue(
  start: Position | undefined, queue: readonly PlannedActionEntry[], abilities: readonly AbilityDto[] = [],
  movementRange = Infinity, positions: Readonly<Record<string, Position>> = {},
): { position: Position | undefined; cells: number } {
  let pos = start;
  let cells = 0;
  for (const a of queue) {
    if (a.type === 'move' && a.targetCell) {
      if (pos) cells += chebyshev(pos, a.targetCell);
      pos = a.targetCell;
      continue;
    }
    const move = a.type === 'ability' ? abilities.find(x => x.id === a.abilityId)?.move : undefined;
    if (!move || !pos) continue;
    if (move.to === 'cell') {
      if (!a.targetCell) continue;
      if (move.usesMovementBudget) cells += chebyshev(pos, a.targetCell);
      pos = a.targetCell;
    } else {
      const target = a.targetCombatantId ? positions[a.targetCombatantId] : undefined;
      if (!target) continue;
      const cap = move.usesMovementBudget ? Math.min(move.maxCells, Math.max(0, movementRange - cells)) : move.maxCells;
      const estimate = estimateMoveToTarget(pos, target, cap);
      if (move.usesMovementBudget) cells += estimate.cells;
      pos = estimate.end;
    }
  }
  return { position: pos, cells };
}

/** Where the combatant would stand after its queued moves (ignoring anyone who might block it). */
export function projectedPosition(
  start: Position | undefined, queue: readonly PlannedActionEntry[], abilities: readonly AbilityDto[] = [],
  movementRange = Infinity, positions: Readonly<Record<string, Position>> = {},
): Position | undefined {
  return walkQueue(start, queue, abilities, movementRange, positions).position;
}

/** Cells of the round's movement budget the queued moves cover, each measured from where the previous one ends. */
export function moveCellsUsed(
  start: Position | undefined, queue: readonly PlannedActionEntry[], abilities: readonly AbilityDto[] = [],
  movementRange = Infinity, positions: Readonly<Record<string, Position>> = {},
): number {
  return walkQueue(start, queue, abilities, movementRange, positions).cells;
}

/**
 * Why a move-to-cell ability (Dash) can't go to `cell` from where the queue's moves end, or null
 * (mirrors the server's PlanValidator): on the grid, within its maxCells, and within the budget
 * left if it uses the movement budget.
 */
export function abilityCellError(
  ability: AbilityDto, from: Position, cell: Position, cellsUsed: number, movementRange: number,
  gridWidth: number, gridHeight: number,
): string | null {
  const move = ability.move;
  if (!move || move.to !== 'cell') return `${ability.name} doesn't take a target cell.`;
  if (!onGrid(cell, gridWidth, gridHeight)) return outOfBoundsError(gridWidth, gridHeight);
  const steps = chebyshev(from, cell);
  if (steps > move.maxCells) return `${ability.name} moves at most ${move.maxCells} ${move.maxCells === 1 ? 'square' : 'squares'}.`;
  if (move.usesMovementBudget && cellsUsed + steps > movementRange)
    return `Moves this round exceed maximum range (${movementRange} squares).`;
  return null;
}

/** What {@link validatePlan} needs to know about the planner and the board. */
export interface PlanFacts {
  maxAp: number;
  movementRange: number;
  abilities: readonly AbilityDto[];
  /** Abilities on cooldown at the start of planning: id → rounds left. */
  cooldowns: Readonly<Record<string, number>>;
  /** Where the planner stands at the start of planning; undefined if unknown. */
  start: Position | undefined;
  /** Everyone's position at the start of planning, to estimate moves to a target (Charge). */
  positions?: Readonly<Record<string, Position>>;
  gridWidth: number;
  gridHeight: number;
  /** AP per basic action type; defaults to {@link DEFAULT_AP_COSTS}. */
  costs?: Readonly<Record<string, number>>;
}

/** Result of {@link validatePlan}: the server's PlanCheck. */
export interface PlanCheck {
  /** Null when the queue is legal; otherwise the first rule it breaks, in the server's words. */
  error: string | null;
  /** AP the queue costs (up to the failing entry when invalid). */
  ap: number;
  /** Movement cells the queue's moves use (up to the failing entry when invalid). */
  cells: number;
}

/**
 * The whole-queue check the server's PlanValidator makes, entry by entry in the same order (ability
 * loadout, cooldown, once per round and target shape → AP budget → moves), built from the helpers
 * above. Run against the cases in contracts/plan-validation-cases.json together with the server, so
 * the two can't drift apart unnoticed.
 */
export function validatePlan(queue: readonly PlannedActionEntry[], facts: PlanFacts): PlanCheck {
  const costs = facts.costs ?? DEFAULT_AP_COSTS;
  const positions = facts.positions ?? {};
  const { gridWidth: w, gridHeight: h, movementRange } = facts;
  let ap = 0;
  for (let i = 0; i < queue.length; i++) {
    const action = queue[i];
    const before = queue.slice(0, i);
    const walked = walkQueue(facts.start, before, facts.abilities, movementRange, positions);
    const fail = (error: string): PlanCheck => ({ error, ap, cells: walked.cells });

    const ability = action.type === 'ability' ? facts.abilities.find(a => a.id === action.abilityId) : undefined;
    if (action.type === 'ability') {
      if (!ability) return fail(`This combatant doesn't have the ability '${action.abilityId}'.`);
      const block = cooldownBlock(ability, before, facts.cooldowns);
      if (block?.reason === 'cooldown')
        return fail(`${ability.name} is on cooldown for ${block.left} more ${block.left === 1 ? 'round' : 'rounds'}.`);
      if (block?.reason === 'once_per_round') return fail(`${ability.name} can only be used once per round.`);
      const shape = targetShapeError(ability, action);
      if (shape) return fail(shape);
    }

    ap += entryCost(action, costs, facts.abilities);
    if (ap > facts.maxAp)
      return fail(`Not enough AP: this plan costs ${apSpent(queue, costs, facts.abilities)} AP but only ${facts.maxAp} AP are available.`);

    if (ability?.move) {
      if (!walked.position) return fail('Current position not found.');
      if (ability.move.to === 'cell') {
        if (!action.targetCell) return fail(`${ability.name} needs a target cell.`);
        const cellError = abilityCellError(ability, walked.position, action.targetCell, walked.cells, movementRange, w, h);
        if (cellError) return fail(cellError);
      }
      continue;
    }

    if (action.type !== 'move') continue;
    if (!action.targetCell) return fail('Move action requires a target cell.');
    if (!onGrid(action.targetCell, w, h)) return fail(outOfBoundsError(w, h));
    if (!walked.position) return fail('Current position not found.');
    if (walked.cells + chebyshev(walked.position, action.targetCell) > movementRange)
      return fail(`Moves this round exceed maximum range (${movementRange} squares).`);
  }
  return { error: null, ap, cells: walkQueue(facts.start, queue, facts.abilities, movementRange, positions).cells };
}

function onGrid(cell: Position, gridWidth: number, gridHeight: number): boolean {
  return cell.x >= 0 && cell.y >= 0 && cell.x < gridWidth && cell.y < gridHeight;
}

/** The server's message for a cell off the grid. */
function outOfBoundsError(gridWidth: number, gridHeight: number): string {
  return `Target cell is out of bounds (0–${gridWidth - 1}, 0–${gridHeight - 1}).`;
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
    if (action.targetCell) return `${name} to (${action.targetCell.x}, ${action.targetCell.y})`;
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
