namespace LMSupply.Console.Host.Infrastructure;

public sealed class RequestIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = context.Request.Headers["X-Request-Id"].FirstOrDefault()
                        ?? Guid.NewGuid().ToString("N");

        context.Response.Headers["X-Request-Id"] = requestId;
        context.Items["RequestId"] = requestId;

        await next(context);
    }
}
