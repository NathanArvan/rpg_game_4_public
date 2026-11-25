import { FeedSegment, NameSides, formatFeedLine } from './feed-format';

describe('formatFeedLine', () => {
  const sides: NameSides = new Map([
    ['Zara Forge', 'party'],
    ['Goblin 1', 'enemy'],
    ['Goblin 12', 'enemy'],
  ]);

  /** Segments as "[style]text" for easy reading; unstyled text as-is. */
  const show = (segments: FeedSegment[]) => segments.map(s => s.style ? `[${s.style}]${s.text}` : s.text);

  it('makes round and encounter-end lines round headings, and tick lines tick headings', () => {
    expect(formatFeedLine('— Battle Start —', sides).kind).toBe('round');
    expect(formatFeedLine('— Round 3 Resolution —', sides).kind).toBe('round');
    expect(formatFeedLine('— Encounter over after round 4: the party wins —', sides).kind).toBe('round');
    expect(formatFeedLine('Tick 2', sides).kind).toBe('tick');
    expect(formatFeedLine('Zara Forge moves to (6, 12)', sides).kind).toBe('event');
  });

  it('colours a hit: names by side, the damage and HP change orange', () => {
    const line = formatFeedLine('Zara Forge strikes Goblin 1 for 5 damage (HP: 17 → 12)', sides);
    expect(show(line.segments)).toEqual([
      '[party]Zara Forge', ' strikes ', '[enemy]Goblin 1', ' ', '[damage]for 5 damage (HP: 17 → 12)',
    ]);
  });

  it('colours a killing blow, including an HP that went below zero', () => {
    expect(show(formatFeedLine('Goblin 1 shoots at Zara Forge for 9 damage — defeated!', sides).segments))
      .toEqual(['[enemy]Goblin 1', ' shoots at ', '[party]Zara Forge', ' ', '[damage]for 9 damage — defeated!']);
    expect(show(formatFeedLine('Zara Forge strikes Goblin 1 for 6 damage (HP: 2 → -4)', sides).segments).pop())
      .toBe('[damage]for 6 damage (HP: 2 → -4)');
  });

  it('greys out a miss', () => {
    expect(show(formatFeedLine('Goblin 1 swings at Zara Forge and misses', sides).segments))
      .toEqual(['[enemy]Goblin 1', ' swings at ', '[party]Zara Forge', ' ', '[miss]and misses']);
  });

  it('matches whole names only: "Goblin 12" is not "Goblin 1"', () => {
    expect(show(formatFeedLine("Goblin 12's attack on Zara Forge fails — target out of melee range", sides).segments))
      .toEqual(['[enemy]Goblin 12', "'s attack on ", '[party]Zara Forge', ' fails — target out of melee range']);
  });

  it('leaves lines with no known names plain', () => {
    expect(formatFeedLine('⚠ Not enough AP', new Map()).segments).toEqual([{ text: '⚠ Not enough AP' }]);
  });

  it('highlights going down, finishing blows, bleeding out and stabilizing (3B.5)', () => {
    expect(show(formatFeedLine('Goblin 1 strikes Zara Forge for 6 damage — goes down!', sides).segments).pop())
      .toBe('[damage]for 6 damage — goes down!');
    expect(show(formatFeedLine('Goblin 1 strikes Zara Forge — finished off!', sides).segments).pop())
      .toBe('[damage]— finished off!');
    expect(show(formatFeedLine('Zara Forge bleeds out', sides).segments)).toEqual(['[party]Zara Forge', ' ', '[damage]bleeds out']);
    expect(show(formatFeedLine('Zara Forge is bleeding out — 1 round left', sides).segments).pop())
      .toBe('[damage]is bleeding out — 1 round left');
    expect(show(formatFeedLine('Goblin 12 stabilizes Goblin 1', sides).segments))
      .toEqual(['[enemy]Goblin 12', ' ', '[save]stabilizes', ' ', '[enemy]Goblin 1']);
    expect(formatFeedLine('End of round', sides).kind).toBe('tick');
  });

  it('highlights ability names and ability misses (3B.3)', () => {
    expect(show(formatFeedLine("Zara Forge's Power Strike hits Goblin 1 for 9 damage (HP: 20 → 11)", sides).segments))
      .toEqual(['[party]Zara Forge', "'s ", '[ability]Power Strike', ' hits ', '[enemy]Goblin 1', ' ', '[damage]for 9 damage (HP: 20 → 11)']);
    expect(show(formatFeedLine("Zara Forge's Power Strike misses Goblin 1", sides).segments))
      .toEqual(['[party]Zara Forge', "'s ", '[ability]Power Strike', ' ', '[miss]misses', ' ', '[enemy]Goblin 1']);
    expect(show(formatFeedLine("Zara Forge's Power Strike on Goblin 1 fails — target out of range", sides).segments))
      .toEqual(['[party]Zara Forge', "'s ", '[ability]Power Strike', ' on ', '[enemy]Goblin 1', ' fails — target out of range']);
    expect(show(formatFeedLine("Zara Forge can't use Power Strike — still on cooldown", sides).segments))
      .toEqual(['[party]Zara Forge', " can't use ", '[ability]Power Strike', ' — still on cooldown']);
  });
});

describe('formatFeedLine heals (3B.3)', () => {
  const sides: NameSides = new Map([['Zara Forge', 'party'], ['Mira', 'party'], ['Goblin 1', 'enemy']]);
  const show = (segments: FeedSegment[]) => segments.map(s => (s.style ? `[${s.style}]` : '') + s.text);

  it('styles the heal amount and the revive teal, and the ability name purple', () => {
    expect(show(formatFeedLine("Mira's Mend heals Zara Forge for 6 (HP: 0 → 6) — back on their feet!", sides).segments))
      .toEqual(['[party]Mira', "'s ", '[ability]Mend', ' heals ', '[party]Zara Forge', ' ', '[save]for 6 (HP: 0 → 6) — back on their feet!']);
  });

  it('names an auto ability that found nobody', () => {
    expect(show(formatFeedLine('Zara Forge uses Cleave — no targets in range', sides).segments))
      .toEqual(['[party]Zara Forge', ' uses ', '[ability]Cleave', ' — no targets in range']);
  });

  it('highlights a move bonus with the damage, and the ability in a move line (3B.3)', () => {
    expect(show(formatFeedLine("Zara Forge's Charge hits Goblin 1 for 7 damage (+3 from moving) (HP: 20 → 13)", sides).segments))
      .toEqual(['[party]Zara Forge', "'s ", '[ability]Charge', ' hits ', '[enemy]Goblin 1', ' ', '[damage]for 7 damage (+3 from moving) (HP: 20 → 13)']);
    expect(show(formatFeedLine('Zara Forge moves to (9, 5) with Dash', sides).segments))
      .toEqual(['[party]Zara Forge', ' moves to (9, 5) with ', '[ability]Dash']);
  });
});

describe('formatFeedLine conditions (3B.4)', () => {
  const sides: NameSides = new Map([['Kestrel', 'party'], ['Goblin 1', 'enemy']]);
  const show = (segments: FeedSegment[]) => segments.map(s => (s.style ? `[${s.style}]` : '') + s.text);

  it('styles the ability and the condition when one lands', () => {
    expect(show(formatFeedLine("Kestrel's Mark: Goblin 1 is Marked until the end of next round", sides).segments))
      .toEqual(['[party]Kestrel', "'s ", '[ability]Mark', ': ', '[enemy]Goblin 1', ' is ', '[condition]Marked', ' until the end of next round']);
  });

  it('styles Guarded, refreshes, misses, resists and expiries', () => {
    expect(show(formatFeedLine('Goblin 1 is Guarded until the end of the round', sides).segments))
      .toEqual(['[enemy]Goblin 1', ' is ', '[condition]Guarded', ' until the end of the round']);
    expect(show(formatFeedLine("Kestrel's Mark: Goblin 1 is still Flame Ward, now for 2 more rounds", sides).segments))
      .toContain('[condition]Flame Ward');
    expect(show(formatFeedLine("Kestrel's Blinding Strike: Goblin 1 isn't Dazzled — the attack missed", sides).segments))
      .toContain('[condition]Dazzled');
    expect(show(formatFeedLine('Goblin 1 resists Burning', sides).segments)).toContain('[condition]Burning');
    expect(show(formatFeedLine('Goblin 1 is no longer Marked', sides).segments))
      .toEqual(['[enemy]Goblin 1', ' is no longer ', '[condition]Marked']);
  });

  it('keeps the damage highlight when conditions changed a hit', () => {
    expect(show(formatFeedLine('Kestrel strikes Goblin 1 for 3 damage (-4 from conditions) (HP: 20 → 17)', sides).segments))
      .toEqual(['[party]Kestrel', ' strikes ', '[enemy]Goblin 1', ' ', '[damage]for 3 damage (-4 from conditions) (HP: 20 → 17)']);
  });

  it('highlights end-of-round periodic lines (3B.4 PR 2)', () => {
    expect(show(formatFeedLine('Goblin 1 takes 3 damage from Burning (HP: 7 → 4)', sides).segments))
      .toEqual(['[enemy]Goblin 1', ' ', '[damage]takes 3 damage', ' from ', '[condition]Burning', ' (HP: 7 → 4)']);
    expect(show(formatFeedLine('Goblin 1 takes 1 damage from Burning (-2 from conditions) — defeated!', sides).segments))
      .toEqual(['[enemy]Goblin 1', ' ', '[damage]takes 1 damage', ' from ', '[condition]Burning', ' ',
        '[damage](-2 from conditions)', ' ', '[damage]— defeated!']);
    expect(show(formatFeedLine('Kestrel heals 2 from Regenerating (HP: 5 → 7)', sides).segments))
      .toEqual(['[party]Kestrel', ' ', '[save]heals 2', ' from ', '[condition]Regenerating', ' (HP: 5 → 7)']);
  });

  it('leaves bleeding out alone (lower-case words are not condition names)', () => {
    expect(show(formatFeedLine('Kestrel is bleeding out — 2 rounds left', sides).segments))
      .toEqual(['[party]Kestrel', ' ', '[damage]is bleeding out — 2 rounds left']);
  });
});
