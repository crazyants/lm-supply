namespace LMSupply.Hardware;

/// <summary>
/// RoPE positional encoding scaling mode for context extension.
/// </summary>
public enum RopeScalingMode
{
    /// <summary>
    /// No explicit scaling; llama-server reads from model metadata automatically.
    /// </summary>
    Default = 0,

    /// <summary>
    /// Linear RoPE scaling (--rope-scaling linear).
    /// </summary>
    Linear = 1,

    /// <summary>
    /// YaRN scaling (--rope-scaling yarn). Best for extending to 4–8× original context.
    /// Requires YarnOriginalContext to be set in LlamaOptions.
    /// </summary>
    YaRN = 2,

    /// <summary>
    /// LongRoPE scaling (--rope-scaling longrope). For extreme context extension.
    /// </summary>
    LongRoPE = 3,
}
