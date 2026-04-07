namespace VeritasAtlas.Api.Infrastructure;

public sealed class WorkflowRequestContext
{
    public string GetRole(HttpRequest request)
    {
        var value = request.Headers["X-Role"].ToString();
        return string.IsNullOrWhiteSpace(value) ? "anonymous" : value.Trim().ToLowerInvariant();
    }
}