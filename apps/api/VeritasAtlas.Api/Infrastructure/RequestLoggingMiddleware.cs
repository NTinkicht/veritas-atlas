namespace VeritasAtlas.Api.Infrastructure;

public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        var correlationId = context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var value)
            ? value?.ToString()
            : null;

        var method = context.Request.Method;
        var path = context.Request.Path.ToString();

        var startedAtUtc = DateTime.UtcNow;

        _logger.LogInformation(
            "Request started {Method} {Path} CorrelationId={CorrelationId}",
            method,
            path,
            correlationId);

        try
        {
            await _next(context);

            var elapsedMs = (DateTime.UtcNow - startedAtUtc).TotalMilliseconds;

            _logger.LogInformation(
                "Request completed {Method} {Path} StatusCode={StatusCode} ElapsedMs={ElapsedMs} CorrelationId={CorrelationId}",
                method,
                path,
                    context.Response.StatusCode,
                Math.Round(elapsedMs, 2),
                correlationId);
        }
        catch (Exception ex)
        {
            var elapsedMs = (DateTime.UtcNow - startedAtUtc).TotalMilliseconds;

            _logger.LogError(
                ex,
                "Request failed {Method} {Path} ElapsedMs={ElapsedMs} CorrelationId={CorrelationId}",
                method,
                path,
                    Math.Round(elapsedMs, 2),
                correlationId);

            throw;
        }
    }
}
