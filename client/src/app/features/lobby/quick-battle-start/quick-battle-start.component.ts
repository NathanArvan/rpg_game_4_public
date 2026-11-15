import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { QuickBattlePresetDto } from '../../../contracts/hub-contracts.generated';
import { QuickBattleService } from '../../../core/services/quick-battle.service';

/** Lobby box: pick a preset and jump straight into a battle with a party of your own. */
@Component({
  selector: 'app-quick-battle-start',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './quick-battle-start.component.html',
  styleUrl: './quick-battle-start.component.scss'
})
export class QuickBattleStartComponent implements OnInit {
  presets: QuickBattlePresetDto[] = [];
  presetId = '';
  starting = false;
  error = '';

  constructor(private quickBattle: QuickBattleService) {}

  async ngOnInit(): Promise<void> {
    try {
      this.presets = await this.quickBattle.listPresets();
      this.presetId = this.presets[0]?.id ?? '';
    } catch (e: any) {
      this.error = 'Could not load battle presets: ' + (e?.message ?? 'Unknown error');
    }
  }

  get selectedPreset(): QuickBattlePresetDto | undefined {
    return this.presets.find(p => p.id === this.presetId);
  }

  async start(): Promise<void> {
    if (!this.presetId || this.starting) return;
    this.starting = true;
    this.error = '';
    try {
      await this.quickBattle.start(this.presetId);
    } catch (e: any) {
      this.error = 'Could not start the battle: ' + (e?.message ?? 'Unknown error');
    } finally {
      this.starting = false;
    }
  }
}
