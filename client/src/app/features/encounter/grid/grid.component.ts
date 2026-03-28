import { CommonModule } from '@angular/common';
import { Component, Input, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { Store } from '@ngxs/store';
import { Observable, combineLatest, map } from 'rxjs';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { MapState } from '../../../state/map/map.state';
import { SetPendingMove } from '../../../state/map/map.actions';
import { ActionPlanned, PlaybackFinished } from '../../../state/encounter/encounter.actions';
import { GameHubService } from '../../../core/services/game-hub.service';
import { Combatant, PlannedAction } from '../../../state/encounter/encounter.model';
import { Position } from '../../../state/map/map.state.model';
import { ResolutionStepDto } from '../../../contracts/hub-contracts.generated';

interface Cell {
  x: number;
  y: number;
}

@Component({
  selector: 'app-grid',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="grid-wrapper">
      <div class="grid-controls" *ngIf="isPlanning$ | async">
        <button
          class="lock-in-btn"
          (click)="onLockIn()"
          [disabled]="!(hasPlannedAction$ | async) || isLockedIn">
          {{ isLockedIn ? 'Locked In' : 'Lock In' }}
        </button>
        <span class="timer" *ngIf="timerSeconds > 0">{{ timerSeconds }}s</span>
        <span class="phase-label">Planning Phase</span>
      </div>
      <div class="active-turn" *ngIf="(isResolving$ | async) && !(isPlayingBack$ | async)">
        <span class="waiting">Resolving actions...</span>
      </div>
      <div class="active-turn" *ngIf="isPlayingBack$ | async">
        <span class="playback-label">Playing back resolution...</span>
      </div>
      <div
        class="grid"
        [style.grid-template-columns]="gridColumns$ | async">
        <div
          *ngFor="let cell of cells$ | async"
          class="cell"
          [class.pending]="isPendingMove(cell)"
          [class.in-range]="isInRange(cell)"
          (click)="onCellClick(cell)">
          <div
            *ngIf="tokenAt(cell) as combatant"
            class="token"
            [class.defeated-token]="combatant.isDefeated"
            [class.enemy-token]="combatant.userId !== myUserId && !combatant.isDefeated"
            [class.token-animating]="isAnimatingToken(combatant.id)"
            [title]="combatant.characterName + ' (HP: ' + combatant.currentHp + '/' + combatant.maxHp + ')'">
            {{ combatant.characterName[0] }}
            <span class="damage-float" *ngIf="getDamageFloat(combatant.id) as dmg">-{{ dmg }}</span>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .grid-wrapper { margin-top: 16px; }
    .grid-controls { display: flex; align-items: center; gap: 12px; margin-bottom: 8px; }
    .lock-in-btn { background: #2e7d32; color: white; border: none; padding: 8px 16px; cursor: pointer; border-radius: 4px; }
    .lock-in-btn:hover:not(:disabled) { background: #1b5e20; }
    .lock-in-btn:disabled { background: #999; cursor: not-allowed; }
    .timer { color: #c0392b; font-weight: bold; font-size: 1.2em; }
    .phase-label { color: #2e7d32; font-weight: bold; }
    .waiting { color: #666; font-style: italic; }
    .active-turn { margin-bottom: 8px; }
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
    .cell.pending { background: #b3d4f5; }
    .cell.in-range { background: #e0e8d0; }
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
    .token.enemy-token {
      background: #b71c1c;
      cursor: crosshair;
    }
    .token.defeated-token {
      background: #9e9e9e;
      opacity: 0.5;
      cursor: not-allowed;
    }
    .token.token-animating {
      transition: all 0.4s ease;
    }
    .playback-label { color: #2e7d32; font-weight: bold; font-style: italic; }
    .damage-float {
      position: absolute;
      top: -18px;
      left: 50%;
      transform: translateX(-50%);
      color: #c0392b;
      font-size: 14px;
      font-weight: bold;
      animation: float-up 0.8s ease-out forwards;
      pointer-events: none;
      z-index: 10;
    }
    @keyframes float-up {
      0% { opacity: 1; transform: translateX(-50%) translateY(0); }
      100% { opacity: 0; transform: translateX(-50%) translateY(-20px); }
    }
  `],
})
export class GridComponent implements OnInit, OnDestroy {
  @Input() joinCode = '';
  @Input() myUserId = '';

  cells$!: Observable<Cell[]>;
  gridColumns$!: Observable<string>;
  isPlanning$!: Observable<boolean>;
  isResolving$!: Observable<boolean>;
  isPlayingBack$!: Observable<boolean>;
  hasPlannedAction$!: Observable<boolean>;

  isLockedIn = false;
  timerSeconds = 0;

  private combatants: Combatant[] = [];
  private positions: Record<string, Position> = {};
  private pendingMove: Position | null = null;
  private myPlannedAction: PlannedAction | null = null;
  private timerInterval: ReturnType<typeof setInterval> | null = null;
  private planningExpiresAt: number | null = null;
  private animatingTokens = new Set<string>();
  private damageFloats = new Map<string, number>();
  private playbackTimeout: ReturnType<typeof setTimeout> | null = null;

  constructor(private store: Store, private hub: GameHubService, private cdr: ChangeDetectorRef) {}

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
    this.isPlayingBack$ = this.store.select(EncounterState.isPlayingBack);
    this.hasPlannedAction$ = this.store.select(EncounterState.myPlannedAction).pipe(map(a => a !== null));

    // Keep local copies for synchronous access in template methods
    this.store.select(MapState.positions).subscribe(p => this.positions = p);
    this.store.select(MapState.pendingMove).subscribe(p => this.pendingMove = p);
    this.store.select(EncounterState.combatants).subscribe(c => this.combatants = c);
    this.store.select(EncounterState.myPlannedAction).subscribe(a => this.myPlannedAction = a);
    this.store.select(EncounterState.lockedInCombatantIds).subscribe(ids => {
      const myCombatant = this.combatants.find(c => c.userId === this.myUserId);
      this.isLockedIn = !!myCombatant && ids.includes(myCombatant.id);
    });

    // Timer countdown
    this.store.select(EncounterState.planningTimerExpiresAt).subscribe(expiresAt => {
      this.planningExpiresAt = expiresAt;
      this.updateTimer();
    });

    this.timerInterval = setInterval(() => this.updateTimer(), 1000);

    // Playback subscription
    this.store.select(EncounterState.resolutionSteps).subscribe(steps => {
      if (steps.length > 0) {
        this.playbackSteps(steps);
      }
    });
  }

  ngOnDestroy(): void {
    if (this.timerInterval) {
      clearInterval(this.timerInterval);
    }
    if (this.playbackTimeout) {
      clearTimeout(this.playbackTimeout);
    }
  }

  private updateTimer(): void {
    if (this.planningExpiresAt) {
      this.timerSeconds = Math.max(0, Math.ceil((this.planningExpiresAt - Date.now()) / 1000));
    } else {
      this.timerSeconds = 0;
    }
  }

  tokenAt(cell: Cell): Combatant | null {
    for (const combatant of this.combatants) {
      const pos = this.positions[combatant.id];
      if (pos && pos.x === cell.x && pos.y === cell.y)
        return combatant;
    }
    return null;
  }

  isPendingMove(cell: Cell): boolean {
    return !!this.pendingMove && this.pendingMove.x === cell.x && this.pendingMove.y === cell.y;
  }

  isInRange(cell: Cell): boolean {
    const myCombatant = this.combatants.find(c => c.userId === this.myUserId);
    if (!myCombatant || this.isLockedIn || this.myPlannedAction) return false;

    const myPos = this.positions[myCombatant.id];
    if (!myPos) return false;

    const dist = Math.max(Math.abs(cell.x - myPos.x), Math.abs(cell.y - myPos.y));
    return dist > 0 && dist <= 6;
  }

  onCellClick(cell: Cell): void {
    const myCombatant = this.combatants.find(c => c.userId === this.myUserId);
    if (!myCombatant) return;
    if (myCombatant.isDefeated) return;
    if (this.isLockedIn) return;

    // Only allow planning during planning phase
    const roundPhase = this.store.selectSnapshot(EncounterState.roundPhase);
    if (roundPhase !== 'planning') return;

    const targetCombatant = this.tokenAt(cell);

    if (targetCombatant && targetCombatant.userId !== this.myUserId && !targetCombatant.isDefeated) {
      // Click on an enemy token — plan attack
      this.store.dispatch(new ActionPlanned({ actionType: 'attack', targetCombatantId: targetCombatant.id }));
      this.hub.planAction(this.joinCode, 'attack', targetCombatant.id);
    } else if (!targetCombatant) {
      // Empty cell — plan move
      this.store.dispatch(new SetPendingMove({ x: cell.x, y: cell.y }));
      this.store.dispatch(new ActionPlanned({ actionType: 'move', targetX: cell.x, targetY: cell.y }));
      this.hub.planAction(this.joinCode, 'move', undefined, cell.x, cell.y);
    }
  }

  onLockIn(): void {
    this.hub.lockIn(this.joinCode);
  }

  isAnimatingToken(combatantId: string): boolean {
    return this.animatingTokens.has(combatantId);
  }

  getDamageFloat(combatantId: string): number | null {
    return this.damageFloats.get(combatantId) ?? null;
  }

  private playbackSteps(steps: ResolutionStepDto[]): void {
    this.animatingTokens.clear();
    this.damageFloats.clear();
    let delay = 0;
    const stepDelay = 600;

    for (const step of steps) {
      this.playbackTimeout = setTimeout(() => {
        if (step.phase === 'movement' && step.actionType === 'move' && step.newX !== null && step.newY !== null) {
          this.animatingTokens.add(step.actorId);
          this.cdr.detectChanges();
        }
        if (step.phase === 'attack' && step.damage !== null && step.targetId) {
          this.damageFloats.set(step.targetId, step.damage);
          this.cdr.detectChanges();
          // Clear damage float after animation
          setTimeout(() => {
            this.damageFloats.delete(step.targetId!);
            this.cdr.detectChanges();
          }, 800);
        }
      }, delay);
      delay += stepDelay;
    }

    // After all steps, finish playback
    this.playbackTimeout = setTimeout(() => {
      this.animatingTokens.clear();
      this.damageFloats.clear();
      this.isLockedIn = false;
      this.store.dispatch(new PlaybackFinished());
      this.cdr.detectChanges();
    }, delay + 500);
  }
}
