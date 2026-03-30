import { Position } from './map.state.model';

export class MapInitialized {
  static readonly type = '[Map] Initialized';
  constructor(
    public encounterId: string,
    public gridWidth: number,
    public gridHeight: number,
    public positions: Record<string, Position>
  ) {}
}

export class ApplyResolutionPositions {
  static readonly type = '[Map] Apply Resolution Positions';
  constructor(public positions: Record<string, Position>) {}
}
