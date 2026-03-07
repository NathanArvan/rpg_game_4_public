import { Injectable } from '@angular/core';
import { Action, Selector, State, StateContext } from '@ngxs/store';
import { JoinSession, PlayerPresenceChanged } from './session.actions';
import { ConnectedPlayer, SessionStateModel } from './session.model';

@State<SessionStateModel>({
  name: 'session',
  defaults: {
    sessionId: null,
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

  @Action(JoinSession)
  join(ctx: StateContext<SessionStateModel>, { sessionId }: JoinSession) {
    ctx.patchState({ sessionId, status: 'connecting' });
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
