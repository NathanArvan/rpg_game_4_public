import { Injectable } from '@angular/core';
import { Action, Selector, State, StateContext } from '@ngxs/store';
import {
  AllLockedIn,
  ClearEncounter,
  ClearMyPlannedAction,
  CombatantDefeated,
  EncounterStarted,
  PlayerLockedIn,
  PlanningPhaseStarted,
  ResolutionCompleted,
  SetMyPlannedAction,
} from './encounter.actions';
import { Combatant, EncounterStateModel } from './encounter.model';
import { CombatantDto, PlannedActionEntry, PlanningCombatantDto, TickResult } from '../../contracts/hub-contracts.generated';

@State<EncounterStateModel>({
  name: 'encounter',
  defaults: {
    encounterId: null,
    active: false,
    roundPhase: null,
    roundNumber: 0,
    combatants: [],
    myPlannedActions: [],
    lockedInCombatantNames: [],
    planningTimerExpiresAt: null,
    resolutionTicks: [],
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
  static roundPhase(state: EncounterStateModel): string | null {
    return state.roundPhase;
  }

  @Selector()
  static roundNumber(state: EncounterStateModel): number {
    return state.roundNumber;
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
  static myPlannedActions(state: EncounterStateModel): PlannedActionEntry[] {
    return state.myPlannedActions;
  }

  @Selector()
  static lockedInCombatantNames(state: EncounterStateModel): string[] {
    return state.lockedInCombatantNames;
  }

  @Selector()
  static planningTimerExpiresAt(state: EncounterStateModel): string | null {
    return state.planningTimerExpiresAt;
  }

  @Selector()
  static resolutionTicks(state: EncounterStateModel): TickResult[] {
    return state.resolutionTicks;
  }

  @Action(EncounterStarted)
  encounterStarted(ctx: StateContext<EncounterStateModel>, { encounterId, combatants }: EncounterStarted) {
    const mapped: Combatant[] = combatants.map((c: CombatantDto) => ({ ...c, isDefeated: false }));
    ctx.patchState({ encounterId, combatants: mapped, active: true });
  }

  @Action(PlanningPhaseStarted)
  planningPhaseStarted(ctx: StateContext<EncounterStateModel>, { roundNumber, planningTimerDurationSeconds, combatants }: PlanningPhaseStarted) {
    console.log('[encounter] PlanningPhaseStarted: round=', roundNumber, 'combatants=', combatants.length);
    const expiresAt = new Date(Date.now() + planningTimerDurationSeconds * 1000).toISOString();
    const mapped: Combatant[] = combatants.map((c: PlanningCombatantDto) => ({
      id: c.id,
      userId: c.userId,
      characterId: '',
      characterName: c.characterName,
      currentHp: c.currentHp,
      maxHp: c.maxHp,
      isDefeated: c.isDefeated,
    }));
    ctx.patchState({
      roundPhase: 'planning',
      roundNumber,
      combatants: mapped,
      myPlannedActions: [],
      lockedInCombatantNames: [],
      planningTimerExpiresAt: expiresAt,
      resolutionTicks: [],
    });
  }

  @Action(ResolutionCompleted)
  resolutionCompleted(ctx: StateContext<EncounterStateModel>, { payload }: ResolutionCompleted) {
    console.log('[encounter] ResolutionCompleted: ticks=', payload.ticks.length, 'defeated=', payload.defeatedCombatantIds);
    // Update combatant HP and defeated flags from resolution results
    const state = ctx.getState();
    const hpMap = new Map<string, number>();
    const defeatedSet = new Set<string>(payload.defeatedCombatantIds);

    for (const tick of payload.ticks) {
      for (const attack of tick.attacks) {
        if (!attack.wasted) {
          hpMap.set(attack.targetId, attack.targetNewHp);
        }
      }
    }

    const combatants: Combatant[] = state.combatants.map(c => ({
      ...c,
      currentHp: hpMap.has(c.id) ? hpMap.get(c.id)! : c.currentHp,
      isDefeated: c.isDefeated || defeatedSet.has(c.id),
    }));

    ctx.patchState({
      roundPhase: 'resolving',
      resolutionTicks: payload.ticks,
      combatants,
    });
  }

  @Action(PlayerLockedIn)
  playerLockedIn(ctx: StateContext<EncounterStateModel>, { characterName }: PlayerLockedIn) {
    console.log('[encounter] PlayerLockedIn:', characterName);
    const state = ctx.getState();
    ctx.patchState({ lockedInCombatantNames: [...state.lockedInCombatantNames, characterName] });
  }

  @Action(AllLockedIn)
  allLockedIn(ctx: StateContext<EncounterStateModel>) {
    console.log('[encounter] AllLockedIn: transitioning to resolving');
    ctx.patchState({ roundPhase: 'resolving' });
  }

  @Action(SetMyPlannedAction)
  setMyPlannedAction(ctx: StateContext<EncounterStateModel>, { actions }: SetMyPlannedAction) {
    ctx.patchState({ myPlannedActions: actions });
  }

  @Action(ClearMyPlannedAction)
  clearMyPlannedAction(ctx: StateContext<EncounterStateModel>) {
    ctx.patchState({ myPlannedActions: [] });
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
      active: false,
      roundPhase: null,
      roundNumber: 0,
      combatants: [],
      myPlannedActions: [],
      lockedInCombatantNames: [],
      planningTimerExpiresAt: null,
      resolutionTicks: [],
    });
  }
}
