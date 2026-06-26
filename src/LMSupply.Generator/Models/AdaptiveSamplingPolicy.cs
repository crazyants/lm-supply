namespace LMSupply.Generator.Models;

/// <summary>
/// Standard, hardware/quantization-aware adjustment to anti-repetition sampling for low-end and
/// quantized models, which are markedly more prone to degenerate run-on / repetition loops (ignoring
/// EOS and emitting text to the length cap). It is well established that such models need a non-trivial
/// repetition defense, yet several model presets (Qwen3, Gemma4, Precise) ship
/// <see cref="GenerationOptions.RepetitionPenalty"/> = 1.0, which disables that defense entirely.
/// </summary>
/// <remarks>
/// This type is <b>pure and side-effect free</b>: it computes a recommended value, it never mutates
/// global state and never changes generation behavior on its own. Whether and where to apply it is the
/// caller's decision — apply it only where the user expressed no explicit preference (e.g. an auto-load
/// default path), so an explicitly chosen sampling value is always respected. It is the single source
/// of truth for "what is the low-end-safe anti-repetition floor", reusable by consumers and by any
/// future opt-in auto-application.
/// </remarks>
public static class AdaptiveSamplingPolicy
{
    /// <summary>
    /// Minimum repetition penalty enforced for low-end/quantized models so the primary anti-repetition
    /// defense is never left fully disabled. Matches <see cref="GenerationOptions.Default"/>'s 1.1.
    /// </summary>
    public const float LowEndMinRepetitionPenalty = 1.1f;

    /// <summary>
    /// Resolves the repetition penalty to actually use. On a low-end/quantized model, a requested value
    /// that would disable the defense (&lt; <see cref="LowEndMinRepetitionPenalty"/>, e.g. a preset's 1.0)
    /// is raised to the safe floor. The requested value is returned unchanged when it is already at or
    /// above the floor, or when not low-end — this only ever <i>raises</i> the defense, never lowers it.
    /// </summary>
    /// <param name="requested">The repetition penalty the caller would otherwise use.</param>
    /// <param name="isLowEnd">True when the model runs in a low-end/quantized context (e.g. CPU fallback,
    /// integrated GPU, or a heavily quantized weight set) where run-on risk is elevated.</param>
    public static float ResolveRepetitionPenalty(float requested, bool isLowEnd)
        => isLowEnd && requested < LowEndMinRepetitionPenalty ? LowEndMinRepetitionPenalty : requested;
}
