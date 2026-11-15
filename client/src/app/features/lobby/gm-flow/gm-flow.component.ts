import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Store } from '@ngxs/store';
import { Observable } from 'rxjs';
import { GameHubService } from '../../../core/services/game-hub.service';
import { CampaignService } from '../../../core/services/campaign.service';
import { SetGmSession } from '../../../state/session/session.actions';
import { SessionState } from '../../../state/session/session.state';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { Combatant } from '../../../state/encounter/encounter.model';

@Component({
  selector: 'app-gm-flow',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './gm-flow.component.html',
  styleUrl: './gm-flow.component.scss'
})
export class GmFlowComponent {
  @Input() playerCount = 0;

  campaignName = '';
  gmCampaignId: string | null = null;
  gmJoinCode: string | null = null;
  gmGameSessionId: string | null = null;
  gmConnected = false;
  gmError = '';
  encounterError = '';
  newMonsterName = '';
  newMonsterAi = true;
  monsters$: Observable<Combatant[]>;

  constructor(
    private campaignService: CampaignService,
    private hub: GameHubService,
    private store: Store
  ) {
    this.monsters$ = store.select(EncounterState.monsterCombatants);

    // This component is destroyed while an encounter is on screen. When the GM returns to the
    // lobby afterwards, pick the session back up instead of starting over at "Create Campaign".
    const joinCode = store.selectSnapshot(SessionState.joinCode);
    if (store.selectSnapshot(SessionState.isGm) && joinCode) {
      this.gmCampaignId = store.selectSnapshot(SessionState.campaignId);
      this.gmGameSessionId = store.selectSnapshot(SessionState.gameSessionId);
      this.gmJoinCode = joinCode;
      this.gmConnected = hub.isConnected();
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
      await this.hub.joinSession(this.gmJoinCode, 'GM', null);
      this.gmConnected = true;
    } catch (e: any) {
      console.error('[gm connect] error:', e);
      this.gmError = 'Failed to connect: ' + (e?.message ?? 'Unknown error');
    }
  }

  async addMonster(): Promise<void> {
    const name = this.newMonsterName.trim();
    if (!name || !this.gmJoinCode) return;
    await this.hub.addMonster(this.gmJoinCode, name, this.newMonsterAi);
    this.newMonsterName = '';
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
