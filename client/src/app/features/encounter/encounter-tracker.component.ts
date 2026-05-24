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
  templateUrl: './encounter-tracker.component.html',
  styleUrl: './encounter-tracker.component.css',
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
