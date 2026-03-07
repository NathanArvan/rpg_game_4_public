import { ConnectedPlayer } from './session.model';

export class JoinSession {
  static readonly type = '[Session] Join';
  constructor(public sessionId: string, public displayName: string) {}
}

export class PlayerPresenceChanged {
  static readonly type = '[Session] Player Presence Changed';
  constructor(public player: ConnectedPlayer) {}
}
