namespace LMSupply.Embedder.Utils;

/// <summary>
/// Provides definitions for built-in supported embedding models.
/// Updated: 2025-12 based on MTEB leaderboard rankings.
/// </summary>
internal static class DefaultModels
{
    // ===== Alias models (all multilingual) =====

    /// <summary>
    /// Default: multilingual-e5-base, 278M params, 100+ languages, balanced quality/speed.
    /// </summary>
    public static ModelInfo MultilingualE5BaseAlias { get; } = new()
    {
        RepoId = "intfloat/multilingual-e5-base",
        AliasName = "default",
        Dimensions = 768,
        MaxSequenceLength = 512,
        PoolingMode = PoolingMode.Mean,
        DoLowerCase = false,
        Description = "Default: multilingual-e5-base, 278M params, 100+ languages, balanced",
        Subfolder = "onnx"
    };

    /// <summary>
    /// Fast: multilingual-e5-small, 118M params, 100+ languages, lightweight.
    /// </summary>
    public static ModelInfo MultilingualE5SmallAlias { get; } = new()
    {
        RepoId = "intfloat/multilingual-e5-small",
        AliasName = "fast",
        Dimensions = 384,
        MaxSequenceLength = 512,
        PoolingMode = PoolingMode.Mean,
        DoLowerCase = false,
        Description = "Fast: multilingual-e5-small, 118M params, 100+ languages, lightweight",
        Subfolder = "onnx"
    };

    /// <summary>
    /// Quality: BGE-M3, 568M params, 100+ languages, 8K context, SOTA multilingual, dense+sparse.
    /// </summary>
    public static ModelInfo BgeM3Alias { get; } = new()
    {
        RepoId = "BAAI/bge-m3",
        AliasName = "quality",
        Dimensions = 1024,
        MaxSequenceLength = 8192,
        PoolingMode = PoolingMode.Cls,
        DoLowerCase = false,
        Description = "Quality: BGE-M3, 568M params, 100+ languages, 8K context, SOTA multilingual",
        Subfolder = "onnx"
    };

    /// <summary>
    /// Large: multilingual-e5-large, 560M params, 100+ languages, highest dense quality.
    /// </summary>
    public static ModelInfo MultilingualE5LargeAlias { get; } = new()
    {
        RepoId = "intfloat/multilingual-e5-large",
        AliasName = "large",
        Dimensions = 1024,
        MaxSequenceLength = 512,
        PoolingMode = PoolingMode.Mean,
        DoLowerCase = false,
        Description = "Large: multilingual-e5-large, 560M params, 100+ languages, highest dense quality",
        Subfolder = "onnx"
    };

    // ===== Explicit models (non-alias, registered by short name) =====

    /// <summary>
    /// all-mpnet-base-v2, 110M params, legacy quality model, English.
    /// </summary>
    public static ModelInfo AllMpnetBaseV2 { get; } = new()
    {
        RepoId = "sentence-transformers/all-mpnet-base-v2",
        AliasName = "all-mpnet-base-v2",
        Dimensions = 768,
        MaxSequenceLength = 384,
        PoolingMode = PoolingMode.Mean,
        DoLowerCase = true,
        Description = "110M params, legacy quality model, English",
        Subfolder = "onnx"
    };

    /// <summary>
    /// bge-base-en-v1.5, 110M params, excellent quality, English.
    /// </summary>
    public static ModelInfo BgeBaseEnV15 { get; } = new()
    {
        RepoId = "BAAI/bge-base-en-v1.5",
        AliasName = "bge-base-en-v1.5",
        Dimensions = 768,
        MaxSequenceLength = 512,
        PoolingMode = PoolingMode.Cls,
        DoLowerCase = true,
        Description = "110M params, excellent quality, English",
        Subfolder = "onnx"
    };

    /// <summary>
    /// bge-large-en-v1.5, 335M params, highest accuracy BGE, English.
    /// </summary>
    public static ModelInfo BgeLargeEnV15 { get; } = new()
    {
        RepoId = "BAAI/bge-large-en-v1.5",
        AliasName = "bge-large-en-v1.5",
        Dimensions = 1024,
        MaxSequenceLength = 512,
        PoolingMode = PoolingMode.Cls,
        DoLowerCase = true,
        Description = "335M params, highest accuracy BGE, English",
        Subfolder = "onnx"
    };

    /// <summary>
    /// e5-small-v2, 33M params, no prefix needed, English.
    /// </summary>
    public static ModelInfo E5SmallV2 { get; } = new()
    {
        RepoId = "intfloat/e5-small-v2",
        AliasName = "e5-small-v2",
        Dimensions = 384,
        MaxSequenceLength = 512,
        PoolingMode = PoolingMode.Mean,
        DoLowerCase = false,
        Description = "33M params, no prefix needed, English",
        Subfolder = "onnx"
    };

    /// <summary>
    /// e5-base-v2, 110M params, excellent retrieval, English.
    /// </summary>
    public static ModelInfo E5BaseV2 { get; } = new()
    {
        RepoId = "intfloat/e5-base-v2",
        AliasName = "e5-base-v2",
        Dimensions = 768,
        MaxSequenceLength = 512,
        PoolingMode = PoolingMode.Mean,
        DoLowerCase = false,
        Description = "110M params, excellent retrieval, English",
        Subfolder = "onnx"
    };

    /// <summary>
    /// multilingual-e5-small, 118M params, 100+ languages, compact.
    /// </summary>
    public static ModelInfo MultilingualE5Small { get; } = new()
    {
        RepoId = "intfloat/multilingual-e5-small",
        AliasName = "multilingual-e5-small",
        Dimensions = 384,
        MaxSequenceLength = 512,
        PoolingMode = PoolingMode.Mean,
        DoLowerCase = false,
        Description = "118M params, 100+ languages, compact",
        Subfolder = "onnx"
    };

    /// <summary>
    /// multilingual-e5-base, 278M params, 100+ languages, quality.
    /// </summary>
    public static ModelInfo MultilingualE5Base { get; } = new()
    {
        RepoId = "intfloat/multilingual-e5-base",
        AliasName = "multilingual-e5-base",
        Dimensions = 768,
        MaxSequenceLength = 512,
        PoolingMode = PoolingMode.Mean,
        DoLowerCase = false,
        Description = "278M params, 100+ languages, quality",
        Subfolder = "onnx"
    };

    /// <summary>
    /// multilingual-e5-large, 560M params, 100+ languages, highest quality.
    /// </summary>
    public static ModelInfo MultilingualE5Large { get; } = new()
    {
        RepoId = "intfloat/multilingual-e5-large",
        AliasName = "multilingual-e5-large",
        Dimensions = 1024,
        MaxSequenceLength = 512,
        PoolingMode = PoolingMode.Mean,
        DoLowerCase = false,
        Description = "560M params, 100+ languages, highest quality",
        Subfolder = "onnx"
    };

    /// <summary>
    /// gte-large-en-v1.5, 434M params, 8K context, highest accuracy GTE.
    /// </summary>
    public static ModelInfo GteLargeEnV15 { get; } = new()
    {
        RepoId = "Alibaba-NLP/gte-large-en-v1.5",
        AliasName = "gte-large-en-v1.5",
        Dimensions = 1024,
        MaxSequenceLength = 8192,
        PoolingMode = PoolingMode.Cls,
        DoLowerCase = false,
        Description = "434M params, 8K context, highest accuracy GTE",
        Subfolder = "onnx"
    };

    /// <summary>
    /// Gets all built-in models.
    /// </summary>
    public static IReadOnlyList<ModelInfo> All { get; } =
    [
        // Alias models (4 standard aliases, all multilingual)
        MultilingualE5BaseAlias,    // default
        MultilingualE5SmallAlias,   // fast
        BgeM3Alias,                 // quality
        MultilingualE5LargeAlias,   // large

        // Explicit models (by short name)
        AllMpnetBaseV2,
        BgeBaseEnV15,
        BgeLargeEnV15,
        E5SmallV2,
        E5BaseV2,
        MultilingualE5Small,
        MultilingualE5Base,
        MultilingualE5Large,
        GteLargeEnV15,
    ];
}
