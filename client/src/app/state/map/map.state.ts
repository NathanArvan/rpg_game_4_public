import { Injectable } from '@angular/core';
import { Action, Selector, State, StateContext } from '@ngxs/store';
import { ClearPendingMove, MapInitialized, SetPendingMove, TokenMoved } from './map.actions';
import { MapStateModel, Position } from './map.state.model';

@State<MapStateModel>({
  name: 'map',
  defaults: {
    gridWidth: 20,
    gridHeight: 20,
    positions: {},
    fogOfWar: [],
    pendingMove: null,
  },
})
@Injectable()
export class MapState {
  @Selector()
  static gridWidth(state: MapStateModel): number {
    return state.gridWidth;
  }

  @Selector()
  static gridHeight(state: MapStateModel): number {
    return state.gridHeight;
  }

  @Selector()
  static positions(state: MapStateModel): Record<string, Position> {
    return state.positions;
  }

  @Selector()
  static pendingMove(state: MapStateModel): Position | null {
    return state.pendingMove;
  }

  @Action(MapInitialized)
  mapInitialized(ctx: StateContext<MapStateModel>, { gridWidth, gridHeight, positions }: MapInitialized) {
    ctx.patchState({ gridWidth, gridHeight, positions, pendingMove: null });
  }

  @Action(TokenMoved)
  tokenMoved(ctx: StateContext<MapStateModel>, { combatantId, x, y }: TokenMoved) {
    const positions = { ...ctx.getState().positions, [combatantId]: { x, y } };
    ctx.patchState({ positions, pendingMove: null });
  }

  @Action(SetPendingMove)
  setPendingMove(ctx: StateContext<MapStateModel>, { position }: SetPendingMove) {
    ctx.patchState({ pendingMove: position });
  }

  @Action(ClearPendingMove)
  clearPendingMove(ctx: StateContext<MapStateModel>) {
    ctx.patchState({ pendingMove: null });
  }
}
