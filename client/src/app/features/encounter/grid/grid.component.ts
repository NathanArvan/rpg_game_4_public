import { CommonModule } from '@angular/common';
import { Component, HostListener, Input, OnInit } from '@angular/core';
import { GmEnemyPlannerComponent } from '../gm-enemy-planner/gm-enemy-planner.component';
import { AbilityBarComponent } from '../ability-bar/ability-bar.component';
import { Store } from '@ngxs/store';
import { Observable, combineLatest, map } from 'rxjs';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { MapState } from '../../../state/map/map.state';
import {
  ArmAbility, DisarmAbility, GmSelectEnemy, GmSetEnemyPlan, SelectMyCombatant, SetMyPlannedAction,
} from '../../../state/encounter/encounter.actions';
import {
  DEFAULT_AP_COSTS, MELEE_REACH, abilityCellError, abilityTakesCell, abilityTakesTargets, apCost, apSpent, canAfford,
  canAffordEntry, canTargetWithAbility, chebyshev, chebyshevPath, describeQueue, entry, estimateMoveToTarget, maxTargets,
  walkQueue,
} from '../../../state/encounter/plan-queue';

import { GameHubService } from '../../../core/services/game-hub.service';
import { Combatant, isControlledBy } from '../../../state/encounter/encounter.model';
import { Position } from '../../../state/map/map.state.model';
import { AbilityDto, ActiveConditionDto, PlannedActionEntry } from '../../../contracts/hub-contracts.generated';
import { ActiveAura, ResolutionPlaybackService } from '../../../core/services/resolution-playback.service';

interface Cell {
  x: number;
  y: number;
}

/** A token on the board, at the position currently shown (animated during playback). */
interface TokenView {
  combatant: Combatant;
  x: number;
  y: number;
}

/** Floating damage / MISS text above a combatant; `stack` lifts it above earlier texts on the same token. */
interface FloatView {
  key: number;
  text: string;
  kind: string;
  x: number;
  y: number;
  stack: number;
}

/** Cell size + the 1px grid gap (see .cell and .grid in the stylesheet). */
const CELL_PITCH_PX = 31;
/** Inset that centres a 24px token in a 30px cell. */
const TOKEN_INSET_PX = 3;
/** Fade time for a token being defeated (matches the defeat beat). */
const FADE_MS = 500;

import { ConditionChipsComponent } from '../condition-chips/condition-chips.component';
import { describeDuration } from '../condition-chips/condition-format';
@Component({
  selector: 'app-grid',
  standalone: true,
  imports: [CommonModule, GmEnemyPlannerComponent, AbilityBarComponent, ConditionChipsComponent],
  templateUrl: './grid.component.html',
  styleUrl: './grid.component.css',
})
export class GridComponent implements OnInit {
  @Input() joinCode = '';
  @Input() myUserId = '';
  @Input() isGm = false;

  cells$!: Observable<Cell[]>;
  gridColumns$!: Observable<string>;
  isPlanning$!: Observable<boolean>;
  timerSeconds$!: Observable<number | null>;

  private combatants: Combatant[] = [];
  private positions: Record<string, Position> = {};


  /** This user's plans this round, per character they control. */
  myPlans: Record<string, PlannedActionEntry[]> = {};
  private selectedCombatantId: string | null = null;
  lockedIn = false;
  gmSelectedEnemyId: string | null = null;
  enemyPlannedActions: Record<string, PlannedActionEntry[]> = {};
  apCosts: Record<string, number> = { ...DEFAULT_AP_COSTS };
  /** The ability the next click on a token uses (3B.3), or null. */
  armedAbilityId: string | null = null;
  /** Targets picked so far for an armed "combatants" ability, in pick order (their roll order). */
  pickedTargetIds: string[] = [];
  /** The token under the pointer, for the Charge path preview. */
  hoveredCombatantId: string | null = null;
  private gridWidth = 20;
  private gridHeight = 20;

  constructor(private store: Store, private hub: GameHubService, private playback: ResolutionPlaybackService) {}

  ngOnInit(): void {
    const gridWidth$ = this.store.select(MapState.gridWidth);
    const gridHeight$ = this.store.select(MapState.gridHeight);

    this.gridColumns$ = gridWidth$.pipe(map(w => `repeat(${w}, 1fr)`));

    this.cells$ = combineLatest([gridWidth$, gridHeight$]).pipe(
      map(([w, h]) => {
        const cells: Cell[] = [];
        for (let y = 0; y < h; y++)
          for (let x = 0; x < w; x++)
            cells.push({ x, y });
        return cells;
      })
    );

    this.isPlanning$ = this.store.select(EncounterState.isPlanning);

    this.timerSeconds$ = this.store.select(EncounterState.planningTimerExpiresAt).pipe(
      map(expiresAt => {
        if (!expiresAt) return null;
        const diff = Math.max(0, Math.floor((new Date(expiresAt).getTime() - Date.now()) / 1000));
        return diff;
      })
    );

    this.store.select(MapState.positions).subscribe(p => this.positions = p);
    gridWidth$.subscribe(w => this.gridWidth = w);
    gridHeight$.subscribe(h => this.gridHeight = h);
    this.store.select(EncounterState.combatants).subscribe(c => this.combatants = c);
    this.store.select(EncounterState.myPlannedActions).subscribe(plans => this.myPlans = plans);
    this.store.select(EncounterState.selectedCombatantId).subscribe(id => this.selectedCombatantId = id);
    // A new round means nobody is locked in yet.
    this.store.select(EncounterState.roundNumber).subscribe(() => this.lockedIn = false);
    this.store.select(EncounterState.gmSelectedEnemyId).subscribe(id => this.gmSelectedEnemyId = id);
    this.store.select(EncounterState.enemyPlannedActions).subscribe(plans => this.enemyPlannedActions = plans);
    this.store.select(EncounterState.apCosts).subscribe(costs => this.apCosts = costs);
    this.store.select(EncounterState.armedAbilityId).subscribe(id => {
      this.armedAbilityId = id;
      this.pickedTargetIds = []; // a new (or no) ability starts a fresh pick
    });
  }

  // ----- Who am I planning for -----

  /** Living characters this user controls (one normally, several in Quick Battle). */
  get myCombatants(): Combatant[] {
    return this.combatants.filter(c => isControlledBy(c, this.myUserId) && !c.isDefeated);
  }

  /** The character being planned for: the one picked, or else the first living one. */
  get selected(): Combatant | null {
    const mine = this.myCombatants;
    return mine.find(c => c.id === this.selectedCombatantId) ?? mine[0] ?? null;
  }

  get selectedPlan(): PlannedActionEntry[] {
    const selected = this.selected;
    return selected ? this.myPlans[selected.id] ?? [] : [];
  }

  get controlsSeveral(): boolean {
    return this.myCombatants.length > 1;
  }

  get plannedCount(): number {
    return this.myCombatants.filter(c => (this.myPlans[c.id]?.length ?? 0) > 0).length;
  }

  get lockInLabel(): string {
    if (this.lockedIn) return 'Locked in ✓';
    return this.controlsSeveral ? `Lock In All (${this.plannedCount}/${this.myCombatants.length} planned)` : 'Lock In';
  }

  isMine(combatant: Combatant): boolean {
    return isControlledBy(combatant, this.myUserId);
  }

  isSelected(combatant: Combatant): boolean {
    return this.controlsSeveral && this.selected?.id === combatant.id;
  }

  hasPlan(combatant: Combatant): boolean {
    return (this.myPlans[combatant.id]?.length ?? 0) > 0;
  }

  // ----- Rendering helpers -----

  private get isPlayingBack(): boolean {
    return this.playback.playing();
  }

  /** Positions on screen: the playback's while a round plays back, otherwise the map's. */
  private get shownPositions(): Record<string, Position> {
    return this.isPlayingBack ? this.playback.positions() : this.positions;
  }

  tokenAt(cell: Cell): Combatant | null {
    const pos = this.shownPositions;
    for (const combatant of this.combatants) {
      const p = pos[combatant.id];
      if (p && p.x === cell.x && p.y === cell.y)
        return combatant;
    }
    return null;
  }

  /**
   * Every token on the board. Tokens live in their own layer above the cells, one element per
   * combatant, so they glide between cells and keep their auras instead of being rebuilt in
   * each new cell.
   */
  get tokens(): TokenView[] {
    const pos = this.shownPositions;
    const views: TokenView[] = [];
    for (const combatant of this.combatants) {
      const p = pos[combatant.id];
      if (p) views.push({ combatant, x: p.x, y: p.y });
    }
    return views;
  }

  trackToken(_: number, token: TokenView): string {
    return token.combatant.id;
  }

  trackKey(_: number, item: { key: number }): number {
    return item.key;
  }

  tokenTransform(token: { x: number; y: number }): string {
    return `translate(${token.x * CELL_PITCH_PX + TOKEN_INSET_PX}px, ${token.y * CELL_PITCH_PX + TOKEN_INSET_PX}px)`;
  }

  /** Durations for the token transitions: transform (movement), opacity and background (defeat fade). */
  get tokenTransitionDurations(): string {
    return `${this.playback.moveMs()}ms, ${FADE_MS}ms, ${FADE_MS}ms`;
  }

  get floatTransitionDuration(): string {
    return `${this.playback.moveMs()}ms`;
  }

  /** Auras glowing on a combatant right now (miss, hit, hurt). */
  aurasFor(combatantId: string): ActiveAura[] {
    return this.playback.auras().filter(a => a.combatantId === combatantId);
  }

  get auraMs(): number {
    return this.playback.timing.auraMs;
  }

  get floatMs(): number {
    return this.playback.timing.floatMs;
  }

  /** Floating damage / MISS texts, positioned over their combatants. */
  get floats(): FloatView[] {
    const pos = this.shownPositions;
    const stacks: Record<string, number> = {};
    const views: FloatView[] = [];
    for (const f of this.playback.floats()) {
      const p = pos[f.combatantId];
      if (!p) continue;
      const stack = stacks[f.combatantId] ?? 0;
      stacks[f.combatantId] = stack + 1;
      views.push({ key: f.key, text: f.text, kind: f.kind, x: p.x, y: p.y, stack });
    }
    return views;
  }

  floatTransform(f: FloatView): string {
    return `translate(${f.x * CELL_PITCH_PX}px, ${f.y * CELL_PITCH_PX - 10 - f.stack * 17}px)`;
  }

  /** Defeated as far as the screen has shown (playback reveals defeats as they happen). */
  isShownDefeated(combatant: Combatant): boolean {
    return this.playback.isDefeated(combatant);
  }

  /** Status as shown, with bleedout rounds left (3B.5). */
  shownStatus(combatant: Combatant) {
    return this.playback.statusOf(combatant);
  }

  /** Conditions as shown (3B.4): playback reveals them as they land. */
  shownConditions(combatant: Combatant): ActiveConditionDto[] {
    return this.playback.conditionsOf(combatant);
  }

  tokenTitle(combatant: Combatant): string {
    const { status, bleedoutRoundsLeft } = this.playback.statusOf(combatant);
    const state = status === 'downed' ? ` — downed, bleeds out in ${bleedoutRoundsLeft} ${bleedoutRoundsLeft === 1 ? 'round' : 'rounds'}`
      : status === 'stable' ? ' — stable' : status === 'dead' ? ' — dead' : '';
    const conditions = this.playback.conditionsOf(combatant);
    const shown = conditions.length > 0
      ? ` — ${conditions.map(c => `${c.condition.name} (${describeDuration(c.roundsLeft)})`).join(', ')}`
      : '';
    return `${combatant.characterName} (HP: ${this.playback.hpOf(combatant)}/${combatant.maxHp})${state}${shown}`;
  }

  onTokenClick(token: TokenView): void {
    this.onCellClick({ x: token.x, y: token.y });
  }

  onTokenHover(token: TokenView | null): void {
    this.hoveredCombatantId = token?.combatant.id ?? null;
  }

  /**
   * Where the combatant's queued moves end and how many movement cells they use, counting ability
   * moves (Dash, Charge) the way the server's PlanValidator does.
   */
  private walk(combatant: Combatant, queue: PlannedActionEntry[]) {
    return walkQueue(this.positions[combatant.id], queue, combatant.abilities, combatant.movementRange, this.positions);
  }

  isInRange(cell: Cell): boolean {
    if (this.armedAbility) return false; // the ability's reach is shown instead
    // GM enemy planning mode — show range around selected enemy
    if (this.isGm && this.gmSelectedEnemyId) {
      const enemy = this.combatants.find(c => c.id === this.gmSelectedEnemyId);
      return enemy ? this.isMoveTarget(enemy, cell, this.enemyQueue(enemy.id)) : false;
    }
    if (this.lockedIn) return false;
    const myCombatant = this.selected;
    return myCombatant ? this.isMoveTarget(myCombatant, cell, this.selectedPlan) : false;
  }

  /**
   * A free cell the combatant can still walk to this round: measured from where its queued moves
   * end, within the movement cells it has left, and only while it can afford another move (3B.2).
   */
  private isMoveTarget(combatant: Combatant, cell: Cell, queue: PlannedActionEntry[]): boolean {
    const start = this.positions[combatant.id];
    if (!start || !canAfford(queue, 'move', combatant.maxAp, this.apCosts, combatant.abilities)) return false;
    const { position, cells } = this.walk(combatant, queue);
    const from = position!;
    const cellsLeft = combatant.movementRange - cells;
    const distance = chebyshev(from, cell);
    const occupant = this.tokenAt(cell);
    return distance > 0 && distance <= cellsLeft && (!occupant || occupant.id === combatant.id);
  }

  private enemyQueue(combatantId: string): PlannedActionEntry[] {
    return this.enemyPlannedActions[combatantId] ?? [];
  }

  isEnemyGhostTarget(cell: Cell): boolean {
    return Object.values(this.enemyPlannedActions).some(queue => queue.some(a =>
      (a.type === 'move' || a.type === 'ability') && a.targetCell?.x === cell.x && a.targetCell?.y === cell.y));
  }

  /** A cell one of your characters plans to move to (a move, or an ability that moves to a cell). */
  isGhostTarget(cell: Cell): boolean {
    return Object.values(this.myPlans).some(plan => plan.some(a =>
      (a.type === 'move' || a.type === 'ability') && a.targetCell?.x === cell.x && a.targetCell?.y === cell.y));
  }

  /** Queue step (1-based) of the selected character's move to this cell, for the marker on the grid. */
  ghostStep(cell: Cell): number | null {
    const plan = this.selectedPlan;
    if (plan.length < 2) return null;
    for (let i = plan.length - 1; i >= 0; i--) {
      const a = plan[i];
      if ((a.type === 'move' || a.type === 'ability') && a.targetCell?.x === cell.x && a.targetCell?.y === cell.y) return i + 1;
    }
    return null;
  }

  /** The last queued action is a move to this cell (clicking it again undoes it). */
  private isLastMoveTarget(cell: Cell): boolean {
    const last = this.selectedPlan[this.selectedPlan.length - 1];
    return last?.type === 'move' && last.targetCell?.x === cell.x && last.targetCell?.y === cell.y;
  }

  isTargeted(combatant: Combatant): boolean {
    return Object.values(this.myPlans).some(plan => plan.some(a =>
      (a.type === 'melee_attack' || a.type === 'ranged_attack' || a.type === 'ability')
      && (a.targetCombatantId === combatant.id || !!a.targetCombatantIds?.includes(combatant.id))));
  }

  /** 1-based pick number of a target chosen for the armed multi-target ability, or null. */
  pickNumber(combatant: Combatant): number | null {
    const i = this.pickedTargetIds.indexOf(combatant.id);
    return i === -1 ? null : i + 1;
  }

  // ----- Abilities (3B.3) -----

  /** Who an armed ability belongs to: the monster the GM is planning, or the character being planned for. */
  private get abilityUser(): Combatant | null {
    if (this.isGm) return this.combatants.find(c => c.id === this.gmSelectedEnemyId) ?? null;
    return this.selected;
  }

  private queueOf(combatant: Combatant): PlannedActionEntry[] {
    return this.isGm ? this.enemyQueue(combatant.id) : this.myPlans[combatant.id] ?? [];
  }

  get armedAbility(): AbilityDto | null {
    const user = this.abilityUser;
    return (this.armedAbilityId && user?.abilities.find(a => a.id === this.armedAbilityId)) || null;
  }

  /** Cells within the armed ability's range of where the user's queued moves end (not for a move to a cell). */
  isInAbilityRange(cell: Cell): boolean {
    const ability = this.armedAbility;
    const user = this.abilityUser;
    if (!ability || !user || abilityTakesCell(ability)) return false;
    const from = this.walk(user, this.queueOf(user)).position;
    const distance = from ? chebyshev(from, cell) : Infinity;
    return distance > 0 && distance <= ability.targeting.range;
  }

  /**
   * A free cell the armed move-to-cell ability (Dash) can go to: within its cells of where the
   * user's queued moves end (and the budget left, if it uses it). Shown in its own colour, since
   * Dash's cells come on top of the normal movement range.
   */
  isAbilityMoveTarget(cell: Cell): boolean {
    const ability = this.armedAbility;
    const user = this.abilityUser;
    if (!ability || !user || !abilityTakesCell(ability)) return false;
    const { position, cells } = this.walk(user, this.queueOf(user));
    if (!position || chebyshev(position, cell) === 0) return false;
    const occupant = this.tokenAt(cell);
    if (occupant && occupant.id !== user.id) return false;
    return abilityCellError(ability, position, cell, cells, user.movementRange, this.gridWidth, this.gridHeight) === null;
  }

  /**
   * The expected path of the armed Charge to the hovered target: from where the user's queued
   * moves end toward it until in melee reach, capped like the server caps it. The real cell is
   * picked when the round resolves (the target may move, someone may block the way).
   */
  isChargePath(cell: Cell): boolean {
    return this.chargePreview.some(p => p.x === cell.x && p.y === cell.y);
  }

  private get chargePreview(): Position[] {
    const ability = this.armedAbility;
    const user = this.abilityUser;
    const target = this.combatants.find(c => c.id === this.hoveredCombatantId);
    const move = ability?.move;
    if (!ability || !user || !target || move?.to !== 'target' || !canTargetWithAbility(user, target, ability)) return [];
    const { position, cells } = this.walk(user, this.queueOf(user));
    const targetPos = this.positions[target.id];
    if (!position || !targetPos || chebyshev(position, targetPos) > ability.targeting.range) return [];
    const cap = move.usesMovementBudget ? Math.min(move.maxCells, Math.max(0, user.movementRange - cells)) : move.maxCells;
    const { cells: steps } = estimateMoveToTarget(position, targetPos, cap);
    return chebyshevPath(position, targetPos).slice(1, steps + 1);
  }

  /** A token the armed ability may be planned on (side and status; range is checked at resolution). */
  isAbilityTarget(combatant: Combatant): boolean {
    const ability = this.armedAbility;
    const user = this.abilityUser;
    return !!ability && !!user && canTargetWithAbility(user, combatant, ability);
  }

  /** "Power Strike: click an enemy (adjacent)", shown while an ability is armed. */
  get armedHint(): string {
    const ability = this.armedAbility;
    if (!ability) return '';
    const t = ability.targeting;
    const reach = t.range === 1 ? 'adjacent' : `up to ${t.range} cells away`;
    const cancel = 'Esc or click the button again to cancel.';
    if (abilityTakesCell(ability) && ability.move) {
      const extra = ability.move.usesMovementBudget ? '' : ', on top of your normal movement';
      return `${ability.name}: click a highlighted cell up to ${ability.move.maxCells} away${extra}. ${cancel}`;
    }
    if (ability.move?.to === 'target') {
      const who = t.filter === 'enemy' ? 'an enemy' : t.filter === 'ally' ? 'an ally' : 'a target';
      return `${ability.name}: click ${who} up to ${t.range} cells away. You run next to them (path shown on hover) and act in the same tick; the run uses your movement. ${cancel}`;
    }
    if (!abilityTakesTargets(ability)) {
      const whom = t.filter === 'enemy' ? 'enemy' : t.filter === 'downed_ally' ? 'downed ally' : t.filter === 'ally' ? 'ally' : 'combatant';
      const hits = t.count ? `the nearest ${t.count} ${whom === 'ally' ? 'allies' : whom + 's'}` : `every ${whom}`;
      return `${ability.name} hits ${hits} ${reach} when it resolves (highlighted). Click the grid or press Enter to queue it. ${cancel}`;
    }
    const self = t.includeSelf ? ' or yourself' : '';
    if (ability.targeting.kind === 'combatants') {
      const many = t.filter === 'enemy' ? 'enemies' : t.filter === 'ally' ? 'allies' : 'targets';
      return `${ability.name}: click up to ${maxTargets(ability)} ${many}${self} (${reach} when it resolves), then Done or Enter. Click a picked one again to drop it. ${cancel}`;
    }
    const who = t.filter === 'enemy' ? 'an enemy' : t.filter === 'ally' ? 'an ally' : t.filter === 'downed_ally' ? 'a downed ally' : 'a target';
    return `${ability.name}: click ${who}${self} (${reach} when it resolves). ${cancel}`;
  }

  /**
   * The armed ability's confirm button: "Done (1/2)" while picking targets for a multi-target
   * ability, "Use Cleave" for one that finds its own targets; null when clicking a target is the
   * only way to queue it.
   */
  get armedConfirmLabel(): string | null {
    const ability = this.armedAbility;
    if (!ability || abilityTakesCell(ability)) return null;
    if (!abilityTakesTargets(ability)) return `Use ${ability.name}`;
    if (ability.targeting.kind === 'combatants') return `Done (${this.pickedTargetIds.length}/${maxTargets(ability)})`;
    return null;
  }

  get canConfirmArmed(): boolean {
    const ability = this.armedAbility;
    return !!ability && !abilityTakesCell(ability) && (!abilityTakesTargets(ability) || this.pickedTargetIds.length > 0);
  }

  /** Queues the armed ability as it stands: no targets (auto / self), or the targets picked so far. */
  confirmArmed(): void {
    const ability = this.armedAbility;
    const user = this.abilityUser;
    if (!ability || !user || !this.canConfirmArmed || this.isPlayingBack || (!this.isGm && this.lockedIn)) return;
    this.enqueueFor(user, abilityTakesTargets(ability)
      ? entry('ability', { abilityId: ability.id, targetCombatantIds: [...this.pickedTargetIds] })
      : entry('ability', { abilityId: ability.id }));
    this.store.dispatch(new DisarmAbility());
  }

  @HostListener('document:keydown.enter')
  onEnter(): void {
    if (this.armedAbilityId) this.confirmArmed();
  }

  /** Appends to the user's queue: the GM's monster or one of this player's characters. */
  private enqueueFor(user: Combatant, action: PlannedActionEntry): void {
    if (this.isGm) this.planEnemy(user, this.enemyQueue(user.id), action);
    else this.plan(user, action);
  }

  toggleAbility(abilityId: string): void {
    this.store.dispatch(this.armedAbilityId === abilityId ? new DisarmAbility() : new ArmAbility(abilityId));
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.armedAbilityId) this.store.dispatch(new DisarmAbility());
  }

  /**
   * A click while an ability is armed (returns false when nothing is armed):
   *  - one that finds its own targets ("auto"): any click queues it;
   *  - a multi-target one ("combatants"): a legal target is picked (or dropped, if already picked),
   *    and the ability is queued once `count` are picked; a click on an empty cell cancels;
   *  - a single-target one: a legal target queues it; anywhere else cancels.
   * Ally targeting goes first, so clicking one of your own characters targets them rather than
   * switching who you plan for.
   */
  private useArmedAbility(user: Combatant, cell: Cell, enqueue: (action: PlannedActionEntry) => void): boolean {
    const ability = this.armedAbility;
    if (!ability) return false;
    if (abilityTakesCell(ability)) {
      if (this.isAbilityMoveTarget(cell)) enqueue(entry('ability', { abilityId: ability.id, targetCell: { x: cell.x, y: cell.y } }));
      this.store.dispatch(new DisarmAbility());
      return true;
    }
    if (!abilityTakesTargets(ability)) {
      enqueue(entry('ability', { abilityId: ability.id }));
      this.store.dispatch(new DisarmAbility());
      return true;
    }

    const target = this.tokenAt(cell);
    const legal = !!target && canTargetWithAbility(user, target, ability);
    if (ability.targeting.kind === 'combatants') {
      if (!target) {
        this.store.dispatch(new DisarmAbility());
      } else if (legal) {
        const picked = this.pickedTargetIds.includes(target.id)
          ? this.pickedTargetIds.filter(id => id !== target.id)
          : [...this.pickedTargetIds, target.id];
        if (picked.length >= maxTargets(ability)) {
          enqueue(entry('ability', { abilityId: ability.id, targetCombatantIds: picked }));
          this.store.dispatch(new DisarmAbility());
        } else {
          this.pickedTargetIds = picked;
        }
      }
      return true;
    }

    if (legal) enqueue(entry('ability', { abilityId: ability.id, targetCombatantId: target!.id }));
    this.store.dispatch(new DisarmAbility());
    return true;
  }

  get hasDefendPlanned(): boolean {
    return this.selectedPlan.some(a => a.type === 'defend');
  }

  get plannedActionSummary(): string {
    const abilities = this.selected?.abilities ?? [];
    return describeQueue(this.selectedPlan, id => this.combatants.find(c => c.id === id)?.characterName,
      id => abilities.find(a => a.id === id)?.name);
  }

  /** AP the selected character's queue costs. */
  get apUsed(): number {
    return apSpent(this.selectedPlan, this.apCosts, this.selected?.abilities ?? []);
  }

  get maxAp(): number {
    return this.selected?.maxAp ?? 0;
  }

  get canAffordDefend(): boolean {
    const selected = this.selected;
    return !!selected && canAfford(this.selectedPlan, 'defend', selected.maxAp, this.apCosts, selected.abilities);
  }

  /** Cells the selected character can still move this round. */
  get moveCellsLeft(): number {
    const selected = this.selected;
    if (!selected) return 0;
    return Math.max(0, selected.movementRange - this.walk(selected, this.selectedPlan).cells);
  }

  /** Whether the planner's queued moves end next to (or on top of) the target: Stabilize's reach. */
  private endsNextTo(planner: Combatant, queue: PlannedActionEntry[], target: Combatant): boolean {
    const from = this.walk(planner, queue).position;
    const targetPos = this.positions[target.id];
    return !!from && !!targetPos && chebyshev(from, targetPos) <= MELEE_REACH;
  }

  /** Downed allies of the party (3B.5), for the stabilize hint in the action bar. */
  get downedAllies(): Combatant[] {
    return this.combatants.filter(c => c.combatantType === 'player' && c.status === 'downed');
  }

  /** Melee if the target is within reach of where the planner's queued moves end, otherwise ranged. */
  private attackTypeAgainst(planner: Combatant, queue: PlannedActionEntry[], target: Combatant): string {
    const from = this.walk(planner, queue).position;
    const targetPos = this.positions[target.id];
    return from && targetPos && chebyshev(from, targetPos) <= MELEE_REACH ? 'melee_attack' : 'ranged_attack';
  }

  // ----- Interaction -----

  onCellClick(cell: Cell): void {
    if (this.isPlayingBack) return;

    // GM enemy planning mode: each click appends to the selected monster's queue.
    if (this.isGm && this.gmSelectedEnemyId) {
      const enemy = this.combatants.find(c => c.id === this.gmSelectedEnemyId);
      if (!enemy) return;
      const queue = this.enemyQueue(enemy.id);
      if (this.useArmedAbility(enemy, cell, action => this.planEnemy(enemy, queue, action))) return;
      const targetCombatant = this.tokenAt(cell);

      let action: PlannedActionEntry | null = null;
      // Anyone not dead can be attacked: a downed or stable target is finished off by a hit (3B.5).
      if (targetCombatant && targetCombatant.id !== enemy.id && targetCombatant.status !== 'dead') {
        action = entry(this.attackTypeAgainst(enemy, queue, targetCombatant), { targetCombatantId: targetCombatant.id });
      } else if (this.isInRange(cell)) {
        action = entry('move', { targetCell: { x: cell.x, y: cell.y } });
      }
      if (action) this.planEnemy(enemy, queue, action);
      return;
    }

    // Normal player mode
    if (this.lockedIn) return;

    const me = this.selected;
    if (me && this.useArmedAbility(me, cell, action => this.plan(me, action))) return;

    const targetCombatant = this.tokenAt(cell);

    // A downed ally: stabilize them, if the selected character's queued moves end next to them (3B.5).
    if (targetCombatant && targetCombatant.status === 'downed' && targetCombatant.combatantType === 'player') {
      if (me && me.id !== targetCombatant.id && this.endsNextTo(me, this.selectedPlan, targetCombatant))
        this.plan(me, entry('stabilize', { targetCombatantId: targetCombatant.id }));
      return;
    }

    // Clicking one of your own characters picks who you're planning for.
    if (targetCombatant && this.isMine(targetCombatant)) {
      if (!targetCombatant.isDefeated) this.store.dispatch(new SelectMyCombatant(targetCombatant.id));
      return;
    }

    const myCombatant = this.selected;
    if (!myCombatant) return;

    const queue = this.selectedPlan;
    if (targetCombatant && targetCombatant.status !== 'dead') {
      const type = this.attackTypeAgainst(myCombatant, queue, targetCombatant);
      this.plan(myCombatant, entry(type, { targetCombatantId: targetCombatant.id }));
    } else if (!targetCombatant && this.isLastMoveTarget(cell)) {
      this.undoLast();
    } else if (this.isInRange(cell)) {
      this.plan(myCombatant, entry('move', { targetCell: { x: cell.x, y: cell.y } }));
    }
  }

  /** Appends an action to the character's queue if its AP allows, and sends the whole queue. */
  private plan(combatant: Combatant, action: PlannedActionEntry): void {
    const queue = this.myPlans[combatant.id] ?? [];
    if (!canAffordEntry(queue, action, combatant.maxAp, this.apCosts, combatant.abilities)) return;
    this.setPlan(combatant, [...queue, action]);
  }

  /** Appends an action to a monster's queue (GM planning) if its AP allows, and sends it. */
  private planEnemy(enemy: Combatant, queue: PlannedActionEntry[], action: PlannedActionEntry): void {
    if (!canAffordEntry(queue, action, enemy.maxAp, this.apCosts, enemy.abilities)) return;
    const next = [...queue, action];
    this.store.dispatch(new GmSetEnemyPlan(enemy.id, next));
    this.hub.planEnemyAction(this.joinCode, enemy.id, { actions: next });
    // Done with this monster once it can't afford even the cheapest action (basic or ability).
    const cheapest = Math.min(
      ...Object.keys(this.apCosts).map(t => apCost(t, this.apCosts)),
      ...enemy.abilities.filter(a => !(enemy.cooldowns[a.id] > 0)).map(a => a.apCost));
    if (apSpent(next, this.apCosts, enemy.abilities) + cheapest > enemy.maxAp) this.store.dispatch(new GmSelectEnemy(null));
  }

  /** Stores and sends a character's queue; an empty queue clears the plan on the server too. */
  private setPlan(combatant: Combatant, queue: PlannedActionEntry[]): void {
    this.store.dispatch(new SetMyPlannedAction(combatant.id, queue));
    this.hub.planAction(this.joinCode, { actions: queue }, combatant.id);
  }

  planDefend(): void {
    const selected = this.selected;
    if (this.lockedIn || !selected) return;
    this.plan(selected, entry('defend'));
  }

  /** Drops the last queued action of the selected character. */
  undoLast(): void {
    const selected = this.selected;
    if (this.lockedIn || !selected || this.selectedPlan.length === 0) return;
    this.setPlan(selected, this.selectedPlan.slice(0, -1));
  }

  /** Clears the selected character's whole queue. */
  clearPlan(): void {
    const selected = this.selected;
    if (this.lockedIn || !selected || this.selectedPlan.length === 0) return;
    this.setPlan(selected, []);
  }

  onLockIn(): void {
    if (this.lockedIn || this.plannedCount === 0) return;
    this.lockedIn = true;
    if (this.armedAbilityId) this.store.dispatch(new DisarmAbility());
    this.hub.lockIn(this.joinCode);
  }

  onForceResolve(): void {
    this.hub.forceResolve(this.joinCode);
  }
}
