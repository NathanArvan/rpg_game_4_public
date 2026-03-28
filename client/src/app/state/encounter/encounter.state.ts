import { Injectable } from '@angular/core';
import { Action, Selector, State, StateContext } from '@ngxs/store';
import { AttackResolved, ClearEncounter, CombatantDefeated, EncounterStarted, TurnAdvanced } from './encounter.actions';
import { Combatant, EncounterStateModel } from './encounter.model';
import { CombatantDto } from '../../contracts/hub-contracts.generated';

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
    const mapped: Combatant[] = combatants.map((c: CombatantDto) => ({ ...c, isDefeated: false }));
    ctx.patchState({ encounterId, combatants: mapped, active: true, activeTurnCombatantId });
  }

  @Action(TurnAdvanced)
  turnAdvanced(ctx: StateContext<EncounterStateModel>, { activeTurnCombatantId }: TurnAdvanced) {
    ctx.patchState({ activeTurnCombatantId });
  }

  @Action(AttackResolved)
  attackResolved(ctx: StateContext<EncounterStateModel>, { targetCombatantId, targetNewHp }: AttackResolved) {
    const state = ctx.getState();
    const combatants = state.combatants.map(c =>
      c.id === targetCombatantId ? { ...c, currentHp: targetNewHp } : c
    );
    ctx.patchState({ combatants });
  }

  @Action(CombatantDefeated)
  combatantDefeated(ctx: StateContext<EncounterStateModel>, { combatantId }: CombatantDefeated) {
    const state = ctx.getState();
    const combatants = state.combatants.map(c =>
      c.id === combatantId ? { ...c, isDefeated: true } : c
    );
    ctx.patchState({ combatants });
  }

  @Action(ClearEncounter)
  clearEncounter(ctx: StateContext<EncounterStateModel>) {
    ctx.patchState({ encounterId: null, combatants: [], active: false, activeTurnCombatantId: null });
  }
}
