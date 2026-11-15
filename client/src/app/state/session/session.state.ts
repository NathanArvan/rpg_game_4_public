import { Injectable } from '@angular/core';
import { Action, Selector, State, StateContext } from '@ngxs/store';
import { JoinSession, LeaveQuickBattle, PlayerPresenceChanged, SetGmSession, SetQuickBattleSession } from './session.actions';
import { ConnectedPlayer, SessionStateModel } from './session.model';

@State<SessionStateModel>({
  name: 'session',
  defaults: {
    campaignId: null,
    gameSessionId: null,
    joinCode: null,
    isGm: false,
    status: 'disconnected',
    connectedPlayers: [],
    quickBattlePresetId: null,
  },
})
@Injectable()
export class SessionState {
  @Selector()
  static connectedPlayers(state: SessionStateModel): ConnectedPlayer[] {
    return state.connectedPlayers;
  }

  @Selector()
  static status(state: SessionStateModel): string {
    return state.status;
  }

  @Selector()
  static joinCode(state: SessionStateModel): string | null {
    return state.joinCode;
  }

  @Selector()
  static campaignId(state: SessionStateModel): string | null {
    return state.campaignId;
  }

  @Selector()
  static gameSessionId(state: SessionStateModel): string | null {
    return state.gameSessionId;
  }

  @Selector()
  static isGm(state: SessionStateModel): boolean {
    return state.isGm;
  }

  @Action(JoinSession)
  join(ctx: StateContext<SessionStateModel>, { joinCode }: JoinSession) {
    ctx.patchState({ joinCode, status: 'connecting' });
  }

  @Action(SetGmSession)
  setGmSession(ctx: StateContext<SessionStateModel>, { campaignId, gameSessionId, joinCode }: SetGmSession) {
    ctx.patchState({ campaignId, gameSessionId, joinCode, isGm: true });
  }

  @Selector()
  static quickBattlePresetId(state: SessionStateModel): string | null {
    return state.quickBattlePresetId;
  }

  @Action(SetQuickBattleSession)
  setQuickBattleSession(ctx: StateContext<SessionStateModel>, { campaignId, gameSessionId, joinCode, presetId }: SetQuickBattleSession) {
    ctx.patchState({ campaignId, gameSessionId, joinCode, isGm: false, status: 'connected', quickBattlePresetId: presetId });
  }

  @Action(LeaveQuickBattle)
  leaveQuickBattle(ctx: StateContext<SessionStateModel>) {
    if (!ctx.getState().quickBattlePresetId) return;
    ctx.patchState({ campaignId: null, gameSessionId: null, joinCode: null, isGm: false, quickBattlePresetId: null });
  }

  @Action(PlayerPresenceChanged)
  presenceChanged(ctx: StateContext<SessionStateModel>, { player }: PlayerPresenceChanged) {
    const players = ctx
      .getState()
      .connectedPlayers.filter((p) => p.userId !== player.userId)
      .concat(player.isOnline ? [player] : []);
    ctx.patchState({ connectedPlayers: players, status: 'connected' });
  }
}
