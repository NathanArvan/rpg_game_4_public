/**
 * Splits a combat log line into styled pieces for display. Pure: the feed stores plain text
 * (what the server and playback produce); this only decides how it looks.
 *
 * - "— Battle Start —", "— Round 2 Resolution —" and "— Encounter over … —" → round headings
 * - "Tick 1" and "End of round" → tick headings
 * - other lines: party names blue, enemy names red, "for 5 damage (HP: 8 → 3)" /
 *   "for 5 damage — defeated!" / "— goes down!" / "— finished off!" orange, "and misses" grey,
 *   bleeding out orange, stabilizes teal (3B.5)
 */

export type FeedLineKind = 'round' | 'tick' | 'event';

/** First line of every encounter's log (added when EncounterStarted arrives). */
export const BATTLE_START_LINE = '— Battle Start —';

export type SegmentStyle = 'party' | 'enemy' | 'damage' | 'miss' | 'save';

export interface FeedSegment {
  text: string;
  style?: SegmentStyle;
}

export interface FeedLine {
  kind: FeedLineKind;
  segments: FeedSegment[];
}

/** Which side each combatant name belongs to. */
export type NameSides = ReadonlyMap<string, 'party' | 'enemy'>;

// From ResolutionFeedFormatter (server): "for {damage} damage (HP: {before} → {after})" or "… — defeated!".
const DAMAGE = String.raw`for \d+ damage(?: \(HP: -?\d+ → -?\d+\)| — defeated!| — goes down!)?|— finished off!|bleeds out|is bleeding out — \d+ rounds? left`;
const MISS = 'and misses';
const SAVE = 'stabilizes';

export function formatFeedLine(text: string, sides: NameSides): FeedLine {
  if (text === BATTLE_START_LINE || /^— Round \d+ Resolution —$/.test(text) || text.startsWith('— Encounter over')) {
    return { kind: 'round', segments: [{ text }] };
  }
  if (/^Tick \d+$/.test(text) || text === 'End of round') return { kind: 'tick', segments: [{ text }] };
  return { kind: 'event', segments: highlight(text, sides) };
}

function highlight(text: string, sides: NameSides): FeedSegment[] {
  // Longest names first, so "Goblin 10" wins over "Goblin 1". A name must not run into
  // more letters or digits ("Goblin 1" is not part of "Goblin 12").
  const names = [...sides.keys()].filter(n => n.length > 0).sort((a, b) => b.length - a.length);
  const nameAlternatives = names.map(n => `${escapeRegExp(n)}(?![\\p{L}\\p{N}])`);
  const pattern = new RegExp([DAMAGE, MISS, SAVE, ...nameAlternatives].map(p => `(${p})`).join('|'), 'gu');

  const segments: FeedSegment[] = [];
  let last = 0;
  for (const match of text.matchAll(pattern)) {
    const start = match.index!;
    if (start > last) segments.push({ text: text.slice(last, start) });
    segments.push({ text: match[0], style: styleOf(match, sides) });
    last = start + match[0].length;
  }
  if (last < text.length) segments.push({ text: text.slice(last) });
  return segments;
}

function styleOf(match: RegExpMatchArray, sides: NameSides): SegmentStyle {
  if (match[1] !== undefined) return 'damage';
  if (match[2] !== undefined) return 'miss';
  if (match[3] !== undefined) return 'save';
  return sides.get(match[0]) ?? 'party';
}

function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
