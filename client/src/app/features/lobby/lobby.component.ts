import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Store } from '@ngxs/store';
import { Observable } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { ConnectedPlayer } from '../../state/session/session.model';
import { SessionState } from '../../state/session/session.state';
import { EncounterState } from '../../state/encounter/encounter.state';
import { EncounterTrackerComponent } from '../encounter/encounter-tracker.component';
import { GridComponent } from '../encounter/grid/grid.component';
import { FeedComponent } from '../encounter/feed/feed.component';
import { AuthPanelComponent } from './auth-panel/auth-panel.component';
import { PlayerJoinComponent } from './player-join/player-join.component';
import { GmFlowComponent } from './gm-flow/gm-flow.component';

@Component({
  selector: 'app-lobby',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    EncounterTrackerComponent,
    GridComponent,
    FeedComponent,
    AuthPanelComponent,
    PlayerJoinComponent,
    GmFlowComponent,
  ],
  templateUrl: './lobby.component.html',
  styleUrl: './lobby.component.css',
})
export class LobbyComponent {
  players$: Observable<ConnectedPlayer[]>;
  encounterActive$: Observable<boolean>;
  joinCode$: Observable<string | null>;

  activeFlow: 'player' | 'gm' = 'player';

  constructor(
    public auth: AuthService,
    private store: Store
  ) {
    this.players$ = store.select(SessionState.connectedPlayers);
    this.encounterActive$ = store.select(EncounterState.active);
    this.joinCode$ = store.select(SessionState.joinCode);
  }

  onAuthenticated(): void {}
}
