import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { CombatSidebarComponent } from '../combat-sidebar/combat-sidebar.component';
import { RoundBannerComponent } from '../round-banner/round-banner.component';
import { GridComponent } from '../grid/grid.component';
import { FeedComponent } from '../feed/feed.component';

@Component({
  selector: 'app-combat-layout',
  standalone: true,
  imports: [CommonModule, CombatSidebarComponent, RoundBannerComponent, GridComponent, FeedComponent],
  templateUrl: './combat-layout.component.html',
  styleUrl: './combat-layout.component.scss',
})
export class CombatLayoutComponent {
  @Input() isGm = false;
  @Input() joinCode = '';
  @Input() myUserId = '';
}
