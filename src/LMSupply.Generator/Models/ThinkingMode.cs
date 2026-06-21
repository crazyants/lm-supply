namespace LMSupply.Generator.Models;

/// <summary>
/// Controls a model's reasoning ("thinking") behavior for chat generation.
/// </summary>
/// <remarks>
/// Thinking-capable models differ in their default: Qwen3 thinks by default, Gemma 4 does not.
/// <see cref="Auto"/> preserves each model's own default; <see cref="On"/>/<see cref="Off"/> override it.
/// Off is the way to stop a thinking-default-on model (Qwen3) from spending tokens on a reasoning
/// block before answering. Only the chat path (/v1/chat/completions) honors this — the raw
/// completion path has no chat template to drive.
/// </remarks>
public enum ThinkingMode
{
    /// <summary>Use the model's built-in default (Qwen3 thinks; Gemma does not). Default.</summary>
    Auto = 0,

    /// <summary>Force reasoning on (inject the formatter thinking token and/or request enable_thinking=true).</summary>
    On = 1,

    /// <summary>Force reasoning off (request enable_thinking=false so the model answers directly).</summary>
    Off = 2
}
