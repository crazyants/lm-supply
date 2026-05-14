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

        // Fast: Gemma 4 E2B — smallest Gemma 4, fits 4GB iGPU/mobile (~3.1GB VRAM).
        // Q4_K_M is the only tier that fits 4GB VRAM (Q8_0=4.8GB, Q5_K_M=3.2GB w/ no KV margin).
        // Tool-call schema compliance at Q4_K_M is lower than on larger models — prefer gguf:default
        // or above for agentic workloads. Auto-selection picks E2B only when E4B doesn't fit.
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
            NumLayers = 26,
            HiddenSize = 2304,
            License = LicenseTier.MIT,
            LicenseName = "Apache 2.0",
            KnownIssues = [GgufModelKnownIssues.ToolUseUnreliableQ4, GgufModelKnownIssues.InstructionFollowingUnreliableQ4],
        },

        // Default: Gemma 4 E4B — best balance of size, speed, and quality
        ["gguf:default"] = new GgufModelInfo
        {
            RepoId = "ggml-org/gemma-4-E4B-it-GGUF",
            DisplayName = "Gemma 4 E4B Instruct",
            DefaultFile = "gemma-4-E4B-it-Q4_K_M.gguf",
            ChatFormat = "gemma4",
            ContextLength = 131072,
            ParameterCount = 4_500_000_000,
            EstimatedSizeBytes = 5_335_285_440L,
            QuantizationType = "Q4_K_M",
            NumLayers = 34,
            HiddenSize = 2560,
            License = LicenseTier.MIT,
            LicenseName = "Apache 2.0",
            KnownIssues = [GgufModelKnownIssues.ToolUseUnreliableQ4, GgufModelKnownIssues.InstructionFollowingUnreliableQ4],
        },

        // Balanced: Gemma 4 E4B Q8_0 — higher quality E4B for 10-12GB VRAM (RTX 3060 12GB, etc.)
        // Fills the gap between default (5.3GB) and quality (16.8GB)
        ["gguf:balanced"] = new GgufModelInfo
        {
            RepoId = "ggml-org/gemma-4-E4B-it-GGUF",
            DisplayName = "Gemma 4 E4B Instruct (Q8_0)",
            DefaultFile = "gemma-4-E4B-it-Q8_0.gguf",
            ChatFormat = "gemma4",
            ContextLength = 131072,
            ParameterCount = 4_500_000_000,
            EstimatedSizeBytes = 7_500_000_000L,
            QuantizationType = "Q8_0",
            NumLayers = 34,
            HiddenSize = 2560,
            License = LicenseTier.MIT,
            LicenseName = "Apache 2.0",
            KnownIssues = [GgufModelKnownIssues.ToolUseUnreliableQ4, GgufModelKnownIssues.InstructionFollowingUnreliableQ4],
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
            NumLayers = 46,
            HiddenSize = 4096,
            License = LicenseTier.MIT,
            LicenseName = "Apache 2.0",
            KnownIssues = [GgufModelKnownIssues.ToolUseUnreliableQ4, GgufModelKnownIssues.InstructionFollowingUnreliableQ4],
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
            NumLayers = 62,
            HiddenSize = 5376,
            License = LicenseTier.MIT,
            LicenseName = "Apache 2.0",
            KnownIssues = [GgufModelKnownIssues.ToolUseUnreliableQ4, GgufModelKnownIssues.InstructionFollowingUnreliableQ4],
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
            NumLayers = 48,
            HiddenSize = 6144,
            ShardCount = 3,
            License = LicenseTier.MIT,
            LicenseName = "Apache 2.0",
        },

        // ============================================================
        // Phi-4 Mini — MIT, strong multilingual (KO/EN), Phi3 chat format, 16K context
        // Preferred for Korean RAG workloads where Gemma 4 E2B shows silent floor at Q4_K_M.
        // GGUF repack via bartowski; matches the ONNX variant at microsoft/Phi-4-mini-instruct-onnx.
        // ============================================================

        // Phi-4-mini Q4_K_M: 3.8B params, ~2.4GB VRAM, best Korean per-GB below 5GB
        ["gguf:phi-4-mini"] = new GgufModelInfo
        {
            RepoId = "bartowski/Phi-4-mini-instruct-GGUF",
            DisplayName = "Phi-4 Mini Instruct",
            DefaultFile = "Phi-4-mini-instruct-Q4_K_M.gguf",
            ChatFormat = "phi3",
            ContextLength = 16384,
            ParameterCount = 3_800_000_000,
            EstimatedSizeBytes = 2_393_600_000L,
            QuantizationType = "Q4_K_M",
            NumLayers = 32,
            HiddenSize = 3072,
            License = LicenseTier.MIT,
            LicenseName = "MIT",
        },

        // ============================================================
        // Qwen 2.5 — Apache 2.0, strong multilingual (KO/ZH/EN), ChatML, 32K context
        // Recommended default for Korean RAG workloads where Gemma 4 family shows silent floor
        // ============================================================

        // Qwen2.5-7B: Best KO/ZH multilingual quality per GB; 32K context; ~4.4GB VRAM
        ["gguf:qwen2.5-7b"] = new GgufModelInfo
        {
            RepoId = "bartowski/Qwen2.5-7B-Instruct-GGUF",
            DisplayName = "Qwen 2.5 7B Instruct",
            DefaultFile = "Qwen2.5-7B-Instruct-Q4_K_M.gguf",
            ChatFormat = "chatml",
            ContextLength = 32768,
            ParameterCount = 7_620_000_000,
            EstimatedSizeBytes = 4_682_024_960L,
            QuantizationType = "Q4_K_M",
            NumLayers = 28,
            HiddenSize = 3584,
            License = LicenseTier.MIT,
            LicenseName = "Apache 2.0",
        },
    };

    /// <summary>
    /// Default context length used to estimate KV cache size when computing the VRAM budget.
    /// llama-server reserves the full <c>--ctx-size</c> KV cache at load time, so a model
    /// that exceeds this budget at this context will OOM. 4096 is conservative and matches
    /// the practical default for most chat workloads.
    /// </summary>
    public const int DefaultBudgetContextLength = 4096;

    /// <summary>
    /// Resolves an alias to model information.
    /// Supports both "gguf:alias" format and plain "alias" format.
    /// </summary>
    /// <param name="aliasOrRepoId">The alias (e.g., "gguf:default", "default", "gguf:auto") or full repo ID.</param>
    /// <returns>Model information if found, null otherwise. The <see cref="GgufModelInfo.AliasName"/> is populated for registered aliases.</returns>
    public static GgufModelInfo? Resolve(string aliasOrRepoId)
    {
        if (string.IsNullOrWhiteSpace(aliasOrRepoId))
            return null;

        // Handle "gguf:auto" alias - select optimal model based on hardware
        if (aliasOrRepoId.Equals("gguf:auto", StringComparison.OrdinalIgnoreCase))
            return GetAutoModel();

        // Try direct lookup with gguf: prefix
        if (_models.TryGetValue(aliasOrRepoId, out var info))
            return WithAlias(info, aliasOrRepoId);

        // Try with gguf: prefix added
        if (!aliasOrRepoId.StartsWith("gguf:", StringComparison.OrdinalIgnoreCase))
        {
            var prefixed = $"gguf:{aliasOrRepoId}";
            if (_models.TryGetValue(prefixed, out info))
                return WithAlias(info, prefixed);
        }

        return null;
    }

    private static GgufModelInfo WithAlias(GgufModelInfo info, string alias)
        => info with { AliasName = alias.ToLowerInvariant() };

    /// <summary>
    /// Gets the optimal GGUF model based on current hardware profile.
    /// Delegates to the VRAM-aware overload using the current GPU.
    /// </summary>
    public static GgufModelInfo GetAutoModel()
        => GetAutoModel(HardwareProfile.Current.GpuInfo);

    /// <summary>
    /// Gets the optimal GGUF model based on actual available VRAM, including KV cache footprint.
    /// Sorts candidates by total VRAM footprint (weights + KV @ default context) descending,
    /// selects largest that fits, falls back to smallest if nothing fits.
    /// </summary>
    public static GgufModelInfo GetAutoModel(GpuInfo gpu)
        => GetAutoSelection(gpu).Selected;

    /// <summary>
    /// Performs auto-selection and returns the full diagnostic <see cref="ModelSelectionResult"/>
    /// including budget breakdown, candidate list with fit info, and the selection reason.
    /// Uses <see cref="DefaultBudgetContextLength"/> for KV cache estimation.
    /// </summary>
    public static ModelSelectionResult GetAutoSelection(GpuInfo gpu)
        => GetAutoSelection(gpu, DefaultBudgetContextLength);

    /// <summary>
    /// Performs auto-selection with an explicit budget context length for KV cache estimation.
    /// </summary>
    public static ModelSelectionResult GetAutoSelection(GpuInfo gpu, int budgetContextLength)
        => GetAutoSelection(gpu, budgetContextLength, null);

    /// <summary>
    /// Performs auto-selection, optionally excluding models with specific known issues.
    /// Models whose <see cref="GgufModelInfo.KnownIssues"/> intersects <paramref name="excludeKnownIssues"/>
    /// are removed before selection. Pass <c>null</c> to include all models (same as the two-param overload).
    /// </summary>
    public static ModelSelectionResult GetAutoSelection(
        GpuInfo gpu,
        int budgetContextLength,
        IReadOnlyCollection<string>? excludeKnownIssues)
    {
        var safetyMargin = VramBudget.GetRecommendedSafetyMargin(gpu);
        var availableVram = VramBudget.GetAvailableBytes(gpu, safetyMargin);

        var eligible = excludeKnownIssues is { Count: > 0 }
            ? _models.Where(kv => !kv.Value.KnownIssues.Any(excludeKnownIssues.Contains))
            : _models.AsEnumerable();

        var candidates = eligible
            .Select(kv => EvaluateCandidate(WithAlias(kv.Value, kv.Key), availableVram, budgetContextLength))
            .OrderByDescending(c => c.TotalBytes)
            .ToList();

        var fitting = candidates.FirstOrDefault(c => c.Fits);
        GgufModelInfo selected;
        ModelSelectionReason reason;

        if (fitting is not null)
        {
            selected = fitting.Model;
            reason = ModelSelectionReason.Fits;
        }
        else
        {
            // Nothing fits in VRAM → return smallest (will use CPU or partial offload)
            var smallest = candidates.LastOrDefault()?.Model
                ?? WithAlias(_models["gguf:fast"], "gguf:fast");
            selected = smallest;
            reason = ModelSelectionReason.FallbackToSmallest;
        }

        return new ModelSelectionResult
        {
            Selected = selected,
            Reason = reason,
            AvailableVramBytes = availableVram,
            SafetyMargin = safetyMargin,
            BudgetContextLength = budgetContextLength,
            Candidates = candidates,
        };
    }

    private static ModelSelectionCandidate EvaluateCandidate(
        GgufModelInfo model, long availableVram, int budgetContextLength)
    {
        var weights = ModelMemoryEstimator.EstimateModelSizeBytes(
            model.ParameterCount,
            model.QuantizationType,
            model.EstimatedSizeBytes);

        // KV cache only computable when architecture fields are set.
        long kvCache = (model.NumLayers > 0 && model.HiddenSize > 0)
            ? ModelMemoryEstimator.EstimateKvCacheBytes(
                budgetContextLength, model.NumLayers, model.HiddenSize)
            : 0L;

        var total = weights + kvCache;
        return new ModelSelectionCandidate
        {
            Model = model,
            WeightsBytes = weights,
            KvCacheBytes = kvCache,
            Fits = total <= availableVram,
        };
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
