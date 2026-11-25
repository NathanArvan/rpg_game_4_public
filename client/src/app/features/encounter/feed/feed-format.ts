/**
 * Splits a combat log line into styled pieces for display. Pure: the feed stores plain text
 * (what the server and playback produce); this only decides how it looks.
 *
 * - "— Battle Start —", "— Round 2 Resolution —" and "— Encounter over … —" → round headings
 * - "Tick 1" and "End of round" → tick headings
 * - other lines: party names blue, enemy names red, "for 5 damage (HP: 8 → 3)" /
 *   "for 5 damage — defeated!" / "— goes down!" / "— finished off!" orange, "and misses" grey,
 *   bleeding out orange, stabilizes and heals teal (3B.5, 3B.3: "for 6 (HP: 0 → 6) — back on their feet!"),
 *   ability names purple (3B.3: "Bruiser's Power Strike hits …", "Mira's Mend heals …",
 *   "Bruiser uses Power Strike on …", "… can't use Power Strike — …"), condition names blue (3B.4:
 *   "Goblin is Marked until the end of next round", "is still Marked, now …", "is no longer Marked",
 *   "isn't Marked — the attack missed", "resists Burning"; the ability in "Kestrel's Mark: …" is purple),
 *   end-of-round periodic lines (3B.4 PR 2: "Goblin takes 3 damage from Burning (HP: 7 → 4)" orange,
 *   "Mira heals 2 from Regenerating" teal, the condition blue)
 */

export type FeedLineKind = 'round' | 'tick' | 'event';

/** First line of every encounter's log (added when EncounterStarted arrives). */
export const BATTLE_START_LINE = '— Battle Start —';

export type SegmentStyle = 'party' | 'enemy' | 'damage' | 'miss' | 'save' | 'ability' | 'condition';

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
// Periodic damage at the end of the round (3B.4 PR 2): "takes 3 damage from Burning (-1 from conditions) — defeated!".
const DAMAGE = String.raw`for \d+ damage(?: \(\+\d+ from moving\))?(?: \([+-]\d+ from conditions\))?(?: \(HP: -?\d+ → -?\d+\)| — defeated!| — goes down!)?|— finished off!|bleeds out|is bleeding out — \d+ rounds? left|takes \d+ damage|\([+-]\d+ from conditions\)|— defeated!|— goes down!`;
const MISS = String.raw`and misses|misses`;
const SAVE = String.raw`stabilizes|for \d+ \(HP: \d+ → \d+\)(?: — back on their feet!)?|heals \d+(?= from )`;
// Ability names in the server's ability lines (3B.3): after "X's " and before " hits / misses / on ",
// or after "uses " / "can't use " and before " on " / " —" / the end; and in movement lines made by
// an ability: "moves to (9, 5) with Dash", "stays at (5, 6) (Charge)".
// (Not the basic "X's attack on …", "X's stabilize on …" and "X's opportunity attack on …" (3B.6) lines.)
const ABILITY = String.raw`(?<='s )(?!attack |stabilize |opportunity attack )[\p{L}\p{N} ]+?(?= (?:hits|misses|heals|on) |: )|(?<=(?:uses|can't use) )[\p{L}\p{N} ]+?(?= on | —|$)|(?<=\) with )[\p{L}\p{N} ]+?(?= \(blocked\)|$)|(?<=\) \()[\p{L}\p{N} ]+?(?=\)$)`;

// Condition names in the server's condition lines (3B.4): after "is ", "is still ", "is no longer ",
// "isn't " or "resists ", a capitalised name, up to " until", " for N", ",", " —" or the end; and in periodic
// lines (3B.4 PR 2) after "damage from " / "heals N from ", up to " (" or the end; and in blocked lines (3B.4 PR 3),
// "Brakka is Stunned and can't act", up to " and can't".
const CONDITION = String.raw`(?<=(?:\bis (?:still |no longer )?|\bisn't |\bresists |\bdamage from |\bheals \d+ from ))\p{Lu}[\p{L}\p{N} ]*?(?= until | for \d|,| —| \(| and can't|$)`;

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
  const pattern = new RegExp([DAMAGE, MISS, SAVE, ABILITY, CONDITION, ...nameAlternatives].map(p => `(${p})`).join('|'), 'gu');

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
  if (match[4] !== undefined) return 'ability';
  if (match[5] !== undefined) return 'condition';
  return sides.get(match[0]) ?? 'party';
}

function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
