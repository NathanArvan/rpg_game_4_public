export interface Position {
  x: number;
  y: number;
}

export interface MapStateModel {
  gridWidth: number;
  gridHeight: number;
  positions: Record<string, Position>; // keyed by combatantId
}
