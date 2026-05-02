using FluentAssertions;
using LMSupply.Hardware;

namespace LMSupply.Llama.Tests;

public class LlamaServerConfigArgsTests
{
    [Fact]
    public void KvCacheQuantizationType_HasAutoValue()
    {
        ((int)KvCacheQuantizationType.Auto).Should().Be(-1);
    }
}
