namespace LMSupply;

/// <summary>
/// Single resolver for the LMSupply non-HF artifact cache root — runtime packages
/// (ONNX runtimes) and llama-server builds. These artifacts are not HuggingFace models
/// and must live outside any HF hub directory; model caching is resolved separately by
/// <see cref="Download.CacheManager"/> following the HF env chain.
/// </summary>
/// <remarks>
/// Resolution order:
/// 1. <c>LMSUPPLY_CACHE_DIR</c> environment variable
/// 2. <c>%LOCALAPPDATA%/LMSupply/cache</c> (platform equivalent via
///    <see cref="Environment.SpecialFolder.LocalApplicationData"/>)
/// </remarks>
public static class LMSupplyCachePaths
{
    /// <summary>
    /// Environment variable that relocates the entire LMSupply non-HF artifact root.
    /// </summary>
    public const string RootEnvironmentVariable = "LMSUPPLY_CACHE_DIR";

    /// <summary>
    /// Gets the root directory for LMSupply non-HF artifacts.
    /// </summary>
    public static string GetRootDirectory()
    {
        var overridden = Environment.GetEnvironmentVariable(RootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
            return overridden;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LMSupply", "cache");
    }

    /// <summary>
    /// Gets the directory for downloaded ONNX runtime packages.
    /// </summary>
    public static string GetRuntimesDirectory()
        => Path.Combine(GetRootDirectory(), "runtimes");

    /// <summary>
    /// Gets the directory for downloaded llama-server builds.
    /// </summary>
    public static string GetLlamaServerDirectory()
        => Path.Combine(GetRootDirectory(), "llama-server");
}
