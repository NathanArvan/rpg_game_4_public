import { Component, EventEmitter, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Store } from '@ngxs/store';
import { AuthService } from '../../../core/services/auth.service';
import { DevService } from '../../../core/services/dev.service';
import { LoadMyCharacters } from '../../../state/character/character.actions';

@Component({
  selector: 'app-auth-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './auth-panel.component.html',
  styleUrl: './auth-panel.component.scss'
})
export class AuthPanelComponent {
  @Output() authenticated = new EventEmitter<void>();

  username = '';
  displayName = '';
  password = '';
  authError = '';

  constructor(
    private auth: AuthService,
    private dev: DevService,
    private store: Store
  ) {}

  async register(): Promise<void> {
    try {
      this.authError = '';
      await this.auth.register(this.username, this.displayName, this.password);
      this.store.dispatch(new LoadMyCharacters());
      this.authenticated.emit();
    } catch {
      this.authError = 'Registration failed. Try a different username.';
    }
  }

  async login(): Promise<void> {
    try {
      this.authError = '';
      await this.auth.login(this.username, this.password);
      this.store.dispatch(new LoadMyCharacters());
      this.authenticated.emit();
    } catch {
      this.authError = 'Login failed. Check credentials.';
    }
  }

  async quickRegister(): Promise<void> {
    try {
      this.authError = '';
      const { username, displayName, password } = this.dev.randomUser();
      await this.auth.register(username, displayName, password);
      this.store.dispatch(new LoadMyCharacters());
      this.authenticated.emit();
    } catch {
      this.authError = 'Quick register failed.';
    }
  }
}
