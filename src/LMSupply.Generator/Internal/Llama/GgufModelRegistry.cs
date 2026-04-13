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
        // Gemma 4 중심 레지스트리 (Apache 2.0, 멀티모달, 네이티브 function calling)
        // llama.cpp b8672+ 에서 Gemma 4 네이티브 지원 (GGUF 메타데이터 자동 감지)
        // ============================================================

        // Fast: Gemma 4 E2B — smallest Gemma 4, fits 4GB iGPU/mobile (~4GB VRAM)
        ["gguf:fast"] = new GgufModelInfo
        {
            RepoId = "unsloth/gemma-4-E2B-it-GGUF",
            DisplayName = "Gemma 4 E2B Instruct",
            DefaultFile = "gemma-4-E2B-it-Q4_K_M.gguf",
            ChatFormat = "gemma4",
            ContextLength = 131072,
            ParameterCount = 2_300_000_000,
            EstimatedSizeBytes = 3_110_000_000L,
            QuantizationType = "Q4_K_M",
            License = LicenseTier.MIT,
            LicenseName = "Apache 2.0",
        },

        // Default: Gemma 4 E4B — best balance of size, speed, and quality
        ["gguf:default"] = new GgufModelInfo
        {
            RepoId = "ggml-org/gemma-4-E4B-it-GGUF",
            DisplayName = "Gemma 4 E4B Instruct",
            DefaultFile = "gemma-4-e4b-it-Q4_K_M.gguf",
            ChatFormat = "gemma4",
            ContextLength = 131072,
            ParameterCount = 4_500_000_000,
            EstimatedSizeBytes = 5_335_285_440L,
            QuantizationType = "Q4_K_M",
            License = LicenseTier.MIT,
            LicenseName = "Apache 2.0",
        },

        // Balanced: Gemma 4 E4B Q8_0 — higher quality E4B for 10-12GB VRAM (RTX 3060 12GB, etc.)
        // Fills the gap between default (5.3GB) and quality (16.8GB)
        ["gguf:balanced"] = new GgufModelInfo
        {
            RepoId = "ggml-org/gemma-4-E4B-it-GGUF",
            DisplayName = "Gemma 4 E4B Instruct (Q8_0)",
            DefaultFile = "gemma-4-e4b-it-Q8_0.gguf",
            ChatFormat = "gemma4",
            ContextLength = 131072,
            ParameterCount = 4_500_000_000,
            EstimatedSizeBytes = 7_500_000_000L,
            QuantizationType = "Q8_0",
            License = LicenseTier.MIT,
            LicenseName = "Apache 2.0",
        },

        // Quality: Gemma 4 26B MoE — 31B-class performance with 4B active params
        ["gguf:quality"] = new GgufModelInfo
        {
            RepoId = "ggml-org/gemma-4-26B-A4B-it-GGUF",
            DisplayName = "Gemma 4 26B A4B Instruct (MoE)",
            DefaultFile = "gemma-4-26B-A4B-it-Q4_K_M.gguf",
            ChatFormat = "gemma4",
            ContextLength = 262144,
            ParameterCount = 26_000_000_000,
            EstimatedSizeBytes = 16_796_010_720L,
            QuantizationType = "Q4_K_M",
            License = LicenseTier.MIT,
            LicenseName = "Apache 2.0",
        },

        // Large: Gemma 4 31B Dense — maximum quality single-GPU model
        ["gguf:large"] = new GgufModelInfo
        {
            RepoId = "ggml-org/gemma-4-31B-it-GGUF",
            DisplayName = "Gemma 4 31B Instruct",
            DefaultFile = "gemma-4-31B-it-Q4_K_M.gguf",
            ChatFormat = "gemma4",
            ContextLength = 262144,
            ParameterCount = 31_000_000_000,
            EstimatedSizeBytes = 18_687_057_376L,
            QuantizationType = "Q4_K_M",
            License = LicenseTier.MIT,
            LicenseName = "Apache 2.0",
        },

        // XLarge: Server-grade MoE model (split GGUF — 3 shards in Q4_K_M/ subfolder)
        ["gguf:xlarge"] = new GgufModelInfo
        {
            RepoId = "unsloth/Qwen3.5-122B-A10B-GGUF",
            DisplayName = "Qwen 3.5 122B A10B (MoE)",
            DefaultFile = "Q4_K_M/Qwen3.5-122B-A10B-Q4_K_M-00001-of-00003.gguf",
            ChatFormat = "chatml",
            ContextLength = 32768,
            ParameterCount = 122_000_000_000,
            EstimatedSizeBytes = 76_536_573_608L,
            QuantizationType = "Q4_K_M",
            ShardCount = 3,
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
