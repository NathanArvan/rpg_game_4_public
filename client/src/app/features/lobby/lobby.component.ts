import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Store } from '@ngxs/store';
import { Observable } from 'rxjs';
import { GameHubService } from '../../core/services/game-hub.service';
import { AuthService } from '../../core/services/auth.service';
import { JoinSession } from '../../state/session/session.actions';
import { ConnectedPlayer } from '../../state/session/session.model';
import { SessionState } from '../../state/session/session.state';

@Component({
  selector: 'app-lobby',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="lobby">
      <h1>RPG Game Lobby</h1>

      <div class="auth-section" *ngIf="!auth.isLoggedIn()">
        <h2>Register / Login</h2>
        <input [(ngModel)]="username" placeholder="Username" />
        <input [(ngModel)]="displayName" placeholder="Display Name" />
        <input [(ngModel)]="password" type="password" placeholder="Password" />
        <button (click)="register()">Register</button>
        <button (click)="login()">Login</button>
        <p *ngIf="authError" class="error">{{ authError }}</p>
      </div>

      <div class="join-section" *ngIf="auth.isLoggedIn()">
        <h2>Join a Session</h2>
        <input [(ngModel)]="sessionCode" placeholder="Session code" />
        <input [(ngModel)]="joinDisplayName" placeholder="Display name in session" />
        <button (click)="join()">Join Session</button>
        <button (click)="auth.logout()">Logout</button>
        <p *ngIf="joinError" class="error">{{ joinError }}</p>
      </div>

      <div class="players-section" *ngIf="(players$ | async)?.length">
        <h2>Connected Players</h2>
        <ul>
          <li *ngFor="let p of players$ | async">
            {{ p.displayName }} <span class="online-dot">●</span>
          </li>
        </ul>
      </div>
    </div>
  `,
  styles: [`
    .lobby { max-width: 500px; margin: 40px auto; font-family: sans-serif; }
    input { display: block; margin: 8px 0; padding: 8px; width: 100%; box-sizing: border-box; }
    button { margin: 4px 4px 4px 0; padding: 8px 16px; cursor: pointer; }
    .error { color: red; }
    .online-dot { color: green; }
    ul { list-style: none; padding: 0; }
    li { padding: 4px 0; font-size: 1.1em; }
  `],
})
export class LobbyComponent {
  players$: Observable<ConnectedPlayer[]>;

  username = '';
  displayName = '';
  password = '';
  sessionCode = '';
  joinDisplayName = '';
  authError = '';
  joinError = '';

  constructor(
    public auth: AuthService,
    private hub: GameHubService,
    private store: Store
  ) {
    this.players$ = store.select(SessionState.connectedPlayers);
  }

  async register(): Promise<void> {
    try {
      this.authError = '';
      await this.auth.register(this.username, this.displayName, this.password);
    } catch {
      this.authError = 'Registration failed. Try a different username.';
    }
  }

  async login(): Promise<void> {
    try {
      this.authError = '';
      await this.auth.login(this.username, this.password);
    } catch {
      this.authError = 'Login failed. Check credentials.';
    }
  }

  async join(): Promise<void> {
    if (!this.sessionCode || !this.joinDisplayName) {
      this.joinError = 'Session code and display name are required.';
      return;
    }
    try {
      this.joinError = '';
      console.log('[join] dispatching JoinSession', this.sessionCode, this.joinDisplayName);
      this.store.dispatch(new JoinSession(this.sessionCode, this.joinDisplayName));
      console.log('[join] calling hub.connect()');
      await this.hub.connect();
      console.log('[join] hub connected, invoking JoinSession on server');
      await this.hub.joinSession(this.sessionCode, this.joinDisplayName);
      console.log('[join] JoinSession invoked successfully');
    } catch (e: any) {
      console.error('[join] error:', e);
      this.joinError = 'Failed to join session: ' + (e?.message ?? 'Unknown error');
    }
  }
}
