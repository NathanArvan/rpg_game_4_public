import { Component, EventEmitter, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Store } from '@ngxs/store';
import { Observable } from 'rxjs';
import { GameHubService } from '../../../core/services/game-hub.service';
import { AuthService } from '../../../core/services/auth.service';
import { JoinSession } from '../../../state/session/session.actions';
import { CharacterState } from '../../../state/character/character.state';
import { CharacterSummary } from '../../../state/character/character.model';

@Component({
  selector: 'app-player-join',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './player-join.component.html',
  styleUrl: './player-join.component.scss'
})
export class PlayerJoinComponent {
  @Output() joined = new EventEmitter<void>();

  characters$: Observable<CharacterSummary[]>;

  joinCode = '';
  joinDisplayName = '';
  selectedCharacterId = '';
  joinError = '';

  constructor(
    private store: Store,
    private hub: GameHubService,
    private auth: AuthService
  ) {
    this.characters$ = store.select(CharacterState.characters);
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
      this.joined.emit();
    } catch (e: any) {
      console.error('[join] error:', e);
      this.joinError = 'Failed to join session: ' + (e?.message ?? 'Unknown error');
    }
  }
}
