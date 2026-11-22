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
  // The log reads oldest first and opens with the battle's start.
  await expect(feedEntries(page).first()).toContainText('Battle Start');

  // Hero (9,9) hits the 1-HP Dummy (9,10); the Dummy's dodge is 1, so the first hit wins.
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

test('Skirmish: command three heroes in one round with Lock In All, queueing two moves (AP)', async ({ browser }) => {
  const page = await newActor(browser);
  await startQuickBattle(page, 'Skirmish');

  // Bruiser (8,2), Skirmisher (10,2), Scout (12,2); goblins on row 14.
  const actionBar = page.locator('.action-bar');
  await expect(actionBar).toContainText('Planning for Bruiser');
  await expect(actionBar.locator('.ap-budget')).toHaveText(/AP 0\/4/);
  // The Bruiser (4 AP, 4 cells a round) queues two moves of 2 cells each (3B.2).
  await cell(page, 8, 4).click();
  await cell(page, 8, 6).click();
  await expect(actionBar.locator('.ap-budget')).toHaveText(/AP 2\/4/);
  await expect(page.locator('.planned-action-display')).toContainText('1. Move to (8, 4) → 2. Move to (8, 6)');
  await expect(cell(page, 8, 7)).not.toHaveClass(/in-range/); // movement budget used up

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
  for (const line of ['Bruiser moves to (8, 4)', 'Bruiser moves to (8, 6)', 'Skirmisher moves to (10, 5)', 'Scout moves to (12, 6)'])
    await expect(feedEntries(page).filter({ hasText: line })).toHaveCount(1);
  await expect(feedEntries(page).filter({ hasText: 'Tick 2' })).toHaveCount(1);
  await expect(feedEntries(page).filter({ hasText: /Goblin \d moves to/ })).toHaveCount(3);
  await expectPlanningRound(page, 2, 15_000); // two ticks of playback
  await expect(tokenAt(page, 8, 6)).toBeVisible();

  // The round timeline stays up for review: one column per tick, the Bruiser's two moves in T1 and T2.
  const timeline = page.locator('app-round-timeline');
  await expect(timeline.locator('h4')).toContainText('Last round (1)');
  await expect(timeline.locator('.tick-head')).toHaveText(['T1', 'T2']);
  const bruiser = timeline.locator('tr', { hasText: 'Bruiser' });
  await expect(bruiser.locator('.lane-name .ap')).toHaveText('2/4 AP');
  await expect(bruiser.locator('.slot.slot-move')).toHaveCount(2);
  await timeline.locator('.tick-head', { hasText: 'T1' }).click();
  await expect(timeline.locator('.details li', { hasText: 'Bruiser moves 2 cells to (8, 4)' })).toHaveCount(1);
});

test('Rescue: a downed hero bleeds out unless an ally stabilizes them (3B.5)', async ({ browser }) => {
  const page = await newActor(browser);
  await startQuickBattle(page, 'Rescue');
  const tracker = page.locator('app-encounter-tracker');
  const archerRow = tracker.locator('tr', { hasText: 'Archer' });

  // Round 1: the Ogre (AI) hits the 1-HP Archer, who goes down instead of dying. Its queued
  // second attack is wasted: finishing blows have to be planned.
  await page.getByRole('button', { name: /Defend/ }).click();               // the Knight defends
  await page.getByRole('button', { name: /Lock In All/ }).click();
  await expectPlanningRound(page, 2, 20_000);
  await expect(archerRow).toContainText('DOWN (2)');
  await expect(feedEntries(page).filter({ hasText: 'Archer for' }).filter({ hasText: 'goes down!' })).toHaveCount(1);
  await expect(tokenAt(page, 9, 10)).toHaveClass(/downed-token/);
  await expect(page.locator('.stabilize-hint')).toContainText('Archer is bleeding out');

  // Round 2: the Knight (already next to the Archer) clicks them to queue Stabilize.
  await tokenAt(page, 9, 10).click();
  await expect(page.locator('.planned-action-display')).toContainText('Stabilize Archer');
  await page.getByRole('button', { name: 'Lock In', exact: true }).click();
  await expect(feedEntries(page).filter({ hasText: 'Knight stabilizes Archer' })).toHaveCount(1, { timeout: 20_000 });
  await expect(archerRow).toContainText('STABLE', { timeout: 20_000 });
});

test('Abilities: queue Power Strike on an adjacent goblin; it resolves and goes on cooldown (3B.3)', async ({ browser }) => {
  const page = await newActor(browser);
  await startQuickBattle(page, 'Abilities');

  // Bruiser (9,9) starts next to Goblin 1 (9,8). Its profile gives it Power Strike (3 AP, cooldown 2).
  const actionBar = page.locator('.action-bar');
  await expect(actionBar).toContainText('Planning for Bruiser');
  const strike = actionBar.locator('.ability-btn', { hasText: 'Power Strike' });
  await expect(strike).toContainText('3 AP');
  await strike.click();
  await expect(page.locator('.armed-hint')).toContainText('Power Strike: click an enemy');
  await expect(tokenAt(page, 9, 8)).toHaveClass(/ability-target/);
  await tokenAt(page, 9, 8).click();
  await expect(page.locator('.planned-action-display')).toContainText('Power Strike on Goblin 1');
  await expect(actionBar.locator('.ap-budget')).toHaveText(/AP 3\/4/);
  await expect(strike).toBeDisabled(); // once per round

  // The other two heroes defend.
  await page.getByRole('button', { name: /Lock In All/ }).click();
  await expect(feedEntries(page).filter({ hasText: "Bruiser's Power Strike" })).toHaveCount(1, { timeout: 20_000 });

  // Round 2: Power Strike is cooling down (2 rounds), so it can't be planned.
  await expectPlanningRound(page, 2, 20_000);
  await expect(actionBar).toContainText('Planning for Bruiser');
  const strikeNow = page.locator('.action-bar .ability-btn', { hasText: 'Power Strike' });
  await expect(strikeNow).toContainText('⏳ 2');
  await expect(strikeNow).toBeDisabled();
  await expect(page.locator('app-encounter-tracker tr', { hasText: 'Bruiser' }).locator('.cooldown-chip'))
    .toContainText('Power Strike 2');
});

test('Abilities: Cleave both adjacent goblins and Mend the hurt Scout (3B.3)', async ({ browser }) => {
  const page = await newActor(browser);
  await startQuickBattle(page, 'Abilities');

  // Bruiser (9,9) starts next to Goblin 1 (9,8) and Goblin 2 (10,8). Cleave finds its own targets.
  const actionBar = page.locator('.action-bar');
  await expect(actionBar).toContainText('Planning for Bruiser');
  await actionBar.locator('.ability-btn', { hasText: 'Cleave' }).click();
  await expect(page.locator('.armed-hint')).toContainText('Cleave hits every enemy adjacent');
  await expect(cell(page, 10, 8)).toHaveClass(/ability-range/);
  await page.locator('.armed-confirm', { hasText: 'Use Cleave' }).click();
  await expect(page.locator('.planned-action-display')).toContainText('Cleave');

  // The Skirmisher (10,11) mends the Scout (11,12), who starts on 5 of 12 HP. Clicking the
  // Scout with Mend armed targets them instead of switching to planning for them.
  await tokenAt(page, 10, 11).click();
  await expect(actionBar).toContainText('Planning for Skirmisher');
  await actionBar.locator('.ability-btn', { hasText: 'Mend' }).click();
  await expect(tokenAt(page, 11, 12)).toHaveClass(/ability-target/);
  await tokenAt(page, 11, 12).click();
  await expect(actionBar).toContainText('Planning for Skirmisher');
  await expect(page.locator('.planned-action-display')).toContainText('Mend on Scout');

  await page.getByRole('button', { name: /Lock In All/ }).click();
  // Both starting goblins get their own Cleave line (enemies who walk up in the same tick are hit too).
  for (const goblin of ['Goblin 1', 'Goblin 2'])
    await expect(feedEntries(page).filter({ hasText: new RegExp(`Bruiser's Cleave (hits|misses) ${goblin}\\b`) })).toHaveCount(1, { timeout: 20_000 });
  await expect(feedEntries(page).filter({ hasText: "Skirmisher's Mend heals Scout for" })).toHaveCount(1, { timeout: 20_000 });
});
