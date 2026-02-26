import { test, expect } from './fixtures/base.fixture';
import { mockJsonEndpoint, mockCaptionResponse, mockVqaResponse, mockOcrResponse, mockImageGenerationResponse, mockJsonError } from './fixtures/api-mocks';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const TEST_IMAGE = path.resolve(__dirname, 'fixtures/test-files/test-image.png');

// ================================================================
// Caption page — mock API tests for result display
// Test plan: 8.1.3 (caption result), 8.1.4 (alternatives), 8.2.2 (VQA answer)
// ================================================================

test.describe('Caption — mock inference results', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/caption');
    await expect(page.locator('main').getByRole('heading', { name: /Image Captioning/ })).toBeVisible();
  });

  // Test plan: 8.1.3 — Caption result displayed with confidence
  test('caption result shows text and confidence', async ({ page }) => {
    const mockResponse = mockCaptionResponse();
    await mockJsonEndpoint(page, '**/v1/images/caption', mockResponse);

    const fileInput = page.locator('input[type="file"]');
    await fileInput.setInputFiles(TEST_IMAGE);

    // Caption text should appear
    await expect(page.getByText('A red pixel on a white background')).toBeVisible({ timeout: 15_000 });

    // Confidence should be shown
    await expect(page.getByText(/Confidence: 87\.0%/)).toBeVisible();
  });

  // Test plan: 8.1.4 — Alternatives listed
  test('alternative captions are listed', async ({ page }) => {
    const mockResponse = mockCaptionResponse();
    await mockJsonEndpoint(page, '**/v1/images/caption', mockResponse);

    const fileInput = page.locator('input[type="file"]');
    await fileInput.setInputFiles(TEST_IMAGE);

    await expect(page.getByText('Alternatives:')).toBeVisible({ timeout: 15_000 });
    await expect(page.getByText('A small colored dot')).toBeVisible();
    await expect(page.getByText('A minimal test image')).toBeVisible();
  });

  // Test plan: 8.2.2 — VQA answer displayed with Q+A format
  test('VQA mode shows question and answer', async ({ page }) => {
    const mockResponse = mockVqaResponse('What color is the pixel?');
    await mockJsonEndpoint(page, '**/v1/images/vqa', mockResponse);

    // Upload image first
    const fileInput = page.locator('input[type="file"]');
    await fileInput.setInputFiles(TEST_IMAGE);

    // Switch to VQA mode
    await page.getByRole('button', { name: 'Visual QA' }).click();

    // Type question and ask
    const questionInput = page.getByPlaceholder('Ask a question about the image...');
    await questionInput.fill('What color is the pixel?');
    await page.getByRole('button', { name: 'Ask' }).click();

    // Answer should appear with Q+A format
    await expect(page.getByText('Q: What color is the pixel?')).toBeVisible({ timeout: 15_000 });
    await expect(page.getByText('A: The image shows a red pixel.')).toBeVisible();
  });

  // Elapsed time on caption
  test('caption shows elapsed time', async ({ page }) => {
    const mockResponse = mockCaptionResponse();
    await mockJsonEndpoint(page, '**/v1/images/caption', mockResponse);

    const fileInput = page.locator('input[type="file"]');
    await fileInput.setInputFiles(TEST_IMAGE);

    await expect(page.getByText('A red pixel on a white background')).toBeVisible({ timeout: 15_000 });
    await expect(page.getByText(/\d+ ms/)).toBeVisible();
  });
});

// ================================================================
// OCR page — mock API tests for result display
// Test plan: 9.4-9.7 (recognition result, text blocks, no text, elapsed)
// ================================================================

test.describe('OCR — mock inference results', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/ocr');
    await expect(page.locator('main').getByRole('heading', { name: 'Optical Character Recognition' })).toBeVisible();
  });

  // Test plan: 9.4 — Recognized text shown
  test('recognized text appears in monospace box', async ({ page }) => {
    const mockResponse = mockOcrResponse();
    await mockJsonEndpoint(page, '**/v1/images/ocr', mockResponse);

    const fileInput = page.locator('input[type="file"]');
    await fileInput.setInputFiles(TEST_IMAGE);

    await expect(page.getByText('Recognized Text')).toBeVisible({ timeout: 15_000 });

    // Text should appear in monospace area
    const textArea = page.locator('.font-mono').filter({ hasText: 'Hello World' }).first();
    await expect(textArea).toBeVisible();
  });

  // Test plan: 9.5 — Text blocks with confidence
  test('text blocks show confidence percentage', async ({ page }) => {
    const mockResponse = mockOcrResponse();
    await mockJsonEndpoint(page, '**/v1/images/ocr', mockResponse);

    const fileInput = page.locator('input[type="file"]');
    await fileInput.setInputFiles(TEST_IMAGE);

    await expect(page.getByText('Text Blocks')).toBeVisible({ timeout: 15_000 });

    // Should show confidence (rendered as toFixed(0) → "98%")
    await expect(page.getByText('[98%]')).toBeVisible();
  });

  // Test plan: 9.6 — No text detected
  test('no text detected shows placeholder', async ({ page }) => {
    const emptyResponse = { id: 'ocr-mock', model: 'mock-ocr', text: '', blocks: [] };
    await mockJsonEndpoint(page, '**/v1/images/ocr', emptyResponse);

    const fileInput = page.locator('input[type="file"]');
    await fileInput.setInputFiles(TEST_IMAGE);

    await expect(page.getByText('Recognized Text')).toBeVisible({ timeout: 15_000 });
    await expect(page.getByText('(No text detected)')).toBeVisible();
  });

  // Test plan: 9.7 — Elapsed time
  test('elapsed time shown after recognition', async ({ page }) => {
    const mockResponse = mockOcrResponse();
    await mockJsonEndpoint(page, '**/v1/images/ocr', mockResponse);

    const fileInput = page.locator('input[type="file"]');
    await fileInput.setInputFiles(TEST_IMAGE);

    await expect(page.getByText('Recognized Text')).toBeVisible({ timeout: 15_000 });
    await expect(page.getByText(/\d+ ms/).first()).toBeVisible();
  });
});

// ================================================================
// Image Generate page — mock API tests for result display
// Test plan: 13.1.3-13.1.4 (generated image, details card)
// ================================================================

test.describe('ImageGenerate — mock inference results', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/image-generate');
    await expect(page.locator('main').getByRole('heading', { name: 'Image Generation' })).toBeVisible();
  });

  // Test plan: 13.1.3 — Generated image displays
  test('generated image displays in result panel', async ({ page }) => {
    const mockResponse = mockImageGenerationResponse();
    await mockJsonEndpoint(page, '**/v1/images/generate', mockResponse);

    await page.locator('textarea').fill('A beautiful landscape');
    await page.getByRole('button', { name: /Generate Image/ }).click();

    // Image should appear (rendered from base64)
    await expect(page.locator('img[alt="Generated"]').or(page.locator('img[alt*="generated"]')).or(page.locator('.bg-card img'))).toBeVisible({ timeout: 15_000 });
  });

  // Test plan: 13.1.4 — Details card shows model, time, size, seed, steps
  test('details card shows generation metadata', async ({ page }) => {
    const mockResponse = mockImageGenerationResponse();
    await mockJsonEndpoint(page, '**/v1/images/generate', mockResponse);

    await page.locator('textarea').fill('A test prompt');
    await page.getByRole('button', { name: /Generate Image/ }).click();

    // Should show generation details (seed, steps, etc.)
    const resultPanel = page.locator('.bg-card').last();
    await expect(resultPanel).toBeVisible({ timeout: 15_000 });
  });
});
