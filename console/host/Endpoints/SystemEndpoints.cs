using LMSupply.Console.Host.Infrastructure;
using LMSupply.Console.Host.Services;

namespace LMSupply.Console.Host.Endpoints;

public static class SystemEndpoints
{
    public static void MapSystemEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/system")
            .WithTags("System")
            ;

        // 시스템 상태
        group.MapGet("/status", (SystemMonitorService monitor, ModelManagerService modelManager) =>
        {
            var status = monitor.GetStatus();
            var loadedModels = modelManager.GetLoadedModels();

            return Results.Ok(new
            {
                status,
                loadedModels = loadedModels.Count,
                models = loadedModels
            });
        })
        .WithName("GetSystemStatus")
        .WithSummary("시스템 상태 조회");

        // GPU 정보
        group.MapGet("/gpu", (SystemMonitorService monitor) =>
        {
            var gpuInfo = monitor.GetGpuInfo();
            return Results.Ok(gpuInfo);
        })
        .WithName("GetGpuInfo")
        .WithSummary("GPU 정보 조회");

        // 메모리 메트릭
        group.MapGet("/memory", (SystemMonitorService monitor) =>
        {
            var memory = monitor.GetMemoryMetrics();
            return Results.Ok(memory);
        })
        .WithName("GetMemoryMetrics")
        .WithSummary("메모리 메트릭 조회");

        // 실시간 메트릭 스트림 (SSE)
        group.MapGet("/metrics/stream", async (HttpContext context, SystemMonitorService monitor) =>
        {
            var cancellationToken = context.RequestAborted;
            var metrics = monitor.StreamMetricsAsync(cancellationToken, intervalMs: 1000);

            await SseHelper.StreamAsync(context, metrics, cancellationToken);
        })
        .WithName("StreamMetrics")
        .WithSummary("실시간 메트릭 스트림 (SSE)");

        // 현재 버전 정보
        group.MapGet("/version", (UpdateService updateService) =>
        {
            return Results.Ok(new { version = updateService.CurrentVersion, rid = updateService.CurrentRid });
        })
        .WithName("GetVersion")
        .WithSummary("현재 버전 정보 조회");

        // 업데이트 확인
        group.MapGet("/update", async (UpdateService updateService) =>
        {
            var result = await updateService.CheckForUpdateAsync();
            return Results.Ok(result);
        })
        .WithName("CheckUpdate")
        .WithSummary("최신 버전 업데이트 확인");

        // 업데이트 적용 (SSE 스트림)
        group.MapPost("/update", async (HttpContext context, UpdateService updateService) =>
        {
            var ct = context.RequestAborted;
            var progress = updateService.ApplyUpdateAsync(ct);
            await SseHelper.StreamAsync(context, progress, ct);
        })
        .WithName("ApplyUpdate")
        .WithSummary("업데이트 다운로드 및 적용");
    }
}
