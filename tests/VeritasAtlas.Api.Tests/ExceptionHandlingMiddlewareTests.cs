using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Api.Infrastructure;
using VeritasAtlas.Application.Common;

namespace VeritasAtlas.Api.Tests;

[TestClass]
public sealed class ExceptionHandlingMiddlewareTests
{
    private static async Task<(int Status, string Body, string? Correlation)> RunAsync(RequestDelegate inner, Action<HttpContext>? setup = null)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        setup?.Invoke(context);
        var pipeline = new CorrelationIdMiddleware(c => new ExceptionHandlingMiddleware(inner).Invoke(c));
        await pipeline.Invoke(context);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context.Response.StatusCode, body, context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }

    [TestMethod]
    public async Task UnhandledExceptionNeverLeaksMessageAndCarriesCorrelationId()
    {
        var (status, body, correlation) = await RunAsync(_ =>
            throw new InvalidOperationException("Host=db.internal;Password=hunter2"));
        Assert.AreEqual(500, status);
        Assert.IsFalse(body.Contains("hunter2") || body.Contains("db.internal"));
        Assert.IsTrue(body.Contains(ExceptionHandlingMiddleware.GenericServerErrorDetail));
        Assert.IsFalse(string.IsNullOrEmpty(correlation));
        Assert.IsTrue(body.Contains(correlation!));
    }

    [TestMethod]
    public async Task DomainExceptionsKeepAppAuthoredMessageAndStatus()
    {
        var (s1, b1, _) = await RunAsync(_ => throw new NotFoundException("missing thing"));
        var (s2, _, _) = await RunAsync(_ => throw new ConflictException("conflict"));
        var (s3, _, _) = await RunAsync(_ => throw new ValidationException("bad"));
        Assert.AreEqual(404, s1);
        Assert.IsTrue(b1.Contains("missing thing"));
        Assert.AreEqual(409, s2);
        Assert.AreEqual(400, s3);
    }

    [TestMethod]
    public async Task ClientAbortIsNotRewrittenAsServerError()
    {
        using var cts = new CancellationTokenSource();
        var (status, body, _) = await RunAsync(
            _ => { cts.Cancel(); throw new OperationCanceledException(); },
            c => c.RequestAborted = cts.Token);
        Assert.AreEqual(200, status);
        Assert.AreEqual(string.Empty, body);
    }

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }

    [TestMethod]
    public async Task ExceptionAfterResponseStartedPropagatesAndWritesNoBody()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature { Body = context.Response.Body });
        var middleware = new ExceptionHandlingMiddleware(_ => throw new InvalidOperationException("Password=hunter2"));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => middleware.Invoke(context));

        Assert.AreEqual(0, context.Response.Body.Length);
    }

    [TestMethod]
    public async Task RequestLoggingDoesNotLogQueryString()
    {
        var logger = new CapturingLogger();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/x";
        context.Request.QueryString = new QueryString("?access_token=SECRETVALUE");
        await new RequestLoggingMiddleware(_ => Task.CompletedTask, logger).Invoke(context);
        Assert.IsTrue(logger.Lines.Count >= 2);
        Assert.IsFalse(logger.Lines.Any(l => l.Contains("SECRETVALUE") || l.Contains("access_token")));
        Assert.IsTrue(logger.Lines.Any(l => l.Contains("/api/x")));
    }

    private sealed class CapturingLogger : ILogger<RequestLoggingMiddleware>
    {
        public List<string> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Lines.Add(formatter(state, exception));
    }
}
