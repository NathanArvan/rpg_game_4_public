import { Injectable } from '@angular/core';
import { Action, Selector, State, StateContext } from '@ngxs/store';
import {
  ActionPlanned,
  AllLockedIn,
  AttackResolved,
  ClearEncounter,
  ClearPlannedAction,
  CombatantDefeated,
  EncounterStarted,
  PlayerLockedIn,
} from './encounter.actions';
import { Combatant, EncounterStateModel, PlannedAction } from './encounter.model';
import { CombatantDto } from '../../contracts/hub-contracts.generated';

@State<EncounterStateModel>({
  name: 'encounter',
  defaults: {
    encounterId: null,
    combatants: [],
    active: false,
    roundPhase: null,
    roundNumber: 0,
    myPlannedAction: null,
    lockedInCombatantIds: [],
    planningTimerExpiresAt: null,
  },
})
@Injectable()
export class EncounterState {
  @Selector()
  static combatants(state: EncounterStateModel): Combatant[] {
    return state.combatants;
  }

  @Selector()
  static active(state: EncounterStateModel): boolean {
    return state.active;
  }

  @Selector()
  static isPlanning(state: EncounterStateModel): boolean {
    return state.roundPhase === 'planning';
  }

  @Selector()
  static isResolving(state: EncounterStateModel): boolean {
    return state.roundPhase === 'resolving';
  }

  @Selector()
  static myPlannedAction(state: EncounterStateModel): PlannedAction | null {
    return state.myPlannedAction;
  }

  @Selector()
  static lockedInCombatantIds(state: EncounterStateModel): string[] {
    return state.lockedInCombatantIds;
  }

  @Selector()
  static planningTimerExpiresAt(state: EncounterStateModel): number | null {
    return state.planningTimerExpiresAt;
  }

  @Selector()
  static roundNumber(state: EncounterStateModel): number {
    return state.roundNumber;
  }

  @Selector()
  static roundPhase(state: EncounterStateModel): string | null {
    return state.roundPhase;
  }

  @Action(EncounterStarted)
  encounterStarted(ctx: StateContext<EncounterStateModel>, action: EncounterStarted) {
    const mapped: Combatant[] = action.combatants.map((c: CombatantDto) => ({ ...c, isDefeated: false }));
    ctx.patchState({
      encounterId: action.encounterId,
      combatants: mapped,
      active: true,
      roundNumber: action.roundNumber,
      roundPhase: action.roundPhase,
      planningTimerExpiresAt: action.planningTimerExpiresAt,
      myPlannedAction: null,
      lockedInCombatantIds: [],
    });
  }

  @Action(ActionPlanned)
  actionPlanned(ctx: StateContext<EncounterStateModel>, { action }: ActionPlanned) {
    ctx.patchState({ myPlannedAction: action });
  }

  @Action(ClearPlannedAction)
  clearPlannedAction(ctx: StateContext<EncounterStateModel>) {
    ctx.patchState({ myPlannedAction: null });
  }

  @Action(PlayerLockedIn)
  playerLockedIn(ctx: StateContext<EncounterStateModel>, { combatantId }: PlayerLockedIn) {
    const state = ctx.getState();
    if (!state.lockedInCombatantIds.includes(combatantId)) {
      ctx.patchState({ lockedInCombatantIds: [...state.lockedInCombatantIds, combatantId] });
    }
  }

  @Action(AllLockedIn)
  allLockedIn(ctx: StateContext<EncounterStateModel>) {
    ctx.patchState({ roundPhase: 'resolving' });
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
    ctx.patchState({
      encounterId: null,
      combatants: [],
      active: false,
      roundPhase: null,
      roundNumber: 0,
      myPlannedAction: null,
      lockedInCombatantIds: [],
      planningTimerExpiresAt: null,
    });
  }
}
