namespace VeritasAtlas.Api.Infrastructure;

public sealed class RequestLoggingMiddleware
{
    public const string MessageWithheld = "[message withheld]";

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

            // Exception messages are free-form text that may hold secrets (connection strings, tokens, JSON,
            // headers) and cannot be reliably redacted, so they are never logged: only exception type names
            // and the outer stack trace (which carries no message text) are written.
            _logger.LogError(
                "Request failed {Method} {Path} ElapsedMs={ElapsedMs} CorrelationId={CorrelationId} ExceptionTypes={ExceptionTypes} Message={Message} StackTrace={StackTrace}",
                method,
                path,
                    Math.Round(elapsedMs, 2),
                correlationId,
                DescribeExceptionTypes(ex),
                MessageWithheld,
                ex.StackTrace);

            throw;
        }
    }

    private static string DescribeExceptionTypes(Exception exception)
    {
        var types = new List<string>();
        for (var current = exception; current is not null && types.Count < 8; current = current.InnerException)
        {
            types.Add(current.GetType().FullName ?? current.GetType().Name);
        }

        return string.Join(" ---> ", types);
    }
}
