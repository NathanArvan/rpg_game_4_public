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
