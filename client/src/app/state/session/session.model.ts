import { PlayerPresenceChangedPayload } from '../../contracts/hub-contracts';

// ConnectedPlayer is an alias for the hub wire type so existing state code keeps working
export type ConnectedPlayer = PlayerPresenceChangedPayload;

export interface SessionStateModel {
  campaignId: string | null;
  gameSessionId: string | null;
  joinCode: string | null;
  isGm: boolean;
  status: 'disconnected' | 'connecting' | 'connected';
  connectedPlayers: ConnectedPlayer[];
}
