namespace LMSupply.Generator.Models;

/// <summary>
/// Configuration options for text generation (inference parameters).
/// </summary>
public sealed class GenerationOptions
{
    /// <summary>
    /// Gets or sets the maximum number of tokens to generate.
    /// Enforced both server-side (llama-server) and client-side as a safety net.
    /// Defaults to 512.
    /// </summary>
    public int MaxTokens { get; set; } = 512;

    /// <summary>
    /// Gets or sets the temperature for sampling.
    /// Higher values produce more random outputs. Range: 0.0 to 2.0.
    /// Defaults to 0.7.
    /// </summary>
    public float Temperature { get; set; } = 0.7f;

    /// <summary>
    /// Gets or sets the top-p (nucleus) sampling parameter.
    /// Considers tokens with cumulative probability mass up to this value. Range: 0.0 to 1.0.
    /// Defaults to 0.9.
    /// </summary>
    public float TopP { get; set; } = 0.9f;

    /// <summary>
    /// Gets or sets the top-k sampling parameter.
    /// Considers only the top k tokens. Set to 0 to disable.
    /// Defaults to 50.
    /// </summary>
    public int TopK { get; set; } = 50;

    /// <summary>
    /// Gets or sets the repetition penalty applied to tokens that have already appeared.
    /// Values greater than 1.0 discourage repetition; 1.0 disables the penalty entirely.
    /// Typical range: 1.0 to 1.3. Values above 1.5 may degrade output quality.
    /// Defaults to 1.1.
    /// </summary>
    /// <remarks>
    /// This is the primary defense against repetition loops. If the model produces
    /// repetitive text, increase this value (e.g., 1.2–1.3). For creative writing
    /// or deterministic tasks, adjust accordingly via <see cref="Creative"/> or <see cref="Precise"/> presets.
    /// Sent to llama-server as <c>repeat_penalty</c>.
    /// </remarks>
    public float RepetitionPenalty { get; set; } = 1.1f;

    /// <summary>
    /// Gets or sets the min-p (minimum probability) sampling parameter.
    /// Filters out tokens below this probability threshold relative to the top token.
    /// Range: 0.0 to 1.0. Set to 0 to disable.
    /// Defaults to 0.05 (5% of top token probability).
    /// </summary>
    /// <remarks>
    /// Min-p is a dynamic cutoff that adapts to confidence levels:
    /// - When the model is confident (high top token probability), more tokens are filtered
    /// - When uncertain (low top token probability), more tokens are considered
    /// Works well with TopK for improved output quality.
    /// </remarks>
    public float MinP { get; set; } = 0.05f;

    /// <summary>
    /// Gets or sets the random seed for reproducible generation.
    /// Set to a specific value for deterministic outputs across identical inputs.
    /// Defaults to -1 (random seed each time).
    /// </summary>
    public int Seed { get; set; } = -1;

    /// <summary>
    /// Gets or sets the frequency penalty (OpenAI-style).
    /// Penalizes tokens proportionally to how often they appear in the text so far.
    /// Complements <see cref="RepetitionPenalty"/> for fine-grained repetition control.
    /// Range: 0.0 to 2.0. Defaults to 0.0 (disabled).
    /// </summary>
    public float FrequencyPenalty { get; set; }

    /// <summary>
    /// Gets or sets the presence penalty (OpenAI-style).
    /// Applies a flat penalty to any token that has appeared at least once.
    /// Encourages the model to introduce new topics rather than revisiting earlier ones.
    /// Range: 0.0 to 2.0. Defaults to 0.0 (disabled).
    /// </summary>
    public float PresencePenalty { get; set; }

    /// <summary>
    /// Gets or sets the stop sequences that will terminate generation.
    /// </summary>
    public IReadOnlyList<string>? StopSequences { get; set; }

    /// <summary>
    /// Gets or sets whether to include the input prompt in the output.
    /// Defaults to false.
    /// </summary>
    public bool IncludePromptInOutput { get; set; }

    /// <summary>
    /// Gets or sets whether to enable random sampling.
    /// When false, uses greedy decoding (always picks highest probability token).
    /// Defaults to true.
    /// </summary>
    public bool DoSample { get; set; } = true;

    /// <summary>
    /// Gets or sets the number of beams for beam search.
    /// Set to 1 to disable beam search.
    /// Note: Beam search disables KV cache sharing for better quality.
    /// Defaults to 1.
    /// </summary>
    public int NumBeams { get; set; } = 1;

    /// <summary>
    /// Gets or sets whether to share buffer between past and present KV cache.
    /// Improves memory efficiency but incompatible with beam search (num_beams > 1).
    /// Defaults to true.
    /// </summary>
    public bool PastPresentShareBuffer { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of new tokens to generate (excluding prompt).
    /// If null, limited only by MaxTokens (prompt + generated).
    /// </summary>
    public int? MaxNewTokens { get; set; }

    /// <summary>
    /// Gets or sets whether to filter reasoning tokens from output.
    /// When true, content within &lt;think&gt;...&lt;/think&gt; tags is removed from streamed output.
    /// Useful for DeepSeek R1 and other reasoning models to show only final answers.
    /// Defaults to false (reasoning tokens are included in output).
    /// </summary>
    public bool FilterReasoningTokens { get; set; }

    /// <summary>
    /// Gets or sets whether to extract reasoning tokens to a separate property.
    /// When true, reasoning content is captured but not included in main output.
    /// Use with GenerateChatWithReasoningAsync to access reasoning content.
    /// Defaults to false.
    /// </summary>
    public bool ExtractReasoningTokens { get; set; }

    /// <summary>
    /// Controls the model's reasoning ("thinking") behavior on the chat path.
    /// <see cref="ThinkingMode.Auto"/> (default) keeps the model's built-in behavior — Qwen3 thinks,
    /// Gemma 4 does not. <see cref="ThinkingMode.On"/> activates thinking (injects the formatter
    /// thinking token for default-off models like Gemma 4 and requests <c>enable_thinking=true</c>);
    /// <see cref="ThinkingMode.Off"/> suppresses it (requests <c>enable_thinking=false</c>) so a
    /// thinking-default-on model (Qwen3) answers directly instead of spending tokens on a reasoning
    /// block. Honored only on the chat path (the raw completion path has no chat template to drive).
    /// </summary>
    public ThinkingMode Thinking { get; set; } = ThinkingMode.Auto;

    /// <summary>
    /// Tool definitions available for the model to call.
    /// When set, the model may respond with tool calls instead of text.
    /// </summary>
    public IReadOnlyList<ChatToolDefinition>? Tools { get; set; }

    #region Phase 3: Grammar Constraints

    /// <summary>
    /// Gets or sets a grammar constraint in GBNF (GGML BNF) format.
    /// Constrains generation to match the specified grammar rules.
    /// Use for enforcing specific output formats like JSON, markdown, etc.
    /// </summary>
    /// <example>
    /// // Simple grammar for yes/no answers:
    /// Grammar = "root ::= (\"yes\" | \"no\")"
    /// </example>
    public string? Grammar { get; set; }

    /// <summary>
    /// Gets or sets a JSON schema to constrain generation.
    /// When set, output will be valid JSON matching this schema.
    /// Supported by llama-server via json_schema parameter.
    /// </summary>
    /// <example>
    /// JsonSchema = "{\"type\":\"object\",\"properties\":{\"answer\":{\"type\":\"string\"}}}"
    /// </example>
    public string? JsonSchema { get; set; }

    #endregion

    /// <summary>
    /// Creates a default instance of GenerationOptions.
    /// </summary>
    public static GenerationOptions Default => new();

    /// <summary>
    /// Creates options optimized for creative text generation.
    /// </summary>
    public static GenerationOptions Creative => new()
    {
        Temperature = 0.9f,
        TopP = 0.95f,
        TopK = 100,
        RepetitionPenalty = 1.2f
    };

    /// <summary>
    /// Creates options optimized for deterministic/precise outputs.
    /// </summary>
    public static GenerationOptions Precise => new()
    {
        Temperature = 0.1f,
        TopP = 0.5f,
        TopK = 10,
        RepetitionPenalty = 1.0f
    };

    /// <summary>
    /// Creates options tuned for Gemma 4 models per Google's published recommendations.
    /// temperature=1.0, top_p=0.95, top_k=64 — required for stable tool-call generation
    /// on E4B and larger. Use instead of <see cref="Default"/> when loading a Gemma 4 model.
    /// </summary>
    public static GenerationOptions Gemma4 => new()
    {
        Temperature = 1.0f,
        TopP = 0.95f,
        TopK = 64,
        RepetitionPenalty = 1.0f,
    };

    /// <summary>
    /// Sampling parameters per official Qwen3 recommendation for thinking mode.
    /// Temperature = 0.6, TopP = 0.95, TopK = 20, MinP = 0.0, RepetitionPenalty = 1.0.
    /// </summary>
    public static GenerationOptions Qwen3 => new()
    {
        Temperature = 0.6f,
        TopP = 0.95f,
        TopK = 20,
        MinP = 0.0f,
        RepetitionPenalty = 1.0f,
    };
}
