using LMSupply.Hardware;
using LMSupply.Runtime;

namespace LMSupply.Generator.Internal.Llama;

/// <summary>
/// Registry of well-known GGUF models with aliases for easy access.
/// Follows the same alias pattern as the ONNX ModelRegistry.
/// </summary>
public static class GgufModelRegistry
{
    private static readonly Dictionary<string, GgufModelInfo> _models = new(StringComparer.OrdinalIgnoreCase)
    {
        // ============================================================
        // Tool-Calling First: llama.cpp 네이티브 핸들러 안정성 기준
        // Option A (안정성 최우선) — Hermes/Mistral/Llama 핸들러
        // ============================================================

        // Fast: Smallest tool-calling capable model (Mistral Nemo handler)
        ["gguf:fast"] = new GgufModelInfo
        {
            RepoId = "mistralai/Ministral-3-3B-Instruct-2512-GGUF",
            DisplayName = "Ministral 3 3B Instruct",
            DefaultFile = "Ministral-3-3B-Instruct-2512-Q4_K_M.gguf",
            ChatFormat = "mistral-nemo",
            ContextLength = 32768,
            ParameterCount = 3_000_000_000,
            EstimatedSizeBytes = 2_000_000_000L,
            QuantizationType = "Q4_K_M",
            License = LicenseTier.MIT,
            LicenseName = "Apache 2.0",
        },

        // Default: Most stable tool-calling model (Hermes native handler)
        ["gguf:default"] = new GgufModelInfo
        {
            RepoId = "NousResearch/Hermes-3-Llama-3.1-8B-GGUF",
            DisplayName = "Hermes 3 Llama 3.1 8B",
            DefaultFile = "Hermes-3-Llama-3.1-8B.Q4_K_M.gguf",
            ChatFormat = "chatml",
            ContextLength = 8192,
            ParameterCount = 8_000_000_000,
            EstimatedSizeBytes = 4_920_000_000L,
            QuantizationType = "Q4_K_M",
            License = LicenseTier.Conditional,
            LicenseName = "Llama 3.1 Community License",
            LicenseRestrictions = "700M MAU limit for commercial use"
        },

        // Quality: Mistral Nemo native handler, Apache 2.0
        ["gguf:quality"] = new GgufModelInfo
        {
            RepoId = "bartowski/Mistral-Nemo-Instruct-2407-GGUF",
            DisplayName = "Mistral Nemo 12B Instruct",
            DefaultFile = "Mistral-Nemo-Instruct-2407-Q4_K_M.gguf",
            ChatFormat = "mistral-nemo",
            ContextLength = 32768,
            ParameterCount = 12_000_000_000,
            EstimatedSizeBytes = 7_000_000_000L,
            QuantizationType = "Q4_K_M",
            License = LicenseTier.MIT,
            LicenseName = "Apache 2.0",
        },

        // Large: High quality dense model
        ["gguf:large"] = new GgufModelInfo
        {
            RepoId = "unsloth/Qwen3-32B-GGUF",
            DisplayName = "Qwen 3 32B",
            DefaultFile = "Qwen3-32B-Q4_K_M.gguf",
            ChatFormat = "chatml",
            ContextLength = 32768,
            ParameterCount = 32_000_000_000,
            EstimatedSizeBytes = 19_000_000_000L,
            QuantizationType = "Q4_K_M",
            License = LicenseTier.MIT,
            LicenseName = "Apache 2.0",
        },

        // XLarge: Server-grade MoE model
        ["gguf:xlarge"] = new GgufModelInfo
        {
            RepoId = "unsloth/Qwen3.5-122B-A10B-GGUF",
            DisplayName = "Qwen 3.5 122B A10B (MoE)",
            DefaultFile = "Qwen3.5-122B-A10B-Q4_K_M.gguf",
            ChatFormat = "chatml",
            ContextLength = 32768,
            ParameterCount = 122_000_000_000,
            EstimatedSizeBytes = 48_000_000_000L,
            QuantizationType = "Q4_K_M",
            License = LicenseTier.MIT,
            LicenseName = "Apache 2.0",
        },
    };

    /// <summary>
    /// Resolves an alias to model information.
    /// Supports both "gguf:alias" format and plain "alias" format.
    /// </summary>
    /// <param name="aliasOrRepoId">The alias (e.g., "gguf:default", "default", "gguf:auto") or full repo ID.</param>
    /// <returns>Model information if found, null otherwise.</returns>
    public static GgufModelInfo? Resolve(string aliasOrRepoId)
    {
        if (string.IsNullOrWhiteSpace(aliasOrRepoId))
            return null;

        // Handle "gguf:auto" alias - select optimal model based on hardware
        if (aliasOrRepoId.Equals("gguf:auto", StringComparison.OrdinalIgnoreCase))
            return GetAutoModel();

        // Try direct lookup with gguf: prefix
        if (_models.TryGetValue(aliasOrRepoId, out var info))
            return info;

        // Try with gguf: prefix added
        if (!aliasOrRepoId.StartsWith("gguf:", StringComparison.OrdinalIgnoreCase))
        {
            if (_models.TryGetValue($"gguf:{aliasOrRepoId}", out info))
                return info;
        }

        return null;
    }

    /// <summary>
    /// Gets the optimal GGUF model based on current hardware profile.
    /// Delegates to the VRAM-aware overload using the current GPU.
    /// </summary>
    public static GgufModelInfo GetAutoModel()
        => GetAutoModel(HardwareProfile.Current.GpuInfo);

    /// <summary>
    /// Gets the optimal GGUF model based on actual available VRAM.
    /// Models are sorted by size descending; selects largest that fits.
    /// Falls back to smallest model if nothing fits (CPU fallback).
    /// </summary>
    public static GgufModelInfo GetAutoModel(GpuInfo gpu)
    {
        var availableVram = VramBudget.GetAvailableBytes(gpu);

        var candidates = _models.Values
            .Where(m => m.EstimatedSizeBytes.HasValue)
            .OrderByDescending(m => m.EstimatedSizeBytes!.Value)
            .ToList();

        foreach (var model in candidates)
        {
            if (model.EstimatedSizeBytes!.Value <= availableVram)
                return model;
        }

        // Nothing fits in VRAM → return smallest (will use CPU or partial offload)
        return candidates.LastOrDefault() ?? _models["gguf:fast"];
    }

    /// <summary>
    /// Gets all registered GGUF models.
    /// </summary>
    public static IReadOnlyList<GgufModelInfo> GetAllModels() =>
        _models.Values.ToList();

    /// <summary>
    /// Gets GGUF models filtered by license tier.
    /// </summary>
    public static IReadOnlyList<GgufModelInfo> GetModelsByLicense(LicenseTier tier) =>
        _models.Values.Where(m => m.License == tier).ToList();

    /// <summary>
    /// Checks if a string is a known GGUF alias.
    /// Only matches "gguf:"-prefixed aliases (e.g., "gguf:default", "gguf:fast", "gguf:auto").
    /// Plain aliases like "default" or "fast" are reserved for ONNX models.
    /// </summary>
    public static bool IsAlias(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        // Only match if it starts with "gguf:" prefix
        // Plain aliases without prefix are reserved for ONNX ModelRegistry
        if (!value.StartsWith("gguf:", StringComparison.OrdinalIgnoreCase))
            return false;

        // Handle "gguf:auto" special alias
        if (value.Equals("gguf:auto", StringComparison.OrdinalIgnoreCase))
            return true;

        return _models.ContainsKey(value);
    }

    /// <summary>
    /// Gets all available alias names including "gguf:auto".
    /// </summary>
    public static IReadOnlyList<string> GetAliases()
    {
        var aliases = new List<string> { "gguf:auto" };
        aliases.AddRange(_models.Keys);
        return aliases;
    }
}
