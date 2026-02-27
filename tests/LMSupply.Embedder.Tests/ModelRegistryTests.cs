using FluentAssertions;
using LMSupply.Embedder.Utils;
using LMSupply.Exceptions;

namespace LMSupply.Embedder.Tests;

public class ModelRegistryTests
{
    private readonly EmbedderModelRegistry _registry = EmbedderModelRegistry.Default;

    [Fact]
    public void TryResolve_ReturnsTrue_ForKnownShortName()
    {
        var result = _registry.TryResolve("all-MiniLM-L6-v2", out var info);

        result.Should().BeTrue();
        info.Should().NotBeNull();
        info!.RepoId.Should().Be("sentence-transformers/all-MiniLM-L6-v2");
        info.Dimensions.Should().Be(384);
    }

    [Fact]
    public void TryResolve_ReturnsFalse_ForUnknownModel()
    {
        var result = _registry.TryResolve("unknown-model", out var info);

        result.Should().BeFalse();
        info.Should().BeNull();
    }

    [Fact]
    public void TryResolve_IsCaseInsensitive()
    {
        var result = _registry.TryResolve("ALL-MINILM-L6-V2", out var info);

        result.Should().BeTrue();
        info.Should().NotBeNull();
    }

    [Fact]
    public void GetAliases_ReturnsNonEmptyList()
    {
        var aliases = _registry.GetAliases();

        aliases.Should().NotBeEmpty();
        aliases.Select(a => a.Name).Should().Contain("default");
        aliases.Select(a => a.Name).Should().Contain("fast");
        aliases.Select(a => a.Name).Should().Contain("auto");
    }

    [Theory]
    [InlineData("all-MiniLM-L6-v2", 384, PoolingMode.Mean)]
    [InlineData("all-mpnet-base-v2", 768, PoolingMode.Mean)]
    [InlineData("bge-small-en-v1.5", 384, PoolingMode.Cls)]
    [InlineData("multilingual-e5-small", 384, PoolingMode.Mean)]
    public void KnownModels_HaveCorrectConfiguration(string modelId, int dimensions, PoolingMode pooling)
    {
        _registry.TryResolve(modelId, out var info);

        info.Should().NotBeNull();
        info!.Dimensions.Should().Be(dimensions);
        info.PoolingMode.Should().Be(pooling);
    }

    [Fact]
    public void DefaultAlias_PointsToBgeSmall()
    {
        _registry.TryResolve("default", out var info);

        info.Should().NotBeNull();
        info!.RepoId.Should().Be("BAAI/bge-small-en-v1.5");
    }

    [Fact]
    public void FastAlias_PointsToMiniLM()
    {
        _registry.TryResolve("fast", out var info);

        info.Should().NotBeNull();
        info!.RepoId.Should().Be("sentence-transformers/all-MiniLM-L6-v2");
    }

    [Fact]
    public void QualityAlias_PointsToGteBase()
    {
        _registry.TryResolve("quality", out var info);

        info.Should().NotBeNull();
        info!.RepoId.Should().Be("Alibaba-NLP/gte-base-en-v1.5");
    }

    [Fact]
    public void AutoAlias_ResolvesToModel()
    {
        var result = _registry.TryResolve("auto", out var info);

        result.Should().BeTrue();
        info.Should().NotBeNull();
        info!.RepoId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Resolve_ThrowsForUnknownModel()
    {
        var act = () => _registry.Resolve("completely-nonexistent-xyz");

        act.Should().Throw<ModelNotFoundException>();
    }

    [Fact]
    public void GetAvailableModels_ReturnsDeduplicatedModels()
    {
        var models = _registry.GetAvailableModels();

        models.Should().NotBeEmpty();
        // All models should have distinct RepoIds
        models.Select(m => m.RepoId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void FullRepoId_ResolvesCorrectly()
    {
        var result = _registry.TryResolve("BAAI/bge-small-en-v1.5", out var info);

        result.Should().BeTrue();
        info.Should().NotBeNull();
        info!.RepoId.Should().Be("BAAI/bge-small-en-v1.5");
    }

    [Fact]
    public void RegisterAlias_WorksForUserAlias()
    {
        var registry = new EmbedderModelRegistry(DefaultModels.All);
        registry.RegisterAlias("my-embed", "BAAI/bge-small-en-v1.5");

        var result = registry.TryResolve("my-embed", out var info);

        result.Should().BeTrue();
        info.Should().NotBeNull();
        info!.RepoId.Should().Be("BAAI/bge-small-en-v1.5");
    }

    [Fact]
    public void RegisterAlias_ThrowsForSystemAlias()
    {
        var registry = new EmbedderModelRegistry(DefaultModels.All);

        var act = () => registry.RegisterAlias("default", "some-model");

        act.Should().Throw<AliasConflictException>();
    }

    [Fact]
    public void RemoveAlias_WorksForUserAlias()
    {
        var registry = new EmbedderModelRegistry(DefaultModels.All);
        registry.RegisterAlias("my-embed", "BAAI/bge-small-en-v1.5");

        registry.RemoveAlias("my-embed").Should().BeTrue();
        registry.TryResolve("my-embed", out _).Should().BeFalse();
    }

    [Fact]
    public void RemoveAlias_ReturnsFalse_ForSystemAlias()
    {
        var registry = new EmbedderModelRegistry(DefaultModels.All);

        registry.RemoveAlias("default").Should().BeFalse();
    }
}
