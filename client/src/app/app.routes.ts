import { Routes } from '@angular/router';
import { LobbyComponent } from './features/lobby/lobby.component';
import { CharacterComponent } from './features/character/character.component';

export const routes: Routes = [
  { path: '', component: LobbyComponent },
  { path: 'character', component: CharacterComponent },
  { path: '**', redirectTo: '' },
];
