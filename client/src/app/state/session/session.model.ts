export interface ConnectedPlayer {
  userId: string;
  displayName: string;
  characterId: string | null;
  isOnline: boolean;
}

export interface SessionStateModel {
  campaignId: string | null;
  gameSessionId: string | null;
  joinCode: string | null;
  isGm: boolean;
  status: 'disconnected' | 'connecting' | 'connected';
  connectedPlayers: ConnectedPlayer[];
}
