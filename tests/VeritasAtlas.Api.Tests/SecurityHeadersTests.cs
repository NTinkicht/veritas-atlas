using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Api.Infrastructure;

namespace VeritasAtlas.Api.Tests;

/// <summary>VER-P0-19: baseline security headers on every API response.</summary>
[TestClass]
public sealed class SecurityHeadersTests
{
    private static async Task<HttpResponseMessage> Run(RequestDelegate inner)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .Configure(app =>
                {
                    app.UseMiddleware<SecurityHeadersMiddleware>();
                    app.Run(inner);
                }))
            .StartAsync();
        return await host.GetTestClient().GetAsync("/");
    }

    [TestMethod]
    [DataRow(200)]
    [DataRow(401)]
    [DataRow(404)]
    [DataRow(500)]
    [DataRow(503)]
    public async Task AddsAllHeadersToEveryStatus(int status)
    {
        using var response = await Run(ctx =>
        {
            ctx.Response.StatusCode = status;
            return Task.CompletedTask;
        });

        Assert.AreEqual("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.AreEqual("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.AreEqual("no-store", response.Headers.CacheControl?.ToString());
        Assert.AreEqual("default-src 'none'; frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [TestMethod]
    public async Task PreservesValuesAHandlerAlreadySet()
    {
        using var response = await Run(ctx =>
        {
            ctx.Response.Headers.CacheControl = "private, max-age=0";
            ctx.Response.Headers["Referrer-Policy"] = "same-origin";
            return Task.CompletedTask;
        });

        Assert.AreEqual("max-age=0, private", response.Headers.CacheControl?.ToString());
        Assert.AreEqual("same-origin", response.Headers.GetValues("Referrer-Policy").Single());
        // Positive control: headers the handler did not set still get defaults.
        Assert.AreEqual("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }
}
