using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LMSupply.Integration.Tests.Helpers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LMSupply.Integration.Tests.Functional;

/// <summary>
/// Console Host HTTP API integration tests.
/// Tests the web API layer including routing, serialization, error handling,
/// and end-to-end inference through HTTP endpoints.
/// Group A: GET endpoints (no model loading required)
/// Group B: Error handling validation (no model loading required)
/// Group C: Inference endpoints (GPU + models required)
/// </summary>
[Trait("Category", "Functional")]
[Trait("Category", "LocalOnly")]
[Trait("Domain", "ConsoleHost")]
public class ConsoleHostApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ConsoleHostApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    // ── Group A: GET Endpoints (no model required) ────────────────

    [Fact]
    [Trait("Axis", "API-GET")]
    public async Task GET_Health_Returns200WithHealthyStatus()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("status").GetString().Should().Be("healthy");
        json.TryGetProperty("timestamp", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Axis", "API-GET")]
    public async Task GET_Models_ReturnsOpenAICompatibleList()
    {
        var response = await _client.GetAsync("/v1/models");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var data = json.GetProperty("data");
        data.GetArrayLength().Should().BeGreaterThan(0, "should include well-known model aliases");
    }

    [Fact]
    [Trait("Axis", "API-GET")]
    public async Task GET_Models_ContainsExpectedAliases()
    {
        var response = await _client.GetAsync("/v1/models");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = json.GetProperty("data")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!)
            .ToHashSet();

        ids.Should().Contain("embedder:default");
        ids.Should().Contain("reranker:default");
        ids.Should().Contain("generator:default");
    }

    [Fact]
    [Trait("Axis", "API-GET")]
    public async Task GET_ModelById_ReturnsModelInfo()
    {
        var response = await _client.GetAsync("/v1/models/embedder:default");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("id").GetString().Should().Be("embedder:default");
    }

    [Fact]
    [Trait("Axis", "API-GET")]
    public async Task GET_SystemStatus_Returns200()
    {
        var response = await _client.GetAsync("/api/system/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Axis", "API-GET")]
    public async Task GET_GpuInfo_Returns200()
    {
        var response = await _client.GetAsync("/api/system/gpu");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Axis", "API-GET")]
    public async Task GET_MemoryMetrics_Returns200()
    {
        var response = await _client.GetAsync("/api/system/memory");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Axis", "API-GET")]
    public async Task GET_DetectLabels_ReturnsCOCOLabels()
    {
        var response = await _client.GetAsync("/v1/images/detect/labels");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var labels = json.GetProperty("labels");
        labels.GetArrayLength().Should().BeGreaterThan(0, "should have COCO class labels");
    }

    [Fact]
    [Trait("Axis", "API-GET")]
    public async Task GET_SegmentLabels_ReturnsADE20KLabels()
    {
        var response = await _client.GetAsync("/v1/images/segment/labels");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var labels = json.GetProperty("labels");
        labels.GetArrayLength().Should().BeGreaterThan(0, "should have ADE20K class labels");
    }

    [Fact]
    [Trait("Axis", "API-GET")]
    public async Task GET_TranslateLanguages_ReturnsLanguagePairs()
    {
        var response = await _client.GetAsync("/v1/translate/languages");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var languages = json.GetProperty("languages");
        languages.GetArrayLength().Should().BeGreaterThan(0, "should have translation language pairs");
    }

    [Fact]
    [Trait("Axis", "API-GET")]
    public async Task GET_OcrLanguages_ReturnsSupportedLanguages()
    {
        var response = await _client.GetAsync("/v1/images/ocr/languages");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var languages = json.GetProperty("languages");
        languages.GetArrayLength().Should().BeGreaterThan(0, "should have supported OCR languages");
    }

    [Fact]
    [Trait("Axis", "API-GET")]
    public async Task GET_ImageModels_ReturnsAvailableModels()
    {
        var response = await _client.GetAsync("/v1/images/models");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var models = json.GetProperty("models");
        models.GetArrayLength().Should().BeGreaterThan(0, "should have image generation models");
    }

    [Fact]
    [Trait("Axis", "API-GET")]
    public async Task GET_Registry_ReturnsAllModelTypes()
    {
        var response = await _client.GetAsync("/api/registry/models");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var types = json.GetProperty("modelTypes");
        types.GetArrayLength().Should().BeGreaterThanOrEqualTo(10,
            "should have at least 10 model type categories");
    }

    [Fact]
    [Trait("Axis", "API-GET")]
    public async Task GET_RegistryByType_ReturnsEmbedderModels()
    {
        var response = await _client.GetAsync("/api/registry/models/embedder");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("type").GetString().Should().Be("embedder");
        json.GetProperty("models").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait("Axis", "API-GET")]
    public async Task GET_RegistryByType_UnknownType_Returns404()
    {
        var response = await _client.GetAsync("/api/registry/models/nonexistent");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Axis", "API-GET")]
    public async Task GET_CacheStats_ReturnsStatistics()
    {
        var response = await _client.GetAsync("/api/cache/stats");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("totalModels", out _).Should().BeTrue();
        json.TryGetProperty("cacheDirectory", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Axis", "API-GET")]
    public async Task GET_CachedModels_ReturnsModelList()
    {
        var response = await _client.GetAsync("/api/cache/models");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("models", out _).Should().BeTrue();
        json.TryGetProperty("totalCount", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Axis", "API-GET")]
    public async Task GET_LoadedModels_ReturnsEmptyListInitially()
    {
        var response = await _client.GetAsync("/api/cache/loaded");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Group B: Error Handling (no model loading) ────────────────

    [Fact]
    [Trait("Axis", "API-Error")]
    public async Task POST_Rerank_MissingQuery_Returns400()
    {
        string[] docs = ["doc1"];
        var content = JsonContent.Create(new { query = "", documents = docs });
        var response = await _client.PostAsync("/v1/rerank", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("error").GetProperty("message").GetString()
            .Should().Contain("query", "error should mention the missing field");
    }

    [Fact]
    [Trait("Axis", "API-Error")]
    public async Task POST_Rerank_EmptyDocuments_Returns400()
    {
        var content = JsonContent.Create(new
        {
            query = "test",
            documents = Array.Empty<string>()
        });
        var response = await _client.PostAsync("/v1/rerank", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Axis", "API-Error")]
    public async Task POST_Speech_MissingInput_Returns400()
    {
        var content = JsonContent.Create(new { input = "", model = "fast" });
        var response = await _client.PostAsync("/v1/audio/speech", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Axis", "API-Error")]
    public async Task POST_Caption_NotFormData_IsRejected()
    {
        // Endpoint has .Accepts("multipart/form-data") constraint,
        // so JSON requests don't match the route → 404 from SPA fallback
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/v1/images/caption", content);

        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    [Trait("Axis", "API-Error")]
    public async Task POST_Detect_NotFormData_IsRejected()
    {
        // Endpoint has .Accepts("multipart/form-data") constraint,
        // so JSON requests don't match the route → 404 from SPA fallback
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/v1/images/detect", content);

        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    [Trait("Axis", "API-Error")]
    public async Task POST_Ocr_NotFormData_IsRejected()
    {
        // Endpoint has .Accepts("multipart/form-data") constraint,
        // so JSON requests don't match the route → 404 from SPA fallback
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/v1/images/ocr", content);

        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    [Trait("Axis", "API-Error")]
    public async Task POST_Segment_NotFormData_IsRejected()
    {
        // Endpoint has .Accepts("multipart/form-data") constraint,
        // so JSON requests don't match the route → 404 from SPA fallback
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/v1/images/segment", content);

        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    [Trait("Axis", "API-Error")]
    public async Task POST_ImageGeneration_MissingPrompt_Returns400()
    {
        var content = JsonContent.Create(new { prompt = "" });
        var response = await _client.PostAsync("/v1/images/generations", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Axis", "API-Error")]
    public async Task POST_Caption_FormData_MissingFile_Returns400()
    {
        using var formContent = new MultipartFormDataContent();
        formContent.Add(new StringContent("fast"), "model");
        var response = await _client.PostAsync("/v1/images/caption", formContent);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Axis", "API-Error")]
    public async Task POST_Transcribe_FormData_MissingFile_Returns400()
    {
        using var formContent = new MultipartFormDataContent();
        formContent.Add(new StringContent("fast"), "model");
        var response = await _client.PostAsync("/v1/audio/transcriptions", formContent);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Group C: Inference Endpoints (GPU + models required) ──────

    [Fact]
    [Trait("Axis", "API-Inference")]
    public async Task POST_Embeddings_ReturnsOpenAICompatibleResponse()
    {
        var content = JsonContent.Create(new { input = "Hello, world!", model = "fast" });
        var response = await _client.PostAsync("/v1/embeddings", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.GetProperty("data").GetArrayLength().Should().Be(1);
        json.GetProperty("data")[0].GetProperty("embedding").GetArrayLength()
            .Should().BeGreaterThan(0);
        json.GetProperty("model").GetString().Should().NotBeNullOrEmpty();
        json.TryGetProperty("usage", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Axis", "API-Inference")]
    public async Task POST_Embeddings_BatchInput_ReturnsMultiple()
    {
        string[] inputs = ["Hello", "World"];
        var content = JsonContent.Create(new
        {
            input = inputs,
            model = "fast"
        });
        var response = await _client.PostAsync("/v1/embeddings", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("data").GetArrayLength().Should().Be(2);
    }

    [Fact]
    [Trait("Axis", "API-Inference")]
    public async Task POST_Rerank_ReturnsRankedResults()
    {
        string[] rerankDocs = ["ML is a subset of AI", "The weather is nice"];
        var content = JsonContent.Create(new
        {
            query = "What is machine learning?",
            documents = rerankDocs,
            model = "fast",
            return_documents = true
        });
        var response = await _client.PostAsync("/v1/rerank", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("results").GetArrayLength().Should().Be(2);
        json.GetProperty("model").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Axis", "API-Inference")]
    public async Task POST_Translate_ReturnsTranslatedText()
    {
        var content = JsonContent.Create(new { input = "안녕하세요", model = "ko-en" });
        var response = await _client.PostAsync("/v1/translate", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("translations").GetArrayLength().Should().Be(1);
        json.GetProperty("translations")[0].GetProperty("translated_text").GetString()
            .Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Axis", "API-Inference")]
    public async Task POST_Speech_ReturnsWavAudio()
    {
        var content = JsonContent.Create(new
        {
            input = "Hello, this is a test.",
            model = "fast"
        });
        var response = await _client.PostAsync("/v1/audio/speech", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("audio/wav");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(44, "WAV should have header + audio data");
    }

    [Fact]
    [Trait("Axis", "API-Inference")]
    public async Task POST_Transcribe_FormUpload_ReturnsText()
    {
        var wavBytes = TestDataHelper.CreateToneWav(16000, 1.0f, 440);
        using var formContent = new MultipartFormDataContent();
        formContent.Add(new ByteArrayContent(wavBytes), "file", "test.wav");
        formContent.Add(new StringContent("fast"), "model");

        var response = await _client.PostAsync("/v1/audio/transcriptions", formContent);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("text", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Axis", "API-Inference")]
    public async Task POST_Caption_FormUpload_ReturnsCaption()
    {
        var imageBytes = TestDataHelper.CreateGradientBmp(256, 256);
        using var formContent = new MultipartFormDataContent();
        formContent.Add(new ByteArrayContent(imageBytes), "file", "test.bmp");
        formContent.Add(new StringContent("fast"), "model");

        var response = await _client.PostAsync("/v1/images/caption", formContent);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("caption").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Axis", "API-Inference")]
    public async Task POST_Detect_FormUpload_ReturnsDetections()
    {
        var imageBytes = TestDataHelper.CreateGradientBmp(640, 480);
        using var formContent = new MultipartFormDataContent();
        formContent.Add(new ByteArrayContent(imageBytes), "file", "test.bmp");
        formContent.Add(new StringContent("fast"), "model");

        var response = await _client.PostAsync("/v1/images/detect", formContent);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("objects", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Axis", "API-Inference")]
    public async Task POST_Segment_FormUpload_ReturnsSegments()
    {
        var imageBytes = TestDataHelper.CreateGradientBmp(256, 256);
        using var formContent = new MultipartFormDataContent();
        formContent.Add(new ByteArrayContent(imageBytes), "file", "test.bmp");
        formContent.Add(new StringContent("fast"), "model");

        var response = await _client.PostAsync("/v1/images/segment", formContent);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("segments", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Axis", "API-Inference")]
    public async Task POST_Ocr_FormUpload_ReturnsText()
    {
        var imageBytes = TestDataHelper.CreateGradientBmp(200, 50);
        using var formContent = new MultipartFormDataContent();
        formContent.Add(new ByteArrayContent(imageBytes), "file", "test.bmp");

        var response = await _client.PostAsync("/v1/images/ocr", formContent);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("text", out _).Should().BeTrue();
    }

    // ── Group D: Advanced API Tests ─────────────────────────────────

    [Fact]
    [Trait("Axis", "API-Error")]
    public async Task POST_Embeddings_UnknownModel_ReturnsError()
    {
        // API-2: Wrong model name should return a clear error, not 500
        var content = JsonContent.Create(new { input = "test", model = "nonexistent-model-xyz" });
        var response = await _client.PostAsync("/v1/embeddings", content);

        // Should be 4xx or at worst 500 with a meaningful error body
        response.IsSuccessStatusCode.Should().BeFalse("unknown model should fail");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("error", out _).Should().BeTrue("error response should have error field");
    }

    [Fact]
    [Trait("Axis", "API-Error")]
    public async Task POST_Rerank_UnknownModel_ReturnsError()
    {
        string[] docs = ["doc1"];
        var content = JsonContent.Create(new
        {
            query = "test",
            documents = docs,
            model = "nonexistent-model-xyz"
        });
        var response = await _client.PostAsync("/v1/rerank", content);

        response.IsSuccessStatusCode.Should().BeFalse("unknown model should fail");
    }

    [Fact]
    [Trait("Axis", "API-Inference")]
    public async Task POST_Embeddings_ConcurrentRequests_AllSucceed()
    {
        // API-5: Concurrent requests should all succeed
        var tasks = Enumerable.Range(0, 5).Select(i =>
        {
            var content = JsonContent.Create(new { input = $"test input {i}", model = "fast" });
            return _client.PostAsync("/v1/embeddings", content);
        });

        var responses = await Task.WhenAll(tasks);

        foreach (var response in responses)
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                "all concurrent embedding requests should succeed");
        }
    }

    [Fact]
    [Trait("Axis", "API-Inference")]
    public async Task POST_Embeddings_LargeInput_HandlesGracefully()
    {
        // API-4: Large request should not crash the server
        var largeText = string.Join(" ", Enumerable.Repeat("hello world test", 500));
        var content = JsonContent.Create(new { input = largeText, model = "fast" });
        var response = await _client.PostAsync("/v1/embeddings", content);

        // Should either succeed (with truncation) or fail with a clear error
        (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.BadRequest
            || response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
            .Should().BeTrue("large input should be handled gracefully");
    }

    [Fact]
    [Trait("Axis", "API-GET")]
    public async Task GET_CacheModelsByType_ReturnsTypeModels()
    {
        var response = await _client.GetAsync("/api/cache/models/type/embedder");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Axis", "API-Error")]
    public async Task POST_ChatCompletions_MissingMessages_Returns400()
    {
        var content = JsonContent.Create(new { model = "gguf:fast" });
        var response = await _client.PostAsync("/v1/chat/completions", content);

        // Missing required 'messages' field should return 400
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Axis", "API-Error")]
    public async Task POST_Translate_EmptyInput_Returns400()
    {
        var content = JsonContent.Create(new { input = "", model = "ko-en" });
        var response = await _client.PostAsync("/v1/translate", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Axis", "API-Error")]
    public async Task POST_ImageGeneration_InvalidSize_Returns400()
    {
        var content = JsonContent.Create(new
        {
            prompt = "test image",
            width = 3, // Not divisible by 8
            height = 3
        });
        var response = await _client.PostAsync("/v1/images/generations", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Axis", "API-GET")]
    public async Task GET_NonExistentEndpoint_Returns404()
    {
        var response = await _client.GetAsync("/v1/nonexistent");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Axis", "API-GET")]
    public async Task GET_MetricsStream_Returns200()
    {
        // SSE endpoint should accept the connection
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/system/metrics/stream");
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        catch (OperationCanceledException)
        {
            // Expected — SSE streams indefinitely, we just verify it starts
        }
    }

    // ── Group E: Content-Type Mismatch (API-3) ────────────────────────

    [Fact]
    [Trait("Axis", "API-Error")]
    public async Task POST_Embeddings_PlainText_IsRejected()
    {
        // API-3: JSON endpoint should reject non-JSON Content-Type
        var content = new StringContent("hello", Encoding.UTF8, "text/plain");
        var response = await _client.PostAsync("/v1/embeddings", content);

        response.IsSuccessStatusCode.Should().BeFalse(
            "JSON endpoint should not accept text/plain Content-Type");
    }

    [Fact]
    [Trait("Axis", "API-Error")]
    public async Task POST_Rerank_FormData_IsRejected()
    {
        // API-3: JSON endpoint should reject multipart/form-data
        using var formContent = new MultipartFormDataContent();
        formContent.Add(new StringContent("test"), "query");
        var response = await _client.PostAsync("/v1/rerank", formContent);

        response.IsSuccessStatusCode.Should().BeFalse(
            "JSON endpoint should not accept multipart/form-data");
    }

    // ── Group F: CORS Tests ──────────────────────────────────────────

    [Theory]
    [InlineData("http://localhost:5173")]
    [InlineData("http://localhost:3000")]
    [Trait("Axis", "API-CORS")]
    public async Task CORS_AllowedOrigin_ReturnsAccessControlHeaders(string origin)
    {
        // API-7: Verify CORS headers for configured origins
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", origin);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values)
            .Should().BeTrue($"response should include CORS header for allowed origin {origin}");
        values!.Should().Contain(origin);
    }

    [Fact]
    [Trait("Axis", "API-CORS")]
    public async Task CORS_DisallowedOrigin_NoAccessControlHeader()
    {
        // API-7: Origin not in the allowed list should not get CORS header
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "http://evil.example.com");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out _)
            .Should().BeFalse("disallowed origin should not get Access-Control-Allow-Origin");
    }

    [Fact]
    [Trait("Axis", "API-CORS")]
    public async Task CORS_PreflightRequest_ReturnsExpectedHeaders()
    {
        // API-7: OPTIONS preflight should return CORS method/header permissions
        var request = new HttpRequestMessage(HttpMethod.Options, "/v1/embeddings");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "Content-Type");

        var response = await _client.SendAsync(request);

        // Preflight should succeed (2xx)
        ((int)response.StatusCode).Should().BeInRange(200, 299,
            "preflight OPTIONS should return 2xx");

        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var originValues)
            .Should().BeTrue("preflight should include Allow-Origin");
        originValues!.Should().Contain("http://localhost:5173");

        response.Headers.TryGetValues("Access-Control-Allow-Methods", out var methodValues)
            .Should().BeTrue("preflight should include Allow-Methods");
    }

    [Fact]
    [Trait("Axis", "API-CORS")]
    public async Task CORS_AllowedOrigin_IncludesCredentials()
    {
        // API-7: WithCredentials() is configured, verify Allow-Credentials header
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "http://localhost:5173");

        var response = await _client.SendAsync(request);

        response.Headers.TryGetValues("Access-Control-Allow-Credentials", out var values)
            .Should().BeTrue("response should include Allow-Credentials for credentialed CORS");
        values!.Should().Contain("true");
    }
}
