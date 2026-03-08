import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Store } from '@ngxs/store';
import { Observable } from 'rxjs';
import { GameHubService } from '../../core/services/game-hub.service';
import { AuthService } from '../../core/services/auth.service';
import { CampaignService } from '../../core/services/campaign.service';
import { DevService } from '../../core/services/dev.service';
import { JoinSession, SetGmSession } from '../../state/session/session.actions';
import { ConnectedPlayer } from '../../state/session/session.model';
import { SessionState } from '../../state/session/session.state';
import { CharacterState } from '../../state/character/character.state';
import { CharacterSummary } from '../../state/character/character.model';
import { LoadMyCharacters } from '../../state/character/character.actions';
import { EncounterState } from '../../state/encounter/encounter.state';
import { EncounterTrackerComponent } from '../encounter/encounter-tracker.component';
import { GridComponent } from '../encounter/grid/grid.component';
import { FeedComponent } from '../encounter/feed/feed.component';

@Component({
  selector: 'app-lobby',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, EncounterTrackerComponent, GridComponent, FeedComponent],
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
        <button (click)="quickRegister()" class="dev-btn">⚡ Quick Register</button>
        <p *ngIf="authError" class="error">{{ authError }}</p>
      </div>

      <div *ngIf="auth.isLoggedIn()">
        <div class="flow-tabs">
          <button [class.active]="activeFlow === 'player'" (click)="activeFlow = 'player'">Player</button>
          <button [class.active]="activeFlow === 'gm'" (click)="activeFlow = 'gm'">GM</button>
          <a routerLink="/character">My Characters</a>
          <button class="logout-btn" (click)="auth.logout()">Logout</button>
        </div>

        <!-- Player Flow -->
        <div class="join-section" *ngIf="activeFlow === 'player'">
          <h2>Join a Session</h2>
          <input [(ngModel)]="joinCode" placeholder="Join code (e.g. ABC123)" />
          <input [(ngModel)]="joinDisplayName" placeholder="Display name in session" />
          <select [(ngModel)]="selectedCharacterId">
            <option value="">-- Select a character --</option>
            <option *ngFor="let c of characters$ | async" [value]="c.id">
              {{ c.name }} ({{ c.race }} {{ c.class }}, Lv{{ c.level }})
            </option>
          </select>
          <button (click)="join()">Join Session</button>
          <p *ngIf="joinError" class="error">{{ joinError }}</p>
        </div>

        <!-- GM Flow -->
        <div class="gm-section" *ngIf="activeFlow === 'gm'">
          <h2>Game Master</h2>

          <div *ngIf="!gmCampaignId">
            <input [(ngModel)]="campaignName" placeholder="Campaign name" />
            <button (click)="createCampaign()">Create Campaign</button>
            <p *ngIf="gmError" class="error">{{ gmError }}</p>
          </div>

          <div *ngIf="gmCampaignId && !gmJoinCode">
            <p>Campaign created! ID: <code>{{ gmCampaignId }}</code></p>
            <button (click)="startSession()">Start Session</button>
            <p *ngIf="gmError" class="error">{{ gmError }}</p>
          </div>

          <div *ngIf="gmJoinCode">
            <p>Session started! Share this join code with players:</p>
            <div class="join-code">{{ gmJoinCode }}</div>
            <button (click)="connectAsGm()">Connect to Session</button>
            <p *ngIf="gmError" class="error">{{ gmError }}</p>
          </div>

          <div *ngIf="gmConnected">
            <button
              (click)="startEncounter()"
              [disabled]="!(players$ | async)?.length"
              class="encounter-btn">
              Start Encounter
            </button>
            <p *ngIf="encounterError" class="error">{{ encounterError }}</p>
          </div>
        </div>
      </div>

      <div class="players-section" *ngIf="(players$ | async)?.length">
        <h2>Connected Players</h2>
        <ul>
          <li *ngFor="let p of players$ | async">
            {{ p.displayName }} <span class="online-dot">●</span>
          </li>
        </ul>
      </div>

      <app-encounter-tracker *ngIf="encounterActive$ | async"></app-encounter-tracker>
      <app-feed *ngIf="encounterActive$ | async"></app-feed>
      <app-grid
        *ngIf="encounterActive$ | async"
        [joinCode]="activeJoinCode"
        [myUserId]="auth.getUserId()">
      </app-grid>
    </div>
  `,
  styles: [`
    .lobby { max-width: 900px; margin: 40px auto; font-family: sans-serif; }
    input, select { display: block; margin: 8px 0; padding: 8px; width: 100%; box-sizing: border-box; }
    button { margin: 4px 4px 4px 0; padding: 8px 16px; cursor: pointer; }
    .error { color: red; }
    .online-dot { color: green; }
    ul { list-style: none; padding: 0; }
    li { padding: 4px 0; font-size: 1.1em; }
    .flow-tabs { margin-bottom: 16px; }
    .flow-tabs button.active { background: #333; color: white; }
    .logout-btn { float: right; }
    .join-code { font-size: 2em; font-weight: bold; letter-spacing: 0.2em; padding: 12px; background: #f0f0f0; border-radius: 4px; text-align: center; margin: 12px 0; }
    .encounter-btn { background: #8b0000; color: white; border: none; }
    .encounter-btn:disabled { background: #ccc; cursor: not-allowed; }
    .dev-btn { background: #d97706; color: white; border: none; }
    .dev-btn:hover { background: #b45309; }
  `],
})
export class LobbyComponent implements OnInit {
  players$: Observable<ConnectedPlayer[]>;
  characters$: Observable<CharacterSummary[]>;
  encounterActive$: Observable<boolean>;

  username = '';
  displayName = '';
  password = '';
  authError = '';

  activeFlow: 'player' | 'gm' = 'player';

  // Player flow
  joinCode = '';
  joinDisplayName = '';
  selectedCharacterId = '';
  joinError = '';

  // GM flow
  campaignName = '';
  gmCampaignId: string | null = null;
  gmJoinCode: string | null = null;
  gmGameSessionId: string | null = null;
  gmConnected = false;
  gmError = '';
  encounterError = '';

  get activeJoinCode(): string {
    return this.gmJoinCode ?? this.joinCode;
  }

  constructor(
    public auth: AuthService,
    private hub: GameHubService,
    private campaignService: CampaignService,
    private store: Store,
    private dev: DevService
  ) {
    this.players$ = store.select(SessionState.connectedPlayers);
    this.characters$ = store.select(CharacterState.characters);
    this.encounterActive$ = store.select(EncounterState.active);
  }

  ngOnInit(): void {
    if (this.auth.isLoggedIn()) {
      this.store.dispatch(new LoadMyCharacters());
    }
  }

  async quickRegister(): Promise<void> {
    try {
      this.authError = '';
      const { username, displayName, password } = this.dev.randomUser();
      await this.auth.register(username, displayName, password);
    } catch {
      this.authError = 'Quick register failed.';
    }
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
    if (!this.joinCode || !this.joinDisplayName) {
      this.joinError = 'Join code and display name are required.';
      return;
    }
    if (!this.selectedCharacterId) {
      this.joinError = 'Please select a character before joining.';
      return;
    }
    try {
      this.joinError = '';
      const charId = this.selectedCharacterId || null;
      this.store.dispatch(new JoinSession(this.joinCode, this.joinDisplayName, charId));
      await this.hub.connect();
      await this.hub.joinSession(this.joinCode, this.joinDisplayName, charId);
    } catch (e: any) {
      console.error('[join] error:', e);
      this.joinError = 'Failed to join session: ' + (e?.message ?? 'Unknown error');
    }
  }

  createCampaign(): void {
    if (!this.campaignName) {
      this.gmError = 'Campaign name is required.';
      return;
    }
    this.gmError = '';
    this.campaignService.createCampaign(this.campaignName).subscribe({
      next: (res) => { this.gmCampaignId = res.id; },
      error: (e) => { this.gmError = 'Failed to create campaign: ' + (e?.message ?? 'Unknown error'); },
    });
  }

  startSession(): void {
    if (!this.gmCampaignId) return;
    this.gmError = '';
    this.campaignService.startSession(this.gmCampaignId).subscribe({
      next: (res) => {
        this.gmJoinCode = res.joinCode;
        this.gmGameSessionId = res.gameSessionId;
        this.store.dispatch(new SetGmSession(this.gmCampaignId!, res.gameSessionId, res.joinCode));
      },
      error: (e) => { this.gmError = 'Failed to start session: ' + (e?.message ?? 'Unknown error'); },
    });
  }

  async connectAsGm(): Promise<void> {
    if (!this.gmJoinCode) return;
    try {
      this.gmError = '';
      await this.hub.connect();
      await this.hub.joinSession(this.gmJoinCode, this.displayName || 'GM', null);
      this.gmConnected = true;
    } catch (e: any) {
      console.error('[gm connect] error:', e);
      this.gmError = 'Failed to connect: ' + (e?.message ?? 'Unknown error');
    }
  }

  async startEncounter(): Promise<void> {
    if (!this.gmJoinCode) return;
    try {
      this.encounterError = '';
      await this.hub.startEncounter(this.gmJoinCode);
    } catch (e: any) {
      console.error('[startEncounter] error:', e);
      this.encounterError = 'Failed to start encounter: ' + (e?.message ?? 'Unknown error');
    }
  }
}
