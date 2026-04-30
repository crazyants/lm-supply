using LMSupply.Generator.Models;

namespace LMSupply.Generator.Abstractions;

/// <summary>
/// Interface for formatting chat messages into model-specific prompt formats.
/// </summary>
public interface IChatFormatter
{
    /// <summary>
    /// Gets the name of the chat format (e.g., "phi3", "llama3", "chatml").
    /// </summary>
    string FormatName { get; }

    /// <summary>
    /// Formats a sequence of chat messages into a model-specific prompt string.
    /// </summary>
    /// <param name="messages">The chat messages to format.</param>
    /// <returns>The formatted prompt string ready for model input.</returns>
    string FormatPrompt(IEnumerable<ChatMessage> messages);

    /// <summary>
    /// Gets the primary stop token for this format.
    /// </summary>
    string GetStopToken();

    /// <summary>
    /// Gets all stop sequences that should terminate generation.
    /// </summary>
    IReadOnlyList<string> GetStopSequences();

    /// <summary>
    /// Renders an opt-in textual reinforcement of tool schemas for models whose
    /// instruction-following is too brittle to follow llama-server's native
    /// JSON-schema templating (e.g. small/quantized Gemma 4 variants).
    /// Returns <c>null</c> by default — formatters opt in by overriding.
    /// When non-null, the active GGUF generator (<c>LlamaServerGeneratorModel</c>)
    /// prepends the fragment as an additional system message before sending the
    /// conversation to llama-server.
    /// </summary>
    /// <param name="tools">The tool definitions visible to the model on this turn.</param>
    /// <returns>A model-friendly textual fragment, or <c>null</c> to skip injection.</returns>
    /// <remarks>
    /// Reference: ecosystem ISSUE Option D-1 (2026-04-30) —
    /// Gemma 4 E4B at gguf:default emits empty tool args because the native
    /// chat template's raw JSON schema is too dense; a textual marker line
    /// (<c>Required parameters (MUST be provided): &lt;name&gt; (&lt;type&gt;)</c>)
    /// raises first-attempt success.
    /// </remarks>
    string? RenderToolPromptFragment(IReadOnlyList<ChatToolDefinition>? tools) => null;
}
