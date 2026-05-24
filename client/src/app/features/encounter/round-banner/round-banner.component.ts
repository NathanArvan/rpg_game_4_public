import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { Store } from '@ngxs/store';
import { Observable, interval, of } from 'rxjs';
import { map, startWith, switchMap } from 'rxjs/operators';
import { EncounterState } from '../../../state/encounter/encounter.state';

@Component({
  selector: 'app-round-banner',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './round-banner.component.html',
  styleUrl: './round-banner.component.scss',
})
export class RoundBannerComponent {
  isPlanning$: Observable<boolean>;
  isResolving$: Observable<boolean>;
  roundNumber$: Observable<number>;
  lockedIn$: Observable<string[]>;
  timerSeconds$: Observable<number | null>;

  constructor(private store: Store) {
    this.isPlanning$ = store.select(EncounterState.isPlanning);
    this.isResolving$ = store.select(EncounterState.isResolving);
    this.roundNumber$ = store.select(EncounterState.roundNumber);
    this.lockedIn$ = store.select(EncounterState.lockedInCombatantNames);

    this.timerSeconds$ = store.select(EncounterState.planningTimerExpiresAt).pipe(
      switchMap(expiresAt => {
        if (!expiresAt) return of(null);
        return interval(1000).pipe(
          startWith(0),
          map(() => Math.max(0, Math.floor((new Date(expiresAt).getTime() - Date.now()) / 1000)))
        );
      })
    );
  }
}
