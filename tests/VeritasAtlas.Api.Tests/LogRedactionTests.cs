using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Api.Infrastructure;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-19: exception messages are free-form and cannot be reliably redacted, so failed-request logs
/// never contain them. These tests drive every leak shape raised in review through the real middleware.
/// </summary>
[TestClass]
public sealed class LogRedactionTests
{
    private const string Secret = "sensitive-suffix";

    private static IEnumerable<object[]> LeakShapes() => new[]
    {
        "Host=db;Username=u;Password=hunter2-" + Secret + ";Database=x",
        "Host=db;Password=\"prefix\"\"" + Secret + "\";Database=x",
        "Host=db;Password='prefix''" + Secret + "';Database=x",
        "Host=db;Password=prefix&" + Secret + ";Database=x",
        "Host=db;Password=prefix," + Secret + ";Pwd=" + Secret + "2;Database=x",
        "/cb?access_token=first-" + Secret + "&refresh_token=second-" + Secret,
        "/cb?access_token=prefix," + Secret + "&state=keep",
        "/cb?access_token=prefix;" + Secret + "#frag",
        "/cb?access_token=it's-" + Secret + "&state=keep",
        "/cb?access_token=pre%2C" + Secret + "%26x%3D1",
        "{\"password\":\"hunter2-" + Secret + "\",\"access_token\":\"" + Secret + "2\"}",
        "{'password': '" + Secret + "'}",
        "{\"auth\":{\"client_secret\":\"" + Secret + "\"}}",
        "[{\"token\":\"a-" + Secret + "\"},{\"token\":\"b-" + Secret + "\"}]",
        "body=\\\"password\\\":\\\"prefix\\\\\\\"" + Secret + "\\\",\\\"x\\\":\\\"keep\\\"",
        "Authorization: Digest username=\"u\", realm=\"r\", response=\"" + Secret + "\", nonce=\"n-" + Secret + "\"",
        "Authorization: Bearer abc-" + Secret,
        "Authorization: Basic " + Secret,
        "https://user:hunter2-" + Secret + "@db.example/path",
        "https://ghp_" + Secret + "@github.com/o/r",
        "Cookie: sid=" + Secret + "; theme=dark",
        "Set-Cookie: session=" + Secret + "; Path=/; HttpOnly",
        "<password>" + Secret + "</password>",
        "x-api-key: " + Secret,
        "/x?sig=" + Secret + "&signature=" + Secret + "2",
    }.Select(m => new object[] { m });

    [TestMethod]
    [DynamicData(nameof(LeakShapes))]
    public async Task FailedRequestLogNeverContainsExceptionMessageText(string message)
    {
        var logger = new CapturingLogger();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/positive-control-path";
        var boom = new InvalidOperationException("outer " + message, new TimeoutException("inner " + message));

        // The exception still propagates unchanged.
        var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => new RequestLoggingMiddleware(_ => throw boom, logger).Invoke(context));
        Assert.AreSame(boom, thrown);

        var everything = string.Join("\n", logger.Entries.Select(e => e.Text + "|" + e.Exception));
        Assert.IsFalse(everything.Contains(Secret, StringComparison.Ordinal), everything);
        Assert.IsFalse(everything.Contains("hunter2", StringComparison.Ordinal), everything);
        Assert.IsFalse(everything.Contains("outer ", StringComparison.Ordinal), everything);
        Assert.IsTrue(logger.Entries.All(e => e.Exception is null), "raw exception must not reach the sink");

        // Positive controls: diagnostics that are safe stay available.
        var failure = logger.Entries.Single(e => e.Level == LogLevel.Error).Text;
        Assert.IsTrue(failure.Contains("System.InvalidOperationException"), failure);
        Assert.IsTrue(failure.Contains("System.TimeoutException"), failure);
        Assert.IsTrue(failure.Contains("/api/positive-control-path"), failure);
        Assert.IsTrue(failure.Contains(RequestLoggingMiddleware.MessageWithheld), failure);
    }

    [TestMethod]
    public async Task SuccessfulRequestIsStillLoggedWithPath()
    {
        var logger = new CapturingLogger();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/ok";
        await new RequestLoggingMiddleware(_ => Task.CompletedTask, logger).Invoke(context);
        Assert.IsTrue(logger.Entries.Any(e => e.Level == LogLevel.Information && e.Text.Contains("/api/ok")));
        Assert.IsFalse(logger.Entries.Any(e => e.Level == LogLevel.Error));
    }

    private sealed class CapturingLogger : ILogger<RequestLoggingMiddleware>
    {
        public List<(LogLevel Level, string Text, Exception? Exception)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
