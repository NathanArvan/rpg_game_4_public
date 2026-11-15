import { expect, test } from '@playwright/test';
import {
  cell,
  expectPlanningRound,
  feedEntries,
  gmAddMonster,
  gmLockInEnemies,
  gmOpenSession,
  newActor,
  playerCreateCharacter,
  playerJoinSession,
  playerLockIn,
  tokenAt,
} from './support/actors';

/**
 * Full Phase 3A loop with two real users (GM + player) in isolated browser contexts:
 * lobby → encounter → round 1 move → round 2 ranged attack, asserting both screens stay in sync.
 *
 * Spawn rules (EncounterService): players start on row 0, monsters on row 19, left to right.
 */
test('GM and player play two simultaneous rounds', async ({ browser }) => {
  const gm = await newActor(browser);
  const player = await newActor(browser);
  const displayName = `E2E Hero ${Date.now() % 10000}`;
  const monsterName = 'Goblin';
  let characterName = '';

  await test.step('lobby: GM opens a session and stages a monster; player joins', async () => {
    const joinCode = await gmOpenSession(gm, `E2E Campaign ${Date.now()}`);
    await gmAddMonster(gm, monsterName, { ai: false }); // this spec covers manual GM planning

    await playerCreateCharacter(player);
    characterName = await playerJoinSession(player, joinCode, displayName);

    // Presence broadcast reaches the GM over SignalR
    await expect(gm.locator('.players-section li', { hasText: displayName })).toBeVisible();
  });

  await test.step('GM starts the encounter; both screens enter round 1 planning', async () => {
    await gm.getByRole('button', { name: 'Start Encounter' }).click();
    for (const page of [gm, player]) {
      await expect(page.locator('app-combat-layout')).toBeVisible();
      await expectPlanningRound(page, 1);
      await expect(tokenAt(page, 0, 0)).toBeVisible(); // player
      await expect(tokenAt(page, 0, 19)).toBeVisible(); // monster
    }
    await expect(tokenAt(player, 0, 0)).toHaveClass(/my-token/);
  });

  await test.step('round 1: player plans a move, both sides lock in, move resolves everywhere', async () => {
    await cell(player, 3, 3).click();
    await expect(player.locator('.planned-action-display')).toContainText('Move');
    await playerLockIn(player);

    // GM sees the lock-in before committing enemies (they default to defend)
    await expect(gm.locator('app-encounter-tracker .lock-in-list')).toContainText(characterName);
    await gmLockInEnemies(gm);

    for (const page of [gm, player]) {
      await expect(feedEntries(page).filter({ hasText: 'Round 1 Resolution' })).toHaveCount(1);
      await expect(feedEntries(page).filter({ hasText: `${characterName} moves to (3, 3)` })).toHaveCount(1);
      await expectPlanningRound(page, 2);
      await expect(tokenAt(page, 3, 3)).toBeVisible();
      await expect(tokenAt(page, 0, 0)).toHaveCount(0);
    }
  });

  await test.step('round 2: player plans a ranged attack on the monster; result is logged on both screens', async () => {
    await tokenAt(player, 0, 19).click();
    await expect(player.locator('.planned-action-display')).toContainText('Ranged Attack');
    await playerLockIn(player);
    await gmLockInEnemies(gm);

    // Hit or miss is random (d20 vs 10), so assert on the attack line rather than the outcome.
    const attackLine = new RegExp(`${characterName}.*${monsterName}`);
    for (const page of [gm, player]) {
      await expect(feedEntries(page).filter({ hasText: 'Round 2 Resolution' })).toHaveCount(1);
      await expect(feedEntries(page).filter({ hasText: attackLine })).toHaveCount(1);
      await expectPlanningRound(page, 3);
    }
  });
});
