namespace LMSupply;

/// <summary>
/// Base class for all LMSupply model options.
/// Provides common configuration properties shared across all packages.
/// </summary>
public abstract class LMSupplyOptionsBase
{
    /// <summary>
    /// Gets or sets the custom cache directory for model files.
    /// <para>Default: null (uses HuggingFace standard cache location: ~/.cache/huggingface/hub)</para>
    /// </summary>
    public string? CacheDirectory { get; set; }

    /// <summary>
    /// Gets or sets the execution provider for inference.
    /// <para>Default: <see cref="ExecutionProvider.Auto"/> (automatically selects the best available provider)</para>
    /// </summary>
    public ExecutionProvider Provider { get; set; } = ExecutionProvider.Auto;

    /// <summary>
    /// Gets or sets the number of threads to use for inference.
    /// <para>Default: null (uses ONNX Runtime default, typically all available cores)</para>
    /// <para>Set to a specific value to limit CPU usage, useful for:</para>
    /// <list type="bullet">
    ///   <item>Running multiple models concurrently</item>
    ///   <item>Leaving CPU headroom for other tasks</item>
    ///   <item>Reducing power consumption</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// This setting primarily affects CPU inference. GPU inference may have different threading behavior
    /// controlled by the GPU driver.
    /// </remarks>
    public int? ThreadCount { get; set; }

    /// <summary>
    /// Gets or sets the quantization hint for model variant selection during download.
    /// Overrides hardware-adaptive default when explicitly specified.
    /// <para>Set automatically when using the qualifier syntax (e.g., "large:fp16").</para>
    /// <para>Examples: "fp16", "int8", "q4", "bnb4", "q4f16"</para>
    /// <para>Default: null (hardware-adaptive selection)</para>
    /// </summary>
    public string? QuantizationHint { get; set; }

    /// <summary>
    /// Splits a model identifier into base ID and optional variant qualifier.
    /// Supports "alias:qualifier" syntax (e.g., "large:fp16", "default:q4").
    /// Does not split HuggingFace repo IDs (containing /) or local paths.
    /// </summary>
    /// <param name="modelIdOrAlias">The model identifier, possibly with qualifier.</param>
    /// <returns>Base model ID and optional qualifier string.</returns>
    public static (string BaseId, string? Qualifier) SplitQualifier(string modelIdOrAlias)
    {
        if (string.IsNullOrWhiteSpace(modelIdOrAlias))
            return (modelIdOrAlias, null);

        // Don't split paths or HF repo IDs
        if (modelIdOrAlias.Contains('/') || modelIdOrAlias.Contains('\\'))
            return (modelIdOrAlias, null);

        var colonIdx = modelIdOrAlias.LastIndexOf(':');
        if (colonIdx <= 0 || colonIdx == modelIdOrAlias.Length - 1)
            return (modelIdOrAlias, null);

        // Don't split Windows drive paths like "C:\..."
        if (colonIdx == 1 && char.IsLetter(modelIdOrAlias[0]))
            return (modelIdOrAlias, null);

        return (modelIdOrAlias[..colonIdx], modelIdOrAlias[(colonIdx + 1)..]);
    }
}
