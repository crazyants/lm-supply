using FluentAssertions;
using LMSupply.Core.Download;
using Xunit;

namespace LMSupply.Core.Tests.Download;

public class GgufFileSelectorTests
{
    // ─── 시나리오별 AvailableMemory ───

    // 4GB VRAM + 16GB RAM (Medium tier)
    private static readonly AvailableMemory MediumHardware = new(
        VramBytes: 4L * 1024 * 1024 * 1024,
        RamBytes: 16L * 1024 * 1024 * 1024);

    // 16GB VRAM + 64GB RAM (Ultra tier)
    private static readonly AvailableMemory UltraHardware = new(
        VramBytes: 16L * 1024 * 1024 * 1024,
        RamBytes: 64L * 1024 * 1024 * 1024);

    // CPU Only + 8GB RAM (Low tier)
    private static readonly AvailableMemory LowHardware = new(
        VramBytes: 0,
        RamBytes: 8L * 1024 * 1024 * 1024);

    // ─── AvailableMemory 계산 ───

    [Fact]
    public void AvailableMemory_UsableVramBytes_DeductsOverhead()
    {
        var memory = new AvailableMemory(VramBytes: 8L * 1024 * 1024 * 1024, RamBytes: 0);
        // 2GB GPU overhead 차감 → 6GB
        memory.UsableVramBytes.Should().Be(6L * 1024 * 1024 * 1024);
    }

    [Fact]
    public void AvailableMemory_UsableRamBytes_DeductsOverhead()
    {
        var memory = new AvailableMemory(VramBytes: 0, RamBytes: 8L * 1024 * 1024 * 1024);
        // 4GB system overhead 차감 → 4GB
        memory.UsableRamBytes.Should().Be(4L * 1024 * 1024 * 1024);
    }

    [Fact]
    public void AvailableMemory_VramZero_UsableVramIsZero()
    {
        var memory = new AvailableMemory(VramBytes: 0, RamBytes: 16L * 1024 * 1024 * 1024);
        memory.UsableVramBytes.Should().Be(0);
    }

    [Fact]
    public void AvailableMemory_FitsInGpu_TrueWhenSmallEnough()
    {
        // 파일 2GB, VRAM 4GB (usable 2GB) → 2GB × 1.1 = 2.2GB > 2GB usable → false
        var memory = new AvailableMemory(
            VramBytes: 4L * 1024 * 1024 * 1024,
            RamBytes: 16L * 1024 * 1024 * 1024);
        var fileSize = 1L * 1024 * 1024 * 1024; // 1GB → 1.1GB with overhead < 2GB usable
        memory.FitsInGpu(fileSize).Should().BeTrue();
    }

    [Fact]
    public void AvailableMemory_FitsInGpu_FalseWhenNoVram()
    {
        LowHardware.FitsInGpu(1L * 1024 * 1024 * 1024).Should().BeFalse();
    }

    // ─── 기본 선택: 메모리 안에서 가장 큰 파일 ───

    [Fact]
    public void Select_PrefersBiggerFileWithinMemory()
    {
        // 3GB, 5GB, 8GB 파일 / VRAM 16GB (usable 14GB)
        var groups = new[]
        {
            MakeGroup("model-Q4_K_M.gguf", 3L * 1024 * 1024 * 1024),
            MakeGroup("model-Q6_K.gguf",   5L * 1024 * 1024 * 1024),
            MakeGroup("model-Q8_0.gguf",   8L * 1024 * 1024 * 1024),
        };

        var result = GgufFileSelector.Select(groups, UltraHardware);

        result.PrimaryFileName.Should().Be("model-Q8_0.gguf");
    }

    [Fact]
    public void Select_FiltersOutTooLargeFiles()
    {
        // 3GB와 8GB 파일 / VRAM 4GB (usable 2GB), RAM 16GB (usable 12GB), total usable ~14GB
        // 8GB × 1.1 = 8.8GB → total usable 14GB보다 작으므로 8GB도 통과!
        // 이 경우 더 큰 8GB 선택
        var groups = new[]
        {
            MakeGroup("model-Q4_K_M.gguf", 3L * 1024 * 1024 * 1024),
            MakeGroup("model-Q8_0.gguf",   8L * 1024 * 1024 * 1024),
        };

        var result = GgufFileSelector.Select(groups, MediumHardware);

        // MediumHardware: VRAM 4GB(usable 2GB) + RAM 16GB(usable 12GB) = total 14GB
        // 8GB × 1.1 = 8.8GB ≤ 14GB → 통과, 3GB × 1.1 = 3.3GB ≤ 14GB → 통과
        // 8GB가 더 크므로 선택
        result.PrimaryFileName.Should().Be("model-Q8_0.gguf");
    }

    [Fact]
    public void Select_FiltersOutFilesTooLargeForTotalMemory()
    {
        // 파일이 너무 커서 VRAM+RAM 합산으로도 불가
        var groups = new[]
        {
            MakeGroup("model-Q4_K_M.gguf", 2L * 1024 * 1024 * 1024),
            MakeGroup("model-huge.gguf",   20L * 1024 * 1024 * 1024),  // 20GB
        };

        // LowHardware: VRAM 0 + RAM 8GB(usable 4GB) = total 4GB
        // 2GB × 1.1 = 2.2GB ≤ 4GB → 통과
        // 20GB × 1.1 = 22GB > 4GB → 필터 아웃
        var result = GgufFileSelector.Select(groups, LowHardware);

        result.PrimaryFileName.Should().Be("model-Q4_K_M.gguf");
    }

    // ─── 분할 파일: 합산 크기로 메모리 체크 ───

    [Fact]
    public void Select_SplitFile_UsesTotalSizeForMemoryCheck()
    {
        // 분할 파일: 각 파트 2.1GB × 3 = 6.3GB
        var splitGroup = new GgufFileGroup
        {
            PrimaryFileName = "model-Q4_K_M-00001-of-00003.gguf",
            Parts = ["model-Q4_K_M-00001-of-00003.gguf",
                     "model-Q4_K_M-00002-of-00003.gguf",
                     "model-Q4_K_M-00003-of-00003.gguf"],
            TotalSizeBytes = 6_300_000_000L
        };
        // 단일 파일 2.5GB
        var smallGroup = MakeGroup("model-Q3_K_M.gguf", 2_500_000_000L);

        // LowHardware: total usable 4GB
        // 6.3GB × 1.1 = 6.93GB > 4GB → 필터 아웃
        // 2.5GB × 1.1 = 2.75GB ≤ 4GB → 통과
        var result = GgufFileSelector.Select([splitGroup, smallGroup], LowHardware);

        result.PrimaryFileName.Should().Be("model-Q3_K_M.gguf");
    }

    // ─── 사용자 지정 양자화 ───

    [Fact]
    public void Select_PreferredQuantization_UsedWhenFits()
    {
        var groups = new[]
        {
            MakeGroup("model-Q4_K_M.gguf", 3L * 1024 * 1024 * 1024),
            MakeGroup("model-Q6_K.gguf",   5L * 1024 * 1024 * 1024),
            MakeGroup("model-Q8_0.gguf",   8L * 1024 * 1024 * 1024),
        };

        // Ultra 하드웨어, Q6_K 선호 → Q6_K 선택 (Q8_0이 더 크지만 사용자 선택 우선)
        var result = GgufFileSelector.Select(groups, UltraHardware, preferredQuantization: "Q6_K");

        result.PrimaryFileName.Should().Be("model-Q6_K.gguf");
    }

    [Fact]
    public void Select_PreferredQuantization_TooLarge_FallsBackToAuto()
    {
        var groups = new[]
        {
            MakeGroup("model-Q4_K_M.gguf", 2L * 1024 * 1024 * 1024),
            MakeGroup("model-Q8_0.gguf",   50L * 1024 * 1024 * 1024),  // 50GB, 너무 큼
        };

        // LowHardware: total usable 4GB
        // Q8_0 선호하지만 50GB × 1.1 > 4GB → 필터 아웃
        // Q4_K_M (2GB) 자동 선택
        var result = GgufFileSelector.Select(groups, LowHardware, preferredQuantization: "Q8_0");

        result.PrimaryFileName.Should().Be("model-Q4_K_M.gguf");
    }

    [Fact]
    public void Select_PreferredQuantization_NullOrEmpty_UsesAutoSelection()
    {
        var groups = new[]
        {
            MakeGroup("model-Q4_K_M.gguf", 3L * 1024 * 1024 * 1024),
            MakeGroup("model-Q8_0.gguf",   8L * 1024 * 1024 * 1024),
        };

        var resultNull = GgufFileSelector.Select(groups, UltraHardware, preferredQuantization: null);
        var resultEmpty = GgufFileSelector.Select(groups, UltraHardware, preferredQuantization: "");

        resultNull.PrimaryFileName.Should().Be("model-Q8_0.gguf");
        resultEmpty.PrimaryFileName.Should().Be("model-Q8_0.gguf");
    }

    // ─── 양자화 이름 매칭 ───

    [Theory]
    [InlineData("model-Q4_K_M.gguf", "Q4_K_M", true)]
    [InlineData("model-Q4_K_M-imat.gguf", "Q4_K_M", true)]       // iMatrix 변형
    [InlineData("Meta-Llama-3-8B.Q4_K_M.gguf", "Q4_K_M", true)]  // 점 구분자
    [InlineData("model_Q4_K_M.gguf", "Q4_K_M", true)]             // 밑줄 구분자
    [InlineData("model-Q8_0.gguf", "Q4_K_M", false)]
    [InlineData("model-IQ4_XS.gguf", "IQ4_XS", true)]             // 신규 타입
    [InlineData("model-BF16.gguf", "BF16", true)]                  // BF16
    public void MatchesQuantization_WorksForVariousFormats(string filename, string quant, bool expected)
    {
        GgufFileSelector.MatchesQuantization(filename, quant).Should().Be(expected);
    }

    // ─── 예외 케이스 ───

    [Fact]
    public void Select_NothingFits_ThrowsWithDetails()
    {
        var groups = new[]
        {
            MakeGroup("model-Q4_K_M.gguf", 50L * 1024 * 1024 * 1024),  // 50GB
        };

        var act = () => GgufFileSelector.Select(groups, LowHardware);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*available*");
    }

    [Fact]
    public void Select_EmptyList_ThrowsArgumentException()
    {
        var act = () => GgufFileSelector.Select([], MediumHardware);
        act.Should().Throw<ArgumentException>();
    }

    // ─── FromHardwareProfile ───

    [Fact]
    public void FromHardwareProfile_CreatesCorrectMemory()
    {
        var profile = LMSupply.Hardware.HardwareProfile.Current;
        var memory = GgufFileSelector.FromHardwareProfile(profile);

        memory.Should().NotBeNull();
        memory.VramBytes.Should().BeGreaterThanOrEqualTo(0);
        memory.RamBytes.Should().BeGreaterThan(0);
    }

    // ─── 헬퍼 ───

    private static GgufFileGroup MakeGroup(string filename, long sizeBytes) =>
        new() { PrimaryFileName = filename, Parts = [filename], TotalSizeBytes = sizeBytes };
}
