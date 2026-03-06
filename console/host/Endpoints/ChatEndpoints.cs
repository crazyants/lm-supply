using LMSupply.Generator;
using LMSupply.Generator.Models;
using LMSupply.Console.Host.Infrastructure;
using LMSupply.Console.Host.Infrastructure.ToolCalling;
using LMSupply.Console.Host.Models.OpenAI;
using LMSupply.Console.Host.Services;

namespace LMSupply.Console.Host.Endpoints;

public static class ChatEndpoints
{
    public static void MapChatEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/v1/chat")
            .WithTags("Chat")
            ;

        // POST /v1/chat/completions - OpenAI compatible
        group.MapPost("/completions", async (ChatCompletionRequest request, HttpContext context, ModelManagerService manager) =>
        {
            var ct = context.RequestAborted;

            // Validate messages before model loading
            if (request.Messages == null || request.Messages.Count == 0)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new ErrorResponse
                {
                    Error = new ErrorDetail { Message = "'messages' field is required and must not be empty", Type = "invalid_request_error" }
                }, ct);
                return;
            }

            // Resolve max tokens
            var maxTokens = request.MaxCompletionTokens ?? request.MaxTokens ?? 2048;

            // Detect feature flags
            var toolsEnabled = ToolCallFormatter.ShouldEnableTools(request.ToolChoice, request.Tools);
            var hasImages = MultimodalHelper.HasImageContent(request.Messages);

            // Handle streaming separately to avoid IResult after response started
            if (request.Stream)
            {
                try
                {
                    await using var scope = await manager.GetGeneratorAsync(request.Model, ct);
                    var messages = await BuildMessagesAsync(request, manager, toolsEnabled, hasImages, ct);
                    var options = new GenerationOptions
                    {
                        MaxTokens = maxTokens,
                        Temperature = request.Temperature ?? 0.7f,
                        TopP = request.TopP ?? 0.9f,
                        FrequencyPenalty = request.FrequencyPenalty ?? 0f,
                        PresencePenalty = request.PresencePenalty ?? 0f,
                        Seed = request.Seed ?? -1,
                        StopSequences = request.Stop?.ToList(),
                        JsonSchema = request.ResponseFormat?.Type == "json_schema" &&
                                     request.ResponseFormat.JsonSchema?.Schema.HasValue == true
                            ? System.Text.Json.JsonSerializer.Serialize(request.ResponseFormat.JsonSchema.Schema.Value)
                            : null
                    };

                    var tokens = scope.Model.GenerateChatAsync(messages, options, ct);
                    await SseHelper.StreamChatCompletionAsync(context, scope.Model.ModelId, tokens, ct);
                }
                catch (Exception ex)
                {
                    // If response hasn't started, we can still write error
                    if (!context.Response.HasStarted)
                    {
                        context.Response.StatusCode = 500;
                        await context.Response.WriteAsJsonAsync(new ErrorResponse
                        {
                            Error = new ErrorDetail { Message = FormatError(ex), Type = "internal_error" }
                        }, ct);
                    }
                }
                return;
            }

            // Non-streaming response
            try
            {
                await using var scope = await manager.GetGeneratorAsync(request.Model, ct);
                var messages = await BuildMessagesAsync(request, manager, toolsEnabled, hasImages, ct);
                var options = new GenerationOptions
                {
                    MaxTokens = maxTokens,
                    Temperature = request.Temperature ?? 0.7f,
                    TopP = request.TopP ?? 0.9f,
                    FrequencyPenalty = request.FrequencyPenalty ?? 0f,
                    PresencePenalty = request.PresencePenalty ?? 0f,
                    Seed = request.Seed ?? -1,
                    StopSequences = request.Stop?.ToList(),
                    JsonSchema = request.ResponseFormat?.Type == "json_schema" &&
                                 request.ResponseFormat.JsonSchema?.Schema.HasValue == true
                        ? System.Text.Json.JsonSerializer.Serialize(request.ResponseFormat.JsonSchema.Schema.Value)
                        : null
                };

                var id = ApiHelper.GenerateId("chatcmpl");
                var n = Math.Max(1, request.N ?? 1);
                var choices = new List<ChatCompletionChoice>();
                var aggregatedUsage = new Usage();

                for (var i = 0; i < n; i++)
                {
                    // For strict JSON schema, retry up to 3 times on invalid JSON
                    var maxAttempts = request.ResponseFormat?.Type == "json_schema" && request.ResponseFormat.JsonSchema?.Strict == true ? 3 : 1;
                    GenerationResult result = default;
                    for (var attempt = 0; attempt < maxAttempts; attempt++)
                    {
                        result = await scope.Model.GenerateChatWithUsageAsync(messages, options, ct);
                        if (ResponseFormatHelper.ValidateJson(result.Content, request.ResponseFormat))
                            break;
                    }

                    // Parse tool calls if tools are enabled
                    IReadOnlyList<ToolCall>? toolCalls = null;
                    string? content = result.Content;
                    string finishReason = "stop";

                    if (toolsEnabled)
                    {
                        toolCalls = ToolCallParser.TryParse(result.Content);
                        if (toolCalls != null)
                        {
                            content = null; // per OpenAI spec: content is null when tool_calls present
                            finishReason = "tool_calls";
                        }
                    }

                    choices.Add(new ChatCompletionChoice
                    {
                        Index = i,
                        Message = new ChatCompletionResponseMessage
                        {
                            Role = "assistant",
                            Content = content,
                            ToolCalls = toolCalls
                        },
                        FinishReason = finishReason
                    });

                    // Aggregate usage (prompt tokens only counted once for first choice)
                    if (i == 0)
                    {
                        aggregatedUsage = new Usage
                        {
                            PromptTokens = result.Usage.PromptTokens,
                            CompletionTokens = result.Usage.CompletionTokens,
                            TotalTokens = result.Usage.TotalTokens
                        };
                    }
                    else
                    {
                        aggregatedUsage = new Usage
                        {
                            PromptTokens = aggregatedUsage.PromptTokens,
                            CompletionTokens = aggregatedUsage.CompletionTokens + result.Usage.CompletionTokens,
                            TotalTokens = aggregatedUsage.PromptTokens + aggregatedUsage.CompletionTokens + result.Usage.CompletionTokens
                        };
                    }
                }

                await context.Response.WriteAsJsonAsync(new ChatCompletionResponse
                {
                    Id = id,
                    Model = scope.Model.ModelId,
                    SystemFingerprint = $"fp_{scope.Model.ModelId.GetHashCode():x8}",
                    Choices = choices,
                    Usage = aggregatedUsage
                }, ct);
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new ErrorResponse
                {
                    Error = new ErrorDetail { Message = FormatError(ex), Type = "internal_error" }
                }, ct);
            }
        })
        .WithName("CreateChatCompletion")
        .WithSummary("Create a chat completion (OpenAI compatible)")
        .WithDescription("Creates a model response for the given chat conversation. Compatible with OpenAI's chat completions API.")
        .Produces<ChatCompletionResponse>()
        .Produces<ErrorResponse>(400)
        .Produces<ErrorResponse>(500);
    }

    /// <summary>
    /// Builds the ChatMessage list from the request, injecting tool schemas and JSON instructions into the system prompt.
    /// </summary>
    private static async Task<IEnumerable<ChatMessage>> BuildMessagesAsync(
        ChatCompletionRequest request,
        ModelManagerService manager,
        bool toolsEnabled,
        bool hasImages,
        CancellationToken ct)
    {
        // Build JSON instruction if response_format is set
        var jsonInstruction = ResponseFormatHelper.BuildJsonInstruction(request.ResponseFormat);

        // Find the existing system message text (first system message)
        string? existingSystem = null;
        foreach (var m in request.Messages)
        {
            if (string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
            {
                existingSystem = m.GetTextContent();
                break;
            }
        }

        // Build the effective system prompt: tool schema + JSON instruction injected
        string? effectiveSystem = existingSystem;
        if (toolsEnabled && request.Tools != null)
            effectiveSystem = ToolCallInjector.BuildSystemPrompt(effectiveSystem, request.Tools);
        if (jsonInstruction != null)
            effectiveSystem = string.IsNullOrWhiteSpace(effectiveSystem) ? jsonInstruction : effectiveSystem + "\n\n" + jsonInstruction;

        var result = new List<ChatMessage>();
        var systemHandled = false;

        foreach (var m in request.Messages)
        {
            var role = m.Role.ToLowerInvariant();

            if (role == "system")
            {
                if (!systemHandled)
                {
                    // Use the effective system prompt (with injections)
                    if (!string.IsNullOrWhiteSpace(effectiveSystem))
                        result.Add(new ChatMessage(ChatRole.System, effectiveSystem));
                    systemHandled = true;
                }
                // Skip additional system messages (collapsed into first)
                continue;
            }

            if (role == "tool")
            {
                // Tool result messages — represent as user messages with tool result context
                var toolContent = m.GetTextContent();
                var toolId = m.ToolCallId ?? "unknown";
                result.Add(new ChatMessage(ChatRole.User, $"[Tool result for {toolId}]: {toolContent}"));
                continue;
            }

            // Extract text content (with optional image captioning for multimodal)
            string text;
            if (hasImages && m.Content.HasValue && m.Content.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                text = await MultimodalHelper.ExtractTextAsync(m.Content.Value, manager, ct);
            else
                text = m.GetTextContent();

            var chatRole = role switch
            {
                "user" => ChatRole.User,
                "assistant" => ChatRole.Assistant,
                _ => ChatRole.User // fallback for unknown roles
            };

            result.Add(new ChatMessage(chatRole, text));
        }

        // If no system message was found but we have injections to add
        if (!systemHandled && !string.IsNullOrWhiteSpace(effectiveSystem))
            result.Insert(0, new ChatMessage(ChatRole.System, effectiveSystem));

        return result;
    }

    private static string FormatError(Exception ex)
    {
        var msg = ex.Message;

        if (msg.Contains("Could not allocate") && msg.Contains("key-value cache"))
            return "GPU memory insufficient for this model's context length. Try a smaller model or reduce max_tokens.";

        if (msg.Contains("out of memory", StringComparison.OrdinalIgnoreCase))
            return $"Out of memory during inference. Try a smaller model. ({msg})";

        return msg;
    }
}
