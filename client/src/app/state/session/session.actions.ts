import { ConnectedPlayer } from './session.model';

export class JoinSession {
  static readonly type = '[Session] Join';
  constructor(public joinCode: string, public displayName: string) {}
}

export class SetGmSession {
  static readonly type = '[Session] Set GM Session';
  constructor(
    public campaignId: string,
    public gameSessionId: string,
    public joinCode: string
  ) {}
}

export class PlayerPresenceChanged {
  static readonly type = '[Session] Player Presence Changed';
  constructor(public player: ConnectedPlayer) {}
}
