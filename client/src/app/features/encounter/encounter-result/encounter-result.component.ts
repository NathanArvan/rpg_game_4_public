import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { Store } from '@ngxs/store';
import { Observable } from 'rxjs';
import { ClearEncounter } from '../../../state/encounter/encounter.actions';
import { EncounterOutcome } from '../../../state/encounter/encounter.model';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { ClearFeed } from '../../../state/feed/feed.actions';

/** Overlay shown over the battlefield once the server broadcasts EncounterEnded. */
@Component({
  selector: 'app-encounter-result',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './encounter-result.component.html',
  styleUrl: './encounter-result.component.scss',
})
export class EncounterResultComponent {
  @Input() isGm = false;

  outcome$: Observable<EncounterOutcome | null>;

  constructor(private store: Store) {
    this.outcome$ = store.select(EncounterState.outcome);
  }

  /** GM sees the result from above; players see it from the party's side. */
  headline(outcome: EncounterOutcome): string {
    if (outcome.winningSide === 'draw') return 'Draw';
    if (this.isGm) return outcome.winningSide === 'party' ? 'Party wins' : 'Enemies win';
    return outcome.winningSide === 'party' ? 'Victory' : 'Defeat';
  }

  tone(outcome: EncounterOutcome): 'win' | 'loss' | 'draw' {
    if (outcome.winningSide === 'draw' || this.isGm) return 'draw';
    return outcome.winningSide === 'party' ? 'win' : 'loss';
  }

  returnToLobby(): void {
    this.store.dispatch([new ClearEncounter(), new ClearFeed()]);
  }
}
