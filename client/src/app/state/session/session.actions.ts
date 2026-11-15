import { ConnectedPlayer } from './session.model';

export class JoinSession {
  static readonly type = '[Session] Join';
  constructor(public joinCode: string, public displayName: string, public characterId: string | null) {}
}

export class SetGmSession {
  static readonly type = '[Session] Set GM Session';
  constructor(
    public campaignId: string,
    public gameSessionId: string,
    public joinCode: string
  ) {}
}

/**
 * A Quick Battle session. The server makes the user its GM (so Force Resolve works), but the screen
 * shows the player view, so isGm stays false here.
 */
export class SetQuickBattleSession {
  static readonly type = '[Session] Set Quick Battle Session';
  constructor(
    public campaignId: string,
    public gameSessionId: string,
    public joinCode: string,
    public presetId: string
  ) {}
}

export class LeaveQuickBattle {
  static readonly type = '[Session] Leave Quick Battle';
}

export class PlayerPresenceChanged {
  static readonly type = '[Session] Player Presence Changed';
  constructor(public player: ConnectedPlayer) {}
}
