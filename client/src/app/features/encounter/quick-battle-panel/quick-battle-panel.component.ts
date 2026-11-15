import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { Store } from '@ngxs/store';
import { Observable } from 'rxjs';
import { GameHubService } from '../../../core/services/game-hub.service';
import { QuickBattleService } from '../../../core/services/quick-battle.service';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { SessionState } from '../../../state/session/session.state';

/** Debug controls shown during a Quick Battle (the user is the GM behind the scenes). */
@Component({
  selector: 'app-quick-battle-panel',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './quick-battle-panel.component.html',
  styleUrl: './quick-battle-panel.component.scss'
})
export class QuickBattlePanelComponent {
  @Input() joinCode = '';

  presetId$: Observable<string | null>;
  isPlanning$: Observable<boolean>;

  constructor(private store: Store, private hub: GameHubService, private quickBattle: QuickBattleService) {
    this.presetId$ = store.select(SessionState.quickBattlePresetId);
    this.isPlanning$ = store.select(EncounterState.isPlanning);
  }

  forceResolve(): void {
    this.hub.forceResolve(this.joinCode);
  }

  async restart(): Promise<void> {
    const presetId = this.store.selectSnapshot(SessionState.quickBattlePresetId);
    if (presetId) await this.quickBattle.start(presetId);
  }

  leave(): void {
    this.quickBattle.leave();
  }
}
