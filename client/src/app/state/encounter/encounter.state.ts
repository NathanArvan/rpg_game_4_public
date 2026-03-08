import { Injectable } from '@angular/core';
import { Action, Selector, State, StateContext } from '@ngxs/store';
import { ClearEncounter, EncounterStarted } from './encounter.actions';
import { Combatant, EncounterStateModel } from './encounter.model';

@State<EncounterStateModel>({
  name: 'encounter',
  defaults: {
    encounterId: null,
    combatants: [],
    active: false,
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

  @Action(EncounterStarted)
  encounterStarted(ctx: StateContext<EncounterStateModel>, { encounterId, combatants }: EncounterStarted) {
    ctx.patchState({ encounterId, combatants, active: true });
  }

  @Action(ClearEncounter)
  clearEncounter(ctx: StateContext<EncounterStateModel>) {
    ctx.patchState({ encounterId: null, combatants: [], active: false });
  }
}
