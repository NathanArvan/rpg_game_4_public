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

/** Earlier rounds' log, oldest line first. */
const MOCK_FEED = [
  '— Round 1 Resolution —',
  'Zara Forge moves to (8, 10)',
  'Goblin 1 moves to (5, 14)',
  '— Round 2 Resolution —',
  'Isolde Blade shoots at Goblin 1 for 3 damage (HP: 20 → 17)',
  'Goblin 2 shoots at Zara Forge for 2 damage (HP: 10 → 8)',
  'Zara Forge shoots at Goblin 2 for 3 damage (HP: 20 → 17)',
  'Goblin 1 shoots at Isolde Blade for 2 damage (HP: 6 → 4)',
];

/**
 * A three-tick round (3B.2 queues): action i of every queue resolves in tick i.
 *   Zara (4 AP):   move → melee Goblin 1 → defend
 *   Isolde (5 AP): ranged Goblin 1 → ranged Goblin 2 → defend
 *   Goblin 1 (4):  ranged Isolde → melee Zara           (done after tick 2)
 *   Goblin 2 (4):  move → melee Zara (out of reach: wasted) → defend
 */
const MOCK_RESOLUTION: ResolutionPayload = {
  newRoundNumber: 4,
  defeatedCombatantIds: [],
  updatedCombatants: PLANNING_COMBATANTS,
  queues: [
    { combatantId: 'c1', combatantName: 'Zara Forge', maxAp: 4, actions: [
      { type: 'move', apCost: 1, targetCombatantId: null, targetCell: { x: 6, y: 12 } },
      { type: 'melee_attack', apCost: 2, targetCombatantId: 'c3', targetCell: null },
      { type: 'defend', apCost: 1, targetCombatantId: null, targetCell: null },
    ] },
    { combatantId: 'c2', combatantName: 'Isolde Blade', maxAp: 5, actions: [
      { type: 'ranged_attack', apCost: 2, targetCombatantId: 'c3', targetCell: null },
      { type: 'ranged_attack', apCost: 2, targetCombatantId: 'c4', targetCell: null },
      { type: 'defend', apCost: 1, targetCombatantId: null, targetCell: null },
    ] },
    { combatantId: 'c3', combatantName: 'Goblin 1', maxAp: 4, actions: [
      { type: 'ranged_attack', apCost: 2, targetCombatantId: 'c2', targetCell: null },
      { type: 'melee_attack', apCost: 2, targetCombatantId: 'c1', targetCell: null },
    ] },
    { combatantId: 'c4', combatantName: 'Goblin 2', maxAp: 4, actions: [
      { type: 'move', apCost: 1, targetCombatantId: null, targetCell: { x: 6, y: 14 } },
      { type: 'melee_attack', apCost: 2, targetCombatantId: 'c1', targetCell: null },
      { type: 'defend', apCost: 1, targetCombatantId: null, targetCell: null },
    ] },
  ],
  ticks: [
    {
      tickIndex: 0,
      movements: [
        { combatantId: 'c1', combatantName: 'Zara Forge', path: [{ x: 8, y: 10 }, { x: 7, y: 11 }, { x: 6, y: 12 }], finalPosition: { x: 6, y: 12 }, wasBlocked: false, blockedByCombatantId: null },
        { combatantId: 'c4', combatantName: 'Goblin 2', path: [{ x: 6, y: 16 }, { x: 6, y: 15 }, { x: 6, y: 14 }], finalPosition: { x: 6, y: 14 }, wasBlocked: false, blockedByCombatantId: null },
      ],
      attacks: [
        { attackerId: 'c2', attackerName: 'Isolde Blade', targetId: 'c3', targetName: 'Goblin 1', attackType: 'ranged_attack', hit: false, damage: 0, targetNewHp: 17, targetDefeated: false, wasted: false, feedText: 'Isolde Blade shoots at Goblin 1 — miss', roll: { d20: 6, accuracy: 2, targetDodge: 12 }, wastedReason: null },
        { attackerId: 'c3', attackerName: 'Goblin 1', targetId: 'c2', targetName: 'Isolde Blade', attackType: 'ranged_attack', hit: true, damage: 2, targetNewHp: 2, targetDefeated: false, wasted: false, feedText: 'Goblin 1 shoots at Isolde Blade for 2 damage (HP: 4 → 2)', roll: { d20: 15, accuracy: 1, targetDodge: 11 }, wastedReason: null },
      ],
      effects: [],
    },
    {
      tickIndex: 1,
      movements: [],
      attacks: [
        { attackerId: 'c1', attackerName: 'Zara Forge', targetId: 'c3', targetName: 'Goblin 1', attackType: 'melee_attack', hit: true, damage: 5, targetNewHp: 12, targetDefeated: false, wasted: false, feedText: 'Zara Forge strikes Goblin 1 for 5 damage (HP: 17 → 12)', roll: { d20: 13, accuracy: 3, targetDodge: 12 }, wastedReason: null },
        { attackerId: 'c2', attackerName: 'Isolde Blade', targetId: 'c4', targetName: 'Goblin 2', attackType: 'ranged_attack', hit: true, damage: 3, targetNewHp: 14, targetDefeated: false, wasted: false, feedText: 'Isolde Blade shoots at Goblin 2 for 3 damage (HP: 17 → 14)', roll: { d20: 17, accuracy: 2, targetDodge: 12 }, wastedReason: null },
        { attackerId: 'c3', attackerName: 'Goblin 1', targetId: 'c1', targetName: 'Zara Forge', attackType: 'melee_attack', hit: false, damage: 0, targetNewHp: 8, targetDefeated: false, wasted: false, feedText: 'Goblin 1 strikes at Zara Forge — miss', roll: { d20: 4, accuracy: 2, targetDodge: 13 }, wastedReason: null },
        { attackerId: 'c4', attackerName: 'Goblin 2', targetId: 'c1', targetName: 'Zara Forge', attackType: 'melee_attack', hit: false, damage: 0, targetNewHp: 8, targetDefeated: false, wasted: true, feedText: "Goblin 2's attack on Zara Forge fails — out of melee range", roll: null, wastedReason: 'out_of_melee_reach' },
      ],
      effects: [],
    },
    {
      tickIndex: 2,
      movements: [],
      attacks: [],
      effects: [
        { combatantId: 'c1', effectType: 'defend', description: 'Zara Forge defends' },
        { combatantId: 'c2', effectType: 'defend', description: 'Isolde Blade defends' },
        { combatantId: 'c4', effectType: 'defend', description: 'Goblin 2 defends' },
      ],
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
