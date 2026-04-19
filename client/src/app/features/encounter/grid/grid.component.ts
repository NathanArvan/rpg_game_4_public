import { CommonModule } from '@angular/common';
import { Component, Input, OnDestroy, OnInit } from '@angular/core';
import { Store } from '@ngxs/store';
import { Observable, combineLatest, map } from 'rxjs';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { MapState } from '../../../state/map/map.state';
import { ClearMyPlannedAction, GmSelectEnemy, GmSetEnemyPlan, SetMyPlannedAction } from '../../../state/encounter/encounter.actions';

import { GameHubService } from '../../../core/services/game-hub.service';
import { Combatant } from '../../../state/encounter/encounter.model';
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
  imports: [CommonModule],
  template: `
    <div class="grid-wrapper">

      <!-- Planning phase action bar -->
      <div class="action-bar" *ngIf="isPlanning$ | async">
        <div class="action-buttons">
          <button
            class="action-btn"
            [class.active]="plannedActionType === 'defend'"
            (click)="planDefend()">
            🛡 Defend
          </button>
          <button
            class="lock-in-btn"
            [disabled]="!myPlannedActions.length || lockedIn"
            (click)="onLockIn()">
            {{ lockedIn ? 'Locked in ✓' : 'Lock In' }}
          </button>
          <button
            class="force-btn"
            *ngIf="isGm"
            (click)="onForceResolve()">
            ⚡ Force Resolve
          </button>
        </div>
        <div class="planned-action-display" *ngIf="myPlannedActions.length">
          Planned: <strong>{{ plannedActionSummary }}</strong>
          <span *ngIf="!lockedIn" class="cancel-link" (click)="cancelPlan()">(cancel)</span>
        </div>
        <div class="timer" *ngIf="timerSeconds$ | async as secs" [class.urgent]="(secs ?? 0) < 10">
          ⏱ {{ secs }}s
        </div>
      </div>

      <!-- Resolving indicator -->
      <div class="resolving-banner" *ngIf="isResolving$ | async">
        Resolving...
      </div>

      <!-- Grid -->
      <div
        class="grid"
        [style.grid-template-columns]="gridColumns$ | async">
        <div
          *ngFor="let cell of cells$ | async"
          class="cell"
          [class.in-range]="isInRange(cell)"
          [class.ghost]="isGhostTarget(cell)"
          [class.enemy-ghost]="isEnemyGhostTarget(cell)"
          (click)="onCellClick(cell)">
          <div
            *ngIf="tokenAt(cell) as combatant"
            class="token"
            [class.my-token]="combatant.userId === myUserId"
            [class.defeated-token]="combatant.isDefeated"
            [class.defeating-token]="isDefeating(combatant)"
            [class.targeted-token]="isTargeted(combatant)"
            [title]="combatant.characterName + ' (HP: ' + combatant.currentHp + '/' + combatant.maxHp + ')'">
            {{ combatant.characterName[0] }}
          </div>
          <!-- Damage numbers overlay -->
          <div
            *ngFor="let dmg of damageNumbersAt(cell)"
            class="damage-number"
            [class.damage-hit]="dmg.isHit"
            [class.damage-miss]="!dmg.isHit">
            {{ dmg.text }}
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .grid-wrapper { margin-top: 16px; }

    .action-bar {
      display: flex;
      align-items: center;
      gap: 16px;
      margin-bottom: 8px;
      padding: 8px;
      background: #f0f4ff;
      border-radius: 4px;
      flex-wrap: wrap;
    }
    .action-buttons { display: flex; gap: 8px; align-items: center; }
    .action-btn {
      background: #5c6bc0;
      color: white;
      border: none;
      padding: 6px 12px;
      cursor: pointer;
      border-radius: 4px;
    }
    .action-btn.active { background: #283593; box-shadow: 0 0 0 2px #ffeb3b; }
    .action-btn:hover { background: #3949ab; }
    .lock-in-btn {
      background: #2e7d32;
      color: white;
      border: none;
      padding: 8px 18px;
      cursor: pointer;
      border-radius: 4px;
      font-weight: bold;
    }
    .lock-in-btn:disabled { background: #888; cursor: not-allowed; }
    .force-btn {
      background: #e65100;
      color: white;
      border: none;
      padding: 6px 12px;
      cursor: pointer;
      border-radius: 4px;
    }
    .planned-action-display { color: #333; font-size: 0.9em; }
    .cancel-link { color: #b71c1c; cursor: pointer; margin-left: 6px; text-decoration: underline; }
    .timer { font-weight: bold; color: #555; }
    .timer.urgent { color: #c62828; animation: pulse 1s infinite; }
    @keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.5; } }

    .resolving-banner {
      padding: 8px;
      background: #fff9c4;
      border-radius: 4px;
      margin-bottom: 8px;
      font-style: italic;
    }

    .grid {
      display: grid;
      gap: 1px;
      background: #999;
      border: 2px solid #555;
      width: fit-content;
    }
    .cell {
      width: 30px;
      height: 30px;
      background: #e8dfc8;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      position: relative;
    }
    .cell:hover { background: #d4c9a8; }
    .cell.in-range { background: #c8e6c9; }
    .cell.ghost { background: #bbdefb; }
    .cell.enemy-ghost { background: #ffcdd2; }
    .token {
      width: 24px;
      height: 24px;
      border-radius: 50%;
      background: #5c6bc0;
      color: white;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 11px;
      font-weight: bold;
      cursor: pointer;
      z-index: 1;
    }
    .token.my-token { background: #2e7d32; }
    .token.defeated-token { background: #9e9e9e; opacity: 0.5; cursor: not-allowed; }
    .token.defeating-token { animation: fadeOut 0.5s forwards; }
    .token.targeted-token { box-shadow: 0 0 0 2px #f44336; }
    @keyframes fadeOut { from { opacity: 1; } to { opacity: 0.2; } }

    .damage-number {
      position: absolute;
      top: -4px;
      left: 50%;
      transform: translateX(-50%);
      font-size: 11px;
      font-weight: bold;
      pointer-events: none;
      z-index: 10;
      animation: floatUp 0.8s ease-out forwards;
    }
    .damage-hit { color: #c62828; }
    .damage-miss { color: #757575; }
    @keyframes floatUp {
      0%   { opacity: 1; transform: translateX(-50%) translateY(0); }
      100% { opacity: 0; transform: translateX(-50%) translateY(-20px); }
    }
  `],
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
  damageNumbers: DamageNumber[] = [];

  myPlannedActions: PlannedActionEntry[] = [];
  lockedIn = false;
  gmSelectedEnemyId: string | null = null;
  enemyPlannedActions: Record<string, PlannedActionEntry> = {};

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
    this.store.select(EncounterState.myPlannedActions).subscribe(a => {
      this.myPlannedActions = a;
      // Reset lock-in state when a new planning phase clears plans
      if (a.length === 0) this.lockedIn = false;
    });
    this.store.select(EncounterState.gmSelectedEnemyId).subscribe(id => this.gmSelectedEnemyId = id);
    this.store.select(EncounterState.enemyPlannedActions).subscribe(plans => this.enemyPlannedActions = plans);

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
      if (!enemy) return false;
      const enemyPos = this.positions[enemy.id];
      if (!enemyPos) return false;
      const chebyshev = Math.max(Math.abs(cell.x - enemyPos.x), Math.abs(cell.y - enemyPos.y));
      return chebyshev > 0 && chebyshev <= 6 && !this.tokenAt(cell);
    }
    if (this.lockedIn) return false;
    const myCombatant = this.combatants.find(c => c.userId === this.myUserId);
    if (!myCombatant) return false;
    const myPos = this.positions[myCombatant.id];
    if (!myPos) return false;
    const chebyshev = Math.max(Math.abs(cell.x - myPos.x), Math.abs(cell.y - myPos.y));
    return chebyshev > 0 && chebyshev <= 6 && !this.tokenAt(cell);
  }

  isEnemyGhostTarget(cell: Cell): boolean {
    for (const action of Object.values(this.enemyPlannedActions)) {
      if (action.type === 'move' && action.targetCell) {
        if (action.targetCell.x === cell.x && action.targetCell.y === cell.y) return true;
      }
    }
    return false;
  }

  isGhostTarget(cell: Cell): boolean {
    const moveAction = this.myPlannedActions.find(a => a.type === 'move');
    if (!moveAction?.targetCell) return false;
    return moveAction.targetCell.x === cell.x && moveAction.targetCell.y === cell.y;
  }

  isTargeted(combatant: Combatant): boolean {
    return this.myPlannedActions.some(a =>
      (a.type === 'melee_attack' || a.type === 'ranged_attack') &&
      a.targetCombatantId === combatant.id
    );
  }

  isDefeating(combatant: Combatant): boolean {
    return this.defeatingNow.has(combatant.id);
  }

  get plannedActionType(): string | null {
    return this.myPlannedActions[0]?.type ?? null;
  }

  get plannedActionSummary(): string {
    const action = this.myPlannedActions[0];
    if (!action) return '';
    if (action.type === 'move' && action.targetCell)
      return `Move to (${action.targetCell.x}, ${action.targetCell.y})`;
    if (action.type === 'melee_attack') return 'Melee Attack';
    if (action.type === 'ranged_attack') return 'Ranged Attack';
    if (action.type === 'defend') return 'Defend';
    return action.type;
  }

  // ----- Interaction -----

  onCellClick(cell: Cell): void {
    if (this.isPlayingBack) return;

    // GM enemy planning mode
    if (this.isGm && this.gmSelectedEnemyId) {
      const enemyId = this.gmSelectedEnemyId;
      const targetCombatant = this.tokenAt(cell);

      if (targetCombatant && targetCombatant.id !== enemyId && !targetCombatant.isDefeated) {
        const enemyPos = this.positions[enemyId];
        const targetPos = this.positions[targetCombatant.id];
        const chebyshev = enemyPos && targetPos
          ? Math.max(Math.abs(targetPos.x - enemyPos.x), Math.abs(targetPos.y - enemyPos.y))
          : 99;
        const attackType = chebyshev <= 1 ? 'melee_attack' : 'ranged_attack';
        const action: PlannedActionEntry = { type: attackType, targetCell: null, targetCombatantId: targetCombatant.id };
        this.store.dispatch(new GmSetEnemyPlan(enemyId, action));
        this.hub.planEnemyAction(this.joinCode, enemyId, { actions: [action] });
        this.store.dispatch(new GmSelectEnemy(null));
      } else if (!targetCombatant && this.isInRange(cell)) {
        const action: PlannedActionEntry = { type: 'move', targetCell: { x: cell.x, y: cell.y }, targetCombatantId: null };
        this.store.dispatch(new GmSetEnemyPlan(enemyId, action));
        this.hub.planEnemyAction(this.joinCode, enemyId, { actions: [action] });
        this.store.dispatch(new GmSelectEnemy(null));
      }
      return;
    }

    // Normal player mode
    if (this.lockedIn) return;

    const myCombatant = this.combatants.find(c => c.userId === this.myUserId);
    if (!myCombatant || myCombatant.isDefeated) return;

    const targetCombatant = this.tokenAt(cell);

    if (targetCombatant && targetCombatant.userId !== this.myUserId && !targetCombatant.isDefeated) {
      const myPos = this.positions[myCombatant.id];
      const targetPos = this.positions[targetCombatant.id];
      const chebyshev = myPos && targetPos
        ? Math.max(Math.abs(targetPos.x - myPos.x), Math.abs(targetPos.y - myPos.y))
        : 99;
      const attackType = chebyshev <= 1 ? 'melee_attack' : 'ranged_attack';

      const action: PlannedActionEntry = { type: attackType, targetCell: null, targetCombatantId: targetCombatant.id };
      this.store.dispatch(new SetMyPlannedAction([action]));
      this.hub.planAction(this.joinCode, { actions: [action] });
    } else if (!targetCombatant && this.isInRange(cell)) {
      if (this.isGhostTarget(cell)) {
        this.cancelPlan();
        return;
      }
      const action: PlannedActionEntry = { type: 'move', targetCell: { x: cell.x, y: cell.y }, targetCombatantId: null };
      this.store.dispatch(new SetMyPlannedAction([action]));
      this.hub.planAction(this.joinCode, { actions: [action] });
    }
  }

  planDefend(): void {
    if (this.lockedIn) return;
    const action: PlannedActionEntry = { type: 'defend', targetCell: null, targetCombatantId: null };
    this.store.dispatch(new SetMyPlannedAction([action]));
    this.hub.planAction(this.joinCode, { actions: [action] });
  }

  cancelPlan(): void {
    this.store.dispatch(new ClearMyPlannedAction());
  }

  onLockIn(): void {
    if (this.lockedIn || !this.myPlannedActions.length) return;
    this.lockedIn = true;
    this.hub.lockIn(this.joinCode);
  }

  onForceResolve(): void {
    this.hub.forceResolve(this.joinCode);
  }

  // ----- Playback animation -----

  private async startPlayback(ticks: TickResult[]): Promise<void> {
    console.log('[grid] startPlayback: starting with', ticks.length, 'ticks');
    this.isPlayingBack = true;
    // Use pre-resolution positions as starting point for animation
    this.animPositions = { ...this.preResolutionPositions };

    const tick = ticks[0]; // 3A: always 1 tick
    console.log('[grid] tick 0:', tick.movements.length, 'movements,', tick.attacks.length, 'attacks');

    // Phase 1: Movement (~1.5s)
    if (tick.movements.length > 0) {
      for (const m of tick.movements) {
        console.log('[grid] movement:', m.combatantName, 'path=', JSON.stringify(m.path), 'final=', JSON.stringify(m.finalPosition));
      }
      const maxPathLen = Math.max(...tick.movements.map(m => m.path.length));
      if (maxPathLen > 1) {
        const stepMs = Math.max(80, Math.floor(1500 / maxPathLen));
        for (let step = 0; step < maxPathLen; step++) {
          if (this.destroyed) return;
          for (const movement of tick.movements) {
            if (step < movement.path.length) {
              this.animPositions = {
                ...this.animPositions,
                [movement.combatantId]: movement.path[step],
              };
            }
          }
          await this.sleep(stepMs);
        }
      }
    }

    // Phase 2: Attacks — show floating damage numbers (~1.5s)
    const newDamageNumbers: DamageNumber[] = [];
    for (const attack of tick.attacks) {
      if (!attack.wasted) {
        newDamageNumbers.push({
          combatantId: attack.targetId,
          text: attack.hit ? `-${attack.damage}` : 'MISS',
          isHit: attack.hit,
          key: Math.random().toString(36).slice(2),
        });
      }
    }
    this.damageNumbers = newDamageNumbers;
    await this.sleep(1200);
    this.damageNumbers = [];

    // Phase 3: Defeat fade (~500ms)
    const newlyDefeated = new Set(
      tick.attacks.filter(a => a.targetDefeated).map(a => a.targetId)
    );
    if (newlyDefeated.size > 0) {
      this.defeatingNow = newlyDefeated;
      await this.sleep(500);
      this.defeatingNow = new Set();
    }

    // Positions already committed to MapState in game-hub.service.ts when ResolutionCompleted arrived.
    // Just clean up playback state so tokenAt() switches back to this.positions (which has final positions).
    this.animPositions = {};
    this.isPlayingBack = false;
    console.log('[grid] playback complete');
  }

  private sleep(ms: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, ms));
  }
}
