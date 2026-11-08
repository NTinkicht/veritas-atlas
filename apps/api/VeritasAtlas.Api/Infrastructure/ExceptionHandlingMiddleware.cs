using System.Text.Json;
using VeritasAtlas.Application.Common;

namespace VeritasAtlas.Api.Infrastructure;

public sealed class ExceptionHandlingMiddleware
{
    public const string GenericServerErrorDetail = "An unexpected error occurred.";

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
            _ => "Internal Server Error"
        };
}
