import { Injectable, NgZone } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { Store } from '@ngxs/store';
import { PlayerPresenceChanged } from '../../state/session/session.actions';
import {
  AllLockedIn,
  ClearMyPlannedAction,
  CombatantDefeated,
  EncounterEnded,
  EncounterStarted,
  MonsterAdded,
  PlayerLockedIn,
  PlanningPhaseStarted,
} from '../../state/encounter/encounter.actions';
import { MapInitialized } from '../../state/map/map.actions';
import { AddFeedBatch } from '../../state/feed/feed.actions';
import { BATTLE_START_LINE } from '../../features/encounter/feed/feed-format';
import { AuthService } from './auth.service';
import { ResolutionPlaybackService } from './resolution-playback.service';
import {
  ActionRejectedPayload,
  AllLockedInPayload,
  CombatantDefeatedPayload,
  EncounterEndedPayload,
  EncounterStartedPayload,
  HubEvents,
  MonsterAddedPayload,
  PlannedActionArray,
  PlayerLockedInPayload,
  PlayerPresenceChangedPayload,
  PlanningPhaseStartedPayload,
  QuickBattlePresetDto,
  QuickBattleStartedResult,
  ResolutionPayload,
} from '../../contracts/hub-contracts.generated';

@Injectable({ providedIn: 'root' })
export class GameHubService {
  private connection: HubConnection;

  constructor(
    private store: Store,
    private auth: AuthService,
    private ngZone: NgZone,
    private playback: ResolutionPlaybackService
  ) {
    this.connection = new HubConnectionBuilder()
      .withUrl('/hubs/game', { accessTokenFactory: () => this.auth.getToken() })
      .withAutomaticReconnect()
      .build();

    this.connection.on(HubEvents.Events.PlayerPresenceChanged, (payload: PlayerPresenceChangedPayload) => {
      console.log('[hub] PlayerPresenceChanged received:', payload);
      this.ngZone.run(() => this.store.dispatch(new PlayerPresenceChanged(payload)));
    });

    this.connection.on(HubEvents.Events.EncounterStarted, (payload: EncounterStartedPayload) => {
      console.log('[hub] EncounterStarted received:', payload);
      this.ngZone.run(() => {
        this.store.dispatch(new EncounterStarted(payload.encounterId, payload.combatants));
        this.store.dispatch(new AddFeedBatch([BATTLE_START_LINE]));
        if (payload.map) {
          this.store.dispatch(new MapInitialized(
            payload.encounterId,
            payload.map.gridWidth,
            payload.map.gridHeight,
            payload.map.positions
          ));
        }
      });
    });

    this.connection.on(HubEvents.Events.PlanningPhaseStarted, (payload: PlanningPhaseStartedPayload) => {
      console.log('[hub] PlanningPhaseStarted received:', payload);
      this.whenPlaybackDone(() => this.store.dispatch(new PlanningPhaseStarted(
        payload.roundNumber,
        payload.planningTimerDurationSeconds,
        payload.combatants,
        payload.apCosts
      )));
    });

    this.connection.on(HubEvents.Events.PlayerLockedIn, (payload: PlayerLockedInPayload) => {
      console.log('[hub] PlayerLockedIn received:', payload);
      this.whenPlaybackDone(() => this.store.dispatch(new PlayerLockedIn(payload.characterName)));
    });

    this.connection.on(HubEvents.Events.AllLockedIn, (_payload: AllLockedInPayload) => {
      console.log('[hub] AllLockedIn received');
      this.whenPlaybackDone(() => this.store.dispatch(new AllLockedIn()));
    });

    this.connection.on(HubEvents.Events.ResolutionCompleted, (payload: ResolutionPayload) => {
      console.log('[hub] ResolutionCompleted received:', payload);
      // Updates the store (HP, positions) and plays the round back tick by tick, feed included.
      this.ngZone.run(() => this.playback.receiveResolution(payload));
    });

    this.connection.on(HubEvents.Events.ActionRejected, (payload: ActionRejectedPayload) => {
      console.warn('[hub] ActionRejected:', payload);
      // Surface it in the feed so a rejected plan (e.g. over the AP budget) isn't silent.
      this.ngZone.run(() => this.store.dispatch(new AddFeedBatch([`⚠ ${payload.reason}`])));
    });

    this.connection.on(HubEvents.Events.CombatantDefeated, (payload: CombatantDefeatedPayload) => {
      console.log('[hub] CombatantDefeated received:', payload);
      this.whenPlaybackDone(() => this.store.dispatch(new CombatantDefeated(payload.combatantId)));
    });

    this.connection.on(HubEvents.Events.EncounterEnded, (payload: EncounterEndedPayload) => {
      console.log('[hub] EncounterEnded received:', payload);
      this.whenPlaybackDone(() => {
        this.store.dispatch(new EncounterEnded(payload.winningSide, payload.finalRoundNumber));
        this.store.dispatch(new AddFeedBatch([`— Encounter over after round ${payload.finalRoundNumber}: ${
          payload.winningSide === 'party' ? 'the party wins' : payload.winningSide === 'enemy' ? 'the enemies win' : 'draw'
        } —`]));
      });
    });

    this.connection.on(HubEvents.Events.MonsterAdded, (payload: MonsterAddedPayload) => {
      console.log('[hub] MonsterAdded received:', payload);
      this.whenPlaybackDone(() => this.store.dispatch(new MonsterAdded(payload)));
    });

    this.connection.onclose((err) => console.error('[hub] connection closed:', err));
    this.connection.onreconnecting((err) => console.warn('[hub] reconnecting:', err));
    this.connection.onreconnected((id) => console.log('[hub] reconnected, connectionId:', id));
  }

  /**
   * Encounter events that arrive while a resolution is playing back wait until it ends, so the
   * screen doesn't jump to the next round (or the result) mid-animation.
   */
  private whenPlaybackDone(apply: () => void): void {
    this.ngZone.run(() => this.playback.afterPlayback(apply));
  }

  isConnected(): boolean {
    return this.connection.state === HubConnectionState.Connected;
  }

  async connect(): Promise<void> {
    console.log('[hub] connect() called, state:', this.connection.state);
    if (this.connection.state === HubConnectionState.Disconnected) {
      await this.connection.start();
      console.log('[hub] connection started, state:', this.connection.state);
    }
  }

  async joinSession(joinCode: string, displayName: string, characterId: string | null): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.JoinSession, joinCode, displayName, characterId);
  }

  async startEncounter(joinCode: string): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.StartEncounter, joinCode);
  }

  /** @param combatantId which of your characters this is for (null if you control only one) */
  async planAction(joinCode: string, action: PlannedActionArray, combatantId: string | null): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.PlanAction, joinCode, action, combatantId);
  }

  async getQuickBattlePresets(): Promise<QuickBattlePresetDto[]> {
    return await this.connection.invoke<QuickBattlePresetDto[]>(HubEvents.Methods.GetQuickBattlePresets);
  }

  async startQuickBattle(presetId: string): Promise<QuickBattleStartedResult> {
    return await this.connection.invoke<QuickBattleStartedResult>(HubEvents.Methods.StartQuickBattle, presetId);
  }

  async lockIn(joinCode: string): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.LockIn, joinCode);
  }

  async forceResolve(joinCode: string): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.ForceResolve, joinCode);
  }

  /** @param profileId combat profile preset; null means the server's default enemy profile. */
  async addMonster(joinCode: string, name: string, isAiControlled: boolean, profileId: string | null = null): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.AddMonster, joinCode, name, isAiControlled, profileId);
  }

  async planEnemyAction(joinCode: string, combatantId: string, action: PlannedActionArray): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.PlanEnemyAction, joinCode, combatantId, action);
  }

  async lockInEnemies(joinCode: string): Promise<void> {
    await this.connection.invoke(HubEvents.Methods.LockInEnemies, joinCode);
  }
}
