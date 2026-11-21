import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { Store } from '@ngxs/store';
import { Observable } from 'rxjs';
import { EncounterState } from '../../state/encounter/encounter.state';
import { Combatant, isControlledBy } from '../../state/encounter/encounter.model';
import { SelectMyCombatant } from '../../state/encounter/encounter.actions';
import { AuthService } from '../../core/services/auth.service';
import { ResolutionPlaybackService } from '../../core/services/resolution-playback.service';

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
  private _selectedId: string | null = null;

  constructor(private store: Store, private auth: AuthService, private playback: ResolutionPlaybackService) {
    this.combatants$ = store.select(EncounterState.combatants);
    this.isPlanning$ = store.select(EncounterState.isPlanning);
    this.isResolving$ = store.select(EncounterState.isResolving);
    this.roundNumber$ = store.select(EncounterState.roundNumber);
    this.lockedIn$ = store.select(EncounterState.lockedInCombatantNames);
    this.lockedIn$.subscribe(names => this._lockedInNames = names);
    store.select(EncounterState.selectedCombatantId).subscribe(id => this._selectedId = id);
  }

  /** HP as far as the round's playback has revealed it (the store already holds the final HP). */
  hpOf(combatant: Combatant): number {
    return this.playback.hpOf(combatant);
  }

  isShownDefeated(combatant: Combatant): boolean {
    return this.playback.isDefeated(combatant);
  }

  isMine(combatant: Combatant): boolean {
    return isControlledBy(combatant, this.auth.getUserId());
  }

  isSelected(combatant: Combatant): boolean {
    return this._selectedId === combatant.id;
  }

  /** In a Quick Battle, clicking one of your characters' rows picks who you're planning for. */
  select(combatant: Combatant): void {
    if (this.isMine(combatant) && !combatant.isDefeated)
      this.store.dispatch(new SelectMyCombatant(combatant.id));
  }

  isLockedIn(combatant: Combatant): boolean {
    return this._lockedInNames.includes(combatant.characterName);
  }
}
