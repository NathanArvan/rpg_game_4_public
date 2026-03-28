import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { Store } from '@ngxs/store';
import { Observable } from 'rxjs';
import { EncounterState } from '../../state/encounter/encounter.state';
import { Combatant } from '../../state/encounter/encounter.model';

@Component({
  selector: 'app-encounter-tracker',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="encounter-tracker">
      <h2>
        Round {{ roundNumber$ | async }}
        <span class="phase-badge" [class.planning]="(isPlanning$ | async)" [class.resolving]="(isResolving$ | async)">
          {{ (roundPhase$ | async) }}
        </span>
      </h2>
      <ul>
        <li *ngFor="let c of combatants$ | async" [class.defeated]="c.isDefeated">
          <span class="lock-status">
            <span *ngIf="isLockedIn(c.id)" class="locked-icon" title="Locked in">&#10003;</span>
            <span *ngIf="!isLockedIn(c.id) && (isPlanning$ | async) && !c.isDefeated" class="planning-icon" title="Still planning">&#8987;</span>
          </span>
          <strong [class.strikethrough]="c.isDefeated">{{ c.characterName }}</strong>
          <span class="hp" *ngIf="!c.isDefeated"> · HP: {{ c.currentHp }}/{{ c.maxHp }}</span>
          <span class="defeated-label" *ngIf="c.isDefeated"> · DEFEATED</span>
        </li>
      </ul>
    </div>
  `,
  styles: [`
    .encounter-tracker { margin-top: 24px; padding: 16px; background: #f9f0e0; border: 1px solid #c8a96e; border-radius: 4px; }
    ul { padding-left: 20px; list-style: none; }
    li { padding: 4px 0; font-size: 1.05em; display: flex; align-items: center; gap: 8px; }
    .hp { color: #c0392b; font-size: 0.9em; margin-left: 8px; }
    .defeated { opacity: 0.5; }
    .strikethrough { text-decoration: line-through; }
    .defeated-label { color: #888; font-size: 0.85em; font-style: italic; margin-left: 8px; }
    .phase-badge { font-size: 0.7em; padding: 2px 8px; border-radius: 4px; margin-left: 8px; text-transform: capitalize; }
    .phase-badge.planning { background: #fff3cd; color: #856404; }
    .phase-badge.resolving { background: #d4edda; color: #155724; }
    .lock-status { width: 20px; text-align: center; }
    .locked-icon { color: #2e7d32; font-weight: bold; }
    .planning-icon { color: #856404; }
  `],
})
export class EncounterTrackerComponent {
  combatants$: Observable<Combatant[]>;
  roundNumber$: Observable<number>;
  roundPhase$: Observable<string | null>;
  isPlanning$: Observable<boolean>;
  isResolving$: Observable<boolean>;

  private lockedInIds: string[] = [];

  constructor(private store: Store) {
    this.combatants$ = store.select(EncounterState.combatants);
    this.roundNumber$ = store.select(EncounterState.roundNumber);
    this.roundPhase$ = store.select(EncounterState.roundPhase);
    this.isPlanning$ = store.select(EncounterState.isPlanning);
    this.isResolving$ = store.select(EncounterState.isResolving);
    store.select(EncounterState.lockedInCombatantIds).subscribe(ids => this.lockedInIds = ids);
  }

  isLockedIn(combatantId: string): boolean {
    return this.lockedInIds.includes(combatantId);
  }
}
