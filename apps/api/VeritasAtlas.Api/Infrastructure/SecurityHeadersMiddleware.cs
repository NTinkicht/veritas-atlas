namespace VeritasAtlas.Api.Infrastructure;

/// <summary>
/// VER-P0-19: baseline hardening headers on every API response (JSON, errors,
/// health). The API never serves browsable HTML, so the policy is the
/// strictest one. A header a handler already set is preserved.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private static readonly (string Name, string Value)[] Defaults =
    [
        ("X-Content-Type-Options", "nosniff"),
        ("Referrer-Policy", "no-referrer"),
        ("Cache-Control", "no-store"),
        ("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'")
    ];

    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task Invoke(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var headers = ((HttpContext)state).Response.Headers;
            foreach (var (name, value) in Defaults)
            {
                if (!headers.ContainsKey(name))
                {
                    headers[name] = value;
                }
            }
            return Task.CompletedTask;
        }, context);

        return _next(context);
    }
}
