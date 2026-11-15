import { CommonModule } from '@angular/common';
import { Component, Input, OnDestroy, OnInit } from '@angular/core';
import { GmEnemyPlannerComponent } from '../gm-enemy-planner/gm-enemy-planner.component';
import { Store } from '@ngxs/store';
import { Observable, combineLatest, map } from 'rxjs';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { MapState } from '../../../state/map/map.state';
import { GmSelectEnemy, GmSetEnemyPlan, SelectMyCombatant, SetMyPlannedAction } from '../../../state/encounter/encounter.actions';
import {
  DEFAULT_AP_COSTS, apCost, apSpent, canAfford, chebyshev, describeQueue, moveCellsUsed, projectedPosition,
} from '../../../state/encounter/plan-queue';

import { GameHubService } from '../../../core/services/game-hub.service';
import { Combatant, isControlledBy } from '../../../state/encounter/encounter.model';
import { Position } from '../../../state/map/map.state.model';
import { PlannedActionEntry, TickResult } from '../../../contracts/hub-contracts.generated';

interface Cell {
  x: number;
  y: number;
}

interface DamageNumber {
  combatantId: string;
  text: string;
  isHit: boolean;
  key: string;
}

@Component({
  selector: 'app-grid',
  standalone: true,
  imports: [CommonModule, GmEnemyPlannerComponent],
  templateUrl: './grid.component.html',
  styleUrl: './grid.component.css',
})
export class GridComponent implements OnInit, OnDestroy {
  @Input() joinCode = '';
  @Input() myUserId = '';
  @Input() isGm = false;

  cells$!: Observable<Cell[]>;
  gridColumns$!: Observable<string>;
  isPlanning$!: Observable<boolean>;
  isResolving$!: Observable<boolean>;
  timerSeconds$!: Observable<number | null>;

  private combatants: Combatant[] = [];
  private positions: Record<string, Position> = {};

  // Playback state
  private animPositions: Record<string, Position> = {};
  private preResolutionPositions: Record<string, Position> = {};
  private isPlayingBack = false;
  private defeatingNow = new Set<string>();
  /** 1-based tick being played back, and how many ticks the round has. */
  playbackTick = 0;
  playbackTickCount = 0;
  damageNumbers: DamageNumber[] = [];

  /** This user's plans this round, per character they control. */
  myPlans: Record<string, PlannedActionEntry[]> = {};
  private selectedCombatantId: string | null = null;
  lockedIn = false;
  gmSelectedEnemyId: string | null = null;
  enemyPlannedActions: Record<string, PlannedActionEntry[]> = {};
  private apCosts: Record<string, number> = { ...DEFAULT_AP_COSTS };

  private destroyed = false;

  constructor(private store: Store, private hub: GameHubService) {}

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
    this.isResolving$ = this.store.select(EncounterState.isResolving);

    this.timerSeconds$ = this.store.select(EncounterState.planningTimerExpiresAt).pipe(
      map(expiresAt => {
        if (!expiresAt) return null;
        const diff = Math.max(0, Math.floor((new Date(expiresAt).getTime() - Date.now()) / 1000));
        return diff;
      })
    );

    this.store.select(MapState.positions).subscribe(p => {
      // Snapshot current positions before overwriting — used as playback starting point
      if (!this.isPlayingBack) {
        this.preResolutionPositions = this.positions;
      }
      this.positions = p;
    });
    this.store.select(EncounterState.combatants).subscribe(c => this.combatants = c);
    this.store.select(EncounterState.myPlannedActions).subscribe(plans => this.myPlans = plans);
    this.store.select(EncounterState.selectedCombatantId).subscribe(id => this.selectedCombatantId = id);
    // A new round means nobody is locked in yet.
    this.store.select(EncounterState.roundNumber).subscribe(() => this.lockedIn = false);
    this.store.select(EncounterState.gmSelectedEnemyId).subscribe(id => this.gmSelectedEnemyId = id);
    this.store.select(EncounterState.enemyPlannedActions).subscribe(plans => this.enemyPlannedActions = plans);
    this.store.select(EncounterState.apCosts).subscribe(costs => this.apCosts = costs);

    // Trigger playback when resolution ticks arrive
    this.store.select(EncounterState.resolutionTicks).subscribe(ticks => {
      if (ticks && ticks.length > 0 && !this.isPlayingBack && !this.destroyed) {
        this.startPlayback(ticks);
      }
    });
  }

  ngOnDestroy(): void {
    this.destroyed = true;
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

  tokenAt(cell: Cell): Combatant | null {
    const pos = this.isPlayingBack ? this.animPositions : this.positions;
    for (const combatant of this.combatants) {
      const p = pos[combatant.id];
      if (p && p.x === cell.x && p.y === cell.y)
        return combatant;
    }
    return null;
  }

  damageNumbersAt(cell: Cell): DamageNumber[] {
    if (this.damageNumbers.length === 0) return [];
    const pos = this.isPlayingBack ? this.animPositions : this.positions;
    return this.damageNumbers.filter(dmg => {
      const p = pos[dmg.combatantId];
      return p && p.x === cell.x && p.y === cell.y;
    });
  }

  isInRange(cell: Cell): boolean {
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
    if (!start || !canAfford(queue, 'move', combatant.maxAp, this.apCosts)) return false;
    const from = projectedPosition(start, queue)!;
    const cellsLeft = combatant.movementRange - moveCellsUsed(start, queue);
    const distance = chebyshev(from, cell);
    const occupant = this.tokenAt(cell);
    return distance > 0 && distance <= cellsLeft && (!occupant || occupant.id === combatant.id);
  }

  private enemyQueue(combatantId: string): PlannedActionEntry[] {
    return this.enemyPlannedActions[combatantId] ?? [];
  }

  isEnemyGhostTarget(cell: Cell): boolean {
    return Object.values(this.enemyPlannedActions).some(queue => queue.some(a =>
      a.type === 'move' && a.targetCell?.x === cell.x && a.targetCell?.y === cell.y));
  }

  /** A cell one of your characters plans to move to. */
  isGhostTarget(cell: Cell): boolean {
    return Object.values(this.myPlans).some(plan => plan.some(a =>
      a.type === 'move' && a.targetCell?.x === cell.x && a.targetCell?.y === cell.y));
  }

  /** Queue step (1-based) of the selected character's move to this cell, for the marker on the grid. */
  ghostStep(cell: Cell): number | null {
    const plan = this.selectedPlan;
    if (plan.length < 2) return null;
    for (let i = plan.length - 1; i >= 0; i--) {
      const a = plan[i];
      if (a.type === 'move' && a.targetCell?.x === cell.x && a.targetCell?.y === cell.y) return i + 1;
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
      (a.type === 'melee_attack' || a.type === 'ranged_attack') && a.targetCombatantId === combatant.id));
  }

  isDefeating(combatant: Combatant): boolean {
    return this.defeatingNow.has(combatant.id);
  }

  get hasDefendPlanned(): boolean {
    return this.selectedPlan.some(a => a.type === 'defend');
  }

  get plannedActionSummary(): string {
    return describeQueue(this.selectedPlan, id => this.combatants.find(c => c.id === id)?.characterName);
  }

  /** AP the selected character's queue costs. */
  get apUsed(): number {
    return apSpent(this.selectedPlan, this.apCosts);
  }

  get maxAp(): number {
    return this.selected?.maxAp ?? 0;
  }

  get canAffordDefend(): boolean {
    const selected = this.selected;
    return !!selected && canAfford(this.selectedPlan, 'defend', selected.maxAp, this.apCosts);
  }

  /** Cells the selected character can still move this round. */
  get moveCellsLeft(): number {
    const selected = this.selected;
    if (!selected) return 0;
    return Math.max(0, selected.movementRange - moveCellsUsed(this.positions[selected.id], this.selectedPlan));
  }

  /** Melee if the target is within reach of where the planner's queued moves end, otherwise ranged. */
  private attackTypeAgainst(plannerId: string, queue: PlannedActionEntry[], target: Combatant): string {
    const from = projectedPosition(this.positions[plannerId], queue);
    const targetPos = this.positions[target.id];
    return from && targetPos && chebyshev(from, targetPos) <= 1 ? 'melee_attack' : 'ranged_attack';
  }

  // ----- Interaction -----

  onCellClick(cell: Cell): void {
    if (this.isPlayingBack) return;

    // GM enemy planning mode: each click appends to the selected monster's queue.
    if (this.isGm && this.gmSelectedEnemyId) {
      const enemy = this.combatants.find(c => c.id === this.gmSelectedEnemyId);
      if (!enemy) return;
      const queue = this.enemyQueue(enemy.id);
      const targetCombatant = this.tokenAt(cell);

      let action: PlannedActionEntry | null = null;
      if (targetCombatant && targetCombatant.id !== enemy.id && !targetCombatant.isDefeated) {
        action = { type: this.attackTypeAgainst(enemy.id, queue, targetCombatant), targetCell: null, targetCombatantId: targetCombatant.id };
      } else if (this.isInRange(cell)) {
        action = { type: 'move', targetCell: { x: cell.x, y: cell.y }, targetCombatantId: null };
      }
      if (!action || !canAfford(queue, action.type, enemy.maxAp, this.apCosts)) return;

      const next = [...queue, action];
      this.store.dispatch(new GmSetEnemyPlan(enemy.id, next));
      this.hub.planEnemyAction(this.joinCode, enemy.id, { actions: next });
      // Done with this monster once it can't afford even the cheapest action.
      const cheapest = Math.min(...Object.keys(this.apCosts).map(t => apCost(t, this.apCosts)));
      if (apSpent(next, this.apCosts) + cheapest > enemy.maxAp) this.store.dispatch(new GmSelectEnemy(null));
      return;
    }

    // Normal player mode
    if (this.lockedIn) return;

    const targetCombatant = this.tokenAt(cell);

    // Clicking one of your own characters picks who you're planning for.
    if (targetCombatant && this.isMine(targetCombatant)) {
      if (!targetCombatant.isDefeated) this.store.dispatch(new SelectMyCombatant(targetCombatant.id));
      return;
    }

    const myCombatant = this.selected;
    if (!myCombatant) return;

    const queue = this.selectedPlan;
    if (targetCombatant && !targetCombatant.isDefeated) {
      const type = this.attackTypeAgainst(myCombatant.id, queue, targetCombatant);
      this.plan(myCombatant, { type, targetCell: null, targetCombatantId: targetCombatant.id });
    } else if (!targetCombatant && this.isLastMoveTarget(cell)) {
      this.undoLast();
    } else if (this.isInRange(cell)) {
      this.plan(myCombatant, { type: 'move', targetCell: { x: cell.x, y: cell.y }, targetCombatantId: null });
    }
  }

  /** Appends an action to the character's queue if its AP allows, and sends the whole queue. */
  private plan(combatant: Combatant, action: PlannedActionEntry): void {
    const queue = this.myPlans[combatant.id] ?? [];
    if (!canAfford(queue, action.type, combatant.maxAp, this.apCosts)) return;
    this.setPlan(combatant, [...queue, action]);
  }

  /** Stores and sends a character's queue; an empty queue clears the plan on the server too. */
  private setPlan(combatant: Combatant, queue: PlannedActionEntry[]): void {
    this.store.dispatch(new SetMyPlannedAction(combatant.id, queue));
    this.hub.planAction(this.joinCode, { actions: queue }, combatant.id);
  }

  planDefend(): void {
    const selected = this.selected;
    if (this.lockedIn || !selected) return;
    this.plan(selected, { type: 'defend', targetCell: null, targetCombatantId: null });
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
    this.hub.lockIn(this.joinCode);
  }

  onForceResolve(): void {
    this.hub.forceResolve(this.joinCode);
  }

  // ----- Playback animation -----

  /** Plays every tick in order: movement, then damage numbers, then defeat fades (3B.2). */
  private async startPlayback(ticks: TickResult[]): Promise<void> {
    this.isPlayingBack = true;
    this.playbackTickCount = ticks.length;
    // Use pre-resolution positions as the starting point; each tick continues from the last.
    this.animPositions = { ...this.preResolutionPositions };

    for (let i = 0; i < ticks.length; i++) {
      if (this.destroyed) return;
      this.playbackTick = i + 1;
      await this.playTick(ticks[i]);
    }

    // Positions already committed to MapState in game-hub.service.ts when ResolutionCompleted arrived.
    // Just clean up playback state so tokenAt() switches back to this.positions (which has final positions).
    this.animPositions = {};
    this.playbackTick = 0;
    this.playbackTickCount = 0;
    this.isPlayingBack = false;
  }

  private async playTick(tick: TickResult): Promise<void> {
    // Phase 1: Movement (~1.2s)
    const maxPathLen = tick.movements.length > 0 ? Math.max(...tick.movements.map(m => m.path.length)) : 0;
    if (maxPathLen > 1) {
      const stepMs = Math.max(80, Math.floor(1200 / maxPathLen));
      for (let step = 0; step < maxPathLen; step++) {
        if (this.destroyed) return;
        for (const movement of tick.movements) {
          if (step < movement.path.length) {
            this.animPositions = { ...this.animPositions, [movement.combatantId]: movement.path[step] };
          }
        }
        await this.sleep(stepMs);
      }
    }

    // Phase 2: Attacks — floating damage numbers (~1s), or a short beat if nobody attacked.
    const newDamageNumbers: DamageNumber[] = tick.attacks
      .filter(attack => !attack.wasted)
      .map(attack => ({
        combatantId: attack.targetId,
        text: attack.hit ? `-${attack.damage}` : 'MISS',
        isHit: attack.hit,
        key: Math.random().toString(36).slice(2),
      }));
    this.damageNumbers = newDamageNumbers;
    await this.sleep(newDamageNumbers.length > 0 ? 1000 : 300);
    this.damageNumbers = [];

    // Phase 3: Defeat fade (~400ms)
    const newlyDefeated = new Set(tick.attacks.filter(a => a.targetDefeated).map(a => a.targetId));
    if (newlyDefeated.size > 0) {
      this.defeatingNow = newlyDefeated;
      await this.sleep(400);
      this.defeatingNow = new Set();
    }
  }

  private sleep(ms: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, ms));
  }
}
