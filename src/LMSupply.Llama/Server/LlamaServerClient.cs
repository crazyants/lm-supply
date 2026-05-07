using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LMSupply.Exceptions;

namespace LMSupply.Llama.Server;

/// <summary>
/// OpenAI-compatible HTTP client for llama-server.
/// </summary>
public sealed class LlamaServerClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly bool _ownsHttpClient;
    private readonly int _maxContextLength;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Creates a new client for the specified server URL.
    /// </summary>
    /// <param name="baseUrl">llama-server base URL.</param>
    /// <param name="httpClient">Optional shared HttpClient. If null, a new one is created and owned.</param>
    /// <param name="maxContextLength">Model context window size — carried in ContextLengthExceededException on overflow.</param>
    public LlamaServerClient(string baseUrl, HttpClient? httpClient = null, int maxContextLength = 0)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _maxContextLength = maxContextLength;

        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient();
            _ownsHttpClient = true;
        }
    }

    /// <summary>
    /// Generates a streaming chat completion.
    /// </summary>
    public async IAsyncEnumerable<string> GenerateChatAsync(
        IEnumerable<ChatCompletionMessage> messages,
        ChatCompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= new ChatCompletionOptions();

        var request = new ChatCompletionRequest
        {
            Messages = messages.ToList(),
            MaxTokens = options.MaxTokens,
            Temperature = options.Temperature,
            TopP = options.TopP,
            TopK = options.TopK > 0 ? options.TopK : null,
            MinP = options.MinP > 0 ? options.MinP : null,
            RepeatPenalty = options.RepeatPenalty != 1.0f ? options.RepeatPenalty : null,
            FrequencyPenalty = options.FrequencyPenalty != 0 ? options.FrequencyPenalty : null,
            PresencePenalty = options.PresencePenalty != 0 ? options.PresencePenalty : null,
            Seed = options.Seed != -1 ? options.Seed : null,
            Stream = true,
            Stop = options.StopSequences?.ToList(),
            Grammar = options.Grammar,
            JsonSchema = options.JsonSchema,
            Tools = options.Tools?.ToList()
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/chat/completions")
        {
            Content = content
        };

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        await EnsureSuccessOrThrowContextExceptionAsync(response, _maxContextLength, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrEmpty(line))
                continue;

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var data = line[6..];

            if (data == "[DONE]")
                break;

            ChatCompletionChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(data, JsonOptions);
            }
            catch (Exception ex)
            {
                Trace.TraceInformation($"[LlamaServerClient] Chat SSE chunk deserialization failed: {ex.Message}");
                continue;
            }

            // Only yield content tokens — reasoning_content (thinking) is intentionally skipped.
            // b8994+: Gemma 4 separates thinking into reasoning_content; content holds the answer.
            var delta = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
            if (!string.IsNullOrEmpty(delta))
            {
                yield return delta;
            }
        }
    }

    /// <summary>
    /// Generates a streaming chat completion with structured data.
    /// Returns text deltas, tool call deltas, and finish reason.
    /// </summary>
    public async IAsyncEnumerable<ChatStreamData> GenerateChatStreamAsync(
        IEnumerable<ChatCompletionMessage> messages,
        ChatCompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= new ChatCompletionOptions();

        var request = new ChatCompletionRequest
        {
            Messages = messages.ToList(),
            MaxTokens = options.MaxTokens,
            Temperature = options.Temperature,
            TopP = options.TopP,
            TopK = options.TopK > 0 ? options.TopK : null,
            MinP = options.MinP > 0 ? options.MinP : null,
            RepeatPenalty = options.RepeatPenalty != 1.0f ? options.RepeatPenalty : null,
            FrequencyPenalty = options.FrequencyPenalty != 0 ? options.FrequencyPenalty : null,
            PresencePenalty = options.PresencePenalty != 0 ? options.PresencePenalty : null,
            Seed = options.Seed != -1 ? options.Seed : null,
            Stream = true,
            Stop = options.StopSequences?.ToList(),
            Grammar = options.Grammar,
            JsonSchema = options.JsonSchema,
            Tools = options.Tools?.ToList()
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/chat/completions")
        {
            Content = content
        };

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        await EnsureSuccessOrThrowContextExceptionAsync(response, _maxContextLength, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrEmpty(line))
                continue;

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var data = line[6..];

            if (data == "[DONE]")
                break;

            ChatCompletionChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(data, JsonOptions);
            }
            catch (Exception ex)
            {
                Trace.TraceInformation($"[LlamaServerClient] Chat stream chunk deserialization failed: {ex.Message}");
                continue;
            }

            var choice = chunk?.Choices?.FirstOrDefault();
            if (choice is null)
                continue;

            if (choice.Delta?.Content is not null ||
                choice.Delta?.ReasoningContent is not null ||
                choice.Delta?.ToolCalls is { Count: > 0 } ||
                choice.FinishReason is not null)
            {
                yield return new ChatStreamData
                {
                    TextDelta = choice.Delta?.Content,
                    ReasoningDelta = choice.Delta?.ReasoningContent,
                    ToolCallDeltas = choice.Delta?.ToolCalls,
                    FinishReason = choice.FinishReason
                };
            }
        }
    }

    /// <summary>
    /// Generates a non-streaming chat completion.
    /// </summary>
    public async Task<string> GenerateChatCompleteAsync(
        IEnumerable<ChatCompletionMessage> messages,
        ChatCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        await foreach (var token in GenerateChatAsync(messages, options, cancellationToken))
        {
            sb.Append(token);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates a non-streaming chat completion with full response including tool calls.
    /// </summary>
    public async Task<ChatCompletionFullResponse> GenerateChatWithToolsAsync(
        IEnumerable<ChatCompletionMessage> messages,
        ChatCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ChatCompletionOptions();

        var request = new ChatCompletionRequest
        {
            Messages = messages.ToList(),
            MaxTokens = options.MaxTokens,
            Temperature = options.Temperature,
            TopP = options.TopP,
            TopK = options.TopK > 0 ? options.TopK : null,
            MinP = options.MinP > 0 ? options.MinP : null,
            RepeatPenalty = options.RepeatPenalty != 1.0f ? options.RepeatPenalty : null,
            FrequencyPenalty = options.FrequencyPenalty != 0 ? options.FrequencyPenalty : null,
            PresencePenalty = options.PresencePenalty != 0 ? options.PresencePenalty : null,
            Seed = options.Seed != -1 ? options.Seed : null,
            Stream = false,
            Stop = options.StopSequences?.ToList(),
            Grammar = options.Grammar,
            JsonSchema = options.JsonSchema,
            Tools = options.Tools?.ToList()
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(
            $"{_baseUrl}/v1/chat/completions",
            content,
            cancellationToken);

        await EnsureSuccessOrThrowContextExceptionAsync(response, _maxContextLength, cancellationToken);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<ChatCompletionFullResponse>(responseJson, JsonOptions);

        return result ?? new ChatCompletionFullResponse();
    }

    /// <summary>
    /// Generates a streaming text completion.
    /// </summary>
    public async IAsyncEnumerable<string> GenerateAsync(
        string prompt,
        CompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= new CompletionOptions();

        var request = new CompletionRequest
        {
            Prompt = prompt,
            NPredict = options.MaxTokens,
            Temperature = options.Temperature,
            TopP = options.TopP,
            TopK = options.TopK > 0 ? options.TopK : null,
            MinP = options.MinP > 0 ? options.MinP : null,
            RepeatPenalty = options.RepeatPenalty != 1.0f ? options.RepeatPenalty : null,
            FrequencyPenalty = options.FrequencyPenalty != 0 ? options.FrequencyPenalty : null,
            PresencePenalty = options.PresencePenalty != 0 ? options.PresencePenalty : null,
            Seed = options.Seed != -1 ? options.Seed : null,
            Stream = true,
            Stop = options.StopSequences?.ToList(),
            Grammar = options.Grammar,
            JsonSchema = options.JsonSchema
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/completion")
        {
            Content = content
        };

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        await EnsureSuccessOrThrowContextExceptionAsync(response, _maxContextLength, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrEmpty(line))
                continue;

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var data = line[6..];

            CompletionChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<CompletionChunk>(data, JsonOptions);
            }
            catch (Exception ex)
            {
                Trace.TraceInformation($"[LlamaServerClient] Completion SSE chunk deserialization failed: {ex.Message}");
                continue;
            }

            if (chunk?.Stop == true)
                break;

            if (!string.IsNullOrEmpty(chunk?.Content))
            {
                yield return chunk.Content;
            }
        }
    }

    /// <summary>
    /// Checks if the server is healthy.
    /// </summary>
    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Trace.TraceInformation($"[LlamaServerClient] Health check failed: {ex.Message}");
            return false;
        }
    }

    #region Tokenize API

    /// <summary>
    /// Counts the number of tokens in the given text using the server's tokenizer.
    /// </summary>
    public async Task<int> CountTokensAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        var request = new { content = text };
        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(
            $"{_baseUrl}/tokenize",
            content,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<TokenizeResponse>(responseJson, JsonOptions);

        return result?.Tokens?.Count ?? 0;
    }

    #endregion

    #region Embedding API

    /// <summary>
    /// Generates embeddings for a single text input.
    /// Requires server started with --embedding flag.
    /// </summary>
    public async Task<float[]> GenerateEmbeddingAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        var result = await GenerateEmbeddingsBatchAsync([input], cancellationToken);
        return result[0];
    }

    /// <summary>
    /// Generates embeddings for multiple text inputs in batch.
    /// Requires server started with --embedding flag.
    /// </summary>
    public async Task<float[][]> GenerateEmbeddingsBatchAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        var request = new EmbeddingRequest
        {
            Input = inputs.Count == 1 ? inputs[0] : inputs
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(
            $"{_baseUrl}/v1/embeddings",
            content,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var embeddingResponse = JsonSerializer.Deserialize<EmbeddingResponse>(responseJson, JsonOptions);

        if (embeddingResponse?.Data == null || embeddingResponse.Data.Count == 0)
        {
            throw new InvalidOperationException("No embeddings returned from server");
        }

        // Sort by index to ensure correct order
        return embeddingResponse.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding)
            .ToArray();
    }

    #endregion

    #region Reranking API

    /// <summary>
    /// Reranks documents by relevance to a query.
    /// Requires server started with --embedding and --pooling rank flags.
    /// </summary>
    public async Task<IReadOnlyList<RerankResult>> RerankAsync(
        string query,
        IReadOnlyList<string> documents,
        int topN = 10,
        CancellationToken cancellationToken = default)
    {
        var request = new RerankRequest
        {
            Query = query,
            Documents = documents.ToList(),
            TopN = Math.Min(topN, documents.Count)
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(
            $"{_baseUrl}/v1/rerank",
            content,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var rerankResponse = JsonSerializer.Deserialize<RerankResponse>(responseJson, JsonOptions);

        return rerankResponse?.Results ?? [];
    }

    #endregion

    /// <summary>
    /// Ensures the HTTP response is successful, converting context overflow errors
    /// to <see cref="ContextLengthExceededException"/>.
    /// </summary>
    private static async Task EnsureSuccessOrThrowContextExceptionAsync(
        HttpResponseMessage response,
        int maxContextLength,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        // Read error body for context overflow detection
        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest &&
            IsContextOverflowError(errorBody))
        {
            throw new ContextLengthExceededException(null, maxContextLength);
        }

        // Fallback to default behavior for non-context errors
        response.EnsureSuccessStatusCode();
    }

    private static bool IsContextOverflowError(string errorBody)
    {
        // llama-server returns errors like:
        // "the prompt is too long" / "input exceeds context" / "context length exceeded"
        return errorBody.Contains("too long", StringComparison.OrdinalIgnoreCase) ||
               errorBody.Contains("context", StringComparison.OrdinalIgnoreCase) &&
               (errorBody.Contains("exceed", StringComparison.OrdinalIgnoreCase) ||
                errorBody.Contains("overflow", StringComparison.OrdinalIgnoreCase) ||
                errorBody.Contains("too large", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}

#region Request/Response Models

/// <summary>
/// Chat completion message.
/// </summary>
public sealed class ChatCompletionMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("tool_calls")]
    public List<ToolCallMessage>? ToolCalls { get; init; }

    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; init; }

    public static ChatCompletionMessage System(string content) => new() { Role = "system", Content = content };
    public static ChatCompletionMessage User(string content) => new() { Role = "user", Content = content };
    public static ChatCompletionMessage Assistant(string content) => new() { Role = "assistant", Content = content };
    public static ChatCompletionMessage Tool(string toolCallId, string content) => new() { Role = "tool", Content = content, ToolCallId = toolCallId };
}

/// <summary>
/// Options for chat completion.
/// </summary>
public sealed class ChatCompletionOptions
{
    public int MaxTokens { get; init; } = 256;
    public float Temperature { get; init; } = 0.7f;
    public float TopP { get; init; } = 0.9f;
    public int TopK { get; init; } = 50;
    public float MinP { get; init; } = 0.05f;
    public float RepeatPenalty { get; init; } = 1.1f;
    public float FrequencyPenalty { get; init; }
    public float PresencePenalty { get; init; }
    public int Seed { get; init; } = -1;
    public IReadOnlyList<string>? StopSequences { get; init; }

    /// <summary>
    /// Grammar constraint in GBNF format (Phase 3).
    /// </summary>
    public string? Grammar { get; init; }

    /// <summary>
    /// JSON schema for structured output (Phase 3).
    /// When set, output will be constrained to match this schema.
    /// </summary>
    public string? JsonSchema { get; init; }

    /// <summary>
    /// Tool definitions available for the model.
    /// When provided, the model may respond with tool_calls.
    /// </summary>
    public IReadOnlyList<ToolDefinition>? Tools { get; init; }
}

/// <summary>
/// Options for text completion.
/// </summary>
public sealed class CompletionOptions
{
    public int MaxTokens { get; init; } = 256;
    public float Temperature { get; init; } = 0.7f;
    public float TopP { get; init; } = 0.9f;
    public int TopK { get; init; } = 50;
    public float MinP { get; init; } = 0.05f;
    public float RepeatPenalty { get; init; } = 1.1f;
    public float FrequencyPenalty { get; init; }
    public float PresencePenalty { get; init; }
    public int Seed { get; init; } = -1;
    public IReadOnlyList<string>? StopSequences { get; init; }

    /// <summary>
    /// Grammar constraint in GBNF format (Phase 3).
    /// </summary>
    public string? Grammar { get; init; }

    /// <summary>
    /// JSON schema for structured output (Phase 3).
    /// When set, output will be constrained to match this schema.
    /// </summary>
    public string? JsonSchema { get; init; }
}

internal sealed class ChatCompletionRequest
{
    public List<ChatCompletionMessage>? Messages { get; set; }
    public int? MaxTokens { get; set; }
    public float? Temperature { get; set; }
    public float? TopP { get; set; }
    public int? TopK { get; set; }
    public float? MinP { get; set; }
    public float? RepeatPenalty { get; set; }
    public float? FrequencyPenalty { get; set; }
    public float? PresencePenalty { get; set; }
    public int? Seed { get; set; }
    public bool Stream { get; set; }
    public List<string>? Stop { get; set; }

    /// <summary>
    /// Grammar constraint in GBNF format (Phase 3).
    /// </summary>
    public string? Grammar { get; set; }

    /// <summary>
    /// JSON schema for structured output (Phase 3).
    /// </summary>
    public string? JsonSchema { get; set; }

    /// <summary>
    /// Tool definitions (OpenAI-compatible).
    /// </summary>
    public List<ToolDefinition>? Tools { get; set; }

    /// <summary>
    /// Re-use KV cache from previous request if possible.
    /// Reduces first token latency for prompts with common prefixes.
    /// </summary>
    public bool CachePrompt { get; set; } = true;
}

internal sealed class CompletionRequest
{
    public string? Prompt { get; set; }
    public int? NPredict { get; set; }
    public float? Temperature { get; set; }
    public float? TopP { get; set; }
    public int? TopK { get; set; }
    public float? MinP { get; set; }
    public float? RepeatPenalty { get; set; }
    public float? FrequencyPenalty { get; set; }
    public float? PresencePenalty { get; set; }
    public int? Seed { get; set; }
    public bool Stream { get; set; }
    public List<string>? Stop { get; set; }

    /// <summary>
    /// Grammar constraint in GBNF format (Phase 3).
    /// </summary>
    public string? Grammar { get; set; }

    /// <summary>
    /// JSON schema for structured output (Phase 3).
    /// </summary>
    public string? JsonSchema { get; set; }

    /// <summary>
    /// Re-use KV cache from previous request if possible.
    /// Reduces first token latency for prompts with common prefixes.
    /// </summary>
    public bool CachePrompt { get; set; } = true;
}

internal sealed class ChatCompletionChunk
{
    public List<ChatCompletionChoice>? Choices { get; set; }
}

internal sealed class ChatCompletionChoice
{
    public ChatCompletionDelta? Delta { get; set; }
    public string? FinishReason { get; set; }
}

internal sealed class ChatCompletionDelta
{
    public string? Content { get; set; }

    [JsonPropertyName("reasoning_content")]
    public string? ReasoningContent { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<ToolCallDelta>? ToolCalls { get; set; }
}

/// <summary>
/// Tool call delta in streaming response.
/// </summary>
public sealed class ToolCallDelta
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("function")]
    public FunctionCallDelta? Function { get; set; }
}

/// <summary>
/// Function call delta in streaming response.
/// </summary>
public sealed class FunctionCallDelta
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }
}

internal sealed class CompletionChunk
{
    public string? Content { get; set; }
    public bool Stop { get; set; }
}

/// <summary>
/// Tool call in a message (OpenAI format).
/// </summary>
public sealed class ToolCallMessage
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public FunctionCallMessage? Function { get; set; }
}

/// <summary>
/// Function call details.
/// </summary>
public sealed class FunctionCallMessage
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }
}

/// <summary>
/// Tool definition for the request (OpenAI format).
/// </summary>
public sealed class ToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public FunctionDefinition? Function { get; set; }
}

/// <summary>
/// Function definition for a tool.
/// </summary>
public sealed class FunctionDefinition
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("parameters")]
    public JsonElement? Parameters { get; set; }
}

/// <summary>
/// Structured streaming data from a chat completion.
/// Exposed to LMSupply.Generator via InternalsVisibleTo for conversion to ChatStreamChunk.
/// </summary>
public sealed class ChatStreamData
{
    /// <summary>
    /// Text content delta.
    /// </summary>
    public string? TextDelta { get; init; }

    /// <summary>
    /// Reasoning/thinking content delta (b8994+: emitted in separate field before content).
    /// </summary>
    public string? ReasoningDelta { get; init; }

    /// <summary>
    /// Tool call deltas (raw SSE format).
    /// </summary>
    public List<ToolCallDelta>? ToolCallDeltas { get; init; }

    /// <summary>
    /// Finish reason (present only on the final chunk).
    /// </summary>
    public string? FinishReason { get; init; }
}

/// <summary>
/// Full (non-streaming) chat completion response.
/// </summary>
public sealed class ChatCompletionFullResponse
{
    public List<ChatCompletionFullChoice>? Choices { get; set; }
}

/// <summary>
/// Choice in a full chat completion response.
/// </summary>
public sealed class ChatCompletionFullChoice
{
    public ChatCompletionResponseMessage? Message { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

/// <summary>
/// Message in a full chat completion response.
/// </summary>
public sealed class ChatCompletionResponseMessage
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("reasoning_content")]
    public string? ReasoningContent { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<ToolCallMessage>? ToolCalls { get; set; }
}

#endregion

#region Embedding Request/Response Models

internal sealed class EmbeddingRequest
{
    /// <summary>
    /// Input text(s) to embed. Can be a single string or array of strings.
    /// </summary>
    public required object Input { get; set; }

    /// <summary>
    /// Model identifier (optional, defaults to loaded model).
    /// </summary>
    public string Model { get; set; } = "default";

    /// <summary>
    /// Encoding format: "float" (default) or "base64".
    /// </summary>
    public string EncodingFormat { get; set; } = "float";
}

internal sealed class EmbeddingResponse
{
    public string Object { get; set; } = "list";
    public List<EmbeddingData> Data { get; set; } = [];
    public string Model { get; set; } = "";
    public EmbeddingUsage Usage { get; set; } = new();
}

internal sealed class EmbeddingData
{
    public string Object { get; set; } = "embedding";
    public float[] Embedding { get; set; } = [];
    public int Index { get; set; }
}

internal sealed class EmbeddingUsage
{
    public int PromptTokens { get; set; }
    public int TotalTokens { get; set; }
}

#endregion

#region Reranking Request/Response Models

internal sealed class RerankRequest
{
    /// <summary>
    /// The search query.
    /// </summary>
    public required string Query { get; set; }

    /// <summary>
    /// Documents to rerank.
    /// </summary>
    public required List<string> Documents { get; set; }

    /// <summary>
    /// Maximum number of results to return.
    /// </summary>
    public int TopN { get; set; } = 10;
}

internal sealed class RerankResponse
{
    public List<RerankResult> Results { get; set; } = [];
}

/// <summary>
/// Result from reranking operation.
/// </summary>
public sealed class RerankResult
{
    /// <summary>
    /// Original index of the document in the input list.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Relevance score (higher is more relevant).
    /// </summary>
    public float RelevanceScore { get; set; }
}

#endregion

#region Tokenize Response Models

internal sealed class TokenizeResponse
{
    public List<int>? Tokens { get; set; }
}

#endregion
