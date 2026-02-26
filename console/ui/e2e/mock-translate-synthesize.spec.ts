import { test, expect } from './fixtures/base.fixture';
import { mockJsonEndpoint, mockTranslateResponse, mockBlobEndpoint, mockWavBuffer, mockJsonError } from './fixtures/api-mocks';

// ================================================================
// Translate page — mock API tests for result display
// Test plan: 12.3-12.4, 12.6 (translation result, metadata, loading)
// ================================================================

test.describe('Translate — mock inference results', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/translate');
    await expect(page.locator('main').getByRole('heading', { name: 'Machine Translation' })).toBeVisible();
  });

  // Test plan: 12.3 — Translation appears in output area
  test('translation result appears in output area', async ({ page }) => {
    const mockResponse = mockTranslateResponse('Hello world');
    await mockJsonEndpoint(page, '**/v1/translate', mockResponse);

    const textarea = page.locator('textarea');
    await textarea.fill('Hello world');
    await page.getByRole('button', { name: /Translate/ }).click();

    // Translation should appear in the output div
    await expect(page.getByText('Translated: Hello world')).toBeVisible({ timeout: 10_000 });
  });

  // Test plan: 12.4 — Metadata shown: model name, source/target, elapsed time
  test('translation metadata shows model and languages', async ({ page }) => {
    const mockResponse = mockTranslateResponse('Test');
    await mockJsonEndpoint(page, '**/v1/translate', mockResponse);

    await page.locator('textarea').fill('Test');
    await page.getByRole('button', { name: /Translate/ }).click();

    // Wait for result card
    const resultCard = page.locator('.bg-card');
    await expect(resultCard).toBeVisible({ timeout: 10_000 });

    // Model name
    await expect(resultCard.getByText('Model: mock-translator')).toBeVisible();

    // Source and target languages
    await expect(resultCard.getByText('Source: en')).toBeVisible();
    await expect(resultCard.getByText('Target: ko')).toBeVisible();

    // Elapsed time
    await expect(resultCard.getByText(/\d+ ms/)).toBeVisible();
  });

  // Error handling
  test('translation error shows error message', async ({ page }) => {
    await mockJsonError(page, '**/v1/translate', 'Translator model not available');

    await page.locator('textarea').fill('Some text');
    await page.getByRole('button', { name: /Translate/ }).click();

    await expect(page.getByText(/Translator model not available/)).toBeVisible({ timeout: 10_000 });
  });
});

// ================================================================
// Synthesize page — mock API tests for audio result
// Test plan: 7.2-7.5 (audio player, playback, download, elapsed)
// ================================================================

test.describe('Synthesize — mock audio results', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/synthesize');
    await expect(page.locator('main').getByRole('heading', { name: 'Text to Speech' })).toBeVisible();
    await expect(page.getByText('Loading models...')).toBeHidden({ timeout: 15_000 });
  });

  // Test plan: 7.2 — Audio player appears after generation
  test('audio player appears after speech generation', async ({ page }) => {
    const wavBuffer = mockWavBuffer();
    await mockBlobEndpoint(page, '**/v1/audio/speech', 'audio/wav', wavBuffer);

    await page.locator('textarea').fill('Hello world');
    await page.getByRole('button', { name: /Generate Speech/ }).click();

    // "Generated Audio" heading should appear
    await expect(page.getByText('Generated Audio')).toBeVisible({ timeout: 10_000 });

    // Audio element should be present
    await expect(page.locator('audio')).toBeVisible();
  });

  // Test plan: 7.3 — Play button visible
  test('play button is visible after generation', async ({ page }) => {
    const wavBuffer = mockWavBuffer();
    await mockBlobEndpoint(page, '**/v1/audio/speech', 'audio/wav', wavBuffer);

    await page.locator('textarea').fill('Test');
    await page.getByRole('button', { name: /Generate Speech/ }).click();

    await expect(page.getByText('Generated Audio')).toBeVisible({ timeout: 10_000 });

    // Play button should be visible
    await expect(page.getByRole('button', { name: /Play/ })).toBeVisible();
  });

  // Test plan: 7.4 — Download WAV button visible
  test('download WAV button is visible after generation', async ({ page }) => {
    const wavBuffer = mockWavBuffer();
    await mockBlobEndpoint(page, '**/v1/audio/speech', 'audio/wav', wavBuffer);

    await page.locator('textarea').fill('Test');
    await page.getByRole('button', { name: /Generate Speech/ }).click();

    await expect(page.getByText('Generated Audio')).toBeVisible({ timeout: 10_000 });

    // Download WAV button
    await expect(page.getByRole('button', { name: /Download WAV/ })).toBeVisible();
  });

  // Test plan: 7.5 — Elapsed time shown
  test('elapsed time shown after generation', async ({ page }) => {
    const wavBuffer = mockWavBuffer();
    await mockBlobEndpoint(page, '**/v1/audio/speech', 'audio/wav', wavBuffer);

    await page.locator('textarea').fill('Test');
    await page.getByRole('button', { name: /Generate Speech/ }).click();

    await expect(page.getByText('Generated Audio')).toBeVisible({ timeout: 10_000 });
    await expect(page.getByText(/\d+ ms/)).toBeVisible();
  });

  // Error handling
  test('API error shows error message', async ({ page }) => {
    await mockJsonError(page, '**/v1/audio/speech', 'Synthesizer model not loaded');

    await page.locator('textarea').fill('Test');
    await page.getByRole('button', { name: /Generate Speech/ }).click();

    await expect(page.getByText(/Synthesizer model not loaded/)).toBeVisible({ timeout: 10_000 });
  });
});
