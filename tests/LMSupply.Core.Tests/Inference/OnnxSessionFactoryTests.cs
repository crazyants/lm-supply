using FluentAssertions;
using LMSupply.Exceptions;
using LMSupply.Inference;
using LMSupply.Runtime;

namespace LMSupply.Core.Tests.Inference;

/// <summary>
/// Tests for OnnxSessionFactory fallback chain behavior.
/// These tests verify that Auto mode correctly configures the GPU provider fallback chain.
/// Note: Tests that require native ONNX Runtime binaries are skipped in unit test environments.
/// </summary>
public class OnnxSessionFactoryTests
{
    [Fact]
    public async Task Auto_ShouldUseFallbackChainFromGpuInfo()
    {
        // Arrange
        await RuntimeManager.Instance.InitializeAsync();
        var gpu = RuntimeManager.Instance.Gpu;

        // Act
        var fallbackChain = gpu?.GetFallbackProviders();

        // Assert
        fallbackChain.Should().NotBeNull();
        fallbackChain.Should().Contain(ExecutionProvider.Cpu, "CPU should always be in fallback chain");
    }

    [Fact]
    public async Task GetFallbackProviders_OnNvidiaGpu_ShouldHaveCudaFirst()
    {
        // Arrange
        await RuntimeManager.Instance.InitializeAsync();
        var gpu = RuntimeManager.Instance.Gpu;

        // Skip if not NVIDIA GPU
        if (gpu?.Vendor != GpuVendor.Nvidia || gpu.CudaDriverVersionMajor < 11)
        {
            return; // Skip test - no NVIDIA GPU available
        }

        // Act
        var fallbackChain = gpu.GetFallbackProviders();

        // Assert
        fallbackChain.Should().NotBeNull();
        fallbackChain[0].Should().Be(ExecutionProvider.Cuda, "CUDA should be first for NVIDIA GPUs");
        fallbackChain[^1].Should().Be(ExecutionProvider.Cpu, "CPU should be last");
    }

    [Fact]
    public async Task GetFallbackProviders_OnWindows_ShouldIncludeDirectML()
    {
        // Arrange
        await RuntimeManager.Instance.InitializeAsync();
        var gpu = RuntimeManager.Instance.Gpu;

        // Skip if not Windows or no DirectML support
        if (!OperatingSystem.IsWindows() || gpu?.DirectMLSupported != true)
        {
            return; // Skip test
        }

        // Act
        var fallbackChain = gpu.GetFallbackProviders();

        // Assert
        fallbackChain.Should().Contain(ExecutionProvider.DirectML, "DirectML should be in fallback chain on Windows");
    }

    [Fact]
    public async Task GetFallbackProviders_CpuShouldAlwaysBeLast()
    {
        // Arrange
        await RuntimeManager.Instance.InitializeAsync();
        var gpu = RuntimeManager.Instance.Gpu;

        // Act
        var fallbackChain = gpu?.GetFallbackProviders() ?? new[] { ExecutionProvider.Cpu };

        // Assert
        fallbackChain[^1].Should().Be(ExecutionProvider.Cpu, "CPU should always be the final fallback");
    }

    [Fact]
    public async Task RuntimeManagerChain_ShouldMatchGpuInfoChain()
    {
        // Arrange
        await RuntimeManager.Instance.InitializeAsync();
        var gpu = RuntimeManager.Instance.Gpu;

        // Act
        var runtimeChain = RuntimeManager.Instance.GetProviderFallbackChain();
        var gpuChain = gpu?.GetFallbackProviders() ?? new[] { ExecutionProvider.Cpu };

        // Assert: Both chains should have the same providers (though different string/enum types)
        runtimeChain.Should().NotBeNull();
        runtimeChain.Should().Contain("cpu", "CPU should always be in chain");

        // If GPU has CUDA, RuntimeManager should have cuda11/cuda12
        if (gpuChain.Contains(ExecutionProvider.Cuda))
        {
            runtimeChain.Should().Match(chain =>
                chain.Contains("cuda11") || chain.Contains("cuda12"),
                "CUDA should be in RuntimeManager chain when GPU supports it");
        }

        // If GPU has DirectML, RuntimeManager should have directml
        if (gpuChain.Contains(ExecutionProvider.DirectML))
        {
            runtimeChain.Should().Contain("directml", "DirectML should be in RuntimeManager chain when GPU supports it");
        }
    }

    [Fact]
    public async Task Auto_OnNvidiaWithDirectML_ShouldHaveBothInChain()
    {
        // Arrange
        await RuntimeManager.Instance.InitializeAsync();
        var gpu = RuntimeManager.Instance.Gpu;

        // Skip if not NVIDIA on Windows
        if (gpu?.Vendor != GpuVendor.Nvidia || !gpu.DirectMLSupported)
        {
            return; // Skip test
        }

        // Act
        var fallbackChain = gpu.GetFallbackProviders().ToList();

        // Assert: NVIDIA Windows should have both CUDA and DirectML
        fallbackChain.Should().HaveCountGreaterThanOrEqualTo(3,
            "NVIDIA on Windows should have at least CUDA, DirectML, and CPU");

        // CUDA should come before DirectML
        var cudaIndex = fallbackChain.IndexOf(ExecutionProvider.Cuda);
        var directMLIndex = fallbackChain.IndexOf(ExecutionProvider.DirectML);
        cudaIndex.Should().BeLessThan(directMLIndex,
            "CUDA should be tried before DirectML on NVIDIA GPUs");
    }

    [Fact]
    public async Task FallbackChain_ShouldBeOrdered_GpuBeforeCpu()
    {
        // Arrange
        await RuntimeManager.Instance.InitializeAsync();
        var gpu = RuntimeManager.Instance.Gpu;

        if (gpu is null)
        {
            return; // Skip - no GPU detected
        }

        // Act
        var fallbackChain = gpu.GetFallbackProviders().ToList();
        var cpuIndex = fallbackChain.IndexOf(ExecutionProvider.Cpu);

        // Assert
        cpuIndex.Should().Be(fallbackChain.Count - 1, "CPU should be the last provider in the chain");

        // All GPU providers should come before CPU
        for (int i = 0; i < cpuIndex; i++)
        {
            fallbackChain[i].Should().NotBe(ExecutionProvider.Cpu, $"Position {i} should be a GPU provider");
        }
    }

    [Fact]
    public void CheckOnnxRuntimeAvailability_ShouldReturnWithoutCrash()
    {
        // The key invariant: this method should NEVER crash the process.
        // On dev machines with VC++ Redistributable, returns true.
        // On fresh machines without it, returns false with a helpful message.
        var (available, errorMessage) = OnnxSessionFactory.CheckOnnxRuntimeAvailability();

        if (available)
        {
            errorMessage.Should().BeNull();
        }
        else
        {
            errorMessage.Should().NotBeNullOrEmpty("should provide actionable guidance when unavailable");
        }
    }

    [Fact]
    public void CheckOnnxRuntimeAvailability_ShouldNotCrashProcess_EvenWhenUnavailable()
    {
        // The method should always return a tuple without crashing,
        // regardless of native library availability.
        var action = () => OnnxSessionFactory.CheckOnnxRuntimeAvailability();
        action.Should().NotThrow("pre-check must never crash the process");
    }

    [Fact]
    public async Task CreateWithInfoAsync_WithSkipProviders_ShouldAcceptOverload()
    {
        // The skipProviders overload exists so OnnxInferenceEngine can request a
        // re-creation that excludes a provider that crashed at inference time.
        // Verify the overload is callable with a non-empty skip list and reaches
        // its precheck (which fails on a missing model file as expected).
        var skip = new[] { ExecutionProvider.DirectML };

        Func<Task> action = async () => await OnnxSessionFactory.CreateWithInfoAsync(
            modelPath: "/nonexistent/model.onnx",
            provider: ExecutionProvider.Auto,
            skipProviders: skip);

        // Should fail at the precheck or session creation, not at signature resolution.
        var ex = await action.Should().ThrowAsync<Exception>();

        // Regression guard: the availability precheck must run AFTER RuntimeManager has had a
        // chance to provision the runtime (see CreateWithFallbackChainAsync's <remarks>), not
        // before. A "native library failed to load" message here would mean the precheck ran
        // first-thing again and rejected before ever attempting provisioning.
        ex.Which.Message.Should().NotContain("native library failed to load",
            "the fallback chain must provision the runtime before checking availability, not the reverse");
    }

    [Fact]
    public async Task CreateWithInfoAsync_WithNullSkipProviders_ShouldBeEquivalentToOriginalOverload()
    {
        // Null skipProviders must behave exactly like the original overload — no provider exclusion.
        Func<Task> action = async () => await OnnxSessionFactory.CreateWithInfoAsync(
            modelPath: "/nonexistent/model.onnx",
            provider: ExecutionProvider.Auto,
            skipProviders: null);

        await action.Should().ThrowAsync<Exception>();
    }

    [SkippableFact]
    public void ConfigureExecutionProvider_Cpu_ReturnsFalse_NoGpuEpAppended()
    {
        // D6: ConfigureExecutionProvider now reports whether a GPU EP was appended, so callers can
        // build accurate ActiveProviders instead of a loadability heuristic. CPU appends no GPU EP.
        var (available, _) = OnnxSessionFactory.CheckOnnxRuntimeAvailability();
        Skip.IfNot(available, "ONNX Runtime not available in this environment");

        using var options = new Microsoft.ML.OnnxRuntime.SessionOptions();
        OnnxSessionFactory.ConfigureExecutionProvider(options, ExecutionProvider.Cpu)
            .Should().BeFalse("CPU configuration appends no GPU execution provider");
    }

    [SkippableFact]
    public async Task CreateWithInfoAsync_ExplicitGpuProvider_WhenSessionCreateThrows_FallsBackToCpu()
    {
        // Skip if ONNX Runtime native library is not available (CI without runtime installed).
        var (available, _) = OnnxSessionFactory.CheckOnnxRuntimeAvailability();
        Skip.IfNot(available, "ONNX Runtime not available in this environment");

        // Simulate a session-creation failure on the first attempt (DML) by injecting a
        // configureOptions callback that throws. The factory should catch this and retry
        // with CPU. The CPU attempt then fails on the nonexistent model file, which is the
        // expected final exception (not the simulated DML error).
        var attemptCount = 0;
        Action<Microsoft.ML.OnnxRuntime.SessionOptions> failOnFirstAttempt = _ =>
        {
            if (Interlocked.Increment(ref attemptCount) == 1)
                throw new InvalidOperationException("Simulated DirectML init failure");
        };

        Func<Task> action = async () => await OnnxSessionFactory.CreateWithInfoAsync(
            "nonexistent_model_for_fallback_test.onnx",
            ExecutionProvider.DirectML,
            failOnFirstAttempt);

        // Before the fix: InvalidOperationException propagates directly (no CPU fallback).
        // After the fix: InvalidOperationException is caught; CPU fallback is attempted;
        //   the final exception is from the CPU session attempt (model file not found).
        var ex = await Assert.ThrowsAnyAsync<Exception>(action);
        ex.Should().NotBeOfType<InvalidOperationException>(
            "the simulated GPU failure should be caught and CPU fallback should be attempted");
        attemptCount.Should().Be(2, "configureOptions should have been called twice: once for GPU, once for CPU fallback");
    }
}
