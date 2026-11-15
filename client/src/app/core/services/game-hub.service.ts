import { Injectable, NgZone } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { Store } from '@ngxs/store';
import { MapState } from '../../state/map/map.state';
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
  ResolutionCompleted,
} from '../../state/encounter/encounter.actions';
import { ApplyResolutionPositions, MapInitialized } from '../../state/map/map.actions';
import { AddFeedBatch } from '../../state/feed/feed.actions';
import { AuthService } from './auth.service';
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
  ResolutionPayload,
} from '../../contracts/hub-contracts.generated';

@Injectable({ providedIn: 'root' })
export class GameHubService {
  private connection: HubConnection;

  constructor(
    private store: Store,
    private auth: AuthService,
    private ngZone: NgZone
  ) {
    this.connection = new HubConnectionBuilder()
      .withUrl('/hubs/game', { accessTokenFactory: () => this.auth.getToken() })
      .withAutomaticReconnect()
      .build();

    this.connection.on(HubEvents.Events.PlayerPresenceChanged, (payload: PlayerPresenceChangedPayload) => {
      console.log('[hub] PlayerPresenceChanged received:', payload);
      this.ngZone.run(() => this.store.dispatch(new PlayerPresenceChanged(payload)));
    });

    this.connection.on(HubEvents.Events.EncounterStarted, (payload: EncounterStartedPayload) => {
      console.log('[hub] EncounterStarted received:', payload);
      this.ngZone.run(() => {
        this.store.dispatch(new EncounterStarted(payload.encounterId, payload.combatants));
        if (payload.map) {
          this.store.dispatch(new MapInitialized(
            payload.encounterId,
            payload.map.gridWidth,
            payload.map.gridHeight,
            payload.map.positions
          ));
        }
      });
    });

    this.connection.on(HubEvents.Events.PlanningPhaseStarted, (payload: PlanningPhaseStartedPayload) => {
      console.log('[hub] PlanningPhaseStarted received:', payload);
      this.ngZone.run(() => this.store.dispatch(new PlanningPhaseStarted(
        payload.roundNumber,
        payload.planningTimerDurationSeconds,
        payload.combatants
      )));
    });

    this.connection.on(HubEvents.Events.PlayerLockedIn, (payload: PlayerLockedInPayload) => {
      console.log('[hub] PlayerLockedIn received:', payload);
      this.ngZone.run(() => this.store.dispatch(new PlayerLockedIn(payload.characterName)));
    });

    this.connection.on(HubEvents.Events.AllLockedIn, (_payload: AllLockedInPayload) => {
      console.log('[hub] AllLockedIn received');
      this.ngZone.run(() => this.store.dispatch(new AllLockedIn()));
    });

    this.connection.on(HubEvents.Events.ResolutionCompleted, (payload: ResolutionPayload) => {
      console.log('[hub] ResolutionCompleted received:', payload);
      this.ngZone.run(() => {
        // Update encounter state (HP, defeated, resolutionTicks) — this triggers playback animation
        this.store.dispatch(new ResolutionCompleted(payload));

        // Immediately commit final positions to MapState so they persist
        // even if playback async timing races with PlanningPhaseStarted.
        // Playback uses animPositions for the visual animation, so this is safe.
        const currentPositions = this.store.selectSnapshot(MapState.positions);
        const finalPositions = { ...currentPositions };
        for (const tick of payload.ticks) {
          for (const movement of tick.movements) {
            finalPositions[movement.combatantId] = movement.finalPosition;
          }
        }
        this.store.dispatch(new ApplyResolutionPositions(finalPositions));

        // Add feed entries as a batch
        const feedTexts: string[] = [`— Round ${payload.newRoundNumber - 1} Resolution —`];
        for (const tick of payload.ticks) {
          for (const movement of tick.movements) {
            feedTexts.push(`${movement.combatantName} moves to (${movement.finalPosition.x}, ${movement.finalPosition.y})${movement.wasBlocked ? ' (blocked)' : ''}`);
          }
          for (const attack of tick.attacks) {
            feedTexts.push(attack.feedText);
          }
        }
        this.store.dispatch(new AddFeedBatch(feedTexts));
      });
    });

    this.connection.on(HubEvents.Events.ActionRejected, (payload: ActionRejectedPayload) => {
      console.warn('[hub] ActionRejected:', payload);
      // No state change needed — just log. Components can add UI feedback later.
    });

    this.connection.on(HubEvents.Events.CombatantDefeated, (payload: CombatantDefeatedPayload) => {
      console.log('[hub] CombatantDefeated received:', payload);
      this.ngZone.run(() => this.store.dispatch(new CombatantDefeated(payload.combatantId)));
    });

    this.connection.on(HubEvents.Events.EncounterEnded, (payload: EncounterEndedPayload) => {
      console.log('[hub] EncounterEnded received:', payload);
      this.ngZone.run(() => {
        this.store.dispatch(new EncounterEnded(payload.winningSide, payload.finalRoundNumber));
        this.store.dispatch(new AddFeedBatch([`— Encounter over after round ${payload.finalRoundNumber}: ${
          payload.winningSide === 'party' ? 'the party wins' : payload.winningSide === 'enemy' ? 'the enemies win' : 'draw'
        } —`]));
      });
    });

    this.connection.on(HubEvents.Events.MonsterAdded, (payload: MonsterAddedPayload) => {
      console.log('[hub] MonsterAdded received:', payload);
      this.ngZone.run(() => this.store.dispatch(new MonsterAdded(payload)));
    });

    this.connection.onclose((err) => console.error('[hub] connection closed:', err));
    this.connection.onreconnecting((err) => console.warn('[hub] reconnecting:', err));
    this.connection.onreconnected((id) => console.log('[hub] reconnected, connectionId:', id));
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

  async planAction(joinCode: string, action: PlannedActionArray): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.PlanAction, joinCode, action);
  }

  async lockIn(joinCode: string): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.LockIn, joinCode);
  }

  async forceResolve(joinCode: string): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.ForceResolve, joinCode);
  }

  async addMonster(joinCode: string, name: string): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.AddMonster, joinCode, name);
  }

  async planEnemyAction(joinCode: string, combatantId: string, action: PlannedActionArray): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.PlanEnemyAction, joinCode, combatantId, action);
  }

  async lockInEnemies(joinCode: string): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.LockInEnemies, joinCode);
  }
}
