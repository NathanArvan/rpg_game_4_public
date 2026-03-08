/**
 * SignalR hub payload types — hand-maintained to mirror C# DTOs in
 * server/src/RpgGame.Api/Contracts/Hub/. When a C# hub payload record changes,
 * update the matching interface here.
 */

export interface PlayerPresenceChangedPayload {
  userId: string;
  displayName: string;
  characterId: string | null;
  isOnline: boolean;
}

export interface CombatantDto {
  id: string;
  userId: string;
  characterId: string;
  characterName: string;
  initiative: number;
  initiativeRoll: number;
  dexModifier: number;
  currentHp: number;
  maxHp: number;
}

export interface PositionDto {
  x: number;
  y: number;
}

export interface MapDto {
  gridWidth: number;
  gridHeight: number;
  positions: Record<string, PositionDto>;
  fogOfWar: string[];
}

export interface EncounterStartedPayload {
  encounterId: string;
  activeTurnCombatantId: string;
  combatants: CombatantDto[];
  map: MapDto;
}

export interface TokenMovedPayload {
  combatantId: string;
  x: number;
  y: number;
}

export interface TurnAdvancedPayload {
  activeTurnCombatantId: string;
}

export interface AttackResolvedPayload {
  attackerId: string;
  attackerName: string;
  targetId: string;
  targetName: string;
  damage: number;
  targetNewHp: number;
  feedText: string;
}

export interface CombatantDefeatedPayload {
  combatantId: string;
}

export interface ActionRejectedPayload {
  reason: string;
}
