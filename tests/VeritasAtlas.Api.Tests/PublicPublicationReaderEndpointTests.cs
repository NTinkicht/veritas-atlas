using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-16 slice 6 (#200): the anonymous public-reader route over real PostgreSQL (own <c>*_pubreader</c> database).
/// Revisions are driven to each lifecycle state through the real API; the visibility facts, which have no write path
/// yet, are set with SQL under a session that disables the user triggers.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class PublicPublicationReaderEndpointTests
{
    private const string SigningSecret = "publication-revision-api-test-only-key-not-for-production-01";
    private static readonly Guid Tenant = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly string Digest = new('a', 64);
    private const string Revisions = "/api/v1/tenant-publication-revisions";
    private const string Public = "/api/v1/public/publications";

    private static string CitationsWith(string sourceUri) =>
        "[{\"citationLabel\":\"[1]\",\"title\":\"T\",\"quote\":\"Q\",\"sourceName\":\"S\"," +
        "\"asOf\":null,\"effectiveDate\":null,\"freshnessLabel\":\"current\",\"sourceUri\":\"" + sourceUri + "\"}]";

    private static string CitationsWithConfidence(string confidenceJson) =>
        "[{\"citationLabel\":\"[1]\",\"title\":\"T\",\"quote\":\"Q\",\"sourceName\":\"S\"," +
        "\"asOf\":null,\"effectiveDate\":null,\"freshnessLabel\":\"current\",\"confidence\":" + confidenceJson + "}]";

    private const string Citations =
        "[{\"citationLabel\":\"[1]\",\"title\":\"Synthetic ruling\",\"quote\":\"Synthetic quote.\",\"sourceName\":\"Synthetic Gazette\"," +
        "\"asOf\":\"2026-01-01T00:00:00+00:00\",\"effectiveDate\":null,\"freshnessLabel\":\"as of 2026-01-01\"," +
        "\"contradictionDisclosure\":\"Conflicts with the 2025 notice.\",\"uncertaintyLimit\":\"Single-source reading.\"," +
        "\"sourceUri\":\"https://gazette.example.org/notice/1\",\"confidence\":\"Medium\"," +
        "\"tenantId\":\"a1111111-1111-4111-8111-111111111111\",\"storageKey\":\"s3://secret\",\"reviewerId\":\"synthetic-reviewer\"}]";

    private sealed record Facts(
        bool Restricted = false, bool Superseded = false, string? Expires = null, bool FreshnessRequired = true,
        string Currency = "Current", bool HistoricalLabel = false, string? CitationsJson = Citations);

    private static void AssertHardenedHeaders(HttpResponseMessage response, string label)
    {
        Assert.AreEqual("no-store", response.Headers.CacheControl?.ToString(), label);
        Assert.AreEqual("nosniff", string.Join(",", response.Headers.GetValues("X-Content-Type-Options")), label);
        Assert.AreEqual("no-referrer", string.Join(",", response.Headers.GetValues("Referrer-Policy")), label);
        Assert.AreEqual("default-src 'none'; frame-ancestors 'none'", string.Join(",", response.Headers.GetValues("Content-Security-Policy")), label);
    }

    [TestMethod]
    public async Task OnlyAPublishedPermittedUnrestrictedCurrentRevisionIsReadableWithTheExactAllowList()
    {
        var connection = Database();
        if (connection is null) return;
        await ResetAsync(connection);
        using var factory = new ApiFactory(connection);
        using var client = factory.CreateClient();

        var id = await Drive(client, connection, "Published", new Facts());
        using var response = await client.GetAsync($"{Public}/{id}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("no-store", response.Headers.CacheControl?.ToString());
        AssertHardenedHeaders(response, "visible");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        CollectionAssert.AreEquivalent(new[] { "id", "freshnessLabel", "humanReviewLabel", "citations" },
            json.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.AreEqual("current", json.GetProperty("freshnessLabel").GetString());
        Assert.AreEqual("independently-reviewed", json.GetProperty("humanReviewLabel").GetString());
        var citation = json.GetProperty("citations").EnumerateArray().Single();
        CollectionAssert.AreEquivalent(
            new[] { "citationLabel", "title", "quote", "sourceName", "asOf", "effectiveDate", "freshnessLabel", "contradictionDisclosure", "uncertaintyLimit", "sourceUri", "confidence" },
            citation.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.AreEqual("[1]", citation.GetProperty("citationLabel").GetString());
        Assert.AreEqual("Synthetic ruling", citation.GetProperty("title").GetString());
        Assert.AreEqual("Synthetic quote.", citation.GetProperty("quote").GetString());
        Assert.AreEqual("Synthetic Gazette", citation.GetProperty("sourceName").GetString());
        Assert.AreEqual("as of 2026-01-01", citation.GetProperty("freshnessLabel").GetString());
        Assert.AreEqual("Conflicts with the 2025 notice.", citation.GetProperty("contradictionDisclosure").GetString());
        Assert.AreEqual("Single-source reading.", citation.GetProperty("uncertaintyLimit").GetString());
        Assert.AreEqual("https://gazette.example.org/notice/1", citation.GetProperty("sourceUri").GetString());
        Assert.AreEqual("Medium", citation.GetProperty("confidence").GetString());
        Assert.AreEqual(JsonValueKind.Null, citation.GetProperty("effectiveDate").ValueKind);
        Assert.AreEqual(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), citation.GetProperty("asOf").GetDateTimeOffset());
        var raw = await response.Content.ReadAsStringAsync();
        foreach (var leak in new[] { "tenant", "a1111111", "storageKey", "s3://", "reviewer", "synthetic-author", Digest })
        {
            Assert.IsFalse(raw.Contains(leak, StringComparison.OrdinalIgnoreCase), $"leaked {leak}");
        }
    }

    [TestMethod]
    public async Task EveryHiddenCauseIsByteIdenticalToAnUnknownId()
    {
        var connection = Database();
        if (connection is null) return;
        await ResetAsync(connection);
        using var factory = new ApiFactory(connection);
        using var client = factory.CreateClient();

        using var unknown = await client.GetAsync($"{Public}/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
        var unknownBody = await unknown.Content.ReadAsStringAsync();
        AssertHardenedHeaders(unknown, "unknown id");

        var cases = new (string Label, string State, Facts Facts)[]
        {
            ("draft", "Draft", new Facts()),
            ("submitted", "Submitted", new Facts()),
            ("changes requested", "ChangesRequested", new Facts()),
            ("approved but not published", "Approved", new Facts()),
            ("withdrawn", "Withdrawn", new Facts()),
            ("published but restricted", "Published", new Facts(Restricted: true)),
            ("superseded", "Published", new Facts(Superseded: true)),
            ("expired", "Published", new Facts(Expires: "now() - interval '1 hour'")),
            ("current-state stale", "Published", new Facts(Currency: "NonCurrent")),
            ("current-state unknown currency", "Published", new Facts(Currency: "Unknown")),
            ("historical without label", "Published", new Facts(FreshnessRequired: false, Currency: "NonCurrent")),
            ("malformed citations", "Published", new Facts(CitationsJson: "{not json")),
            ("non-array citations", "Published", new Facts(CitationsJson: "{\"a\":1}")),
            ("javascript source link", "Published", new Facts(CitationsJson: CitationsWith("javascript:alert(1)"))),
            ("plain-http source link", "Published", new Facts(CitationsJson: CitationsWith("http://gazette.example.org/n"))),
            ("unknown confidence band", "Published", new Facts(CitationsJson: CitationsWithConfidence("\"Certain\""))),
            ("lowercase confidence band", "Published", new Facts(CitationsJson: CitationsWithConfidence("\"high\""))),
            ("numeric confidence band", "Published", new Facts(CitationsJson: CitationsWithConfidence("3"))),
            ("credentialed source link", "Published", new Facts(CitationsJson: CitationsWith("https://user:pw@gazette.example.org/n"))),
        };
        foreach (var (label, state, facts) in cases)
        {
            var id = await Drive(client, connection, state, facts);
            using var response = await client.GetAsync($"{Public}/{id}");
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, label);
            Assert.AreEqual(unknownBody, await response.Content.ReadAsStringAsync(), label);
            AssertHardenedHeaders(response, label);
        }

        // Positive control: the same shape with a valid https link is readable, so the refusals above are caused by the link.
        var control = await Drive(client, connection, "Published", new Facts(CitationsJson: CitationsWith("https://gazette.example.org/n")));
        using var readable = await client.GetAsync($"{Public}/{control}");
        Assert.AreEqual(HttpStatusCode.OK, readable.StatusCode, "valid https source link control");

        // Positive control: a known confidence band in the same shape is readable, so the refusals above are caused by the band.
        var bandControl = await Drive(client, connection, "Published", new Facts(CitationsJson: CitationsWithConfidence("\"High\"")));
        using var bandReadable = await client.GetAsync($"{Public}/{bandControl}");
        Assert.AreEqual(HttpStatusCode.OK, bandReadable.StatusCode, "known confidence band control");

        // A freshly created, never-touched revision keeps the restrictive defaults and is hidden even if published.
        var defaults = await Drive(client, connection, "Published", null);
        using var hidden = await client.GetAsync($"{Public}/{defaults}");
        Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode, "restrictive defaults");
    }

    [TestMethod]
    public async Task ExportPreservesEveryCitationFieldAndCarriesAVerifiableDigest()
    {
        var connection = Database();
        if (connection is null) return;
        await ResetAsync(connection);
        using var factory = new ApiFactory(connection);
        using var client = factory.CreateClient();

        var id = await Drive(client, connection, "Published", new Facts());
        using var read = await client.GetAsync($"{Public}/{id}");
        var readJson = await read.Content.ReadFromJsonAsync<JsonElement>();
        using var response = await client.GetAsync($"{Public}/{id}/export");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        AssertHardenedHeaders(response, "export");
        Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType);
        StringAssert.Contains(response.Content.Headers.ContentDisposition?.ToString() ?? "", $"publication-{id:N}.json");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var digest = string.Join(",", response.Headers.GetValues("Content-Digest"));
        Assert.AreEqual($"sha-256=:{Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(bytes))}:", digest);

        // Tamper control: a single flipped byte no longer matches the advertised digest.
        var tampered = (byte[])bytes.Clone();
        tampered[tampered.Length / 2] ^= 0x01;
        Assert.AreNotEqual(digest, $"sha-256=:{Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(tampered))}:");

        var json = JsonDocument.Parse(bytes).RootElement;
        CollectionAssert.AreEquivalent(new[] { "format", "id", "freshnessLabel", "humanReviewLabel", "citations" },
            json.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.AreEqual("veritas-atlas.publication-export/v1", json.GetProperty("format").GetString());
        Assert.AreEqual(id, json.GetProperty("id").GetGuid());
        // Reference retention: citations are identical to what the anonymous reader shows.
        Assert.AreEqual(readJson.GetProperty("citations").GetRawText(), json.GetProperty("citations").GetRawText());
        Assert.AreEqual("https://gazette.example.org/notice/1",
            json.GetProperty("citations")[0].GetProperty("sourceUri").GetString());
        var raw = Encoding.UTF8.GetString(bytes);
        foreach (var leak in new[] { "tenant", "a1111111", "storageKey", "s3://", "reviewer", "synthetic-author", Digest })
        {
            Assert.IsFalse(raw.Contains(leak, StringComparison.OrdinalIgnoreCase), $"leaked {leak}");
        }
    }

    [TestMethod]
    public async Task ExportOfAnyHiddenRevisionIsByteIdenticalToAnUnknownIdAndWithdrawalClosesIt()
    {
        var connection = Database();
        if (connection is null) return;
        await ResetAsync(connection);
        using var factory = new ApiFactory(connection);
        using var client = factory.CreateClient();

        using var unknown = await client.GetAsync($"{Public}/{Guid.NewGuid()}/export");
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
        var unknownBody = await unknown.Content.ReadAsStringAsync();
        AssertHardenedHeaders(unknown, "unknown export");
        Assert.IsFalse(unknown.Headers.Contains("Content-Digest"));

        var cases = new (string Label, string State, Facts Facts)[]
        {
            ("draft", "Draft", new Facts()),
            ("approved but not published", "Approved", new Facts()),
            ("withdrawn", "Withdrawn", new Facts()),
            ("published but restricted", "Published", new Facts(Restricted: true)),
            ("expired", "Published", new Facts(Expires: "now() - interval '1 hour'")),
            ("current-state stale", "Published", new Facts(Currency: "NonCurrent")),
        };
        foreach (var (label, state, facts) in cases)
        {
            var hidden = await Drive(client, connection, state, facts);
            using var response = await client.GetAsync($"{Public}/{hidden}/export");
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, label);
            Assert.AreEqual(unknownBody, await response.Content.ReadAsStringAsync(), label);
            AssertHardenedHeaders(response, label);
            Assert.IsFalse(response.Headers.Contains("Content-Digest"), label);
        }

        // Positive control: the same drive with permitted facts exports, so the refusals above are caused by state/facts.
        var visible = await Drive(client, connection, "Published", new Facts());
        using var ok = await client.GetAsync($"{Public}/{visible}/export");
        Assert.AreEqual(HttpStatusCode.OK, ok.StatusCode, "positive control");

        // Withdrawal closes a previously exportable revision at once.
        var publisher = Sign("synthetic-publisher", "publisher", Tenant.ToString("D"));
        using var withdraw = await Send(client, HttpMethod.Post, $"{Revisions}/{visible}/transitions", publisher,
            new { action = "Withdraw", expectedVersion = 3, operationId = $"{visible:N}-withdraw" });
        Assert.AreEqual(HttpStatusCode.OK, withdraw.StatusCode);
        using var after = await client.GetAsync($"{Public}/{visible}/export");
        Assert.AreEqual(HttpStatusCode.NotFound, after.StatusCode);
        Assert.AreEqual(unknownBody, await after.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task HtmlExportIsAccessibleEncodedAndCarriesAVerifiableDigest()
    {
        var connection = Database();
        if (connection is null) return;
        await ResetAsync(connection);
        using var factory = new ApiFactory(connection);
        using var client = factory.CreateClient();

        var id = await Drive(client, connection, "Published", new Facts());
        using var response = await client.GetAsync($"{Public}/{id}/export.html");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        AssertHardenedHeaders(response, "html export");
        Assert.AreEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        StringAssert.Contains(response.Content.Headers.ContentDisposition?.ToString() ?? "", $"publication-{id:N}.html");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.AreEqual($"sha-256=:{Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(bytes))}:",
            string.Join(",", response.Headers.GetValues("Content-Digest")));
        var html = Encoding.UTF8.GetString(bytes);
        foreach (var expected in new[]
        {
            "<html lang=\"en\">", "<title>", "<h1>", "<article>", "Synthetic ruling", "Synthetic quote.", "Synthetic Gazette",
            "as of 2026-01-01", "Conflicts with the 2025 notice.", "Single-source reading.", "Medium",
            "<a href=\"https://gazette.example.org/notice/1\"",
        })
        {
            StringAssert.Contains(html, expected);
        }

        foreach (var leak in new[] { "<script", "<style", "tenant", "a1111111", "storageKey", "s3://", "reviewer", "synthetic-author", Digest })
        {
            Assert.IsFalse(html.Contains(leak, StringComparison.OrdinalIgnoreCase), $"leaked {leak}");
        }
    }

    [TestMethod]
    public async Task HtmlExportEncodesHostileCitationText()
    {
        var connection = Database();
        if (connection is null) return;
        await ResetAsync(connection);
        using var factory = new ApiFactory(connection);
        using var client = factory.CreateClient();

        const string hostile =
            "[{\"citationLabel\":\"[1]\",\"title\":\"<script>alert(1)</script>\",\"quote\":\"\\\"><img src=x onerror=alert(2)>\"," +
            "\"sourceName\":\"S & Co\",\"asOf\":null,\"effectiveDate\":null,\"freshnessLabel\":\"current\"," +
            "\"sourceUri\":\"https://gazette.example.org/n?a=1&b=\\\"onmouseover=alert(3)\"}]";
        var id = await Drive(client, connection, "Published", new Facts(CitationsJson: hostile));
        using var response = await client.GetAsync($"{Public}/{id}/export.html");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(html.Contains("<script", StringComparison.OrdinalIgnoreCase), "script tag");
        Assert.IsFalse(html.Contains("<img", StringComparison.OrdinalIgnoreCase), "img tag");
        StringAssert.Contains(html, "&lt;script&gt;alert(1)&lt;/script&gt;");
        StringAssert.Contains(html, "S &amp; Co");
        Assert.IsFalse(html.Contains("\"onmouseover", StringComparison.OrdinalIgnoreCase), "attribute breakout");
    }

    [TestMethod]
    public async Task HtmlExportOfAnyHiddenRevisionIsByteIdenticalToAnUnknownIdAndWithdrawalClosesIt()
    {
        var connection = Database();
        if (connection is null) return;
        await ResetAsync(connection);
        using var factory = new ApiFactory(connection);
        using var client = factory.CreateClient();

        using var unknown = await client.GetAsync($"{Public}/{Guid.NewGuid()}/export.html");
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
        var unknownBody = await unknown.Content.ReadAsStringAsync();
        AssertHardenedHeaders(unknown, "unknown html export");
        Assert.IsFalse(unknown.Headers.Contains("Content-Digest"));

        var cases = new (string Label, string State, Facts Facts)[]
        {
            ("draft", "Draft", new Facts()),
            ("approved but not published", "Approved", new Facts()),
            ("withdrawn", "Withdrawn", new Facts()),
            ("published but restricted", "Published", new Facts(Restricted: true)),
            ("expired", "Published", new Facts(Expires: "now() - interval '1 hour'")),
            ("current-state stale", "Published", new Facts(Currency: "NonCurrent")),
        };
        foreach (var (label, state, facts) in cases)
        {
            var hidden = await Drive(client, connection, state, facts);
            using var response = await client.GetAsync($"{Public}/{hidden}/export.html");
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, label);
            Assert.AreEqual(unknownBody, await response.Content.ReadAsStringAsync(), label);
            AssertHardenedHeaders(response, label);
            Assert.IsFalse(response.Headers.Contains("Content-Digest"), label);
        }

        var visible = await Drive(client, connection, "Published", new Facts());
        using var ok = await client.GetAsync($"{Public}/{visible}/export.html");
        Assert.AreEqual(HttpStatusCode.OK, ok.StatusCode, "positive control");

        var publisher = Sign("synthetic-publisher", "publisher", Tenant.ToString("D"));
        using var withdraw = await Send(client, HttpMethod.Post, $"{Revisions}/{visible}/transitions", publisher,
            new { action = "Withdraw", expectedVersion = 3, operationId = $"{visible:N}-withdraw" });
        Assert.AreEqual(HttpStatusCode.OK, withdraw.StatusCode);
        using var after = await client.GetAsync($"{Public}/{visible}/export.html");
        Assert.AreEqual(HttpStatusCode.NotFound, after.StatusCode);
        Assert.AreEqual(unknownBody, await after.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task AnExpresslyLabelledHistoricalPublicationIsReadableAsHistorical()
    {
        var connection = Database();
        if (connection is null) return;
        await ResetAsync(connection);
        using var factory = new ApiFactory(connection);
        using var client = factory.CreateClient();

        var id = await Drive(client, connection, "Published",
            new Facts(FreshnessRequired: false, Currency: "NonCurrent", HistoricalLabel: true, CitationsJson: null));
        using var response = await client.GetAsync($"{Public}/{id}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("historical", json.GetProperty("freshnessLabel").GetString());
        Assert.AreEqual(0, json.GetProperty("citations").GetArrayLength());
    }

    [TestMethod]
    public async Task TheAnonymousRouteDoesNotOpenTheTenantRoutes()
    {
        var connection = Database();
        if (connection is null) return;
        await ResetAsync(connection);
        using var factory = new ApiFactory(connection);
        using var client = factory.CreateClient();
        using var tenantRoute = await client.GetAsync($"{Revisions}/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.Unauthorized, tenantRoute.StatusCode);
        using var notAGuid = await client.GetAsync($"{Public}/not-a-guid");
        Assert.AreNotEqual(HttpStatusCode.OK, notAGuid.StatusCode);
    }

    /// <summary>Creates a permitted revision, drives it to <paramref name="state"/> through the API, then sets the facts.</summary>
    private static async Task<Guid> Drive(HttpClient client, string connection, string state, Facts? facts)
    {
        var tenant = Tenant.ToString("D");
        var author = Sign("synthetic-author", "operator", tenant);
        var reviewer = Sign("synthetic-reviewer", "reviewer", tenant);
        var publisher = Sign("synthetic-publisher", "publisher", tenant);
        var created = await Send(client, HttpMethod.Post, Revisions, author,
            new { contentDigest = Digest, policyPermitsPublicDisplay = true });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var steps = state switch
        {
            "Draft" => Array.Empty<(string, string)>(),
            "Submitted" => [("author", "Submit")],
            "ChangesRequested" => [("author", "Submit"), ("reviewer", "RequestChanges")],
            "Approved" => [("author", "Submit"), ("reviewer", "Approve")],
            "Published" => [("author", "Submit"), ("reviewer", "Approve"), ("publisher", "Publish")],
            "Withdrawn" => [("author", "Submit"), ("reviewer", "Approve"), ("publisher", "Publish"), ("publisher", "Withdraw")],
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
        long version = 0;
        foreach (var (who, action) in steps)
        {
            var bearer = who switch { "author" => author, "reviewer" => reviewer, _ => publisher };
            var response = await Send(client, HttpMethod.Post, $"{Revisions}/{id}/transitions", bearer,
                new { action, expectedVersion = version, operationId = $"{id:N}-{version}" });
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"{state}: {action}");
            version++;
        }

        if (facts is not null) await SetFacts(connection, id, facts);
        return id;
    }

    private static async Task SetFacts(string connection, Guid id, Facts f)
    {
        await using var conn = new NpgsqlConnection(connection);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // Simulates corrupt or legacy rows the write path could never produce (slice 7 keeps non-restrictive facts on the
        // Approved/Published line only): the guard trigger and the restrictive-facts CHECK are bypassed so the reader's
        // fail-closed behaviour is still proven against them. The database is reset per test.
        cmd.CommandText =
            "SET session_replication_role = replica; " +
            "ALTER TABLE tenant_publication_revisions DROP CONSTRAINT IF EXISTS ck_tenant_publication_revisions_facts_restrictive; " +
            "UPDATE tenant_publication_revisions SET restricted = @r, superseded = @s, " +
            $"expires_at_utc = {(f.Expires ?? "NULL")}, freshness_required = @f, evidence_currency = @c, " +
            "historical_label_present = @h, public_citations = CAST(@p AS jsonb) WHERE id = @id";
        if (f.CitationsJson is "{not json")
        {
            // Not valid JSON cannot be stored in jsonb; store a JSON string instead, which the allow-list cannot read.
            cmd.Parameters.AddWithValue("p", "\"{not json\"");
        }
        else
        {
            cmd.Parameters.AddWithValue("p", (object?)f.CitationsJson ?? DBNull.Value);
        }

        cmd.Parameters.AddWithValue("r", f.Restricted);
        cmd.Parameters.AddWithValue("s", f.Superseded);
        cmd.Parameters.AddWithValue("f", f.FreshnessRequired);
        cmd.Parameters.AddWithValue("c", f.Currency);
        cmd.Parameters.AddWithValue("h", f.HistoricalLabel);
        cmd.Parameters.AddWithValue("id", id);
        Assert.AreEqual(1, await cmd.ExecuteNonQueryAsync());
    }

    private static async Task<HttpResponseMessage> Send(
        HttpClient client, HttpMethod method, string path, string bearer, object? body)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (method == HttpMethod.Post) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static string? Database()
    {
        var connectionString = Environment.GetEnvironmentVariable("VERITAS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("VERITAS_TEST_POSTGRES is not set.");
            return null;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.Database += "_pubreader";
        return builder.ConnectionString;
    }

    private static async Task ResetAsync(string connectionString)
    {
        await using var db = new VeritasAtlasDbContext(
            new DbContextOptionsBuilder<VeritasAtlasDbContext>().UseNpgsql(connectionString).Options);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    private static string Sign(string name, string role, string tenant)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.Name, name), new(ClaimTypes.Role, role), new(TenantClaim.Type, tenant),
        };
        var now = DateTime.UtcNow;
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: RuntimeSecurity.DefaultJwtIssuer, audience: RuntimeSecurity.DefaultJwtAudience, claims: claims,
            notBefore: now.AddMinutes(-1), expires: now.AddMinutes(10),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningSecret)), SecurityAlgorithms.HmacSha256)));
    }

    private sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:Jwt:Secret", SigningSecret);
            builder.UseSetting("Auth:Bootstrap:Username", "synthetic-bootstrap-operator");
            builder.UseSetting("Auth:Bootstrap:Password", "synthetic-bootstrap-password-not-real-0005");
            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IAccessTokenRevocationStore>(new InMemoryAccessTokenRevocationStore()));
        }
    }
}
