import { Injectable, NgZone } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { Store } from '@ngxs/store';
import { PlayerPresenceChanged } from '../../state/session/session.actions';
import { AuthService } from './auth.service';

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

    this.connection.on('PlayerPresenceChanged', (payload) => {
      console.log('[hub] PlayerPresenceChanged received:', payload);
      this.ngZone.run(() => this.store.dispatch(new PlayerPresenceChanged(payload)));
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

  async joinSession(joinCode: string, displayName: string): Promise<void> {
    await this.connection.invoke('JoinSession', joinCode, displayName);
  }
}
