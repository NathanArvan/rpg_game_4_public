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
  templateUrl: './gm-enemy-planner.component.html',
  styleUrl: './gm-enemy-planner.component.css',
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
