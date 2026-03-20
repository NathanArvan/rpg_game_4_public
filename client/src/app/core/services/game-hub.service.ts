import { Injectable, NgZone } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { Store } from '@ngxs/store';
import { PlayerPresenceChanged } from '../../state/session/session.actions';
import { AttackResolved, CombatantDefeated, EncounterStarted, TurnAdvanced } from '../../state/encounter/encounter.actions';
import { MapInitialized, TokenMoved, ClearPendingMove } from '../../state/map/map.actions';
import { AddFeedEntry } from '../../state/feed/feed.actions';
import { AuthService } from './auth.service';
import {
  ActionRejectedPayload,
  AttackResolvedPayload,
  CombatantDefeatedPayload,
  EncounterStartedPayload,
  HubEvents,
  PlayerPresenceChangedPayload,
  TokenMovedPayload,
  TurnAdvancedPayload,
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
          payload.activeTurnCombatantId
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

    this.connection.on(HubEvents.Events.TurnAdvanced, (payload: TurnAdvancedPayload) => {
      console.log('[hub] TurnAdvanced received:', payload);
      this.ngZone.run(() => this.store.dispatch(new TurnAdvanced(payload.activeTurnCombatantId)));
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

  async moveToken(joinCode: string, combatantId: string, x: number, y: number): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.MoveToken, joinCode, combatantId, x, y);
  }

  async endTurn(joinCode: string): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.EndTurn, joinCode);
  }

  async declareAttack(joinCode: string, targetCombatantId: string): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.DeclareAttack, joinCode, targetCombatantId);
  }
}
