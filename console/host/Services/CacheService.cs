using LMSupply.Download;

namespace LMSupply.Console.Host.Services;

/// <summary>
/// HuggingFace cache management service (thin wrapper over CacheManager).
/// </summary>
public sealed partial class CacheService
{
    private readonly string _cacheDirectory;
    private readonly ILogger<CacheService> _logger;

    public CacheService(IConfiguration configuration, ILogger<CacheService> logger)
    {
        _cacheDirectory = configuration["ModelManager:CacheDirectory"]
            ?? CacheManager.GetDefaultCacheDirectory();
        _logger = logger;

        LogCacheDirectory(_logger, _cacheDirectory);
    }

    /// <summary>
    /// Cache directory path.
    /// </summary>
    public string CacheDirectory => _cacheDirectory;

    /// <summary>
    /// Gets all cached models.
    /// </summary>
    public IReadOnlyList<CachedModelInfo> GetCachedModels()
        => CacheManager.GetCachedModelsWithInfo(_cacheDirectory);

    /// <summary>
    /// Gets cached models by type (excludes incomplete models).
    /// </summary>
    public IReadOnlyList<CachedModelInfo> GetCachedModelsByType(ModelType type)
        => CacheManager.GetCachedModelsByType(_cacheDirectory, type);

    /// <summary>
    /// Deletes a cached model.
    /// </summary>
    /// <returns>True if the model was found and deleted, false otherwise.</returns>
    public bool DeleteModel(string repoId)
    {
        try
        {
            var deleted = CacheManager.DeleteModel(_cacheDirectory, repoId);
            if (deleted)
            {
                LogModelDeleted(_logger, repoId);
            }
            else
            {
                LogModelNotFoundForDeletion(_logger, repoId);
            }
            return deleted;
        }
        catch (Exception ex)
        {
            LogDeleteModelFailed(_logger, ex, repoId);
            return false;
        }
    }

    /// <summary>
    /// Gets total cache size.
    /// </summary>
    public long GetTotalCacheSize()
        => CacheManager.GetTotalCacheSize(_cacheDirectory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cache directory: {CacheDirectory}")]
    private static partial void LogCacheDirectory(ILogger logger, string cacheDirectory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted model: {RepoId}")]
    private static partial void LogModelDeleted(ILogger logger, string repoId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Model not found for deletion: {RepoId}")]
    private static partial void LogModelNotFoundForDeletion(ILogger logger, string repoId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to delete model: {RepoId}")]
    private static partial void LogDeleteModelFailed(ILogger logger, Exception exception, string repoId);
}
