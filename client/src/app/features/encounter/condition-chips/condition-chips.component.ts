import { Component, Input } from '@angular/core';
import { ActiveConditionDto } from '../../../contracts/hub-contracts.generated';
import { activeConditionTooltip, shortDuration } from './condition-format';

/**
 * A combatant's conditions as small chips (3B.4): red for harmful, blue for beneficial, with how
 * long each lasts in words and a tooltip saying what it does. `compact` shows only a dot per
 * condition with its initial, for under a token.
 */
@Component({
  selector: 'app-condition-chips',
  imports: [],
  templateUrl: './condition-chips.component.html',
  styleUrl: './condition-chips.component.scss',
})
export class ConditionChipsComponent {
  @Input() conditions: readonly ActiveConditionDto[] = [];
  @Input() compact = false;

  readonly tooltip = activeConditionTooltip;
  readonly duration = shortDuration;
}
