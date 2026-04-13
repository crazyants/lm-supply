using FluentAssertions;
using LMSupply.Generator.Internal.Llama;

namespace LMSupply.Generator.Tests;

public class GgufModelDownloaderTests
{
    [Fact]
    public void GenerateShardFilenames_ThreeShards_GeneratesCorrectNames()
    {
        var first = "Q4_K_M/Qwen3.5-122B-A10B-Q4_K_M-00001-of-00003.gguf";

        var result = GgufModelDownloader.GenerateShardFilenames(first, 3);

        result.Should().HaveCount(3);
        result[0].Should().Be("Q4_K_M/Qwen3.5-122B-A10B-Q4_K_M-00001-of-00003.gguf");
        result[1].Should().Be("Q4_K_M/Qwen3.5-122B-A10B-Q4_K_M-00002-of-00003.gguf");
        result[2].Should().Be("Q4_K_M/Qwen3.5-122B-A10B-Q4_K_M-00003-of-00003.gguf");
    }

    [Fact]
    public void GenerateShardFilenames_TwoShards_GeneratesCorrectNames()
    {
        var first = "model-Q4_K_M-00001-of-00002.gguf";

        var result = GgufModelDownloader.GenerateShardFilenames(first, 2);

        result.Should().HaveCount(2);
        result[0].Should().Be("model-Q4_K_M-00001-of-00002.gguf");
        result[1].Should().Be("model-Q4_K_M-00002-of-00002.gguf");
    }

    [Fact]
    public void GenerateShardFilenames_NonSplitFile_ReturnsSingle()
    {
        var filename = "model-Q4_K_M.gguf";

        var result = GgufModelDownloader.GenerateShardFilenames(filename, 1);

        result.Should().HaveCount(1);
        result[0].Should().Be(filename);
    }

    [Fact]
    public void GenerateShardFilenames_PreservesSubfolder()
    {
        var first = "subfolder/deep/model-00001-of-00005.gguf";

        var result = GgufModelDownloader.GenerateShardFilenames(first, 5);

        result.Should().HaveCount(5);
        result.Should().AllSatisfy(f => f.Should().StartWith("subfolder/deep/model-"));
        result[4].Should().Be("subfolder/deep/model-00005-of-00005.gguf");
    }
}
