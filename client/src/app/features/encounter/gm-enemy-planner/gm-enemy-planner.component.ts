import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Store } from '@ngxs/store';
import { Observable } from 'rxjs';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { GmClearEnemyPlan, GmSelectEnemy, GmSetEnemyPlan } from '../../../state/encounter/encounter.actions';
import { Combatant } from '../../../state/encounter/encounter.model';
import { GameHubService } from '../../../core/services/game-hub.service';
import { PlannedActionEntry } from '../../../contracts/hub-contracts.generated';

@Component({
  selector: 'app-gm-enemy-planner',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="enemy-planner">
      <h3>Enemy Control</h3>

      <!-- Add Monster -->
      <div class="add-monster-row">
        <input
          type="text"
          [(ngModel)]="newMonsterName"
          placeholder="Monster name"
          class="monster-name-input"
          (keyup.enter)="onAddMonster()" />
        <button class="add-btn" (click)="onAddMonster()" [disabled]="!newMonsterName.trim()">
          + Add Monster
        </button>
      </div>

      <!-- Enemy Planning Panel (planning phase only) -->
      <ng-container *ngIf="isPlanning$ | async">
        <div class="enemies-header">
          <span>Enemies</span>
          <span *ngIf="gmSelectedEnemyId$ | async as selId" class="selection-hint">
            Click a cell or token on the grid to plan
          </span>
        </div>

        <div class="enemy-list">
          <div
            *ngFor="let enemy of monsters$ | async"
            class="enemy-row"
            [class.selected]="(gmSelectedEnemyId$ | async) === enemy.id">

            <div class="enemy-info" (click)="onSelectEnemy(enemy.id)">
              <span class="enemy-name">{{ enemy.characterName }}</span>
              <span class="enemy-hp">{{ enemy.currentHp }}/{{ enemy.maxHp }} HP</span>
            </div>

            <div class="enemy-plan-badge" *ngIf="getPlan(enemy.id) as plan">
              {{ planSummary(plan) }}
            </div>
            <div class="enemy-plan-badge empty" *ngIf="!getPlan(enemy.id)">
              (will defend)
            </div>

            <div class="enemy-actions">
              <button class="defend-btn" (click)="onDefend(enemy.id)" title="Plan Defend">
                🛡
              </button>
              <button
                class="clear-btn"
                *ngIf="getPlan(enemy.id)"
                (click)="onClearPlan(enemy.id)"
                title="Clear plan">
                ✕
              </button>
            </div>
          </div>

          <div class="no-enemies" *ngIf="(monsters$ | async)?.length === 0">
            No enemies. Add one above.
          </div>
        </div>

        <button
          class="lock-in-enemies-btn"
          [disabled]="lockedIn"
          (click)="onLockInEnemies()">
          {{ lockedIn ? 'Enemies Locked In ✓' : 'Lock In Enemies' }}
        </button>
      </ng-container>
    </div>
  `,
  styles: [`
    .enemy-planner {
      background: #2d2d2d;
      color: #f0e6d3;
      border-radius: 6px;
      padding: 12px;
      margin-top: 12px;
      min-width: 240px;
    }
    h3 { margin: 0 0 10px; font-size: 1em; color: #ff8a65; }

    .add-monster-row {
      display: flex;
      gap: 6px;
      margin-bottom: 10px;
    }
    .monster-name-input {
      flex: 1;
      padding: 5px 8px;
      border: 1px solid #555;
      border-radius: 4px;
      background: #3a3a3a;
      color: #f0e6d3;
      font-size: 0.85em;
    }
    .add-btn {
      background: #e53935;
      color: white;
      border: none;
      padding: 5px 10px;
      border-radius: 4px;
      cursor: pointer;
      font-size: 0.85em;
      white-space: nowrap;
    }
    .add-btn:disabled { background: #555; cursor: not-allowed; }

    .enemies-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      font-size: 0.8em;
      color: #aaa;
      margin-bottom: 6px;
    }
    .selection-hint { color: #ffb74d; font-style: italic; }

    .enemy-list { display: flex; flex-direction: column; gap: 4px; margin-bottom: 10px; }

    .enemy-row {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 6px 8px;
      background: #3a3a3a;
      border-radius: 4px;
      cursor: pointer;
      border: 1px solid transparent;
    }
    .enemy-row:hover { background: #444; }
    .enemy-row.selected {
      border-color: #ffb74d;
      background: #4a3a2a;
    }

    .enemy-info { flex: 1; display: flex; flex-direction: column; }
    .enemy-name { font-size: 0.9em; font-weight: bold; }
    .enemy-hp { font-size: 0.75em; color: #aaa; }

    .enemy-plan-badge {
      font-size: 0.75em;
      color: #ff8a65;
      max-width: 90px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .enemy-plan-badge.empty { color: #666; }

    .enemy-actions { display: flex; gap: 4px; }
    .defend-btn, .clear-btn {
      background: none;
      border: 1px solid #555;
      border-radius: 3px;
      padding: 2px 5px;
      cursor: pointer;
      font-size: 0.8em;
      color: #ccc;
    }
    .defend-btn:hover { background: #555; }
    .clear-btn:hover { background: #b71c1c; color: white; border-color: #b71c1c; }

    .no-enemies { font-size: 0.8em; color: #666; text-align: center; padding: 8px; }

    .lock-in-enemies-btn {
      width: 100%;
      padding: 8px;
      background: #c62828;
      color: white;
      border: none;
      border-radius: 4px;
      cursor: pointer;
      font-weight: bold;
    }
    .lock-in-enemies-btn:disabled { background: #555; cursor: not-allowed; }
    .lock-in-enemies-btn:not(:disabled):hover { background: #b71c1c; }
  `],
})
export class GmEnemyPlannerComponent {
  @Input() joinCode = '';

  monsters$: Observable<Combatant[]>;
  isPlanning$: Observable<boolean>;
  gmSelectedEnemyId$: Observable<string | null>;

  private enemyPlannedActions: Record<string, PlannedActionEntry> = {};

  newMonsterName = '';
  lockedIn = false;

  constructor(private store: Store, private hub: GameHubService) {
    this.monsters$ = store.select(EncounterState.monsterCombatants);
    this.isPlanning$ = store.select(EncounterState.isPlanning);
    this.gmSelectedEnemyId$ = store.select(EncounterState.gmSelectedEnemyId);

    store.select(EncounterState.enemyPlannedActions).subscribe(plans => {
      this.enemyPlannedActions = plans;
    });
    // Reset lock-in when a new planning phase starts
    store.select(EncounterState.isPlanning).subscribe(planning => {
      if (planning) this.lockedIn = false;
    });
  }

  getPlan(combatantId: string): PlannedActionEntry | null {
    return this.enemyPlannedActions[combatantId] ?? null;
  }

  planSummary(action: PlannedActionEntry): string {
    if (action.type === 'move' && action.targetCell)
      return `→ Move (${action.targetCell.x},${action.targetCell.y})`;
    if (action.type === 'melee_attack') return '⚔ Melee Attack';
    if (action.type === 'ranged_attack') return '🏹 Ranged Attack';
    if (action.type === 'defend') return '🛡 Defend';
    return action.type;
  }

  onAddMonster(): void {
    const name = this.newMonsterName.trim();
    if (!name) return;
    this.hub.addMonster(this.joinCode, name);
    this.newMonsterName = '';
  }

  onSelectEnemy(combatantId: string): void {
    const current = this.store.selectSnapshot(EncounterState.gmSelectedEnemyId);
    // Clicking the already-selected enemy deselects it
    this.store.dispatch(new GmSelectEnemy(current === combatantId ? null : combatantId));
  }

  onDefend(combatantId: string): void {
    const action: PlannedActionEntry = { type: 'defend', targetCell: null, targetCombatantId: null };
    this.store.dispatch(new GmSetEnemyPlan(combatantId, action));
    this.hub.planEnemyAction(this.joinCode, combatantId, { actions: [action] });
    // Deselect if this enemy was selected
    const selected = this.store.selectSnapshot(EncounterState.gmSelectedEnemyId);
    if (selected === combatantId) this.store.dispatch(new GmSelectEnemy(null));
  }

  onClearPlan(combatantId: string): void {
    this.store.dispatch(new GmClearEnemyPlan(combatantId));
    // No server call needed — server defaults to defend on lock-in
  }

  onLockInEnemies(): void {
    if (this.lockedIn) return;
    this.lockedIn = true;
    this.store.dispatch(new GmSelectEnemy(null));
    this.hub.lockInEnemies(this.joinCode);
  }
}
