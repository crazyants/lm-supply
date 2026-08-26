namespace LMSupply.Generator.Models;

/// <summary>
/// Which <see cref="ToolChoice"/> mode a value represents.
/// </summary>
public enum ToolChoiceMode
{
    /// <summary>Model decides freely whether to call a tool.</summary>
    Auto = 0,

    /// <summary>Suppress tool calls even though <see cref="GenerationOptions.Tools"/> is set.</summary>
    None = 1,

    /// <summary>Force the model to call at least one tool.</summary>
    Required = 2,

    /// <summary>Force the model to call one specific, named function.</summary>
    Function = 3
}

/// <summary>
/// Controls whether, and which, tool the model must call. Only meaningful when
/// <see cref="GenerationOptions.Tools"/> is non-empty; a null <see cref="GenerationOptions.ToolChoice"/>
/// leaves the model free to decide (equivalent to <see cref="Auto"/>).
/// </summary>
/// <remarks>
/// Backend support: llama-server only, sent as the OpenAI-compatible <c>tool_choice</c> request field
/// (<c>"auto"</c> / <c>"none"</c> / <c>"required"</c> / <c>{"type":"function","function":{"name":...}}</c>).
/// Ignored by the ONNX backend.
/// </remarks>
public sealed class ToolChoice
{
    /// <summary>Model decides freely whether to call a tool. Equivalent to leaving <see cref="GenerationOptions.ToolChoice"/> unset.</summary>
    public static readonly ToolChoice Auto = new(ToolChoiceMode.Auto, null);

    /// <summary>Suppress tool calls even though <see cref="GenerationOptions.Tools"/> is set.</summary>
    public static readonly ToolChoice None = new(ToolChoiceMode.None, null);

    /// <summary>Force the model to call at least one tool.</summary>
    public static readonly ToolChoice Required = new(ToolChoiceMode.Required, null);

    /// <summary>Force the model to call the named function.</summary>
    /// <param name="name">The function name, matching a <see cref="ChatToolDefinition.Name"/> in <see cref="GenerationOptions.Tools"/>.</param>
    public static ToolChoice Function(string name) =>
        new(ToolChoiceMode.Function, name ?? throw new ArgumentNullException(nameof(name)));

    /// <summary>Which mode this instance represents.</summary>
    public ToolChoiceMode Mode { get; }

    /// <summary>The forced function name when <see cref="Mode"/> is <see cref="ToolChoiceMode.Function"/>; null otherwise.</summary>
    public string? FunctionName { get; }

    private ToolChoice(ToolChoiceMode mode, string? functionName)
    {
        Mode = mode;
        FunctionName = functionName;
    }
}
