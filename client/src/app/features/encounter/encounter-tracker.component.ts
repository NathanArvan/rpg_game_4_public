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
      <h2>Initiative Order</h2>
      <ol>
        <li *ngFor="let c of combatants$ | async" [class.defeated]="c.isDefeated">
          <strong [class.strikethrough]="c.isDefeated">{{ c.characterName }}</strong> —
          {{ c.initiative }}
          <span class="roll-detail">(rolled {{ c.initiativeRoll }} + {{ c.dexModifier }} DEX)</span>
          <span class="hp" *ngIf="!c.isDefeated"> · HP: {{ c.currentHp }}/{{ c.maxHp }}</span>
          <span class="defeated-label" *ngIf="c.isDefeated"> · DEFEATED</span>
        </li>
      </ol>
    </div>
  `,
  styles: [`
    .encounter-tracker { margin-top: 24px; padding: 16px; background: #f9f0e0; border: 1px solid #c8a96e; border-radius: 4px; }
    ol { padding-left: 20px; }
    li { padding: 4px 0; font-size: 1.05em; }
    .roll-detail { color: #666; font-size: 0.9em; margin-left: 8px; }
    .hp { color: #c0392b; font-size: 0.9em; margin-left: 8px; }
    .defeated { opacity: 0.5; }
    .strikethrough { text-decoration: line-through; }
    .defeated-label { color: #888; font-size: 0.85em; font-style: italic; margin-left: 8px; }
  `],
})
export class EncounterTrackerComponent {
  combatants$: Observable<Combatant[]>;

  constructor(private store: Store) {
    this.combatants$ = store.select(EncounterState.combatants);
  }
}
