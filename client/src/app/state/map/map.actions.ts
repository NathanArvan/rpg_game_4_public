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

export class TokenMoved {
  static readonly type = '[Map] Token Moved';
  constructor(public combatantId: string, public x: number, public y: number) {}
}

export class SetPendingMove {
  static readonly type = '[Map] Set Pending Move';
  constructor(public position: Position) {}
}

export class ClearPendingMove {
  static readonly type = '[Map] Clear Pending Move';
}

export class BatchUpdatePositions {
  static readonly type = '[Map] Batch Update Positions';
  constructor(public positions: Record<string, Position>) {}
}
