import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Store } from '@ngxs/store';
import { map } from 'rxjs';
import { ResolutionPlaybackService } from '../../../core/services/resolution-playback.service';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { Lane, LaneSlot, buildLanes } from './round-lanes';

/**
 * The round as a table: one row per combatant, one column per tick, so it's visible that
 * everyone's 1st action resolves together, then everyone's 2nd, and so on. Follows playback
 * as it runs; afterwards it stays up as "Last round", and clicking a tick shows its details.
 */
@Component({
  selector: 'app-round-timeline',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './round-timeline.component.html',
  styleUrl: './round-timeline.component.scss',
})
export class RoundTimelineComponent {
  private readonly playback = inject(ResolutionPlaybackService);
  private readonly order = toSignal(
    inject(Store).select(EncounterState.combatants).pipe(map(cs => cs.map(c => c.id))),
    { initialValue: [] as string[] });

  readonly playing = this.playback.playing;
  readonly resolution = this.playback.resolution;
  readonly roundNumber = computed(() => (this.resolution()?.newRoundNumber ?? 1) - 1);

  readonly lanes = computed<Lane[]>(() => {
    const payload = this.resolution();
    return payload ? buildLanes(payload, this.order()) : [];
  });

  readonly ticks = computed(() => (this.resolution()?.ticks ?? []).map((_, i) => i));

  /** A tick picked for review once playback is over (null = the last tick). */
  private readonly picked = signal<{ payload: unknown; tick: number } | null>(null);

  /** 0-based tick being shown: the one playing, else the one picked, else the last. */
  readonly shownTick = computed(() => {
    if (this.playing()) return Math.max(0, this.playback.tick() - 1);
    const picked = this.picked();
    if (picked && picked.payload === this.resolution()) return picked.tick;
    return Math.max(0, this.ticks().length - 1);
  });

  /** One line per combatant for the shown tick: what happened, or (not played yet) what's planned. */
  readonly detailLines = computed(() => {
    const tick = this.shownTick();
    return this.lanes()
      .map(lane => lane.slots[tick])
      .filter(s => s && s.kind !== 'idle')
      .map(s => this.revealed(s) ? s.detail : `${this.laneName(s)}: ${s.label}…`);
  });

  pickTick(tick: number): void {
    if (!this.playing()) this.picked.set({ payload: this.resolution(), tick });
  }

  /**
   * Whether a slot's outcome has been shown yet: earlier ticks have; in the tick playing, moves
   * show as they walk and the rest once its attacks land.
   */
  revealed(slot: LaneSlot): boolean {
    if (!this.playing()) return true;
    const current = this.playback.tick() - 1;
    if (slot.tick !== current) return slot.tick < current;
    return slot.kind === 'move' || this.playback.phase() !== 'movement';
  }

  slotClasses(slot: LaneSlot): Record<string, boolean> {
    const revealed = this.revealed(slot);
    return {
      [`slot-${slot.kind}`]: true,
      [`outcome-${slot.outcome}`]: revealed,
      pending: !revealed,
      current: slot.tick === this.shownTick(),
    };
  }

  slotTitle(slot: LaneSlot): string {
    return this.revealed(slot) ? `Tick ${slot.tick + 1}: ${slot.detail}` : `Tick ${slot.tick + 1}: ${slot.label}`;
  }

  trackLane(_: number, lane: Lane): string {
    return lane.combatantId;
  }

  private laneName(slot: LaneSlot): string {
    return this.lanes().find(l => l.slots.includes(slot))?.name ?? '';
  }
}
