// AUTO-GENERATED — do not edit by hand.
// Source: C# records in server/src/RpgGame.Api/Contracts/Hub/
// Regenerate: npm run contracts:hub
export interface ActionRejectedPayload {
  reason: string;
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

export interface EncounterEndedPayload {
  encounterId: string;
}

export interface EncounterStartedPayload {
  encounterId: string;
  activeTurnCombatantId: string;
  combatants: CombatantDto[];
  map: MapDto;
}

export interface PositionDto {
  x: number;
  y: number;
}

export interface MapDto {
  gridWidth: number;
  gridHeight: number;
  positions: { [key: string]: PositionDto };
  fogOfWar: string[];
}

export interface PlayerPresenceChangedPayload {
  userId: string;
  displayName: string;
  characterId: string | null;
  isOnline: boolean;
}

export interface TokenMovedPayload {
  combatantId: string;
  x: number;
  y: number;
}

export interface TurnAdvancedPayload {
  activeTurnCombatantId: string;
}

export const HubEvents = {
  Events: {
    PlayerPresenceChanged: 'PlayerPresenceChanged',
    EncounterStarted: 'EncounterStarted',
    TokenMoved: 'TokenMoved',
    TurnAdvanced: 'TurnAdvanced',
    ActionRejected: 'ActionRejected',
    AttackResolved: 'AttackResolved',
    CombatantDefeated: 'CombatantDefeated',
    EncounterEnded: 'EncounterEnded',
  },
  Methods: {
    JoinSession: 'JoinSession',
    StartEncounter: 'StartEncounter',
    MoveToken: 'MoveToken',
    DeclareAttack: 'DeclareAttack',
    EndTurn: 'EndTurn',
  },
} as const;
