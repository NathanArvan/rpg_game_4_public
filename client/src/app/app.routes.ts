import { Routes } from '@angular/router';
import { LobbyComponent } from './features/lobby/lobby.component';
import { CharacterComponent } from './features/character/character.component';
import { EncounterPreviewComponent } from './features/encounter/preview/encounter-preview/encounter-preview.component';

export const routes: Routes = [
  { path: '', component: LobbyComponent },
  { path: 'character', component: CharacterComponent },
  { path: 'preview', component: EncounterPreviewComponent },
  { path: '**', redirectTo: '' },
];
