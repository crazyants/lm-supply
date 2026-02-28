using System.Diagnostics;
using LMSupply.Exceptions;

namespace LMSupply;

/// <summary>
/// Abstract base class for all domain model registries.
/// Provides unified resolution for system aliases, user aliases, model IDs, and fallbacks.
/// </summary>
/// <remarks>
/// Resolution order: auto -> user alias -> system alias -> full ID -> short name -> local path -> HF fallback.
/// </remarks>
public abstract class ModelRegistryBase<TModelInfo> : IModelRegistry<TModelInfo>
    where TModelInfo : IModelInfoBase
{
    private readonly Dictionary<string, TModelInfo> _systemAliases
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string> _userAliases
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, TModelInfo> _modelsById
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, TModelInfo> _modelsByShortName
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<TModelInfo> _allModels = [];

    protected ModelRegistryBase(IEnumerable<TModelInfo> systemModels)
    {
        foreach (var model in systemModels)
        {
            _systemAliases[model.AliasName] = model;

            if (!_modelsById.ContainsKey(model.Id))
                _modelsById[model.Id] = model;

            var shortName = model.Id.Contains('/')
                ? model.Id.Split('/').Last()
                : model.Id;
            if (!_modelsByShortName.ContainsKey(shortName))
                _modelsByShortName[shortName] = model;

            if (!_allModels.Any(m => m.Id.Equals(model.Id, StringComparison.OrdinalIgnoreCase)))
                _allModels.Add(model);
        }
    }

    /// <inheritdoc />
    public TModelInfo Resolve(string modelIdOrAlias)
    {
        if (TryResolve(modelIdOrAlias, out var modelInfo))
            return modelInfo!;

        throw new ModelNotFoundException(
            $"Model '{modelIdOrAlias}' not found in registry.", modelIdOrAlias);
    }

    /// <inheritdoc />
    public bool TryResolve(string modelIdOrAlias, out TModelInfo? modelInfo)
    {
        modelInfo = default;

        if (string.IsNullOrWhiteSpace(modelIdOrAlias))
            return false;

        // 1. "auto"
        if (modelIdOrAlias.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            modelInfo = GetAutoModel();
            return true;
        }

        // 2. User alias -> resolve target (no re-entry to user aliases)
        if (_userAliases.TryGetValue(modelIdOrAlias, out var targetId))
        {
            return TryResolveInternal(targetId, out modelInfo);
        }

        // 3-7. Standard resolution
        return TryResolveInternal(modelIdOrAlias, out modelInfo);
    }

    private bool TryResolveInternal(string modelIdOrAlias, out TModelInfo? modelInfo)
    {
        // 3. System alias
        if (_systemAliases.TryGetValue(modelIdOrAlias, out modelInfo!))
            return true;

        // 4. Full model ID
        if (_modelsById.TryGetValue(modelIdOrAlias, out modelInfo!))
            return true;

        // 5. Short name
        if (_modelsByShortName.TryGetValue(modelIdOrAlias, out modelInfo!))
            return true;

        // 6. Local path
        if (IsLocalPath(modelIdOrAlias))
        {
            modelInfo = CreateFallbackModelInfo(modelIdOrAlias);
            return true;
        }

        // 7. HuggingFace repo pattern
        if (modelIdOrAlias.Contains('/'))
        {
            modelInfo = CreateFallbackModelInfo(modelIdOrAlias);
            return true;
        }

        modelInfo = default;
        return false;
    }

    /// <inheritdoc />
    public void RegisterAlias(string aliasName, string targetModelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aliasName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetModelId);

        if (_systemAliases.ContainsKey(aliasName)
            || aliasName.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            throw new AliasConflictException(aliasName);
        }

        if (_userAliases.ContainsKey(targetModelId))
        {
            throw new AliasChainException(aliasName, targetModelId);
        }

        _userAliases[aliasName] = targetModelId;
        Trace.TraceInformation($"[ModelRegistry] Registered user alias '{aliasName}' -> '{targetModelId}'");
    }

    /// <inheritdoc />
    public bool RemoveAlias(string aliasName)
    {
        if (string.IsNullOrWhiteSpace(aliasName))
            return false;

        if (_systemAliases.ContainsKey(aliasName))
            return false;

        return _userAliases.Remove(aliasName);
    }

    /// <inheritdoc />
    public IReadOnlyList<AliasInfo> GetAliases()
    {
        var result = new List<AliasInfo>();
        result.Add(new AliasInfo("auto", "(hardware-adaptive)", AliasKind.System));

        foreach (var (name, model) in _systemAliases)
            result.Add(new AliasInfo(name, model.Id, AliasKind.System));

        foreach (var (name, targetId) in _userAliases)
            result.Add(new AliasInfo(name, targetId, AliasKind.User));

        return result;
    }

    /// <inheritdoc />
    public IReadOnlyList<TModelInfo> GetAvailableModels() => _allModels.AsReadOnly();

    /// <summary>
    /// Returns the model info for the "auto" alias, which selects a model based on hardware capabilities.
    /// </summary>
    protected abstract TModelInfo GetAutoModel();

    /// <summary>
    /// Creates a fallback model info for an unknown model ID (e.g., a HuggingFace repo or local path).
    /// </summary>
    protected abstract TModelInfo CreateFallbackModelInfo(string modelId);

    private static bool IsLocalPath(string value)
    {
        return value.Contains('\\')
            || value.Contains(":/")
            || value.Contains(":\\")
            || value.StartsWith('.')
            || value.StartsWith('/');
    }
}
