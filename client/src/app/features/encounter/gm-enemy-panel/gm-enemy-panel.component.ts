import { Component, Input, OnChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Store } from '@ngxs/store';
import { Observable } from 'rxjs';
import { Combatant } from '../../../state/encounter/encounter.model';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { GameHubService } from '../../../core/services/game-hub.service';

interface EnemyActionSelection {
  actionType: string;
  targetCombatantId?: string;
  targetX?: number;
  targetY?: number;
}

@Component({
  selector: 'app-gm-enemy-panel',
  imports: [CommonModule, FormsModule],
  templateUrl: './gm-enemy-panel.component.html',
  styleUrl: './gm-enemy-panel.component.scss'
})
export class GmEnemyPanelComponent implements OnChanges {
  @Input() joinCode = '';
  @Input() isGm = false;

  enemies$: Observable<Combatant[]>;
  gmEnemiesLockedIn$: Observable<boolean>;
  enemyPlannedActions$: Observable<Record<string, string>>;
  playerCombatants$: Observable<Combatant[]>;
  isPlanning$: Observable<boolean>;

  actionSelections: Record<string, EnemyActionSelection> = {};

  constructor(private store: Store, private hub: GameHubService) {
    this.enemies$ = this.store.select(EncounterState.enemyCombatants);
    this.gmEnemiesLockedIn$ = this.store.select(EncounterState.gmEnemiesLockedIn);
    this.enemyPlannedActions$ = this.store.select(EncounterState.enemyPlannedActions);
    this.isPlanning$ = this.store.select(EncounterState.isPlanning);
    this.playerCombatants$ = this.store.select(EncounterState.combatants).pipe(
      (source) => new Observable(observer => source.subscribe(combatants =>
        observer.next(combatants.filter(c => c.combatantType === 'player' && !c.isDefeated))
      ))
    );
  }

  ngOnChanges(): void {
    // Reset selections when inputs change
    this.actionSelections = {};
  }

  getSelection(enemyId: string): EnemyActionSelection {
    if (!this.actionSelections[enemyId]) {
      this.actionSelections[enemyId] = { actionType: 'defend' };
    }
    return this.actionSelections[enemyId];
  }

  async planAction(enemyId: string): Promise<void> {
    const sel = this.getSelection(enemyId);
    await this.hub.planEnemyAction(
      this.joinCode,
      enemyId,
      sel.actionType,
      sel.targetCombatantId,
      sel.targetX,
      sel.targetY
    );
  }

  async lockInEnemies(): Promise<void> {
    await this.hub.lockInEnemies(this.joinCode);
  }
}
