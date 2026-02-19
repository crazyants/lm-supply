namespace LMSupply.Generator.Models;

/// <summary>
/// Configuration options for text generation (inference parameters).
/// </summary>
public sealed class GenerationOptions
{
    /// <summary>
    /// Gets or sets the maximum number of tokens to generate.
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
    /// Gets or sets the repetition penalty.
    /// Values greater than 1.0 discourage repetition.
    /// Defaults to 1.1.
    /// </summary>
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
    /// Gets or sets the frequency penalty.
    /// Penalizes tokens based on how often they appear in the text so far.
    /// Higher values reduce repetition of frequent tokens.
    /// Range: 0.0 to 2.0. Defaults to 0.0 (disabled).
    /// </summary>
    public float FrequencyPenalty { get; set; }

    /// <summary>
    /// Gets or sets the presence penalty.
    /// Penalizes tokens that have appeared at all in the text so far.
    /// Higher values encourage the model to discuss new topics.
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
}
