import { Injectable } from '@angular/core';
import { Store } from '@ngxs/store';
import { QuickBattlePresetDto } from '../../contracts/hub-contracts.generated';
import { ClearEncounter } from '../../state/encounter/encounter.actions';
import { ClearFeed } from '../../state/feed/feed.actions';
import { LeaveQuickBattle, SetQuickBattleSession } from '../../state/session/session.actions';
import { GameHubService } from './game-hub.service';

/** Starting, restarting and leaving one-click battles (the user commands a party against AI enemies). */
@Injectable({
  providedIn: 'root'
})
export class QuickBattleService {
  constructor(private hub: GameHubService, private store: Store) {}

  async listPresets(): Promise<QuickBattlePresetDto[]> {
    await this.hub.connect();
    return this.hub.getQuickBattlePresets();
  }

  /** Starts a fresh battle from the preset, replacing whatever is on screen. */
  async start(presetId: string): Promise<void> {
    await this.hub.connect();
    // Clear first: EncounterStarted for the new battle can arrive before the call returns.
    this.store.dispatch([new ClearEncounter(), new ClearFeed()]);
    const session = await this.hub.startQuickBattle(presetId);
    this.store.dispatch(new SetQuickBattleSession(
      session.campaignId, session.gameSessionId, session.joinCode, session.presetId));
  }

  leave(): void {
    this.store.dispatch([new ClearEncounter(), new ClearFeed(), new LeaveQuickBattle()]);
  }
}
