using System.Diagnostics;
using FluentAssertions;
using LMSupply.Generator.Internal.Llama;
using Xunit;

namespace LMSupply.Generator.Tests.Internal.Llama;

/// <summary>
/// Tests for the severity-aware Trace emission helper that runs at GPU layer decision time.
/// Verifies the regression fix for B-3 (silent CPU-only fallback): full CPU fallback emits
/// TraceWarning, partial offload stays at TraceInformation.
/// </summary>
public class LlamaOffloadTraceHelperTests
{
    [Fact]
    public void TraceOffloadDecision_ZeroLayers_EmitsWarning()
    {
        var listener = new CapturingTraceListener();
        Trace.Listeners.Add(listener);
        try
        {
            var estimate = new ResourceEstimate
            {
                EstimatedVramBytes = 0,
                EstimatedRamBytes = 22L * 1024 * 1024 * 1024,
                RecommendedGpuLayers = 0,
                TotalLayers = 32,
                CanFitInVram = false,
                CanFitInRam = true,
            };

            LlamaOffloadTraceHelper.TraceOffloadDecision(
                estimate,
                freeVramBytes: 512L * 1024 * 1024,
                totalVramBytes: 24L * 1024 * 1024 * 1024);

            listener.Warnings.Should().ContainSingle(w =>
                w.Contains("CPU-only fallback") &&
                w.Contains("0/32") &&
                w.Contains("LMSUPPLY_VRAM_BUDGET_MB"),
                "0 GPU layers must emit TraceWarning with VRAM figures and override hint");
            listener.Information.Should().BeEmpty(
                "the 0-layer path must NOT also emit TraceInformation");
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    [Fact]
    public void TraceOffloadDecision_PartialOffload_EmitsInformation()
    {
        var listener = new CapturingTraceListener();
        Trace.Listeners.Add(listener);
        try
        {
            var estimate = new ResourceEstimate
            {
                EstimatedVramBytes = 12L * 1024 * 1024 * 1024,
                EstimatedRamBytes = 10L * 1024 * 1024 * 1024,
                RecommendedGpuLayers = 18,
                TotalLayers = 32,
                CanFitInVram = false,
                CanFitInRam = true,
            };

            LlamaOffloadTraceHelper.TraceOffloadDecision(
                estimate,
                freeVramBytes: 13L * 1024 * 1024 * 1024,
                totalVramBytes: 24L * 1024 * 1024 * 1024);

            listener.Information.Should().ContainSingle(s =>
                s.Contains("Auto partial offload") && s.Contains("18/32"),
                "partial offload must emit TraceInformation");
            listener.Warnings.Should().BeEmpty(
                "partial offload must NOT trigger Warning severity");
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    [Fact]
    public void TraceOffloadDecision_ZeroLayers_MessageIsAsciiOnly()
    {
        var listener = new CapturingTraceListener();
        Trace.Listeners.Add(listener);
        try
        {
            var estimate = new ResourceEstimate
            {
                EstimatedVramBytes = 0,
                EstimatedRamBytes = 8L * 1024 * 1024 * 1024,
                RecommendedGpuLayers = 0,
                TotalLayers = 24,
                CanFitInVram = false,
                CanFitInRam = true,
            };

            LlamaOffloadTraceHelper.TraceOffloadDecision(
                estimate, freeVramBytes: 0, totalVramBytes: 8L * 1024 * 1024 * 1024);

            var warning = listener.Warnings.Should().ContainSingle().Subject;
            // Logging convention: operational logs must be ASCII (English) only.
            warning.Should().MatchRegex("^[\\x00-\\x7F]+$",
                "operational log messages must be ASCII per FluxIndex/LMSupply Logging convention");
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    private sealed class CapturingTraceListener : TraceListener
    {
        public List<string> Warnings { get; } = [];
        public List<string> Information { get; } = [];

        public override void TraceEvent(TraceEventCache? cache, string source, TraceEventType eventType, int id, string? message)
        {
            if (message == null) return;
            switch (eventType)
            {
                case TraceEventType.Warning:
                    Warnings.Add(message);
                    break;
                case TraceEventType.Information:
                    Information.Add(message);
                    break;
            }
        }

        public override void Write(string? message) { /* unused */ }
        public override void WriteLine(string? message) { /* unused */ }
    }
}
