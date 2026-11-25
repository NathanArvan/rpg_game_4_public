import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { Store } from '@ngxs/store';
import { CombatLayoutComponent } from '../../combat-layout/combat-layout.component';
import { EncounterStarted, PlanningPhaseStarted, ClearEncounter, PlayerLockedIn } from '../../../../state/encounter/encounter.actions';
import { MapInitialized } from '../../../../state/map/map.actions';
import { AddFeedBatch, ClearFeed } from '../../../../state/feed/feed.actions';
import { ResolutionPlaybackService } from '../../../../core/services/resolution-playback.service';
import { AbilityDto, ActiveConditionDto, AttackResult, CombatantDto, PlanningCombatantDto, QueuedActionDto, ResolutionPayload } from '../../../../contracts/hub-contracts.generated';
import {
  makeAbility, makeActiveCondition, makeAttack, makeCombatantDto, makePayload, makePlanningCombatant, makeEffect, makeMove, makeQueue, makeQueuedAction, makeReaction, makeRoll, makeTick,
  makePreparedAbility, makeUnusedReaction, makeWatching,
} from '../../../../testing/builders';

const ENCOUNTER_ID = 'preview-encounter-1';
const USER_PLAYER = 'user-player-1';
const USER_MONSTER = 'user-gm';

// Mock data is built with the test builders, so a contract that grows only needs a new default there.
const POWER_STRIKE: AbilityDto = makeAbility({
  id: 'power_strike', name: 'Power Strike', description: 'A heavy melee blow: weapon damage +4.', apCost: 3, cooldown: 2, tags: ['melee'],
});
const FIREBOLT: AbilityDto = makeAbility({
  id: 'firebolt', name: 'Firebolt', description: 'A bolt of flame at a target up to 8 cells away: d8 + 1.', apCost: 3, cooldown: 1,
  tags: ['flame', 'ranged', 'spell'], targeting: { range: 8 },
});
/** Loadouts (3B.3), and Firebolt shown on cooldown. */
const CLEAVE: AbilityDto = makeAbility({
  id: 'cleave', name: 'Cleave', description: 'A wide swing that hits every adjacent enemy, each with its own roll.', apCost: 3, cooldown: 2,
  tags: ['melee'], targeting: { kind: 'auto' },
});
const TWIN_SHOT: AbilityDto = makeAbility({
  id: 'twin_shot', name: 'Twin Shot', description: 'Two quick shots at up to two enemies within 8 cells.', apCost: 3, cooldown: 1,
  tags: ['ranged'], targeting: { kind: 'combatants', range: 8, count: 2 },
});
const MEND: AbilityDto = makeAbility({
  id: 'mend', name: 'Mend', description: 'Heal an ally within 3 cells (or yourself) for d6 + 2.', apCost: 2, cooldown: 2,
  tags: ['healing'], targeting: { filter: 'ally', range: 3, includeSelf: true },
});
const DASH: AbilityDto = makeAbility({
  id: 'dash', name: 'Dash', description: 'Sprint up to 3 cells, on top of your normal movement.', apCost: 1, cooldown: 2,
  tags: ['movement'], targeting: { kind: 'self', filter: 'ally', range: 0 },
  move: { to: 'cell', maxCells: 3, usesMovementBudget: false },
});
const CHARGE: AbilityDto = makeAbility({
  id: 'charge', name: 'Charge', description: 'Run at an enemy up to 6 cells away and strike: weapon damage, +3 if you covered 2+ cells.', apCost: 3, cooldown: 2,
  tags: ['melee', 'movement'], targeting: { range: 6 },
  move: { to: 'target', maxCells: 6, usesMovementBudget: true },
});
/** A prepared reaction (3B.6 PR 2): queued last, it watches for the rest of the round. */
const BRACE: AbilityDto = makePreparedAbility({
  description: 'Ends your queue. Until the end of the round, strike the first enemy that steps into your reach (weapon die + 2).',
});
const ABILITIES: Record<string, AbilityDto[]> = {
  c1: [POWER_STRIKE, CLEAVE, FIREBOLT, BRACE], c2: [TWIN_SHOT, MEND, DASH, CHARGE], c3: [BRACE], c4: [POWER_STRIKE, CHARGE],
};
const COOLDOWNS: Record<string, Record<string, number>> = { c1: { firebolt: 1 } };
/** Conditions (3B.4): Zara is Emboldened, Goblin 1 is Marked and Exposed. */
const CONDITIONS: Record<string, ActiveConditionDto[]> = {
  c1: [
    makeActiveCondition({ condition: { id: 'emboldened', name: 'Emboldened', harmful: false, description: '+2 damage on every hit.', modifiers: { damageDealt: 2 } }, roundsLeft: 0 }),
    makeActiveCondition({ condition: { id: 'slowed', name: 'Slowed', description: '2 fewer cells of movement each round.', modifiers: { movement: -2 } }, roundsLeft: 0, sourceId: 'c3' }),
    makeActiveCondition({ condition: { id: 'weakened', name: 'Weakened', description: '1 less AP each round.', modifiers: { ap: -1 } }, roundsLeft: 0, sourceId: 'c4' }),
  ],
  c4: [makeActiveCondition({ condition: { id: 'stunned', name: 'Stunned', description: "Can't act.", modifiers: { blocks: ['all'] } }, roundsLeft: 0, sourceId: 'c1' })],
  c3: [
    makeActiveCondition({ condition: { id: 'marked', name: 'Marked', description: 'Takes 2 extra damage from every hit.', modifiers: { damageTaken: { percent: 0, flat: 2, tags: [] } } }, roundsLeft: 1, sourceId: 'c2' }),
    makeActiveCondition({ condition: { id: 'exposed', name: 'Exposed', description: '-3 Dodge: easier to hit.', modifiers: { dodge: -3 } }, roundsLeft: 1, sourceId: 'c2' }),
  ],
};

/** Effective budgets and blocks the server would send for the mock conditions above (3B.4 PR 3). */
const BUDGETS: Record<string, Partial<PlanningCombatantDto>> = {
  c1: { movementRange: 2, maxAp: 3, baseMovementRange: 4, baseMaxAp: 4 },
  c4: { blocks: [{ kind: 'all', conditionId: 'stunned', conditionName: 'Stunned' }, { kind: 'move', conditionId: 'stunned', conditionName: 'Stunned' }] },
};

const COMBATANTS_DTO: CombatantDto[] = [
  makeCombatantDto({ id: 'c1', userId: USER_PLAYER, characterId: 'ch1', characterName: 'Zara Forge', currentHp: 8, maxHp: 10, profileId: 'bruiser', profileName: 'Bruiser', movementRange: 4, maxAp: 4 }),
  makeCombatantDto({ id: 'c2', userId: 'user-player-2', characterId: 'ch2', characterName: 'Isolde Blade', currentHp: 4, maxHp: 6, profileId: 'scout', profileName: 'Scout', movementRange: 8, maxAp: 5 }),
  makeCombatantDto({ id: 'c3', userId: USER_MONSTER, characterName: 'Goblin 1', currentHp: 17, maxHp: 20, combatantType: 'monster', isAiControlled: true, profileId: 'grunt', profileName: 'Grunt', movementRange: 5, maxAp: 4 }),
  makeCombatantDto({ id: 'c4', userId: USER_MONSTER, characterName: 'Goblin 2', currentHp: 17, maxHp: 20, combatantType: 'monster', isAiControlled: false, profileId: 'brute', profileName: 'Brute', movementRange: 4, maxAp: 4 }),
].map(c => ({ ...c, abilities: ABILITIES[c.id] ?? [] }));

const PLANNING_COMBATANTS: PlanningCombatantDto[] = COMBATANTS_DTO.map(({ characterId, ...c }) =>
  makePlanningCombatant({ ...c, cooldowns: COOLDOWNS[c.id] ?? {}, conditions: CONDITIONS[c.id] ?? [], ...BUDGETS[c.id] }));

const NAMES: Record<string, string> = Object.fromEntries(COMBATANTS_DTO.map(c => [c.id, c.characterName]));

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

const queue = (id: string, maxAp: number, ...actions: Partial<QueuedActionDto>[]) =>
  makeQueue({ combatantId: id, combatantName: NAMES[id], maxAp, actions: actions.map(a => makeQueuedAction(a)) });
const walk = (id: string, ...cells: [number, number][]) =>
  makeMove({ combatantId: id, combatantName: NAMES[id], path: cells.map(([x, y]) => ({ x, y })) });
const strike = (attackerId: string, targetId: string, a: Partial<AttackResult>) =>
  makeAttack({ attackerId, attackerName: NAMES[attackerId], targetId, targetName: NAMES[targetId], ...a });
const defends = (id: string) => makeEffect({ combatantId: id, effectType: 'defend', description: `${NAMES[id]} defends` });

/**
 * A three-tick round (3B.2 queues): action i of every queue resolves in tick i.
 *   Zara (4 AP):   move → melee Goblin 1 → move away (Goblin 1's opportunity attack hits her as she steps off, 3B.6)
 *   Isolde (5 AP): ranged Goblin 1 → ranged Goblin 2 → defend
 *   Goblin 1 (4):  ranged Isolde → Brace                (watches from tick 2; nobody steps in, so it goes unused, 3B.6)
 *   Goblin 2 (4):  move → melee Zara (out of reach: wasted) → defend
 */
const MOCK_RESOLUTION: ResolutionPayload = makePayload({
  newRoundNumber: 4,
  unusedReactions: [makeUnusedReaction({ combatantId: 'c3', combatantName: NAMES['c3'], feedText: "Goblin 1's Brace goes unused" })],
  updatedCombatants: PLANNING_COMBATANTS,
  queues: [
    queue('c1', 4,
      { type: 'move', apCost: 1, targetCell: { x: 6, y: 12 } },
      { type: 'melee_attack', apCost: 2, targetCombatantId: 'c3' },
      { type: 'move', apCost: 1, targetCell: { x: 8, y: 10 } }),
    queue('c2', 5,
      { type: 'ranged_attack', apCost: 2, targetCombatantId: 'c3' },
      { type: 'ranged_attack', apCost: 2, targetCombatantId: 'c4' },
      { type: 'defend', apCost: 1 }),
    queue('c3', 4,
      { type: 'ranged_attack', apCost: 2, targetCombatantId: 'c2' },
      { type: 'ability', apCost: 2, abilityId: 'brace' }),
    queue('c4', 4,
      { type: 'move', apCost: 1, targetCell: { x: 6, y: 14 } },
      { type: 'melee_attack', apCost: 2, targetCombatantId: 'c1' },
      { type: 'defend', apCost: 1 }),
  ],
  ticks: [
    makeTick({
      tickIndex: 0,
      movements: [walk('c1', [8, 10], [7, 11], [6, 12]), walk('c4', [6, 16], [6, 15], [6, 14])],
      attacks: [
        strike('c2', 'c3', { attackType: 'ranged_attack', hit: false, targetNewHp: 17, feedText: 'Isolde Blade shoots at Goblin 1 — miss', roll: makeRoll({ d20: 6, accuracy: 2, targetDodge: 12 }) }),
        strike('c3', 'c2', { attackType: 'ranged_attack', hit: true, damage: 2, targetNewHp: 2, feedText: 'Goblin 1 shoots at Isolde Blade for 2 damage (HP: 4 → 2)', roll: makeRoll({ d20: 15, accuracy: 1, targetDodge: 11 }) }),
      ],
    }),
    makeTick({
      tickIndex: 1,
      watching: [makeWatching({ combatantId: 'c3', combatantName: NAMES['c3'], feedText: 'Goblin 1 prepares Brace' })],
      attacks: [
        strike('c1', 'c3', { hit: true, damage: 5, targetNewHp: 12, feedText: 'Zara Forge strikes Goblin 1 for 5 damage (HP: 17 → 12)', roll: makeRoll({ d20: 13, accuracy: 3, targetDodge: 12 }) }),
        strike('c2', 'c4', { attackType: 'ranged_attack', hit: true, damage: 3, targetNewHp: 14, feedText: 'Isolde Blade shoots at Goblin 2 for 3 damage (HP: 17 → 14)', roll: makeRoll({ d20: 17, accuracy: 2, targetDodge: 12 }) }),
        strike('c4', 'c1', { hit: false, targetNewHp: 8, wasted: true, feedText: "Goblin 2's attack on Zara Forge fails — out of melee range", wastedReason: 'out_of_melee_reach' }),
      ],
    }),
    makeTick({
      tickIndex: 2,
      movements: [walk('c1', [6, 12], [7, 11], [8, 10])],
      reactions: [makeReaction({
        reactorId: 'c3', reactorName: NAMES['c3'], triggerId: 'c1', triggerName: NAMES['c1'], step: 1,
        feedText: "Goblin 1's opportunity attack on Zara Forge hits for 2 damage (HP: 8 → 6)",
        attack: strike('c3', 'c1', { hit: true, damage: 2, targetNewHp: 6, feedText: "Goblin 1's opportunity attack on Zara Forge hits for 2 damage (HP: 8 → 6)", roll: makeRoll({ d20: 14, accuracy: 2, targetDodge: 13 }) }),
      })],
      effects: [defends('c2'), defends('c4')],
    }),
  ],
});

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
