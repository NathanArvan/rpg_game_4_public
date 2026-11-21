import { Injectable, signal } from '@angular/core';
import { Actions, ofActionDispatched, Store } from '@ngxs/store';
import { ResolutionPayload } from '../../contracts/hub-contracts.generated';
import { ClearEncounter, EncounterStarted, ResolutionCompleted } from '../../state/encounter/encounter.actions';
import { Combatant } from '../../state/encounter/encounter.model';
import { EncounterState } from '../../state/encounter/encounter.state';
import { ApplyResolutionPositions } from '../../state/map/map.actions';
import { MapState } from '../../state/map/map.state';
import { Position } from '../../state/map/map.state.model';
import { AddFeedBatch } from '../../state/feed/feed.actions';
import {
  AuraKind, FloatCue, PLAYBACK_TIMING, PlaybackBeat, PlaybackPhase, PlaybackTiming,
  buildTimeline, positionsAfterResolution, positionsBeforeResolution,
} from './playback-timeline';

export interface ActiveAura {
  key: number;
  combatantId: string;
  kind: AuraKind;
}

export interface ActiveFloat extends FloatCue {
  key: number;
}

/**
 * Plays back a round's resolution on every screen that shows it (grid, tracker, feed).
 *
 * The store gets the round's final state the moment `ResolutionCompleted` arrives (it stays the
 * source of truth). While playback runs, this service holds what the screen should show *so far*:
 * positions, revealed HP, defeats, auras and floating text. Read them through `positions`,
 * `hpOf()` and `isDefeated()`.
 *
 * Encounter events that arrive mid-playback (the next planning phase, lock-ins, the encounter's
 * end) wait in a queue until playback finishes. See `afterPlayback`.
 */
@Injectable({ providedIn: 'root' })
export class ResolutionPlaybackService {
  readonly playing = signal(false);
  /** 1-based tick being played, and how many ticks the round has. */
  readonly tick = signal(0);
  readonly tickCount = signal(0);
  /** The part of the tick being played (movement → attacks → defeats), or null when not playing. */
  readonly phase = signal<PlaybackPhase | null>(null);
  /**
   * The round being played, or the last one played (kept for review until the encounter is left).
   * Null before the first resolution.
   */
  readonly resolution = signal<ResolutionPayload | null>(null);
  /** Token positions so far. Only meaningful while `playing`. */
  readonly positions = signal<Record<string, Position>>({});
  /** How long tokens take to glide to a new position (0 = jump). */
  readonly moveMs = signal(0);
  readonly auras = signal<ActiveAura[]>([]);
  readonly floats = signal<ActiveFloat[]>([]);

  private readonly hp = signal<Record<string, number>>({});
  private readonly defeated = signal<ReadonlySet<string>>(new Set());

  timing: PlaybackTiming = PLAYBACK_TIMING;

  private deferred: Array<() => void> = [];
  /** Bumped by every start and reset; a running playback stops when it no longer matches. */
  private generation = 0;
  private nextKey = 0;

  constructor(private store: Store, actions$: Actions) {
    // Leaving or restarting an encounter abandons its playback and anything waiting on it.
    actions$.pipe(ofActionDispatched(ClearEncounter, EncounterStarted)).subscribe(() => this.reset());
  }

  /** Runs `fn` now, or once the current playback (and anything queued before it) has finished. */
  afterPlayback(fn: () => void): void {
    if (this.playing()) this.deferred.push(fn);
    else fn();
  }

  /** Handles a `ResolutionCompleted` from the server: updates the store and plays the round. */
  receiveResolution(payload: ResolutionPayload): void {
    this.afterPlayback(() => this.start(payload));
  }

  /** HP to show for a combatant: what playback has revealed so far, else the store's (never below 0). */
  hpOf(c: Combatant): number {
    const hp = this.playing() ? this.hp()[c.id] ?? c.currentHp : c.currentHp;
    return Math.max(0, hp);
  }

  isDefeated(c: Combatant): boolean {
    return this.playing() ? this.defeated().has(c.id) : c.isDefeated;
  }

  /** Stops any playback and drops everything waiting on it. */
  reset(): void {
    this.generation++;
    this.deferred = [];
    this.playing.set(false);
    this.tick.set(0);
    this.tickCount.set(0);
    this.phase.set(null);
    this.resolution.set(null);
    this.positions.set({});
    this.moveMs.set(0);
    this.auras.set([]);
    this.floats.set([]);
    this.hp.set({});
    this.defeated.set(new Set());
  }

  private start(payload: ResolutionPayload): void {
    const combatantsBefore = this.store.selectSnapshot(EncounterState.combatants);
    const positionsBefore = this.store.selectSnapshot(MapState.positions);

    // Playback state first, so nothing renders the final state before playback takes over.
    const generation = ++this.generation;
    this.positions.set(positionsBeforeResolution(positionsBefore, payload.ticks));
    this.hp.set(Object.fromEntries(combatantsBefore.map(c => [c.id, c.currentHp])));
    this.defeated.set(new Set(combatantsBefore.filter(c => c.isDefeated).map(c => c.id)));
    this.moveMs.set(0);
    this.tick.set(0);
    this.tickCount.set(payload.ticks.length);
    this.phase.set(null);
    this.resolution.set(payload);
    this.playing.set(true);

    this.store.dispatch(new ResolutionCompleted(payload));
    this.store.dispatch(new ApplyResolutionPositions(positionsAfterResolution(positionsBefore, payload.ticks)));

    void this.play(buildTimeline(payload, this.timing), generation);
  }

  private async play(beats: PlaybackBeat[], generation: number): Promise<void> {
    for (const beat of beats) {
      if (generation !== this.generation) return;
      this.apply(beat);
      await this.sleep(beat.durationMs);
    }
    if (generation !== this.generation) return;
    this.finish();
  }

  private apply(beat: PlaybackBeat): void {
    this.tick.set(beat.tick + 1);
    this.phase.set(beat.phase);
    if (beat.positions) {
      this.moveMs.set(beat.moveMs ?? 0);
      this.positions.update(p => ({ ...p, ...beat.positions }));
    }
    for (const cue of beat.auras ?? []) {
      const key = this.nextKey++;
      this.auras.update(list => [...list, { ...cue, key }]);
      setTimeout(() => this.auras.update(list => list.filter(a => a.key !== key)), this.timing.auraMs);
    }
    for (const cue of beat.floats ?? []) {
      const key = this.nextKey++;
      this.floats.update(list => [...list, { ...cue, key }]);
      setTimeout(() => this.floats.update(list => list.filter(f => f.key !== key)), this.timing.floatMs);
    }
    if (beat.hp) this.hp.update(hp => ({ ...hp, ...beat.hp }));
    if (beat.defeated) this.defeated.update(set => new Set([...set, ...beat.defeated!]));
    if (beat.feed?.length) this.store.dispatch(new AddFeedBatch(beat.feed));
  }

  private finish(): void {
    // Auras and floating text left over expire on their own timers.
    this.playing.set(false);
    this.tick.set(0);
    this.tickCount.set(0);
    this.phase.set(null);
    this.moveMs.set(0);
    // Replay what waited, in order; stop if one of them starts another playback.
    while (this.deferred.length > 0 && !this.playing())
      this.deferred.shift()!();
  }

  private sleep(ms: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, ms));
  }
}
