import { test, expect } from '@playwright/test';

test('landing page shows login', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('form')).toBeVisible();
});

test('login flow', async ({ page }) => {
  await page.goto('/');

  await page.fill('input[type="email"]', 'test@example.com');
  await page.fill('input[type="password"]', 'password123');
  await page.click('button[type="submit"]');

  await expect(page).toHaveURL('/dashboard');
});

test('authenticated user sees layout', async ({ page }) => {
  // Assume user is logged in via session/cookies
  await page.goto('/dashboard');

  await expect(page.locator('[data-testid="sidenav"]')).toBeVisible();
  await expect(page.locator('[data-testid="header"]')).toBeVisible();
  await expect(page.locator('[data-testid="main"]')).toBeVisible();
});
