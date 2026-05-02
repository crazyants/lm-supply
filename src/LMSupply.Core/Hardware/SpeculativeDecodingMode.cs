namespace LMSupply.Hardware;

/// <summary>
/// Speculative decoding strategy for llama-server.
/// </summary>
public enum SpeculativeDecodingMode
{
    /// <summary>
    /// Auto-select: enables Ngram if server build ≥ minimum (b8500), otherwise None.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Disabled.
    /// </summary>
    None = 1,

    /// <summary>
    /// N-gram based speculation (no draft model required).
    /// Emits --spec-type ngram-simple on llama-server ≥ b8994, ngram on b8500–b8993.
    /// Provides 1.5–3× speedup with no quality loss.
    /// </summary>
    Ngram = 2,

    /// <summary>
    /// Draft model based speculation (--model-draft &lt;path&gt;).
    /// Requires DraftModelPath to be set in LlamaOptions.
    /// </summary>
    DraftModel = 3,
}
