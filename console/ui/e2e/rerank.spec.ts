import { test, expect } from './fixtures/base.fixture';

test.describe('Rerank page', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/rerank');
    await expect(page.locator('main').getByRole('heading', { name: 'Document Reranking' })).toBeVisible();
  });

  // Test plan: 5.1 — Model dropdown
  test('model selector loads reranker models', async ({ page }) => {
    await expect(page.getByText('Loading models...')).toBeHidden({ timeout: 15_000 });
    const select = page.locator('select');
    const optionCount = await select.locator('option').count();
    expect(optionCount).toBeGreaterThan(0);

    // P0: No "undefined" in options
    const options = await select.locator('option').allTextContents();
    for (const opt of options) {
      expect(opt).not.toContain('undefined');
    }
  });

  // Test plan: 5.5 — Disabled state
  test('submit button is disabled when query or documents are empty', async ({ page }) => {
    await expect(page.getByText('Loading models...')).toBeHidden({ timeout: 15_000 });
    const submitButton = page.getByRole('button', { name: /Rerank/ });
    await expect(submitButton).toBeDisabled();
  });

  // Test plan: 5 — Form elements present
  test('has query input, documents textarea, and Top K input', async ({ page }) => {
    // Query input
    const queryInput = page.getByPlaceholder(/query|search/i);
    await expect(queryInput).toBeVisible();

    // Documents textarea
    const docsTextarea = page.getByPlaceholder(/documents.*one per line/i);
    await expect(docsTextarea).toBeVisible();

    // Top K input (label and input are siblings, not linked via for/id)
    await expect(page.getByText('Top K')).toBeVisible();
    const topKInput = page.locator('input[type="number"]').first();
    await expect(topKInput).toBeVisible();
    // Default value should be 5
    const value = await topKInput.inputValue();
    expect(value).toBe('5');
  });

  // Submit enables with query + documents
  test('submit button enables when query and documents are provided', async ({ page }) => {
    await expect(page.getByText('Loading models...')).toBeHidden({ timeout: 15_000 });

    const queryInput = page.getByPlaceholder(/query|search/i);
    const docsTextarea = page.getByPlaceholder(/documents.*one per line/i);

    await queryInput.fill('What is machine learning?');
    await docsTextarea.fill('ML is a subset of AI\nDeep learning uses neural networks\nNatural language processing');

    const submitButton = page.getByRole('button', { name: /Rerank/ });
    const select = page.locator('select');
    const modelValue = await select.inputValue();
    if (modelValue) {
      await expect(submitButton).toBeEnabled();
    }
  });
});
