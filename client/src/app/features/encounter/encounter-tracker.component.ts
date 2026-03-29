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
      <div class="header">
        <h2>
          <ng-container *ngIf="isPlanning$ | async">Planning — Round {{ roundNumber$ | async }}</ng-container>
          <ng-container *ngIf="isResolving$ | async">Resolving...</ng-container>
          <ng-container *ngIf="!(isPlanning$ | async) && !(isResolving$ | async)">Combatants</ng-container>
        </h2>
      </div>

      <ul class="lock-in-list" *ngIf="isPlanning$ | async">
        <li *ngFor="let name of lockedIn$ | async">
          ✓ {{ name }}
        </li>
        <li *ngIf="!(lockedIn$ | async)?.length" class="waiting">Waiting for players to lock in...</li>
      </ul>

      <table>
        <thead>
          <tr>
            <th>Name</th>
            <th>HP</th>
            <th>Status</th>
          </tr>
        </thead>
        <tbody>
          <tr *ngFor="let c of combatants$ | async" [class.defeated]="c.isDefeated">
            <td><strong [class.strikethrough]="c.isDefeated">{{ c.characterName }}</strong></td>
            <td>
              <span *ngIf="!c.isDefeated">{{ c.currentHp }}/{{ c.maxHp }}</span>
            </td>
            <td>
              <span *ngIf="c.isDefeated" class="defeated-label">DEFEATED</span>
              <span *ngIf="!c.isDefeated && isLockedIn(c)" class="locked-in-badge">✓ Ready</span>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  `,
  styles: [`
    .encounter-tracker { margin-top: 24px; padding: 16px; background: #f9f0e0; border: 1px solid #c8a96e; border-radius: 4px; }
    .header { display: flex; justify-content: space-between; align-items: center; }
    h2 { margin: 0 0 12px; }
    table { width: 100%; border-collapse: collapse; }
    th, td { padding: 6px 8px; text-align: left; border-bottom: 1px solid #ddd; }
    th { font-weight: bold; color: #555; }
    .defeated { opacity: 0.5; }
    .strikethrough { text-decoration: line-through; }
    .defeated-label { color: #888; font-size: 0.85em; font-style: italic; }
    .locked-in-badge { color: #2e7d32; font-size: 0.85em; font-weight: bold; }
    .lock-in-list { list-style: none; padding: 0; margin: 0 0 12px; color: #2e7d32; font-size: 0.9em; }
    .lock-in-list li.waiting { color: #888; font-style: italic; }
  `],
})
export class EncounterTrackerComponent {
  combatants$: Observable<Combatant[]>;
  isPlanning$: Observable<boolean>;
  isResolving$: Observable<boolean>;
  roundNumber$: Observable<number>;
  lockedIn$: Observable<string[]>;

  private _lockedInNames: string[] = [];

  constructor(private store: Store) {
    this.combatants$ = store.select(EncounterState.combatants);
    this.isPlanning$ = store.select(EncounterState.isPlanning);
    this.isResolving$ = store.select(EncounterState.isResolving);
    this.roundNumber$ = store.select(EncounterState.roundNumber);
    this.lockedIn$ = store.select(EncounterState.lockedInCombatantNames);
    this.lockedIn$.subscribe(names => this._lockedInNames = names);
  }

  isLockedIn(combatant: Combatant): boolean {
    return this._lockedInNames.includes(combatant.characterName);
  }
}
