using FluentAssertions;
using LMSupply.Hardware;
using LMSupply.Runtime;

namespace LMSupply.Core.Tests.Hardware;

public class VramBudgetTests
{
    private const long GB = 1024L * 1024 * 1024;

    [Fact]
    public void GetAvailableBytes_WithFreeMemory_ReturnsFreeMemoryWithSafetyMargin()
    {
        // Arrange: 16GB total, 12GB free → expect ~10.2GB (12 * 0.85)
        var gpu = new GpuInfo
        {
            Vendor = GpuVendor.Nvidia,
            TotalMemoryBytes = 16 * GB,
            FreeMemoryBytes = 12 * GB,
        };

        // Act
        var available = VramBudget.GetAvailableBytes(gpu);

        // Assert: 12GB * 0.85 = 10.2GB
        var expected = (long)(12 * GB * 0.85);
        available.Should().Be(expected);
    }

    [Fact]
    public void GetAvailableBytes_WithoutFreeMemory_UsesTotalWithSafetyMargin()
    {
        // Arrange: 8GB total, null free → expect ~6.8GB (8 * 0.85)
        var gpu = new GpuInfo
        {
            Vendor = GpuVendor.Intel,
            TotalMemoryBytes = 8 * GB,
            FreeMemoryBytes = null,
        };

        // Act
        var available = VramBudget.GetAvailableBytes(gpu);

        // Assert: 8GB * 0.85 = 6.8GB
        var expected = (long)(8 * GB * 0.85);
        available.Should().Be(expected);
    }

    [Fact]
    public void GetAvailableBytes_CpuOnly_ReturnsZero()
    {
        // Arrange: Unknown vendor, no memory info
        var gpu = new GpuInfo
        {
            Vendor = GpuVendor.Unknown,
            TotalMemoryBytes = null,
            FreeMemoryBytes = null,
        };

        // Act
        var available = VramBudget.GetAvailableBytes(gpu);

        // Assert
        available.Should().Be(0);
    }

    [Fact]
    public void GetAvailableBytes_CustomSafetyMargin()
    {
        // Arrange: 10GB free, 0.1 margin → expect ~9GB
        var gpu = new GpuInfo
        {
            Vendor = GpuVendor.Nvidia,
            TotalMemoryBytes = 16 * GB,
            FreeMemoryBytes = 10 * GB,
        };

        // Act
        var available = VramBudget.GetAvailableBytes(gpu, safetyMargin: 0.1);

        // Assert: 10GB * 0.9 = 9GB
        var expected = (long)(10 * GB * 0.9);
        available.Should().Be(expected);
    }

    [Fact]
    public void CanFitModel_ModelFits_ReturnsTrue()
    {
        // Arrange: 4GB model, 12GB free
        var gpu = new GpuInfo
        {
            Vendor = GpuVendor.Nvidia,
            TotalMemoryBytes = 16 * GB,
            FreeMemoryBytes = 12 * GB,
        };
        var modelSize = 4 * GB;

        // Act
        var result = VramBudget.CanFitModel(gpu, modelSize);

        // Assert: 12GB * 0.85 = 10.2GB > 4GB
        result.Should().BeTrue();
    }

    [Fact]
    public void CanFitModel_ModelTooLarge_ReturnsFalse()
    {
        // Arrange: 6GB model, 4GB free
        var gpu = new GpuInfo
        {
            Vendor = GpuVendor.Amd,
            TotalMemoryBytes = 8 * GB,
            FreeMemoryBytes = 4 * GB,
        };
        var modelSize = 6 * GB;

        // Act
        var result = VramBudget.CanFitModel(gpu, modelSize);

        // Assert: 4GB * 0.85 = 3.4GB < 6GB
        result.Should().BeFalse();
    }
}
