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
    // Clear first: EncounterStarted for the new battle can arrive before the call returns. Dropping the
    // old battle's session too keeps the combat screen down (the lobby shows it only once a join code is
    // known), so nothing can be planned against the previous battle's join code in that gap.
    this.store.dispatch([new ClearEncounter(), new ClearFeed(), new LeaveQuickBattle()]);
    const session = await this.hub.startQuickBattle(presetId);
    this.store.dispatch(new SetQuickBattleSession(
      session.campaignId, session.gameSessionId, session.joinCode, session.presetId));
  }

  leave(): void {
    this.store.dispatch([new ClearEncounter(), new ClearFeed(), new LeaveQuickBattle()]);
  }
}
