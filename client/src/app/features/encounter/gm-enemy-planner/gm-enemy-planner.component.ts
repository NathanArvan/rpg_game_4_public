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
import { DEFAULT_AP_COSTS, apSpent, canAfford } from '../../../state/encounter/plan-queue';

@Component({
  selector: 'app-gm-enemy-planner',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './gm-enemy-planner.component.html',
  styleUrl: './gm-enemy-planner.component.css',
})
export class GmEnemyPlannerComponent {
  @Input() joinCode = '';

  monsters$: Observable<Combatant[]>;
  isPlanning$: Observable<boolean>;
  gmSelectedEnemyId$: Observable<string | null>;

  private enemyPlannedActions: Record<string, PlannedActionEntry[]> = {};
  private apCosts: Record<string, number> = { ...DEFAULT_AP_COSTS };

  lockedIn = false;

  constructor(private store: Store, private hub: GameHubService) {
    this.monsters$ = store.select(EncounterState.monsterCombatants);
    this.isPlanning$ = store.select(EncounterState.isPlanning);
    this.gmSelectedEnemyId$ = store.select(EncounterState.gmSelectedEnemyId);

    store.select(EncounterState.enemyPlannedActions).subscribe(plans => {
      this.enemyPlannedActions = plans;
    });
    store.select(EncounterState.apCosts).subscribe(costs => this.apCosts = costs);
    // Reset lock-in when a new planning phase starts
    store.select(EncounterState.isPlanning).subscribe(planning => {
      if (planning) this.lockedIn = false;
    });
  }

  /** The monster's planned queue, or null if the GM hasn't planned it. */
  getPlan(combatantId: string): PlannedActionEntry[] | null {
    const queue = this.enemyPlannedActions[combatantId];
    return queue && queue.length > 0 ? queue : null;
  }

  queueSummary(queue: PlannedActionEntry[]): string {
    return queue.map(a => this.planSummary(a)).join(' · ');
  }

  apLabel(enemy: Combatant): string {
    return `${apSpent(this.getPlan(enemy.id) ?? [], this.apCosts)}/${enemy.maxAp} AP`;
  }

  planSummary(action: PlannedActionEntry): string {
    if (action.type === 'move' && action.targetCell)
      return `→ Move (${action.targetCell.x},${action.targetCell.y})`;
    if (action.type === 'melee_attack') return '⚔ Melee Attack';
    if (action.type === 'ranged_attack') return '🏹 Ranged Attack';
    if (action.type === 'defend') return '🛡 Defend';
    return action.type;
  }

  onSelectEnemy(combatantId: string): void {
    const current = this.store.selectSnapshot(EncounterState.gmSelectedEnemyId);
    // Clicking the already-selected enemy deselects it
    this.store.dispatch(new GmSelectEnemy(current === combatantId ? null : combatantId));
  }

  /** Appends a Defend to the monster's queue if its AP allows. */
  onDefend(enemy: Combatant): void {
    const queue = this.getPlan(enemy.id) ?? [];
    if (!canAfford(queue, 'defend', enemy.maxAp, this.apCosts)) return;
    const next = [...queue, { type: 'defend', targetCell: null, targetCombatantId: null }];
    this.store.dispatch(new GmSetEnemyPlan(enemy.id, next));
    this.hub.planEnemyAction(this.joinCode, enemy.id, { actions: next });
  }

  /** Clears the monster's queue here and on the server (unplanned monsters defend on lock-in). */
  onClearPlan(combatantId: string): void {
    this.store.dispatch(new GmClearEnemyPlan(combatantId));
    this.hub.planEnemyAction(this.joinCode, combatantId, { actions: [] });
  }

  onLockInEnemies(): void {
    if (this.lockedIn) return;
    this.lockedIn = true;
    this.store.dispatch(new GmSelectEnemy(null));
    this.hub.lockInEnemies(this.joinCode);
  }
}
