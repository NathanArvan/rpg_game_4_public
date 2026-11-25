import cases from '../../../../../contracts/plan-validation-cases.json';
import { AbilityDto, PlannedActionEntry } from '../../contracts/hub-contracts.generated';
import { makeAbility } from '../../testing/builders';
import { Position } from '../map/map.state.model';
import { entry, validatePlan } from './plan-queue';

/**
 * The plan-validation cases shared with the server (contracts/plan-validation-cases.json,
 * TESTING_ROADMAP item 5): the server's PlanValidator runs the same table, so validatePlan can't
 * drift from it unnoticed. Messages are the server's.
 */
interface SharedCase {
  name: string;
  maxAp?: number;
  movementRange?: number;
  start?: number[] | null;
  abilities?: string[];
  cooldowns?: Record<string, number>;
  positions?: Record<string, number[]>;
  conditions?: { name: string; movement?: number; ap?: number; blocks?: string[] }[];
  queue: string[];
  expect: { error?: string; ap?: number; cells?: number };
}

interface LibraryAbility {
  name: string; apCost: number; cooldown: number;
  targeting: { kind: string; count: number | null };
  move: AbilityDto['move'];
}

const file = cases as unknown as {
  grid: { width: number; height: number };
  defaults: { maxAp: number; movementRange: number; start: number[] };
  abilities: Record<string, LibraryAbility>;
  cases: SharedCase[];
};

const at = ([x, y]: number[]): Position => ({ x, y });

function toAbility(id: string): AbilityDto {
  const a = file.abilities[id];
  if (!a) throw new Error(`ability '${id}' isn't in the library`);
  return makeAbility({ id, name: a.name, apCost: a.apCost, cooldown: a.cooldown, targeting: { ...a.targeting }, move: a.move });
}

/** The table's compact queue syntax (see its "about"). */
function parse(text: string): PlannedActionEntry {
  const [verb, ...rest] = text.trim().split(/\s+/);
  const cell = (s: string): Position => at(s.replace('@', '').split(',').map(Number));
  switch (verb) {
    case 'move': return entry('move', rest[0] ? { targetCell: cell(rest[0]) } : {});
    case 'melee': return entry('melee_attack', { targetCombatantId: rest[0] });
    case 'ranged': return entry('ranged_attack', { targetCombatantId: rest[0] });
    case 'stabilize': return entry('stabilize', { targetCombatantId: rest[0] });
    case 'defend': return entry('defend');
    case 'ability': {
      const [abilityId, ...args] = rest;
      const fields: Partial<PlannedActionEntry> = { abilityId };
      for (const arg of args) {
        if (arg.startsWith('@')) fields.targetCell = cell(arg);
        else if (arg.startsWith('[')) fields.targetCombatantIds = arg.slice(1, -1).split(',');
        else fields.targetCombatantId = arg;
      }
      return entry('ability', fields);
    }
    default: throw new Error(`can't parse "${text}"`);
  }
}

describe('validatePlan: shared plan-validation cases', () => {
  it('loads the table', () => {
    expect(file.cases.length).toBeGreaterThan(0);
  });

  for (const c of file.cases) {
    it(c.name, () => {
      const start = c.start === undefined ? file.defaults.start : c.start;
      // The server sends the effective budgets and blocks (3B.4 PR 3); this works them out the same way.
      const conditions = c.conditions ?? [];
      const sum = (f: (x: typeof conditions[number]) => number | undefined) => conditions.reduce((n, x) => n + (f(x) ?? 0), 0);
      const firstBlocking = (kind: string) => conditions.find(x => x.blocks?.includes('all'))
        ?? (kind === 'move' ? conditions.find(x => x.blocks?.includes('move')) : undefined);
      const blocks = (['all', 'move'] as const).flatMap(kind => {
        const by = firstBlocking(kind);
        return by ? [{ kind, conditionName: by.name }] : [];
      });
      const result = validatePlan(c.queue.map(parse), {
        maxAp: Math.max(0, (c.maxAp ?? file.defaults.maxAp) + sum(x => x.ap)),
        movementRange: Math.max(0, (c.movementRange ?? file.defaults.movementRange) + sum(x => x.movement)),
        blocks,
        abilities: (c.abilities ?? []).map(toAbility),
        cooldowns: c.cooldowns ?? {},
        start: start ? at(start) : undefined,
        positions: Object.fromEntries(Object.entries(c.positions ?? {}).map(([id, p]) => [id, at(p)])),
        gridWidth: file.grid.width,
        gridHeight: file.grid.height,
      });

      if (c.expect.error !== undefined) {
        expect(result.error).toBe(c.expect.error);
      } else {
        expect(result.error).toBeNull();
        expect({ ap: result.ap, cells: result.cells }).toEqual({ ap: c.expect.ap!, cells: c.expect.cells! });
      }
    });
  }
});
