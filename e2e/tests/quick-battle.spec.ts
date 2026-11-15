import { expect, test } from '@playwright/test';
import { cell, expectPlanningRound, feedEntries, newActor, startQuickBattle, tokenAt } from './support/actors';

/**
 * Quick Battle: one person, no session setup, a party against AI enemies.
 * Preset layouts live in server/src/RpgGame.Api/QuickBattle/Presets/.
 */

test('Training Dummy: play a Quick Battle to victory, then play again', async ({ browser }) => {
  test.setTimeout(120_000);
  const page = await newActor(browser);
  await startQuickBattle(page, 'Training Dummy');

  // Quick Battle has no planning timer.
  await expect(page.locator('app-round-banner .timer')).toHaveCount(0);
  await expect(page.locator('app-quick-battle-panel')).toContainText('training-dummy');

  // Hero (9,9) hits the 1-HP Dummy (9,10) until a d20 lands 10+. ~55% per round.
  const result = page.locator('.result-card');
  for (let round = 1; round <= 15 && !(await result.isVisible()); round++) {
    await expectPlanningRound(page, round);
    await tokenAt(page, 9, 10).click();
    await expect(page.locator('.planned-action-display')).toContainText('Melee Attack on Dummy');
    // The Dummy (AI) is already locked in, so the round resolves the moment we lock in.
    await page.getByRole('button', { name: 'Lock In', exact: true }).click();
    await expect(feedEntries(page).filter({ hasText: `Round ${round} Resolution` })).toHaveCount(1);
    await expect(page.locator('app-round-banner .phase')).toHaveText(
      new RegExp(`Planning\\s+.\\s+Round ${round + 1}\\b|Encounter over`), { timeout: 10_000 });
  }

  await expect(result.locator('.headline')).toHaveText('Victory');

  await page.getByRole('button', { name: 'Play again' }).click();
  await expect(result).toHaveCount(0);
  await expectPlanningRound(page, 1);
  await expect(tokenAt(page, 9, 10)).toBeVisible(); // a fresh Dummy
});

test('Skirmish: command three heroes in one round with Lock In All', async ({ browser }) => {
  const page = await newActor(browser);
  await startQuickBattle(page, 'Skirmish');

  // Bruiser (8,2), Skirmisher (10,2), Scout (12,2); goblins on row 14.
  const actionBar = page.locator('.action-bar');
  await expect(actionBar).toContainText('Planning for Bruiser');
  await cell(page, 8, 4).click();

  await tokenAt(page, 10, 2).click();
  await expect(actionBar).toContainText('Planning for Skirmisher');
  await cell(page, 10, 5).click();

  await page.locator('app-encounter-tracker tr', { hasText: 'Scout' }).click(); // the tracker selects too
  await expect(actionBar).toContainText('Planning for Scout');
  await cell(page, 12, 6).click();

  const lockIn = page.getByRole('button', { name: /Lock In All/ });
  await expect(lockIn).toHaveText(/Lock In All \(3\/3 planned\)/);
  await lockIn.click();

  await expect(feedEntries(page).filter({ hasText: 'Round 1 Resolution' })).toHaveCount(1);
  for (const line of ['Bruiser moves to (8, 4)', 'Skirmisher moves to (10, 5)', 'Scout moves to (12, 6)'])
    await expect(feedEntries(page).filter({ hasText: line })).toHaveCount(1);
  await expect(feedEntries(page).filter({ hasText: /Goblin \d moves to/ })).toHaveCount(3);
  await expectPlanningRound(page, 2);
  await expect(tokenAt(page, 8, 4)).toBeVisible();
});
