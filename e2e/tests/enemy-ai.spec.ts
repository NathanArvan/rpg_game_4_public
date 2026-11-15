import { expect, test } from '@playwright/test';
import {
  cell,
  expectPlanningRound,
  feedEntries,
  gmAddMonster,
  gmOpenSession,
  newActor,
  playerCreateCharacter,
  playerJoinSession,
  tokenAt,
} from './support/actors';

/**
 * An AI-controlled monster plans and locks in on its own: the round resolves as soon as the player
 * locks in, with no GM input, and the monster walks toward the party.
 *
 * Spawn rules (EncounterService): players start on row 0, monsters on row 19. The AI moves up to
 * 6 cells along the straight path toward the nearest enemy.
 */
test('AI monster closes in without the GM planning it', async ({ browser }) => {
  const gm = await newActor(browser);
  const player = await newActor(browser);
  const displayName = `E2E AI ${Date.now() % 10000}`;
  const monsterName = 'Goblin';

  await test.step('lobby: GM stages an AI monster; player joins', async () => {
    const joinCode = await gmOpenSession(gm, `E2E AI Campaign ${Date.now()}`);
    await gmAddMonster(gm, monsterName); // AI by default
    await expect(gm.locator('.monster-list .ai-badge')).toBeVisible();

    await playerCreateCharacter(player);
    await playerJoinSession(player, joinCode, displayName);
    await expect(gm.locator('.players-section li', { hasText: displayName })).toBeVisible();
  });

  await test.step('round 1 opens with the AI monster already locked in', async () => {
    await gm.getByRole('button', { name: 'Start Encounter' }).click();
    for (const page of [gm, player]) {
      await expect(page.locator('app-combat-layout')).toBeVisible();
      await expectPlanningRound(page, 1);
    }
    await expect(gm.locator('app-encounter-tracker .lock-in-list')).toContainText(monsterName);
    await expect(gm.locator('.enemy-plan-badge.ai')).toBeVisible();
  });

  await test.step('player locks in; the round resolves with no GM action and the goblin advances', async () => {
    await cell(player, 0, 1).click(); // one step forward
    // Not playerLockIn(): with every enemy already locked in, the round resolves immediately and
    // the action bar (with its "Locked in" button) goes away.
    await player.getByRole('button', { name: 'Lock In', exact: true }).click();

    for (const page of [gm, player]) {
      await expect(feedEntries(page).filter({ hasText: 'Round 1 Resolution' })).toHaveCount(1);
      await expect(feedEntries(page).filter({ hasText: `${monsterName} moves to (0, 13)` })).toHaveCount(1);
      await expectPlanningRound(page, 2);
      await expect(tokenAt(page, 0, 13)).toBeVisible();
    }
  });
});
