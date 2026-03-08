import { Injectable } from '@angular/core';
import { Action, Selector, State, StateContext } from '@ngxs/store';
import { ClearEncounter, EncounterStarted, TurnAdvanced } from './encounter.actions';
import { Combatant, EncounterStateModel } from './encounter.model';

@State<EncounterStateModel>({
  name: 'encounter',
  defaults: {
    encounterId: null,
    combatants: [],
    active: false,
    activeTurnCombatantId: null,
  },
})
@Injectable()
export class EncounterState {
  @Selector()
  static combatants(state: EncounterStateModel): Combatant[] {
    return [...state.combatants].sort((a, b) => b.initiative - a.initiative);
  }

  @Selector()
  static active(state: EncounterStateModel): boolean {
    return state.active;
  }

  @Selector()
  static activeTurnCombatantId(state: EncounterStateModel): string | null {
    return state.activeTurnCombatantId;
  }

  @Action(EncounterStarted)
  encounterStarted(ctx: StateContext<EncounterStateModel>, { encounterId, combatants, activeTurnCombatantId }: EncounterStarted) {
    ctx.patchState({ encounterId, combatants, active: true, activeTurnCombatantId });
  }

  @Action(TurnAdvanced)
  turnAdvanced(ctx: StateContext<EncounterStateModel>, { activeTurnCombatantId }: TurnAdvanced) {
    ctx.patchState({ activeTurnCombatantId });
  }

  @Action(ClearEncounter)
  clearEncounter(ctx: StateContext<EncounterStateModel>) {
    ctx.patchState({ encounterId: null, combatants: [], active: false, activeTurnCombatantId: null });
  }
}
