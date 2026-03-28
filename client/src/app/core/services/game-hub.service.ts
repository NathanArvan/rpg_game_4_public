import { Injectable, NgZone } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { Store } from '@ngxs/store';
import { PlayerPresenceChanged } from '../../state/session/session.actions';
import {
  ActionPlanned,
  AllLockedIn,
  AttackResolved,
  ClearEncounter,
  CombatantDefeated,
  EncounterStarted,
  PlanningPhaseStarted,
  PlayerLockedIn,
  ResolutionCompleted,
} from '../../state/encounter/encounter.actions';
import { BatchUpdatePositions, MapInitialized, TokenMoved, ClearPendingMove } from '../../state/map/map.actions';
import { AddFeedEntry } from '../../state/feed/feed.actions';
import { AuthService } from './auth.service';
import {
  ActionPlannedPayload,
  ActionRejectedPayload,
  AllLockedInPayload,
  AttackResolvedPayload,
  CombatantDefeatedPayload,
  EncounterEndedPayload,
  EncounterStartedPayload,
  HubEvents,
  PlanningPhaseStartedPayload,
  PlayerLockedInPayload,
  PlayerPresenceChangedPayload,
  ResolutionCompletedPayload,
  TokenMovedPayload,
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
        this.store.dispatch(new EncounterStarted(
          payload.encounterId,
          payload.combatants,
          payload.roundNumber,
          payload.roundPhase,
          payload.planningTimerExpiresAt
        ));
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

    this.connection.on(HubEvents.Events.TokenMoved, (payload: TokenMovedPayload) => {
      console.log('[hub] TokenMoved received:', payload);
      this.ngZone.run(() => this.store.dispatch(new TokenMoved(payload.combatantId, payload.x, payload.y)));
    });

    this.connection.on(HubEvents.Events.ActionPlanned, (payload: ActionPlannedPayload) => {
      console.log('[hub] ActionPlanned received:', payload);
      // ActionPlanned is caller-only — we already have the planned action locally
    });

    this.connection.on(HubEvents.Events.PlayerLockedIn, (payload: PlayerLockedInPayload) => {
      console.log('[hub] PlayerLockedIn received:', payload);
      this.ngZone.run(() => this.store.dispatch(new PlayerLockedIn(payload.combatantId)));
    });

    this.connection.on(HubEvents.Events.AllLockedIn, (payload: AllLockedInPayload) => {
      console.log('[hub] AllLockedIn received:', payload);
      this.ngZone.run(() => this.store.dispatch(new AllLockedIn()));
    });

    this.connection.on(HubEvents.Events.ActionRejected, (payload: ActionRejectedPayload) => {
      console.warn('[hub] ActionRejected:', payload);
      this.ngZone.run(() => this.store.dispatch(new ClearPendingMove()));
    });

    this.connection.on(HubEvents.Events.AttackResolved, (payload: AttackResolvedPayload) => {
      console.log('[hub] AttackResolved received:', payload);
      this.ngZone.run(() => {
        this.store.dispatch(new AttackResolved(payload.targetId, payload.targetNewHp, payload.feedText));
        this.store.dispatch(new AddFeedEntry(payload.feedText));
      });
    });

    this.connection.on(HubEvents.Events.CombatantDefeated, (payload: CombatantDefeatedPayload) => {
      console.log('[hub] CombatantDefeated received:', payload);
      this.ngZone.run(() => this.store.dispatch(new CombatantDefeated(payload.combatantId)));
    });

    this.connection.on(HubEvents.Events.ResolutionCompleted, (payload: ResolutionCompletedPayload) => {
      console.log('[hub] ResolutionCompleted received:', payload);
      this.ngZone.run(() => {
        const positions: Record<string, { x: number; y: number }> = {};
        for (const [id, pos] of Object.entries(payload.updatedPositions)) {
          positions[id] = { x: pos.x, y: pos.y };
        }
        this.store.dispatch(new ResolutionCompleted(payload.steps, payload.updatedPositions, payload.encounterEnded));
        this.store.dispatch(new BatchUpdatePositions(positions));
        for (const step of payload.steps) {
          this.store.dispatch(new AddFeedEntry(step.feedText));
        }
      });
    });

    this.connection.on(HubEvents.Events.PlanningPhaseStarted, (payload: PlanningPhaseStartedPayload) => {
      console.log('[hub] PlanningPhaseStarted received:', payload);
      this.ngZone.run(() => {
        this.store.dispatch(new PlanningPhaseStarted(payload.encounterId, payload.roundNumber, payload.planningTimerExpiresAt));
        this.store.dispatch(new ClearPendingMove());
      });
    });

    this.connection.on(HubEvents.Events.EncounterEnded, (payload: EncounterEndedPayload) => {
      console.log('[hub] EncounterEnded received:', payload);
      this.ngZone.run(() => this.store.dispatch(new ClearEncounter()));
    });

    this.connection.onclose((err) => console.error('[hub] connection closed:', err));
    this.connection.onreconnecting((err) => console.warn('[hub] reconnecting:', err));
    this.connection.onreconnected((id) => console.log('[hub] reconnected, connectionId:', id));
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

  async planAction(joinCode: string, actionType: string, targetCombatantId?: string, targetX?: number, targetY?: number): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.PlanAction, joinCode, actionType, targetCombatantId ?? null, targetX ?? null, targetY ?? null);
  }

  async lockIn(joinCode: string): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.LockIn, joinCode);
  }

  async forceResolve(joinCode: string): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.ForceResolve, joinCode);
  }
}
