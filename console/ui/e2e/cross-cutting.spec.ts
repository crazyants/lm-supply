import { test, expect } from './fixtures/base.fixture';

// ================================================================
// Cross-cutting: ModelSelector consistency — Test plan section 15.2
// P0: No "undefined" in any model dropdown
// ================================================================

test.describe('ModelSelector consistency across pages', () => {
  const pagesWithModelSelector = [
    { path: '/chat', heading: /Chat/, label: 'Chat' },
    { path: '/embed', heading: /Text Embedding/, label: 'Embed' },
    { path: '/rerank', heading: /Document Reranking/, label: 'Rerank' },
    { path: '/transcribe', heading: /Speech to Text/, label: 'Transcribe' },
    { path: '/synthesize', heading: /Text to Speech/, label: 'Synthesize' },
  ];

  for (const { path, heading, label } of pagesWithModelSelector) {
    // Test plan: 15.2.1-15.2.5 — Alias not "undefined"
    test(`${label} page model dropdown does not show "undefined"`, async ({ page }) => {
      await page.goto(path);
      await expect(page.locator('main').getByRole('heading', { name: heading }).first()).toBeVisible();

      // Wait for models to load
      await expect(page.getByText('Loading models...')).toBeHidden({ timeout: 15_000 });

      const select = page.locator('select');
      const options = await select.locator('option').allTextContents();

      for (const opt of options) {
        expect(opt).not.toContain('undefined');
        expect(opt.trim()).not.toBe('');
      }
    });
  }
});

// ================================================================
// Cross-cutting: Error handling — Test plan section 15.1
// ================================================================

test.describe('Error handling', () => {
  // Test plan: 15.1.1 — Pages load without crashing when navigated to quickly
  test('rapid navigation between pages does not crash', async ({ page }) => {
    const routes = ['/chat', '/embed', '/rerank', '/transcribe', '/synthesize',
      '/caption', '/ocr', '/detect', '/segment', '/translate', '/image-generate', '/models'];

    for (const route of routes) {
      await page.goto(route);
      // Each page should have a visible main element (not a crash)
      await expect(page.locator('main')).toBeVisible();
    }
  });
});

// ================================================================
// Cross-cutting: Responsive layout — Test plan section 15.3
// ================================================================

test.describe('Responsive layout', () => {
  // Test plan: 15.3.1 — Narrow viewport
  test('layout remains usable at 1024px width', async ({ page }) => {
    await page.setViewportSize({ width: 1024, height: 768 });
    await page.goto('/');

    // Sidebar should still be visible
    const sidebar = page.locator('aside, nav').first();
    await expect(sidebar).toBeVisible();

    // Main content should be visible
    await expect(page.locator('main')).toBeVisible();

    // Navigate to a content-heavy page
    await page.goto('/models');
    await expect(page.locator('main').getByRole('heading', { name: /Model Management/ })).toBeVisible();

    // No horizontal overflow — page width should not exceed viewport
    const bodyWidth = await page.evaluate(() => document.body.scrollWidth);
    expect(bodyWidth).toBeLessThanOrEqual(1024 + 20); // small tolerance
  });
});
