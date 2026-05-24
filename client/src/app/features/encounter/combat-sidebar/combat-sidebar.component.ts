import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { EncounterTrackerComponent } from '../encounter-tracker.component';
import { GmEnemyPlannerComponent } from '../gm-enemy-planner/gm-enemy-planner.component';

@Component({
  selector: 'app-combat-sidebar',
  standalone: true,
  imports: [CommonModule, EncounterTrackerComponent, GmEnemyPlannerComponent],
  templateUrl: './combat-sidebar.component.html',
  styleUrl: './combat-sidebar.component.scss',
})
export class CombatSidebarComponent {
  @Input() isGm = false;
  @Input() joinCode = '';
}
