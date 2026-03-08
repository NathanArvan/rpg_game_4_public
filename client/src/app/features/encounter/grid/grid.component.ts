import { CommonModule } from '@angular/common';
import { Component, Input, OnInit } from '@angular/core';
import { Store } from '@ngxs/store';
import { Observable, combineLatest, map } from 'rxjs';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { MapState } from '../../../state/map/map.state';
import { SetPendingMove } from '../../../state/map/map.actions';
import { GameHubService } from '../../../core/services/game-hub.service';
import { Combatant } from '../../../state/encounter/encounter.model';
import { Position } from '../../../state/map/map.state.model';

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
      <div class="grid-controls" *ngIf="isMyTurn$ | async">
        <button class="end-turn-btn" (click)="onEndTurn()">End Turn</button>
        <span class="turn-indicator">Your turn!</span>
      </div>
      <div class="active-turn" *ngIf="!(isMyTurn$ | async)">
        <span class="waiting">Waiting for {{ activeTurnName$ | async }}...</span>
      </div>
      <div
        class="grid"
        [style.grid-template-columns]="gridColumns$ | async">
        <div
          *ngFor="let cell of cells$ | async"
          class="cell"
          [class.pending]="isPendingMove(cell)"
          (click)="onCellClick(cell)">
          <div
            *ngIf="tokenAt(cell) as combatant"
            class="token"
            [class.active-token]="combatant.id === (activeTurnCombatantId$ | async)"
            [class.defeated-token]="combatant.isDefeated"
            [class.enemy-token]="combatant.userId !== myUserId && !combatant.isDefeated"
            [title]="combatant.characterName + ' (HP: ' + combatant.currentHp + '/' + combatant.maxHp + ')'">
            {{ combatant.characterName[0] }}
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .grid-wrapper { margin-top: 16px; }
    .grid-controls { display: flex; align-items: center; gap: 12px; margin-bottom: 8px; }
    .end-turn-btn { background: #2e7d32; color: white; border: none; padding: 8px 16px; cursor: pointer; border-radius: 4px; }
    .end-turn-btn:hover { background: #1b5e20; }
    .turn-indicator { color: #2e7d32; font-weight: bold; }
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
    .token.active-token {
      background: #e53935;
      box-shadow: 0 0 0 2px #ffeb3b;
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
  `],
})
export class GridComponent implements OnInit {
  @Input() joinCode = '';
  @Input() myUserId = '';

  cells$!: Observable<Cell[]>;
  gridColumns$!: Observable<string>;
  activeTurnCombatantId$!: Observable<string | null>;
  isMyTurn$!: Observable<boolean>;
  activeTurnName$!: Observable<string | null>;

  private combatants: Combatant[] = [];
  private positions: Record<string, Position> = {};
  private pendingMove: Position | null = null;

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

    this.activeTurnCombatantId$ = this.store.select(EncounterState.activeTurnCombatantId);

    this.isMyTurn$ = combineLatest([
      this.activeTurnCombatantId$,
      this.store.select(EncounterState.combatants),
    ]).pipe(
      map(([activeId, combatants]) => {
        this.combatants = combatants;
        const myCombatant = combatants.find(c => c.userId === this.myUserId);
        return !!myCombatant && myCombatant.id === activeId && !myCombatant.isDefeated;
      })
    );

    this.activeTurnName$ = combineLatest([
      this.activeTurnCombatantId$,
      this.store.select(EncounterState.combatants),
    ]).pipe(
      map(([activeId, combatants]) => {
        const active = combatants.find(c => c.id === activeId);
        return active?.characterName ?? null;
      })
    );

    // Keep local copies for synchronous access in template methods
    this.store.select(MapState.positions).subscribe(p => this.positions = p);
    this.store.select(MapState.pendingMove).subscribe(p => this.pendingMove = p);
    this.store.select(EncounterState.combatants).subscribe(c => this.combatants = c);
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

  onCellClick(cell: Cell): void {
    const myCombatant = this.combatants.find(c => c.userId === this.myUserId);
    if (!myCombatant) return;

    const state = this.store.snapshot();
    const activeTurnId = state.encounter?.activeTurnCombatantId;
    if (myCombatant.id !== activeTurnId) return; // not my turn
    if (myCombatant.isDefeated) return;

    const targetCombatant = this.tokenAt(cell);

    if (targetCombatant && targetCombatant.userId !== this.myUserId && !targetCombatant.isDefeated) {
      // Click on an enemy token — declare attack
      this.hub.declareAttack(this.joinCode, targetCombatant.id);
    } else if (!targetCombatant) {
      // Empty cell — move
      this.store.dispatch(new SetPendingMove({ x: cell.x, y: cell.y }));
      this.hub.moveToken(this.joinCode, myCombatant.id, cell.x, cell.y);
    }
  }

  onEndTurn(): void {
    this.hub.endTurn(this.joinCode);
  }
}
