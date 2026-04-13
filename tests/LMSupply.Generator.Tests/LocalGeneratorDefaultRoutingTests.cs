using System.Diagnostics;
using FluentAssertions;

namespace LMSupply.Generator.Tests;

/// <summary>
/// Verifies that LocalGenerator.LoadAsync("default") delegates to the
/// auto hardware-aware selection path. We detect the delegation by
/// listening for the "[LocalGenerator.auto]" Trace line emitted by
/// LoadAutoAsync, without actually downloading any model weights.
/// </summary>
[Collection("TraceListeners")]
public class LocalGeneratorDefaultRoutingTests
{
    private sealed class CapturingListener : TraceListener
    {
        private readonly List<string> _lines = new();
        public IReadOnlyList<string> Lines => _lines;

        public override void Write(string? message) { if (message != null) _lines.Add(message); }
        public override void WriteLine(string? message) { if (message != null) _lines.Add(message); }
    }

    private static async Task<IReadOnlyList<string>> CaptureTraceForLoadAsync(string modelId)
    {
        var listener = new CapturingListener();
        Trace.Listeners.Add(listener);
        try
        {
            try
            {
                // Pre-canceled token: LogAutoSelection is synchronous and fires
                // before any cancellation check in the downstream loader, so the
                // Trace line is always captured deterministically. The downstream
                // load either observes cancellation or hits another error path.
                using var cts = new CancellationTokenSource();
                cts.Cancel();
                await LocalGenerator.LoadAsync(modelId, cancellationToken: cts.Token);
            }
            catch
            {
                // Expected: cancellation, network failure, or path resolution error.
            }
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
        return listener.Lines;
    }

    [Fact]
    public async Task LoadAsync_Default_EmitsAutoTraceLine()
    {
        var lines = await CaptureTraceForLoadAsync("default");
        lines.Should().Contain(l => l.Contains("[LocalGenerator.auto]"));
    }

    [Fact]
    public async Task LoadAsync_Auto_EmitsAutoTraceLine()
    {
        var lines = await CaptureTraceForLoadAsync("auto");
        lines.Should().Contain(l => l.Contains("[LocalGenerator.auto]"));
    }

    [Fact]
    public async Task LoadAsync_ExplicitRepoId_DoesNotEmitAutoTraceLine()
    {
        var lines = await CaptureTraceForLoadAsync("microsoft/Phi-4-mini-instruct-onnx");
        lines.Should().NotContain(l => l.Contains("[LocalGenerator.auto]"));
    }

    [Fact]
    public void FastAlias_StillResolvesToPhi4Mini()
    {
        var registry = GeneratorModelRegistry.Default;
        var resolved = registry.TryResolve("fast", out var info);

        resolved.Should().BeTrue();
        info.Should().NotBeNull();
        info!.ModelId.Should().Be("microsoft/Phi-4-mini-instruct-onnx");
    }

    [Fact]
    public void PhiMiniAlias_ResolvesToPhi4Mini()
    {
        var registry = GeneratorModelRegistry.Default;
        var resolved = registry.TryResolve("phi-4-mini", out var info);

        resolved.Should().BeTrue();
        info.Should().NotBeNull();
        info!.ModelId.Should().Be("microsoft/Phi-4-mini-instruct-onnx");
    }
}
