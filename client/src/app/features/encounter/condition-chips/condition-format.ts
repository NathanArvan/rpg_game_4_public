import { ActiveConditionDto, ConditionDto, ConditionModifiersDto } from '../../../contracts/hub-contracts.generated';

/**
 * Words for conditions (3B.4), shared by chips, tooltips and the ability bar. Pure.
 *
 * Players never see a duration number (SPRINT_3B4 C5): `roundsLeft` counts the full rounds a
 * condition lasts after the current one, and this turns it into words.
 */

/** "until end of round" (0), "until end of next round" (1), "for N more rounds" (2+). */
export function describeDuration(roundsLeft: number): string {
  if (roundsLeft <= 0) return 'until end of round';
  if (roundsLeft === 1) return 'until end of next round';
  return `for ${roundsLeft} more rounds`;
}

/** A short version for chips: "this round", "next round", "3 rounds". */
export function shortDuration(roundsLeft: number): string {
  if (roundsLeft <= 0) return 'this round';
  if (roundsLeft === 1) return 'next round';
  return `${roundsLeft} rounds`;
}

/** For an ability's tooltip: how long the condition it applies lasts (the round it lands in counts). */
export function describeAppliedDuration(duration: number): string {
  return describeDuration(duration);
}

const signed = (n: number) => (n > 0 ? `+${n}` : `${n}`);

/** What a condition changes, one phrase per modifier: "+2 to hit", "takes 50% less damage", … */
export function describeModifiers(m: ConditionModifiersDto): string[] {
  const parts: string[] = [];
  const kind = m.attackKind === 'melee' ? ' (melee)' : m.attackKind === 'ranged' ? ' (ranged)' : '';
  if (m.accuracy) parts.push(`${signed(m.accuracy)} to hit${kind}`);
  if (m.dodge) parts.push(`${signed(m.dodge)} Dodge${kind}`);
  if (m.damageDealt) parts.push(`${signed(m.damageDealt)} damage per hit`);
  if (m.damageTaken) {
    const from = m.damageTaken.tags.length > 0 ? ` from ${m.damageTaken.tags.join('/')}` : '';
    const { percent, flat } = m.damageTaken;
    if (flat) parts.push(`takes ${Math.abs(flat)} ${flat > 0 ? 'extra' : 'less'} damage per hit${from}`);
    if (percent === -100) parts.push(`immune to damage${from}`);
    else if (percent) parts.push(`takes ${Math.abs(percent)}% ${percent > 0 ? 'more' : 'less'} damage${from}`);
  }
  if (m.movement) parts.push(`${signed(m.movement)} movement (from next round)`);
  if (m.ap) parts.push(`${signed(m.ap)} AP (from next round)`);
  if (m.periodic) {
    const amount = [m.periodic.die, m.periodic.bonus ? `${m.periodic.bonus}` : ''].filter(Boolean).join(' + ');
    parts.push(m.periodic.kind === 'heal' ? `heals ${amount} each round` : `takes ${amount} damage each round`);
  }
  if (m.blocks.includes('all')) parts.push("can't act");
  else if (m.blocks.includes('move')) parts.push("can't move");
  return parts;
}

/**
 * Which conditions change a round budget (3B.4 PR 3), for the AP counter: "-1 Weakened", "+1 Hasted, -1 Weakened".
 * Empty when none does. The numbers come from the server's effective values; this only names the causes.
 */
export function budgetNote(conditions: readonly ActiveConditionDto[], field: 'ap' | 'movement'): string {
  return conditions
    .filter(c => c.condition.modifiers[field] !== 0)
    .map(c => `${signed(c.condition.modifiers[field])} ${c.condition.name}`)
    .join(', ');
}

/** A condition's tooltip: name, what it does, how long it lasts. */
export function conditionTooltip(c: ConditionDto, roundsLeft?: number): string {
  const lines = [c.name];
  const what = describeModifiers(c.modifiers);
  if (c.description) lines.push(c.description);
  else if (what.length > 0) lines.push(what.join(', '));
  if (c.description && what.length > 0) lines.push(`(${what.join(', ')})`);
  if (roundsLeft !== undefined) lines.push(describeDuration(roundsLeft));
  return lines.join('\n');
}

/** Tooltip for an active condition. */
export function activeConditionTooltip(a: ActiveConditionDto): string {
  return conditionTooltip(a.condition, a.roundsLeft);
}
