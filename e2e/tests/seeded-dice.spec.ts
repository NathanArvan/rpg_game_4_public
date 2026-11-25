import { expect, test } from '@playwright/test';
import { expectPlanningRound, feedEntries, newActor, startQuickBattle, tokenAt } from './support/actors';

/**
 * Exact outcomes with seeded dice (TESTING_ROADMAP item 6). The stack must run with Dice:Seed=1:
 * docker-compose.override.yml (local dev) and docker-compose.e2e.yml (CI) both set it. Each round's
 * rolls then depend only on the seed and the round number, and plans resolve in the combatants'
 * creation order (the preset's order), so the same plan gives the same result every run.
 *
 * If a rules change moves these numbers (a new roll, a different roll order), re-read them from one
 * run's combat log and update them here, like a characterization test.
 */

test('Seeded dice: Cleave and Mend land exactly the same every run (Abilities preset)', async ({ browser }) => {
  const page = await newActor(browser);
  await startQuickBattle(page, 'Abilities');

  // Same plan as the Abilities test in quick-battle.spec.ts: the Bruiser (9,9) Cleaves, the
  // Skirmisher (10,11) Mends the hurt Scout (11,12), and the AI plays the goblins, Shaman and Ogre.
  const actionBar = page.locator('.action-bar');
  await expect(actionBar).toContainText('Planning for Bruiser');
  await actionBar.locator('.ability-btn', { hasText: 'Cleave' }).click();
  await page.locator('.armed-confirm', { hasText: 'Use Cleave' }).click();
  await expect(page.locator('.planned-action-display')).toContainText('Cleave');

  await tokenAt(page, 10, 11).click();
  await expect(actionBar).toContainText('Planning for Skirmisher');
  await actionBar.locator('.ability-btn', { hasText: 'Mend' }).click();
  await tokenAt(page, 11, 12).click();
  await expect(page.locator('.planned-action-display')).toContainText('Mend on Scout');

  await page.getByRole('button', { name: /Lock In All/ }).click();
  await expectPlanningRound(page, 2, 30_000);

  for (const line of [
    "Bruiser's Cleave misses Goblin 1",
    "Bruiser's Cleave hits Goblin 2 for 5 damage (HP: 10 → 5)",
    "Bruiser's Cleave misses Goblin 3",
    "Bruiser's Cleave hits Ogre for 7 damage (HP: 28 → 21)",
    "Skirmisher's Mend heals Scout for 5 (HP: 5 → 10)",
  ])
    await expect(feedEntries(page).filter({ hasText: line })).toHaveCount(1);

  const hp = (name: string) => page.locator('app-encounter-tracker tr', { hasText: name }).locator('td').nth(1);
  await expect(hp('Goblin 2')).toHaveText('5/10');
  await expect(hp('Ogre')).toHaveText('21/28');
  await expect(hp('Scout')).toHaveText('10/12');
  await expect(hp('Bruiser')).toHaveText('13/24');
});

test('Seeded dice: Firebolt sets the Ogre Burning, and it burns at the end of the round (Conditions preset, 3B.4)', async ({ browser }) => {
  const page = await newActor(browser);
  await startQuickBattle(page, 'Conditions');
  const actionBar = page.locator('.action-bar');

  // Round 1: the Scout (11,13) Marks Goblin 1 (10,7) (Exposed: -3 Dodge through round 2); everyone else defends.
  await tokenAt(page, 11, 13).click();
  await expect(actionBar).toContainText('Planning for Scout');
  await actionBar.locator('.ability-btn', { hasText: 'Mark' }).click();
  await tokenAt(page, 10, 7).click();
  await expect(page.locator('.planned-action-display')).toContainText('Mark on Goblin 1');
  await page.getByRole('button', { name: /Lock In All/ }).click();
  await expectPlanningRound(page, 2, 30_000);

  // Round 2: the Scout Firebolts the Ogre where it now stands (it walked to (9,9) in round 1).
  await tokenAt(page, 11, 13).click();
  await expect(actionBar).toContainText('Planning for Scout');
  await actionBar.locator('.ability-btn', { hasText: 'Firebolt' }).click();
  await tokenAt(page, 9, 9).click();
  await expect(page.locator('.planned-action-display')).toContainText('Firebolt on Ogre');
  await page.getByRole('button', { name: /Lock In All/ }).click();
  await expectPlanningRound(page, 3, 30_000);

  // The Ogre doesn't resist (its d100 is over the Brute's 25%); Burning and Goblin 2's Envenom on the
  // Bruiser (round 1) tick at the end of the round, before conditions expire.
  for (const line of [
    "Scout's Firebolt hits Ogre for 9 damage (HP: 28 → 19)",
    "Scout's Firebolt: Ogre is Burning until the end of next round",
    'Bruiser takes 2 damage from Poisoned (HP: 5 → 3)',
    'Ogre takes 4 damage from Burning (HP: 19 → 15)',
  ])
    await expect(feedEntries(page).filter({ hasText: line })).toHaveCount(1);

  const ogreRow = page.locator('app-encounter-tracker tr', { hasText: 'Ogre' });
  await expect(ogreRow.locator('td').nth(1)).toHaveText('15/28');
  await expect(ogreRow.locator('.chip[data-condition="burning"]')).toContainText('this round');
});

test('Seeded dice: Slowed, Shield Bash\'s stun and Weakened (Crowd Control preset, 3B.4 PR 3)', async ({ browser }) => {
  const page = await newActor(browser);
  await startQuickBattle(page, 'Crowd Control');
  const actionBar = page.locator('.action-bar');

  // Round 1: the Bruiser (9,12) defends (the others too, unplanned). Goblin 1 walks next to it and Goblin 2's
  // Hamstring Slows it.
  await expect(actionBar).toContainText('Planning for Bruiser');
  await actionBar.getByRole('button', { name: /Defend/ }).click();
  await page.getByRole('button', { name: /Lock In All/ }).click();
  await expectPlanningRound(page, 2, 30_000);
  await expect(feedEntries(page).filter({ hasText: "Goblin 2's Hamstring: Bruiser is Slowed until the end of next round" })).toHaveCount(1);

  // Round 2: Slowed shows in the action bar (2 cells instead of 4); the Bruiser Shield Bashes Goblin 1 (9,11) in tick 1.
  await expect(actionBar.locator('[data-testid="move-note"]')).toHaveText('🦶 2 (-2 Slowed)');
  await actionBar.locator('.ability-btn', { hasText: 'Shield Bash' }).click();
  await tokenAt(page, 9, 11).click();
  await expect(page.locator('.planned-action-display')).toContainText('Shield Bash on Goblin 1');
  await page.getByRole('button', { name: /Lock In All/ }).click();
  await expectPlanningRound(page, 3, 30_000);

  // Goblin 1 is stunned for the rest of round 2: its tick-2 swing never happens. The Shaman's Sap Weakens the Bruiser.
  for (const line of [
    "Bruiser's Shield Bash hits Goblin 1 for 4 damage (HP: 10 → 6)",
    "Bruiser's Shield Bash: Goblin 1 is Stunned until the end of the round",
    "Goblin 1 is Stunned and can't act",
    "Shaman's Sap: Bruiser is Weakened until the end of next round",
    'Goblin 1 is no longer Stunned',
  ])
    await expect(feedEntries(page).filter({ hasText: line })).toHaveCount(1);

  // Round 3: Goblin 1 acts again (no chip); the Bruiser has one AP less.
  await expect(page.locator('app-encounter-tracker tr', { hasText: 'Goblin 1' }).locator('.chip')).toHaveCount(0);
  await expect(actionBar.locator('.ap-budget')).toHaveText('AP 0/3 (-1 Weakened)');
});
