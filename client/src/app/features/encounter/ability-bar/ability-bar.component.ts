import { Component, EventEmitter, Input, Output } from '@angular/core';
import { AbilityDto, PlannedActionEntry } from '../../../contracts/hub-contracts.generated';
import { Combatant } from '../../../state/encounter/encounter.model';
import { DEFAULT_AP_COSTS, abilityUnavailableReason } from '../../../state/encounter/plan-queue';

/** One button's worth of display data. */
export interface AbilityButton {
  ability: AbilityDto;
  /** Why it can't be queued right now (cooldown, already queued, AP), or null. */
  unavailable: string | null;
  cooldownLeft: number;
  armed: boolean;
  tooltip: string;
}

/**
 * The selected combatant's abilities (3B.3): one button each, with its AP cost and cooldown.
 * Clicking a ready ability arms it (the next grid click picks its target); clicking the armed one
 * disarms it. Presentational: the parent dispatches ArmAbility / DisarmAbility.
 */
@Component({
  selector: 'app-ability-bar',
  imports: [],
  templateUrl: './ability-bar.component.html',
  styleUrl: './ability-bar.component.scss'
})
export class AbilityBarComponent {
  @Input() combatant: Combatant | null = null;
  @Input() queue: PlannedActionEntry[] = [];
  @Input() apCosts: Record<string, number> = { ...DEFAULT_AP_COSTS };
  @Input() armedAbilityId: string | null = null;
  @Input() disabled = false;

  /** The ability clicked: arm it, or disarm it if it was armed. */
  @Output() toggle = new EventEmitter<string>();

  get buttons(): AbilityButton[] {
    const c = this.combatant;
    if (!c) return [];
    return c.abilities.map(ability => {
      const unavailable = abilityUnavailableReason(ability, this.queue, c.maxAp, this.apCosts, c.abilities, c.cooldowns);
      return {
        ability,
        unavailable,
        cooldownLeft: c.cooldowns[ability.id] ?? 0,
        armed: this.armedAbilityId === ability.id,
        tooltip: AbilityBarComponent.tooltip(ability, unavailable),
      };
    });
  }

  onClick(button: AbilityButton): void {
    if (this.disabled || (button.unavailable && !button.armed)) return;
    this.toggle.emit(button.ability.id);
  }

  static tooltip(ability: AbilityDto, unavailable: string | null): string {
    const t = ability.targeting;
    const who = t.filter === 'enemy' ? 'an enemy' : t.filter === 'ally' ? 'an ally' : t.filter === 'downed_ally' ? 'a downed ally' : 'anyone';
    const reach = t.range === 1 ? 'adjacent' : `within ${t.range} cells`;
    const cooldown = ability.cooldown > 0 ? ` · cooldown ${ability.cooldown} ${ability.cooldown === 1 ? 'round' : 'rounds'}` : '';
    const lines = [
      `${ability.name} — ${ability.apCost} AP${cooldown}`,
      ability.description,
      `Target: ${who} ${reach}`,
    ];
    if (unavailable) lines.push(`(${unavailable})`);
    return lines.filter(l => l).join('\n');
  }
}
