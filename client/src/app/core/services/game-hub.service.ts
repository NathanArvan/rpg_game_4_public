import { Injectable } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { Store } from '@ngxs/store';
import { PlayerPresenceChanged } from '../../state/session/session.actions';
import { AuthService } from './auth.service';

@Injectable({ providedIn: 'root' })
export class GameHubService {
  private connection: HubConnection;

  constructor(
    private store: Store,
    private auth: AuthService
  ) {
    this.connection = new HubConnectionBuilder()
      .withUrl('/hubs/game', { accessTokenFactory: () => this.auth.getToken() })
      .withAutomaticReconnect()
      .build();

    this.connection.on('PlayerPresenceChanged', (payload) =>
      this.store.dispatch(new PlayerPresenceChanged(payload))
    );
  }

  async connect(): Promise<void> {
    if (this.connection.state === HubConnectionState.Disconnected) {
      await this.connection.start();
    }
  }

  async joinSession(sessionId: string, displayName: string): Promise<void> {
    await this.connection.invoke('JoinSession', sessionId, displayName);
  }
}
