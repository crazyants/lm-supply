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

    /// <summary>
    /// Renders a reduced textual reinforcement of tool schemas for use when the model's
    /// thinking mode is active (see <see cref="GetThinkingToken"/>). Returns <c>null</c>
    /// by default — meaning no fragment is injected when thinking is enabled, because
    /// the llama-server Jinja2 template already injects the full structured schema.
    /// Formatters that opt in to thinking-mode-specific fragments override this method
    /// (e.g. Gemma 4 provides required-params-only hints to reduce system prompt pressure).
    /// </summary>
    /// <param name="tools">The tool definitions visible to the model on this turn.</param>
    /// <returns>A minimal hint fragment, or <c>null</c> to skip injection entirely.</returns>
    string? RenderToolPromptFragmentWhenThinking(IReadOnlyList<ChatToolDefinition>? tools) => null;

    /// <summary>
    /// Creates a stateful parser that extracts model-native tool-call wrapper
    /// tokens from the streaming text channel and converts them into structured
    /// <c>ChatToolCallDelta</c> events. Returns <c>null</c> by default — formatters
    /// opt in by overriding when their model emits a wrapper that llama-server
    /// does not recognize.
    /// </summary>
    /// <returns>A new parser instance, or <c>null</c> if this formatter relies
    /// entirely on llama-server's native tool-call extraction.</returns>
    /// <remarks>
    /// When a non-null parser is returned, the active GGUF generator
    /// (<c>LlamaServerGeneratorModel</c>) treats the parser's output as the
    /// authoritative tool-call source for the turn and SUPPRESSES server-emitted
    /// tool-call deltas (which on Gemma 4 are typically half-formed name-only
    /// pattern matches that never invoke).
    ///
    /// Reference: ecosystem ISSUE Option D-5 (2026-05-01) —
    /// Filer cycle-701 surfaced that Gemma 4 emits its native
    /// <c>&lt;|tool_call&gt;call:NAME{ARGS_JSON}&lt;tool_call|&gt;</c> wrapper into the
    /// text channel; without this hook the wrapper tokens leak as plain text and
    /// no tool ever invokes (chat-rag flow 0% success on gguf:default).
    /// </remarks>
    IToolCallStreamParser? CreateToolCallStreamParser() => null;

    /// <summary>
    /// Returns the token string that activates the model's built-in thinking mode
    /// when prepended to the first system message, or <c>null</c> if the model does
    /// not support a thinking mode via prompt injection.
    /// </summary>
    /// <remarks>
    /// Gemma 4 returns <c>"&lt;|think|&gt;"</c>. All other formatters return <c>null</c>.
    /// <see cref="LMSupply.Generator.Models.GenerationOptions.Thinking"/> must be
    /// <see cref="LMSupply.Generator.Models.ThinkingMode.On"/> for the token to be injected by the generator.
    /// </remarks>
    string? GetThinkingToken() => null;
}
