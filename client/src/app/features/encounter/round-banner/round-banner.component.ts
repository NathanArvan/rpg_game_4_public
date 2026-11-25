import { CommonModule } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { Store } from '@ngxs/store';
import { Observable, interval, of } from 'rxjs';
import { map, startWith, switchMap } from 'rxjs/operators';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { EncounterOutcome } from '../../../state/encounter/encounter.model';
import { ResolutionPlaybackService } from '../../../core/services/resolution-playback.service';
import { PlaybackPhase } from '../../../core/services/playback-timeline';

const PHASE_LABELS: Record<PlaybackPhase, string> = {
  movement: 'Movement',
  attacks: 'Attacks',
  defeats: 'Defeats',
  revives: 'Back up',
  effects: 'Effects',
  periodic: 'End of round',
  bleedout: 'End of round',
  expiries: 'End of round',
  unused: 'End of round',
  idle: '',
};

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
  outcome$: Observable<EncounterOutcome | null>;

  private readonly playback = inject(ResolutionPlaybackService);
  readonly tick = this.playback.tick;
  readonly tickCount = this.playback.tickCount;
  readonly phaseLabel = computed(() => {
    const phase = this.playback.phase();
    return phase ? PHASE_LABELS[phase] : '';
  });

  constructor(private store: Store) {
    this.isPlanning$ = store.select(EncounterState.isPlanning);
    this.isResolving$ = store.select(EncounterState.isResolving);
    this.roundNumber$ = store.select(EncounterState.roundNumber);
    this.lockedIn$ = store.select(EncounterState.lockedInCombatantNames);
    this.outcome$ = store.select(EncounterState.outcome);

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
