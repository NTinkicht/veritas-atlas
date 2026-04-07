namespace VeritasAtlas.Api.Infrastructure;

public sealed class WorkflowAuthorizationService
{
    public void RequireKnownRole(string role)
    {
        var allowed = new[] { "anonymous", "operator", "reviewer", "publisher", "admin" };

        if (!allowed.Contains(role, StringComparer.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"Unknown role '{role}'.");
        }
    }
}