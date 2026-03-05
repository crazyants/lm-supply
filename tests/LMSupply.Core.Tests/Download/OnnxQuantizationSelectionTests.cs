using FluentAssertions;
using LMSupply.Core.Download;
using LMSupply.Hardware;

namespace LMSupply.Core.Tests.Download;

public class OnnxQuantizationSelectionTests
{
    [Fact]
    public void SelectBestQuantizationVariants_Quant4Priority_SelectsInt4File()
    {
        var candidates = new List<RepoFile>
        {
            new() { Path = "onnx/model.onnx", Type = "file", Size = 4_000_000_000L },
            new() { Path = "onnx/model_int8.onnx", Type = "file", Size = 1_000_000_000L },
            new() { Path = "onnx/model_int4.onnx", Type = "file", Size = 500_000_000L },
        };

        var prefs = ModelPreferences.ForTier(PerformanceTier.Low);  // Quant4 우선
        var selected = ModelDiscoveryService.SelectBestVariantsForTest(candidates, prefs);

        selected.Should().Contain("onnx/model_int4.onnx");
        selected.Should().NotContain("onnx/model.onnx");
    }

    [Fact]
    public void SelectBestQuantizationVariants_DefaultPriority_SelectsFp32()
    {
        var candidates = new List<RepoFile>
        {
            new() { Path = "onnx/model.onnx", Type = "file", Size = 4_000_000_000L },
            new() { Path = "onnx/model_int4.onnx", Type = "file", Size = 500_000_000L },
        };

        var prefs = ModelPreferences.ForTier(PerformanceTier.Ultra);  // Default 우선
        var selected = ModelDiscoveryService.SelectBestVariantsForTest(candidates, prefs);

        selected.Should().Contain("onnx/model.onnx");
    }

    [Fact]
    public void SelectBestQuantizationVariants_UnknownQuantType_FallsBackToSize()
    {
        // model_uint8.onnx  → GetBaseModelName strips "_uint8" → base: "model"
        // model_q4.onnx     → GetBaseModelName strips "_q4"    → base: "model"
        // Both land in the same group but neither matches _int8 / _int4 / _fp16 checks,
        // and HasQuantizationSuffix returns true for both so Default check also fails.
        // → bestVariant stays null → size-based fallback triggers (MaxBy for High tier).
        var candidates = new List<RepoFile>
        {
            new() { Path = "onnx/model_uint8.onnx", Type = "file", Size = 800_000_000L },
            new() { Path = "onnx/model_q4.onnx",   Type = "file", Size = 2_000_000_000L },
        };

        var prefs = ModelPreferences.ForTier(PerformanceTier.High); // PreferLowMemory = false
        var selected = ModelDiscoveryService.SelectBestVariantsForTest(candidates, prefs);

        // 이름 매칭 없음 → 크기 큰 것 선택 (MaxBy)
        selected.Should().HaveCount(1);
        selected.Should().Contain("onnx/model_q4.onnx");
        selected.Should().NotContain("onnx/model_uint8.onnx");
    }

    [Fact]
    public void SelectBestQuantizationVariants_UnknownQuantType_PreferLowMemory_FallsBackToSmallest()
    {
        // Same grouping logic as above but with PreferLowMemory = true (Low tier).
        // Size-based fallback should pick the smallest file (MinBy).
        var candidates = new List<RepoFile>
        {
            new() { Path = "onnx/model_uint8.onnx", Type = "file", Size = 800_000_000L },
            new() { Path = "onnx/model_q4.onnx",   Type = "file", Size = 2_000_000_000L },
        };

        var prefs = ModelPreferences.ForTier(PerformanceTier.Low); // PreferLowMemory = true
        var selected = ModelDiscoveryService.SelectBestVariantsForTest(candidates, prefs);

        // 이름 매칭 없음 → 크기 작은 것 선택 (MinBy)
        selected.Should().HaveCount(1);
        selected.Should().Contain("onnx/model_uint8.onnx");
        selected.Should().NotContain("onnx/model_q4.onnx");
    }

    [Fact]
    public void SelectBestQuantizationVariants_SizeZeroFiles_IgnoredInFallback()
    {
        // When some files have Size=0 (unknown), they should be excluded from size-based
        // fallback so MaxBy/MinBy doesn't incorrectly select a 0-byte file.
        var candidates = new List<RepoFile>
        {
            new() { Path = "onnx/model_uint8.onnx", Type = "file", Size = 0L },           // unknown size
            new() { Path = "onnx/model_q4.onnx",   Type = "file", Size = 2_000_000_000L },
        };

        var prefs = ModelPreferences.ForTier(PerformanceTier.High); // PreferLowMemory = false
        var selected = ModelDiscoveryService.SelectBestVariantsForTest(candidates, prefs);

        // Size=0 파일은 제외 → 크기 있는 파일 중 MaxBy → model_q4.onnx
        selected.Should().HaveCount(1);
        selected.Should().Contain("onnx/model_q4.onnx");
        selected.Should().NotContain("onnx/model_uint8.onnx");
    }
}
