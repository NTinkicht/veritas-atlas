using System.Text.Json;
using VeritasAtlas.Application.Common;

namespace VeritasAtlas.Api.Infrastructure;

public sealed class ExceptionHandlingMiddleware
{
    public const string GenericServerErrorDetail = "An unexpected error occurred.";

    public const string ServiceUnavailableDetail = "The service is temporarily unavailable. Try again later.";
    public const int RetryAfterSeconds = 30;

    private readonly RequestDelegate _next;

    public ExceptionHandlingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (NotFoundException ex)
        {
            await WriteProblemDetailsAsync(context, StatusCodes.Status404NotFound, ex.Message);
        }
        catch (ConflictException ex)
        {
            await WriteProblemDetailsAsync(context, StatusCodes.Status409Conflict, ex.Message);
        }
        catch (ValidationException ex)
        {
            await WriteProblemDetailsAsync(context, StatusCodes.Status400BadRequest, ex.Message);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client went away; nothing to write.
        }
        catch (Exception ex) when (!context.Response.HasStarted && DatabaseUnavailable.Is(ex))
        {
            // VER-P0-19: a database outage is retryable, not an application bug.
            // The exception text (connection strings, hosts) is never echoed.
            context.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var correlationId = context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var value) ? value?.ToString() : null;
            var detail = correlationId is null
                ? ServiceUnavailableDetail
                : $"{ServiceUnavailableDetail} Correlation id: {correlationId}";
            await WriteProblemDetailsAsync(context, StatusCodes.Status503ServiceUnavailable, detail);
        }
        catch (Exception) when (!context.Response.HasStarted)
        {
            // Never echo exception text (may contain connection strings, SQL, paths). Details go to the log only.
            var correlationId = context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var value) ? value?.ToString() : null;
            var detail = correlationId is null
                ? GenericServerErrorDetail
                : $"{GenericServerErrorDetail} Correlation id: {correlationId}";
            await WriteProblemDetailsAsync(context, StatusCodes.Status500InternalServerError, detail);
        }
    }

    private static async Task WriteProblemDetailsAsync(HttpContext context, int statusCode, string detail)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var payload = new
        {
            type = "about:blank",
            title = GetTitle(statusCode),
            status = statusCode,
            detail
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }

    private static string GetTitle(int statusCode) =>
        statusCode switch
        {
            400 => "Bad Request",
            404 => "Not Found",
            409 => "Conflict",
            503 => "Service Unavailable",
            _ => "Internal Server Error"
        };
}
