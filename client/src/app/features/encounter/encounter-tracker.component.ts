import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { Store } from '@ngxs/store';
import { Observable } from 'rxjs';
import { EncounterState } from '../../state/encounter/encounter.state';
import { Combatant, isControlledBy } from '../../state/encounter/encounter.model';
import { SelectMyCombatant } from '../../state/encounter/encounter.actions';
import { AuthService } from '../../core/services/auth.service';
import { ResolutionPlaybackService } from '../../core/services/resolution-playback.service';
import { ActiveConditionDto } from '../../contracts/hub-contracts.generated';
import { ConditionChipsComponent } from './condition-chips/condition-chips.component';

@Component({
  selector: 'app-encounter-tracker',
  standalone: true,
  imports: [CommonModule, ConditionChipsComponent],
  templateUrl: './encounter-tracker.component.html',
  styleUrl: './encounter-tracker.component.css',
})
export class EncounterTrackerComponent {
  /** Abilities on cooldown (3B.3), by name, for the chips under the combatant's name. */
  cooldownsOf(c: Combatant): { name: string; left: number }[] {
    return Object.entries(c.cooldowns ?? {})
      .filter(([, left]) => left > 0)
      .map(([id, left]) => ({ name: c.abilities.find(a => a.id === id)?.name ?? id, left }));
  }

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

  /** Conditions as far as the round's playback has revealed them (3B.4). */
  conditionsOf(combatant: Combatant): ActiveConditionDto[] {
    return this.playback.conditionsOf(combatant);
  }

  isShownDefeated(combatant: Combatant): boolean {
    return this.playback.isDefeated(combatant);
  }

  /** Standing, downed (with rounds left), stable or dead, as revealed by playback (3B.5). */
  statusOf(combatant: Combatant) {
    return this.playback.statusOf(combatant);
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
