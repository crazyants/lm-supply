namespace LMSupply.Llama.Server;

/// <summary>
/// Options for llama-server auto-update behavior.
/// </summary>
public sealed class LlamaServerUpdateOptions
{
    /// <summary>
    /// Default options instance.
    /// </summary>
    public static LlamaServerUpdateOptions Default { get; } = new();

    /// <summary>
    /// How often to check for new versions.
    /// Default: 24 hours.
    /// </summary>
    public TimeSpan VersionCheckInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Whether to automatically download updates in the background.
    /// Default: true.
    /// </summary>
    public bool AutoDownloadUpdates { get; set; } = true;

    /// <summary>
    /// Whether to apply updates during WarmupAsync.
    /// If true, WarmupAsync will block until updates are applied.
    /// Default: true.
    /// </summary>
    public bool UpdateOnWarmup { get; set; } = true;

    /// <summary>
    /// Whether to include prerelease versions.
    /// Default: false (only stable releases).
    /// </summary>
    public bool IncludePrerelease { get; set; }

    /// <summary>
    /// Maximum number of previous versions to keep for rollback.
    /// Default: 2.
    /// </summary>
    public int MaxVersionsToKeep { get; set; } = 2;

    /// <summary>
    /// Timeout for GitHub API requests.
    /// Default: 10 seconds.
    /// </summary>
    public TimeSpan ApiTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Whether to enable verbose logging.
    /// Default: false.
    /// </summary>
    public bool Verbose { get; set; }
}
