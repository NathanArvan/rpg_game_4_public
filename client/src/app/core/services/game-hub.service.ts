import { Inject, Injectable, InjectionToken, NgZone, inject } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { Store } from '@ngxs/store';
import { PlayerPresenceChanged } from '../../state/session/session.actions';
import {
  AllLockedIn,
  ClearMyPlannedAction,
  CombatantDefeated,
  EncounterEnded,
  EncounterStarted,
  MonsterAdded,
  PlayerLockedIn,
  PlanningPhaseStarted,
} from '../../state/encounter/encounter.actions';
import { MapInitialized } from '../../state/map/map.actions';
import { AddFeedBatch } from '../../state/feed/feed.actions';
import { BATTLE_START_LINE } from '../../features/encounter/feed/feed-format';
import { AuthService } from './auth.service';
import { ResolutionPlaybackService } from './resolution-playback.service';
import {
  ActionRejectedPayload,
  AllLockedInPayload,
  CombatantDefeatedPayload,
  EncounterEndedPayload,
  EncounterStartedPayload,
  HubEvents,
  MonsterAddedPayload,
  PlannedActionArray,
  PlayerLockedInPayload,
  PlayerPresenceChangedPayload,
  PlanningPhaseStartedPayload,
  QuickBattlePresetDto,
  QuickBattleStartedResult,
  ResolutionPayload,
} from '../../contracts/hub-contracts.generated';

/** A server → client event name (`HubEvents.Events`). */
export type HubEventName = keyof typeof HubEvents.Events;

/**
 * When each broadcast takes effect relative to a resolution playing back:
 *  - `now`: applied as it arrives, even mid-playback (presence, a new encounter, a rejected plan);
 *  - `after_playback`: held until the current playback (and anything queued before it) ends, so the
 *    screen doesn't jump to the next round, a defeat or the result mid-animation;
 *  - `starts_playback`: handed to ResolutionPlaybackService, which queues it behind a running playback.
 * Every broadcast must be listed (the type requires it): a new one needs a decision here, and the
 * spec pins the whole table.
 */
export const BROADCAST_TIMING: Readonly<Record<HubEventName, 'now' | 'after_playback' | 'starts_playback'>> = {
  PlayerPresenceChanged: 'now',
  EncounterStarted: 'now',
  ActionRejected: 'now',
  ResolutionCompleted: 'starts_playback',
  PlanningPhaseStarted: 'after_playback',
  PlayerLockedIn: 'after_playback',
  AllLockedIn: 'after_playback',
  CombatantDefeated: 'after_playback',
  EncounterEnded: 'after_playback',
  MonsterAdded: 'after_playback',
};

/** Builds the SignalR connection. A token so specs can hand the service a fake connection. */
export const HUB_CONNECTION_FACTORY = new InjectionToken<() => HubConnection>('HUB_CONNECTION_FACTORY', {
  providedIn: 'root',
  factory: () => {
    const auth = inject(AuthService);
    return () => new HubConnectionBuilder()
      .withUrl('/hubs/game', { accessTokenFactory: () => auth.getToken() })
      .withAutomaticReconnect()
      .build();
  },
});

@Injectable({ providedIn: 'root' })
export class GameHubService {
  private connection: HubConnection;

  constructor(
    private store: Store,
    private ngZone: NgZone,
    private playback: ResolutionPlaybackService,
    @Inject(HUB_CONNECTION_FACTORY) createConnection: () => HubConnection,
  ) {
    this.connection = createConnection();

    this.on('PlayerPresenceChanged', (payload: PlayerPresenceChangedPayload) =>
      this.store.dispatch(new PlayerPresenceChanged(payload)));

    this.on('EncounterStarted', (payload: EncounterStartedPayload) => {
      this.store.dispatch(new EncounterStarted(payload.encounterId, payload.combatants));
      this.store.dispatch(new AddFeedBatch([BATTLE_START_LINE]));
      if (payload.map) {
        this.store.dispatch(new MapInitialized(
          payload.encounterId,
          payload.map.gridWidth,
          payload.map.gridHeight,
          payload.map.positions
        ));
      }
    });

    this.on('PlanningPhaseStarted', (payload: PlanningPhaseStartedPayload) =>
      this.store.dispatch(new PlanningPhaseStarted(
        payload.roundNumber,
        payload.planningTimerDurationSeconds,
        payload.combatants,
        payload.apCosts
      )));

    this.on('PlayerLockedIn', (payload: PlayerLockedInPayload) =>
      this.store.dispatch(new PlayerLockedIn(payload.characterName)));

    this.on('AllLockedIn', (_payload: AllLockedInPayload) => this.store.dispatch(new AllLockedIn()));

    // Updates the store (HP, positions) and plays the round back tick by tick, feed included.
    this.on('ResolutionCompleted', (payload: ResolutionPayload) => this.playback.receiveResolution(payload));

    // Surface it in the feed so a rejected plan (e.g. over the AP budget) isn't silent.
    this.on('ActionRejected', (payload: ActionRejectedPayload) =>
      this.store.dispatch(new AddFeedBatch([`⚠ ${payload.reason}`])));

    this.on('CombatantDefeated', (payload: CombatantDefeatedPayload) =>
      this.store.dispatch(new CombatantDefeated(payload.combatantId)));

    this.on('EncounterEnded', (payload: EncounterEndedPayload) => {
      this.store.dispatch(new EncounterEnded(payload.winningSide, payload.finalRoundNumber));
      this.store.dispatch(new AddFeedBatch([`— Encounter over after round ${payload.finalRoundNumber}: ${
        payload.winningSide === 'party' ? 'the party wins' : payload.winningSide === 'enemy' ? 'the enemies win' : 'draw'
      } —`]));
    });

    this.on('MonsterAdded', (payload: MonsterAddedPayload) => this.store.dispatch(new MonsterAdded(payload)));

    this.connection.onclose((err) => console.error('[hub] connection closed:', err));
    this.connection.onreconnecting((err) => console.warn('[hub] reconnecting:', err));
    this.connection.onreconnected((id) => console.log('[hub] reconnected, connectionId:', id));
  }

  /**
   * Registers the handler for one broadcast, applied inside Angular's zone at the time its entry in
   * {@link BROADCAST_TIMING} says: at once, or once the current playback has finished.
   */
  private on<T>(event: HubEventName, apply: (payload: T) => void): void {
    this.connection.on(HubEvents.Events[event], (payload: T) => {
      console.log(`[hub] ${event} received:`, payload);
      this.ngZone.run(() => {
        if (BROADCAST_TIMING[event] === 'after_playback') this.playback.afterPlayback(() => apply(payload));
        else apply(payload);
      });
    });
  }

  isConnected(): boolean {
    return this.connection.state === HubConnectionState.Connected;
  }

  async connect(): Promise<void> {
    console.log('[hub] connect() called, state:', this.connection.state);
    if (this.connection.state === HubConnectionState.Disconnected) {
      await this.connection.start();
      console.log('[hub] connection started, state:', this.connection.state);
    }
  }

  async joinSession(joinCode: string, displayName: string, characterId: string | null): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.JoinSession, joinCode, displayName, characterId);
  }

  async startEncounter(joinCode: string): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.StartEncounter, joinCode);
  }

  /** @param combatantId which of your characters this is for (null if you control only one) */
  async planAction(joinCode: string, action: PlannedActionArray, combatantId: string | null): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.PlanAction, joinCode, action, combatantId);
  }

  async getQuickBattlePresets(): Promise<QuickBattlePresetDto[]> {
    return await this.connection.invoke<QuickBattlePresetDto[]>(HubEvents.Methods.GetQuickBattlePresets);
  }

  async startQuickBattle(presetId: string): Promise<QuickBattleStartedResult> {
    return await this.connection.invoke<QuickBattleStartedResult>(HubEvents.Methods.StartQuickBattle, presetId);
  }

  async lockIn(joinCode: string): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.LockIn, joinCode);
  }

  async forceResolve(joinCode: string): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.ForceResolve, joinCode);
  }

  /** @param profileId combat profile preset; null means the server's default enemy profile. */
  async addMonster(joinCode: string, name: string, isAiControlled: boolean, profileId: string | null = null): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.AddMonster, joinCode, name, isAiControlled, profileId);
  }

  async planEnemyAction(joinCode: string, combatantId: string, action: PlannedActionArray): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.PlanEnemyAction, joinCode, combatantId, action);
  }

  async lockInEnemies(joinCode: string): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.LockInEnemies, joinCode);
  }
}
