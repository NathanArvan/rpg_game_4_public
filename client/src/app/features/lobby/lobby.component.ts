import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Store } from '@ngxs/store';
import { Observable, combineLatest, map } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { ConnectedPlayer } from '../../state/session/session.model';
import { SessionState } from '../../state/session/session.state';
import { EncounterState } from '../../state/encounter/encounter.state';
import { AuthPanelComponent } from './auth-panel/auth-panel.component';
import { PlayerJoinComponent } from './player-join/player-join.component';
import { GmFlowComponent } from './gm-flow/gm-flow.component';
import { CombatLayoutComponent } from '../encounter/combat-layout/combat-layout.component';
import { QuickBattleStartComponent } from './quick-battle-start/quick-battle-start.component';

@Component({
  selector: 'app-lobby',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    AuthPanelComponent,
    PlayerJoinComponent,
    GmFlowComponent,
    CombatLayoutComponent,
    QuickBattleStartComponent,
  ],
  templateUrl: './lobby.component.html',
  styleUrl: './lobby.component.css',
})
export class LobbyComponent {
  players$: Observable<ConnectedPlayer[]>;
  /**
   * Show the combat layout: an encounter is active and the session's join code is known. Quick
   * Battle's EncounterStarted arrives before StartQuickBattle returns the join code; planning in
   * that gap would send an empty join code, so the lobby stays up until both are in.
   */
  inCombat$: Observable<boolean>;
  joinCode$: Observable<string | null>;
  isGm$: Observable<boolean>;

  activeFlow: 'player' | 'gm' = 'player';

  constructor(
    public auth: AuthService,
    private store: Store
  ) {
    this.players$ = store.select(SessionState.connectedPlayers);
    this.joinCode$ = store.select(SessionState.joinCode);
    this.inCombat$ = combineLatest([store.select(EncounterState.active), this.joinCode$])
      .pipe(map(([active, joinCode]) => active && !!joinCode));
    this.isGm$ = store.select(SessionState.isGm);
  }

  onAuthenticated(): void {}
}
