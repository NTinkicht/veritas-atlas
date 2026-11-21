using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Api.Infrastructure;

namespace VeritasAtlas.Api.Tests;

[TestClass]
public sealed class LogRedactionTests
{
    [TestMethod]
    [DataRow("Host=db;Username=u;Password=hunter2;Database=x", "hunter2")]
    [DataRow("Host=db;pwd=hunter2", "hunter2")]
    [DataRow("client_secret=hunter2&x=1", "hunter2")]
    [DataRow("access_token: hunter2", "hunter2")]
    [DataRow("ApiKey=hunter2", "hunter2")]
    [DataRow("Authorization: Bearer hunter2.abc-def", "hunter2")]
    [DataRow("failed https://user:hunter2@db.example/path", "hunter2")]
    [DataRow("Password=\"hunter2 with space\"", "hunter2")]
    [DataRow("Password=\"prefix\"\"sensitive-suffix\";Database=x", "sensitive-suffix")]
    [DataRow("Host=db;Password=prefix&sensitive-suffix;Database=x", "sensitive-suffix")]
    [DataRow("Host=db;Password=prefix,sensitive-suffix;Database=x", "sensitive-suffix")]
    [DataRow("Host=db;Password=prefix sensitive-suffix;Database=x", "sensitive-suffix")]
    [DataRow("Password='prefix''sensitive-suffix';Database=x", "sensitive-suffix")]
    [DataRow("Password=\"a\"\"b\"\"sensitive-suffix\"", "sensitive-suffix")]
    public void RedactMasksSecretValues(string input, string secret)
    {
        var output = LogRedactor.Redact(input);
        Assert.IsFalse(output.Contains(secret, StringComparison.Ordinal), output);
        Assert.IsTrue(output.Contains(LogRedactor.Mask, StringComparison.Ordinal), output);
    }

    [TestMethod]
    public void RedactKeepsConnectionStringStructureAfterUnquotedPassword()
    {
        Assert.AreEqual("Host=db;Password=[REDACTED];Database=x", LogRedactor.Redact("Host=db;Password=a&b,c;Database=x"));
    }

    [TestMethod]
    public void RedactKeepsOtherQueryParametersAfterSecret()
    {
        var output = LogRedactor.Redact("GET /cb?code=keep1&access_token=hunter2&state=keep2");
        Assert.AreEqual("GET /cb?code=keep1&access_token=[REDACTED]&state=keep2", output);
    }

    [TestMethod]
    [DataRow("/cb?access_token=first-secret&refresh_token=second-secret", "/cb?access_token=[REDACTED]&refresh_token=[REDACTED]")]
    [DataRow("/cb?a=keep1&access_token=s1&b=keep2&refresh_token=s2&c=keep3&api_key=s3&d=keep4",
        "/cb?a=keep1&access_token=[REDACTED]&b=keep2&refresh_token=[REDACTED]&c=keep3&api_key=[REDACTED]&d=keep4")]
    [DataRow("Host=db;Password=s1;Token=s2;Database=x", "Host=db;Password=[REDACTED];Token=[REDACTED];Database=x")]
    [DataRow("Host=db;Password=a&b;ApiKey=c,d;Database=x", "Host=db;Password=[REDACTED];ApiKey=[REDACTED];Database=x")]
    [DataRow("/cb?access_token=prefix,sensitive-suffix&state=keep", "/cb?access_token=[REDACTED]&state=keep")]
    [DataRow("/cb?access_token=prefix;sensitive-suffix&state=keep", "/cb?access_token=[REDACTED]&state=keep")]
    [DataRow("/cb?access_token=pre%2Csuf%26fix%3D1&state=keep", "/cb?access_token=[REDACTED]&state=keep")]
    [DataRow("/cb?access_token=prefix,sensitive-suffix#frag", "/cb?access_token=[REDACTED]#frag")]
    [DataRow("/cb?code=keep&access_token=a,b;c&refresh_token=d;e,f&state=keep", "/cb?code=keep&access_token=[REDACTED]&refresh_token=[REDACTED]&state=keep")]
    [DataRow("Host=db;Password=a,b&c d;Pwd=e;Database=x", "Host=db;Password=[REDACTED];Pwd=[REDACTED];Database=x")]
    [DataRow("Host=db;Password=\"a;b\"\"c\";Database=x", "Host=db;Password=[REDACTED];Database=x")]
    [DataRow("Authorization: Bearer abc%2Cdef,ghi", "Authorization: Bearer [REDACTED]")]
    [DataRow("failed /cb?token=s1,x then Host=db;Password=s2;Database=x", "failed /cb?token=[REDACTED] then Host=db;Password=[REDACTED];Database=x")]
    [DataRow("GET /cb?token=s1 Bearer s2", "GET /cb?token=[REDACTED] Bearer [REDACTED]")]
    [DataRow("GET /cb?token=s1&x=keep Authorization: Bearer s2", "GET /cb?token=[REDACTED]&x=keep Authorization: Bearer [REDACTED]")]
    public void RedactHandlesMultipleSecretsAndAdjacentShapes(string input, string expected)
    {
        Assert.AreEqual(expected, LogRedactor.Redact(input));
    }

    [TestMethod]
    public void RedactKeepsNonSecretText()
    {
        const string input = "Host=db.example;Database=veritas;timeout waiting for connection to /api/x";
        Assert.AreEqual(input, LogRedactor.Redact(input));
    }

    [TestMethod]
    public async Task FailedRequestLogNeverContainsSecretsFromOuterOrInnerException()
    {
        var logger = new CapturingLogger();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/x";
        var boom = new InvalidOperationException("outer Password=outersecret",
            new TimeoutException("inner token=innersecret"));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => new RequestLoggingMiddleware(_ => throw boom, logger).Invoke(context));

        Assert.IsTrue(logger.Entries.Count >= 2);
        Assert.IsFalse(logger.Entries.Any(e => e.Text.Contains("outersecret") || e.Text.Contains("innersecret")),
            string.Join("\n", logger.Entries.Select(e => e.Text)));
        Assert.IsTrue(logger.Entries.All(e => e.Exception is null), "raw exception must not reach the sink");
        // Positive control: diagnostics remain useful.
        var failure = logger.Entries.Single(e => e.Level == LogLevel.Error).Text;
        Assert.IsTrue(failure.Contains("System.InvalidOperationException"));
        Assert.IsTrue(failure.Contains("System.TimeoutException"));
        Assert.IsTrue(failure.Contains("/api/x"));
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
