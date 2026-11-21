import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { Store } from '@ngxs/store';
import { CombatLayoutComponent } from '../../combat-layout/combat-layout.component';
import { EncounterStarted, PlanningPhaseStarted, ClearEncounter, PlayerLockedIn } from '../../../../state/encounter/encounter.actions';
import { MapInitialized } from '../../../../state/map/map.actions';
import { AddFeedBatch, ClearFeed } from '../../../../state/feed/feed.actions';
import { ResolutionPlaybackService } from '../../../../core/services/resolution-playback.service';
import { CombatantDto, PlanningCombatantDto, ResolutionPayload } from '../../../../contracts/hub-contracts.generated';

const ENCOUNTER_ID = 'preview-encounter-1';
const USER_PLAYER = 'user-player-1';
const USER_MONSTER = 'user-gm';

const COMBATANTS_DTO: CombatantDto[] = [
  { id: 'c1', userId: USER_PLAYER, characterId: 'ch1', characterName: 'Zara Forge', currentHp: 8, maxHp: 10, combatantType: 'player', isAiControlled: false, profileId: 'bruiser', profileName: 'Bruiser', movementRange: 4, maxAp: 4 },
  { id: 'c2', userId: 'user-player-2', characterId: 'ch2', characterName: 'Isolde Blade', currentHp: 4, maxHp: 6, combatantType: 'player', isAiControlled: false, profileId: 'scout', profileName: 'Scout', movementRange: 8, maxAp: 5 },
  { id: 'c3', userId: USER_MONSTER, characterId: '', characterName: 'Goblin 1', currentHp: 17, maxHp: 20, combatantType: 'monster', isAiControlled: true, profileId: 'grunt', profileName: 'Grunt', movementRange: 5, maxAp: 4 },
  { id: 'c4', userId: USER_MONSTER, characterId: '', characterName: 'Goblin 2', currentHp: 17, maxHp: 20, combatantType: 'monster', isAiControlled: false, profileId: 'brute', profileName: 'Brute', movementRange: 4, maxAp: 4 },
];

const PLANNING_COMBATANTS: PlanningCombatantDto[] = COMBATANTS_DTO.map(c => ({
  id: c.id,
  userId: c.userId,
  characterName: c.characterName,
  currentHp: c.currentHp,
  maxHp: c.maxHp,
  isDefeated: false,
  combatantType: c.combatantType,
  isAiControlled: c.isAiControlled,
  profileId: c.profileId,
  profileName: c.profileName,
  movementRange: c.movementRange,
  maxAp: c.maxAp,
}));

const MOCK_POSITIONS: Record<string, { x: number; y: number }> = {
  c1: { x: 8, y: 10 },
  c2: { x: 1, y: 8 },
  c3: { x: 5, y: 13 },
  c4: { x: 6, y: 16 },
};

/** Earlier rounds' log, oldest line first (the feed shows the newest on top). */
const MOCK_FEED = [
  '— Round 2 Resolution —',
  'Zara Forge moves to (8, 10)',
  'Goblin 1 moves to (5, 14)',
  '— Round 3 Resolution —',
  'Isolde Blade shoots at Goblin 1 for 3 damage (HP: 20 → 17)',
  'Goblin 2 shoots at Zara Forge for 2 damage (HP: 10 → 8)',
  'Zara Forge shoots at Goblin 2 for 3 damage (HP: 20 → 17)',
  'Goblin 1 shoots at Isolde Blade for 2 damage (HP: 6 → 4)',
];

const MOCK_RESOLUTION: ResolutionPayload = {
  newRoundNumber: 4,
  defeatedCombatantIds: [],
  updatedCombatants: PLANNING_COMBATANTS,
  ticks: [
    {
      tickIndex: 0,
      movements: [
        { combatantId: 'c1', combatantName: 'Zara Forge', path: [{ x: 8, y: 10 }, { x: 7, y: 11 }, { x: 6, y: 12 }], finalPosition: { x: 6, y: 12 }, wasBlocked: false, blockedByCombatantId: null },
        { combatantId: 'c3', combatantName: 'Goblin 1', path: [{ x: 5, y: 13 }], finalPosition: { x: 5, y: 13 }, wasBlocked: true, blockedByCombatantId: 'c1' },
      ],
      attacks: [
        { attackerId: 'c1', attackerName: 'Zara Forge', targetId: 'c3', targetName: 'Goblin 1', attackType: 'ranged_attack', hit: true, damage: 3, targetNewHp: 14, targetDefeated: false, wasted: false, feedText: 'Zara Forge shoots Goblin 1 for 3 damage' },
        { attackerId: 'c3', attackerName: 'Goblin 1', targetId: 'c2', targetName: 'Isolde Blade', attackType: 'ranged_attack', hit: true, damage: 2, targetNewHp: 2, targetDefeated: false, wasted: false, feedText: 'Goblin 1 shoots Isolde Blade for 2 damage' },
      ],
      effects: [],
    },
    {
      // A second tick (3B.2): Zara closes in and strikes.
      tickIndex: 1,
      movements: [],
      attacks: [
        { attackerId: 'c1', attackerName: 'Zara Forge', targetId: 'c3', targetName: 'Goblin 1', attackType: 'melee_attack', hit: true, damage: 5, targetNewHp: 9, targetDefeated: false, wasted: false, feedText: 'Zara Forge strikes Goblin 1 for 5 damage' },
      ],
      effects: [],
    },
  ],
};

@Component({
  selector: 'app-encounter-preview',
  standalone: true,
  imports: [CommonModule, CombatLayoutComponent],
  templateUrl: './encounter-preview.component.html',
  styleUrl: './encounter-preview.component.scss',
})
export class EncounterPreviewComponent implements OnInit {
  isGm = false;
  phase: 'planning' | 'resolving' = 'planning';

  readonly myUserIdPlayer = USER_PLAYER;
  readonly myUserIdGm = USER_MONSTER;

  get myUserId(): string {
    return this.isGm ? this.myUserIdGm : this.myUserIdPlayer;
  }

  constructor(private store: Store, private playback: ResolutionPlaybackService) {}

  ngOnInit(): void {
    this.seedState();
  }

  toggleRole(): void {
    this.isGm = !this.isGm;
  }

  setPhase(phase: 'planning' | 'resolving'): void {
    this.phase = phase;
    this.playback.reset();
    if (phase === 'planning') {
      this.store.dispatch(new PlanningPhaseStarted(4, 300, PLANNING_COMBATANTS));
    } else {
      this.playback.receiveResolution(MOCK_RESOLUTION);
    }
  }

  reset(): void {
    this.phase = 'planning';
    this.store.dispatch(new ClearEncounter());
    this.store.dispatch(new ClearFeed());
    setTimeout(() => this.seedState(), 0);
  }

  private seedState(): void {
    this.store.dispatch(new EncounterStarted(ENCOUNTER_ID, COMBATANTS_DTO));
    this.store.dispatch(new MapInitialized(ENCOUNTER_ID, 20, 20, MOCK_POSITIONS));
    this.store.dispatch(new PlanningPhaseStarted(4, 300, PLANNING_COMBATANTS));
    this.store.dispatch(new AddFeedBatch(MOCK_FEED));
    this.store.dispatch(new PlayerLockedIn('Isolde Blade'));
  }
}
