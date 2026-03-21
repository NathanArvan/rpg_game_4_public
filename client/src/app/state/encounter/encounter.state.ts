import { Injectable } from '@angular/core';
import { Action, Selector, State, StateContext } from '@ngxs/store';
import {
  ActionPlanned,
  AllLockedIn,
  AttackResolved,
  ClearEncounter,
  ClearPlannedAction,
  CombatantDefeated,
  EnemyActionPlanned,
  EncounterStarted,
  GmEnemiesLockedIn,
  PlanningPhaseStarted,
  PlaybackFinished,
  PlayerLockedIn,
  ResolutionCompleted,
} from './encounter.actions';
import { Combatant, EncounterStateModel, PlannedAction } from './encounter.model';
import { CombatantDto, ResolutionStepDto } from '../../contracts/hub-contracts.generated';

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
    resolutionSteps: [],
    isPlayingBack: false,
    gmEnemiesLockedIn: false,
    enemyPlannedActions: {},
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

  @Selector()
  static resolutionSteps(state: EncounterStateModel): ResolutionStepDto[] {
    return state.resolutionSteps;
  }

  @Selector()
  static isPlayingBack(state: EncounterStateModel): boolean {
    return state.isPlayingBack;
  }

  @Selector()
  static enemyCombatants(state: EncounterStateModel): Combatant[] {
    return state.combatants.filter(c => c.combatantType === 'monster' && !c.isDefeated);
  }

  @Selector()
  static gmEnemiesLockedIn(state: EncounterStateModel): boolean {
    return state.gmEnemiesLockedIn;
  }

  @Selector()
  static enemyPlannedActions(state: EncounterStateModel): Record<string, string> {
    return state.enemyPlannedActions;
  }

  @Action(EncounterStarted)
  encounterStarted(ctx: StateContext<EncounterStateModel>, action: EncounterStarted) {
    const mapped: Combatant[] = action.combatants.map((c: CombatantDto) => ({
      ...c,
      isDefeated: false,
      combatantType: (c.combatantType === 'monster' ? 'monster' : 'player') as 'player' | 'monster',
    }));
    ctx.patchState({
      encounterId: action.encounterId,
      combatants: mapped,
      active: true,
      roundNumber: action.roundNumber,
      roundPhase: action.roundPhase,
      planningTimerExpiresAt: action.planningTimerExpiresAt,
      myPlannedAction: null,
      lockedInCombatantIds: [],
      gmEnemiesLockedIn: false,
      enemyPlannedActions: {},
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
    if (combatantId === 'gm_enemies') {
      ctx.dispatch(new GmEnemiesLockedIn());
      return;
    }
    const state = ctx.getState();
    if (!state.lockedInCombatantIds.includes(combatantId)) {
      ctx.patchState({ lockedInCombatantIds: [...state.lockedInCombatantIds, combatantId] });
    }
  }

  @Action(EnemyActionPlanned)
  enemyActionPlanned(ctx: StateContext<EncounterStateModel>, { combatantId, actionType }: EnemyActionPlanned) {
    const state = ctx.getState();
    ctx.patchState({ enemyPlannedActions: { ...state.enemyPlannedActions, [combatantId]: actionType } });
  }

  @Action(GmEnemiesLockedIn)
  gmEnemiesLockedIn(ctx: StateContext<EncounterStateModel>) {
    ctx.patchState({ gmEnemiesLockedIn: true });
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

  @Action(ResolutionCompleted)
  resolutionCompleted(ctx: StateContext<EncounterStateModel>, { steps, encounterEnded }: ResolutionCompleted) {
    const state = ctx.getState();
    // Apply HP/defeated deltas from steps
    let combatants = [...state.combatants];
    for (const step of steps) {
      if (step.actionType === 'attack' && step.targetId && step.newHp !== null) {
        combatants = combatants.map(c =>
          c.id === step.targetId
            ? { ...c, currentHp: step.newHp!, isDefeated: step.defeated ? true : c.isDefeated }
            : c
        );
      }
    }
    ctx.patchState({
      resolutionSteps: steps,
      isPlayingBack: true,
      combatants,
      active: !encounterEnded,
    });
  }

  @Action(PlanningPhaseStarted)
  planningPhaseStarted(ctx: StateContext<EncounterStateModel>, action: PlanningPhaseStarted) {
    ctx.patchState({
      roundPhase: 'planning',
      roundNumber: action.roundNumber,
      planningTimerExpiresAt: action.planningTimerExpiresAt,
      myPlannedAction: null,
      lockedInCombatantIds: [],
      resolutionSteps: [],
      isPlayingBack: false,
      gmEnemiesLockedIn: false,
      enemyPlannedActions: {},
    });
  }

  @Action(PlaybackFinished)
  playbackFinished(ctx: StateContext<EncounterStateModel>) {
    ctx.patchState({ isPlayingBack: false });
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
      resolutionSteps: [],
      isPlayingBack: false,
      gmEnemiesLockedIn: false,
      enemyPlannedActions: {},
    });
  }
}
