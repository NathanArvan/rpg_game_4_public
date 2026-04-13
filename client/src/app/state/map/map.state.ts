import { Injectable } from '@angular/core';
import { Action, Selector, State, StateContext } from '@ngxs/store';
import { ApplyResolutionPositions, MapInitialized } from './map.actions';
import { MapStateModel, Position } from './map.state.model';

@State<MapStateModel>({
  name: 'map',
  defaults: {
    gridWidth: 20,
    gridHeight: 20,
    positions: {},
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

  @Action(MapInitialized)
  mapInitialized(ctx: StateContext<MapStateModel>, { gridWidth, gridHeight, positions }: MapInitialized) {
    ctx.patchState({ gridWidth, gridHeight, positions });
  }

  @Action(ApplyResolutionPositions)
  applyResolutionPositions(ctx: StateContext<MapStateModel>, { positions }: ApplyResolutionPositions) {
    console.log('[map] ApplyResolutionPositions:', JSON.stringify(positions));
    ctx.patchState({ positions });
  }
}
