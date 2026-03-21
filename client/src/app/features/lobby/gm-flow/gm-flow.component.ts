import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Store } from '@ngxs/store';
import { GameHubService } from '../../../core/services/game-hub.service';
import { CampaignService } from '../../../core/services/campaign.service';
import { SetGmSession } from '../../../state/session/session.actions';
import { EncounterState } from '../../../state/encounter/encounter.state';

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
  isPlanning = false;

  constructor(
    private campaignService: CampaignService,
    private hub: GameHubService,
    private store: Store
  ) {
    this.store.select(EncounterState.isPlanning).subscribe(v => this.isPlanning = v);
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

  async forceResolve(): Promise<void> {
    if (!this.gmJoinCode) return;
    try {
      await this.hub.forceResolve(this.gmJoinCode);
    } catch (e: any) {
      console.error('[forceResolve] error:', e);
    }
  }
}
