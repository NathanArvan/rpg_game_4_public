import { Browser, expect, Locator, Page } from '@playwright/test';

/**
 * Small page-object helpers for driving the lobby and combat UI.
 * Each actor gets its own BrowserContext so auth (sessionStorage) and SignalR connections are isolated,
 * exactly like two people on two machines.
 */

export async function newActor(browser: Browser): Promise<Page> {
  const context = await browser.newContext();
  const page = await context.newPage();
  page.on('pageerror', err => console.error(`[pageerror] ${err.message}`));
  await page.goto('/');
  await quickRegister(page);
  return page;
}

export async function quickRegister(page: Page): Promise<void> {
  await page.getByRole('button', { name: /Quick Register/ }).click();
  await expect(page.getByRole('button', { name: 'Logout' })).toBeVisible();
}

/** GM: create campaign → start session → connect. Returns the join code. */
export async function gmOpenSession(page: Page, campaignName: string): Promise<string> {
  await page.getByRole('button', { name: 'GM', exact: true }).click();
  await page.getByPlaceholder('Campaign name').fill(campaignName);
  await page.getByRole('button', { name: 'Create Campaign' }).click();
  await page.getByRole('button', { name: 'Start Session' }).click();

  const joinCodeBox = page.locator('.join-code');
  await expect(joinCodeBox).toHaveText(/^\s*[A-Z0-9]{6}\s*$/);
  const joinCode = (await joinCodeBox.innerText()).trim();

  await page.getByRole('button', { name: 'Connect to Session' }).click();
  await expect(page.getByPlaceholder('Monster name')).toBeVisible();
  return joinCode;
}

/** GM: stage a monster in the lobby. AI-controlled by default, like the checkbox. */
export async function gmAddMonster(page: Page, name: string, { ai = true }: { ai?: boolean } = {}): Promise<void> {
  await page.getByPlaceholder('Monster name').fill(name);
  await page.getByLabel(/AI controls this monster/).setChecked(ai);
  await page.getByRole('button', { name: /Add Monster/ }).click();
  await expect(page.locator('.monster-list .monster-name', { hasText: name })).toBeVisible();
}

/** Player: quick-create a random character on /character, then return to the lobby. */
export async function playerCreateCharacter(page: Page): Promise<void> {
  await page.getByRole('link', { name: 'My Characters' }).click();
  await page.getByRole('button', { name: /Quick Create Random/ }).click();
  // Quick create selects the new character and opens its edit view
  await expect(page.getByRole('heading', { name: 'Edit Character' })).toBeVisible();
  await page.getByRole('link', { name: /Back to Lobby/ }).click();
}

/** Player: join with the (only) character. Returns the character name as shown in combat. */
export async function playerJoinSession(page: Page, joinCode: string, displayName: string): Promise<string> {
  await page.getByPlaceholder(/Join code/).fill(joinCode);
  await page.getByPlaceholder('Display name in session').fill(displayName);
  const characterSelect = page.locator('app-player-join select');
  const characterOption = characterSelect.locator('option').nth(1); // index 0 is the placeholder
  await expect(characterOption).toBeAttached();
  // Option text is "<name> (<race> <class>, Lv<n>)"
  const characterName = (await characterOption.innerText()).replace(/\s*\(.*$/, '').trim();
  await characterSelect.selectOption({ index: 1 });
  await page.getByRole('button', { name: 'Join Session' }).click();
  return characterName;
}

// ---- Combat helpers ----

export function cell(page: Page, x: number, y: number): Locator {
  return page.locator(`.grid .cell[data-x="${x}"][data-y="${y}"]`);
}

export function tokenAt(page: Page, x: number, y: number): Locator {
  return cell(page, x, y).locator('.token');
}

export function feedEntries(page: Page): Locator {
  return page.locator('app-feed .entry');
}

/** @param timeout ms to wait; playback takes about 3 s per tick, so multi-action rounds need longer. */
export async function expectPlanningRound(page: Page, round: number, timeout?: number): Promise<void> {
  await expect(page.locator('app-round-banner .phase')).toHaveText(new RegExp(`Planning\\s+.\\s+Round ${round}\\b`), { timeout });
}

export async function playerLockIn(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Lock In', exact: true }).click();
  await expect(page.getByRole('button', { name: /Locked in/ })).toBeDisabled();
}

export async function gmLockInEnemies(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Lock In Enemies' }).click();
}

// ---- Quick Battle ----

/** Lobby: start a Quick Battle from a preset (by its menu name) and wait for round 1. */
export async function startQuickBattle(page: Page, presetName: string): Promise<void> {
  await page.getByLabel('Battle preset').selectOption({ label: presetName });
  await page.getByRole('button', { name: 'Start Quick Battle' }).click();
  await expect(page.locator('app-combat-layout')).toBeVisible();
  await expectPlanningRound(page, 1);
}
