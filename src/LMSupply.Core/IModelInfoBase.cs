namespace LMSupply;

/// <summary>
/// Base interface for model information across all LMSupply packages.
/// Defines common properties shared by all model types.
/// </summary>
public interface IModelInfoBase
{
    /// <summary>
    /// Gets the unique identifier for this model (typically HuggingFace repo ID).
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the user-friendly alias name for this model (e.g., "default", "fast", "quality").
    /// </summary>
    string AliasName { get; }

    /// <summary>
    /// Gets a human-readable description of the model.
    /// </summary>
    string? Description { get; }
}

/// <summary>
/// Represents an alias registration in a model registry.
/// </summary>
/// <param name="Name">The alias name (e.g., "default", "my-model").</param>
/// <param name="TargetModelId">The model ID this alias resolves to.</param>
/// <param name="Kind">Whether this is a system or user alias.</param>
public record AliasInfo(string Name, string TargetModelId, AliasKind Kind);

/// <summary>
/// Distinguishes system-defined aliases from user-defined aliases.
/// </summary>
public enum AliasKind
{
    /// <summary>Built-in alias defined by the package (e.g., "default", "fast").</summary>
    System,
    /// <summary>User-registered alias.</summary>
    User
}

/// <summary>
/// Interface for model registries that manage model configurations and aliases.
/// </summary>
/// <typeparam name="TModelInfo">The type of model information managed by this registry.</typeparam>
public interface IModelRegistry<TModelInfo> where TModelInfo : IModelInfoBase
{
    /// <summary>
    /// Resolves a model identifier to its full information. Throws ModelNotFoundException if not found.
    /// </summary>
    TModelInfo Resolve(string modelIdOrAlias);

    /// <summary>
    /// Tries to resolve a model identifier to its full information.
    /// </summary>
    bool TryResolve(string modelIdOrAlias, out TModelInfo? modelInfo);

    /// <summary>
    /// Registers a user-defined alias. Throws AliasConflictException if the name
    /// conflicts with a system alias, or AliasChainException if the target is a user alias.
    /// </summary>
    void RegisterAlias(string aliasName, string targetModelId);

    /// <summary>
    /// Removes a user-defined alias. Returns false if the alias doesn't exist.
    /// System aliases cannot be removed.
    /// </summary>
    bool RemoveAlias(string aliasName);

    /// <summary>
    /// Gets all registered aliases (system + user).
    /// </summary>
    IReadOnlyList<AliasInfo> GetAliases();

    /// <summary>
    /// Gets all registered model information (deduplicated).
    /// </summary>
    IReadOnlyList<TModelInfo> GetAvailableModels();
}
