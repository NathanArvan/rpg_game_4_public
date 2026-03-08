import { Injectable } from '@angular/core';
import { Action, Selector, State, StateContext } from '@ngxs/store';
import { JoinSession, PlayerPresenceChanged, SetGmSession } from './session.actions';
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

  @Action(PlayerPresenceChanged)
  presenceChanged(ctx: StateContext<SessionStateModel>, { player }: PlayerPresenceChanged) {
    const players = ctx
      .getState()
      .connectedPlayers.filter((p) => p.userId !== player.userId)
      .concat(player.isOnline ? [player] : []);
    ctx.patchState({ connectedPlayers: players, status: 'connected' });
  }
}
