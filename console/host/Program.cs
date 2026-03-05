using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.FileProviders;
using LMSupply.Console.Host.Endpoints;
using LMSupply.Console.Host.Infrastructure;
using LMSupply.Console.Host.Services;

var builder = WebApplication.CreateBuilder(args);

// Kestrel 설정 - 대용량 파일 업로드 허용 (최대 500MB)
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 500 * 1024 * 1024; // 500MB
});

// JSON 직렬화 설정
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

// CORS 설정 (개발용)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
    // SSE endpoints handle CORS manually to avoid middleware conflicts
    options.AddPolicy("SSE", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
    });
});

// OpenAPI/Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "LMSupply Console API", Version = "v1" });
});

// 서비스 등록
builder.Services.AddSingleton<CacheService>();
builder.Services.AddSingleton<SystemMonitorService>();
builder.Services.AddSingleton<ModelManagerService>();
builder.Services.AddSingleton<DownloadService>();
builder.Services.AddSingleton<UpdateService>();
builder.Services.AddSingleton<TempFileService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TempFileService>());

var app = builder.Build();

// Swagger UI
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "LMSupply Console API v1");
    c.RoutePrefix = "swagger";
});

app.UseCors();
app.UseMiddleware<RequestIdMiddleware>();
app.UseMiddleware<ErrorMiddleware>();

// 임베디드 리소스에서 정적 파일 제공 (wwwroot가 빌드 시 없으면 매니페스트도 없음)
var assembly = Assembly.GetExecutingAssembly();
ManifestEmbeddedFileProvider? embeddedProvider = null;
try
{
    embeddedProvider = new ManifestEmbeddedFileProvider(assembly, "wwwroot");
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = embeddedProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = embeddedProvider });
}
catch (InvalidOperationException)
{
    // 매니페스트가 없는 경우 (wwwroot 없이 빌드됨) — swagger만 제공
}

// API 엔드포인트 매핑
app.MapModelsEndpoints();
app.MapSystemEndpoints();
app.MapChatEndpoints();
app.MapEmbedEndpoints();
app.MapRerankEndpoints();
app.MapTranscribeEndpoints();
app.MapSynthesizeEndpoints();
app.MapCaptionEndpoints();
app.MapOcrEndpoints();
app.MapDetectEndpoints();
app.MapSegmentEndpoints();
app.MapTranslateEndpoints();
app.MapImageEndpoints();
app.MapFileEndpoints();
app.MapModelRegistryEndpoints();

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// 루트 엔드포인트
app.MapGet("/", () =>
{
    var indexFile = embeddedProvider?.GetFileInfo("index.html");
    if (indexFile is { Exists: true })
    {
        return Results.Stream(indexFile.CreateReadStream(), "text/html");
    }
    return Results.Redirect("/swagger");
});

// SPA 폴백: API 이외의 GET 경로는 index.html로 (클라이언트 사이드 라우팅)
// Only match GET requests — POST/PUT/DELETE to unknown routes should return 404, not HTML
app.MapFallback(context =>
{
    if (!HttpMethods.IsGet(context.Request.Method))
    {
        context.Response.StatusCode = 404;
        return Task.CompletedTask;
    }

    var indexFile = embeddedProvider?.GetFileInfo("index.html");
    if (indexFile is { Exists: true })
    {
        context.Response.ContentType = "text/html";
        return indexFile.CreateReadStream().CopyToAsync(context.Response.Body);
    }

    context.Response.StatusCode = 404;
    return Task.CompletedTask;
});

app.Run();
