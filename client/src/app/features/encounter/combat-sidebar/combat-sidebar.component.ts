import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { EncounterTrackerComponent } from '../encounter-tracker.component';

@Component({
  selector: 'app-combat-sidebar',
  standalone: true,
  imports: [CommonModule, EncounterTrackerComponent],
  templateUrl: './combat-sidebar.component.html',
  styleUrl: './combat-sidebar.component.scss',
})
export class CombatSidebarComponent {
  @Input() isGm = false;
  @Input() joinCode = '';
}
