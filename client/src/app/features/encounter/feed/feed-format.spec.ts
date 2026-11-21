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
});
