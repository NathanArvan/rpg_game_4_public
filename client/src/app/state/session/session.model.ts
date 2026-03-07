export interface ConnectedPlayer {
  userId: string;
  displayName: string;
  isOnline: boolean;
}

export interface SessionStateModel {
  sessionId: string | null;
  status: 'disconnected' | 'connecting' | 'connected';
  connectedPlayers: ConnectedPlayer[];
}
