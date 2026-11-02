using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-06 (#156): the HTTP raw-source fetcher. SYNTHETIC hosts and bytes only; a fake
/// <see cref="HttpMessageHandler"/> and a fake resolver mean no test can reach a network.
/// </summary>
[TestClass]
public sealed class HttpRawSourceFetcherTests
{
    private static readonly Guid Tenant = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid Source = Guid.Parse("c3333333-3333-4333-8333-333333333333");
    private static readonly FetchExpectation Expect = new("v1", "gb", "statute");
    private static readonly SourceFetchLimits Limits = new(1024, TimeSpan.FromSeconds(5));
    private const string Actor = "synthetic-fetcher";
    private const string Start = "https://legislation.example.test/act/1";

    private static SourceFetchPolicy Policy(int maxRedirects = 3, bool plaintext = false) => new(
        "v1", "gb", "statute",
        ["legislation.example.test", "mirror.example.test", "internal.example.test", "rebind.example.test"],
        plaintext ? ["https", "http"] : ["https"], plaintext ? [443, 80] : [443], maxRedirects, plaintext);

    private sealed class Rig
    {
        public readonly List<HttpRequestMessage> Sent = [];
        public readonly FakePolicies Policies = new();
        public readonly FakeAttempts Attempts = new();
        public readonly FakeRaw Raw = new();
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond = _ => Task.FromResult(Ok("<p>act</p>"));
        public readonly Dictionary<string, IPAddress[]> Dns = new()
        {
            ["legislation.example.test"] = [IPAddress.Parse("93.184.216.34")],
            ["mirror.example.test"] = [IPAddress.Parse("93.184.216.35")],
            ["internal.example.test"] = [IPAddress.Parse("10.0.0.5")],
            ["rebind.example.test"] = [IPAddress.Parse("127.0.0.1")],
        };

        public Rig(SourceFetchPolicy? policy = null) { if (policy != null) Policies.Current = policy; else Policies.Current = Policy(); }

        public HttpRawSourceFetcher Fetcher() => new(
            new HttpClient(new Handler(this)) { Timeout = Timeout.InfiniteTimeSpan },
            new FakeResolver(Dns), Policies, Attempts, Raw, TimeProvider.System);

        public Task<RawSourceFetchResult> Fetch(string uri = Start, string key = "k1", string? pin = null, SourceFetchLimits? limits = null) =>
            Fetcher().FetchAsync(Tenant, new RawSourceFetchRequest(Source, key, uri, Expect, limits ?? Limits, pin), Actor, CancellationToken.None);
    }

    private sealed class Handler(Rig rig) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            rig.Sent.Add(request);
            var response = await rig.Respond(request).WaitAsync(ct);
            response.RequestMessage ??= request;
            return response;
        }
    }

    private sealed class FakeResolver(Dictionary<string, IPAddress[]> dns) : IHostAddressResolver
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct) =>
            dns.TryGetValue(host, out var a) ? Task.FromResult<IReadOnlyList<IPAddress>>(a)
                : throw new System.Net.Sockets.SocketException(11001);
    }

    private sealed class FakePolicies : ITenantSourceFetchPolicyRegistry
    {
        public SourceFetchPolicy? Current;
        public Task<SourceFetchPolicyProposalResult> ProposeAsync(Guid t, Guid s, SourceFetchPolicy p, bool w, string a, CancellationToken c) =>
            throw new NotSupportedException();
        public Task<TenantSourceFetchPolicyModel?> GetCurrentAsync(Guid t, Guid s, CancellationToken c) =>
            Task.FromResult(Current is null || t != Tenant || s != Source ? null
                : new TenantSourceFetchPolicyModel(Guid.NewGuid(), s, 1, Current, false, DateTime.UtcNow));
    }

    private sealed class FakeAttempts : ITenantFetchAttemptLedger
    {
        public readonly List<TenantFetchAttemptModel> Items = [];
        public Task<FetchAttemptRecordResult> RecordAsync(Guid t, FetchAttemptInput i, string a, CancellationToken c)
        {
            var m = new TenantFetchAttemptModel(Guid.NewGuid(), i.SourceEntryId, i.AttemptKey, i.RequestedUri, i.AttemptedAtUtc,
                i.Outcome, i.FailureClass, i.HttpStatus, i.RawRetrievalId, DateTime.UtcNow);
            Items.Add(m);
            return Task.FromResult(new FetchAttemptRecordResult(FetchAttemptRecordOutcome.Recorded, m));
        }
        public Task<IReadOnlyList<TenantFetchAttemptModel>> ListForSourceAsync(Guid t, Guid s, CancellationToken c) =>
            Task.FromResult<IReadOnlyList<TenantFetchAttemptModel>>(Items.ToList());
    }

    private sealed class FakeRaw : ITenantRawRetrievalLedger
    {
        public readonly List<(TenantRawRetrievalModel Model, byte[] Bytes)> Items = [];
        public Task<RawRetrievalRecordResult> RecordAsync(Guid t, RawRetrievalInput i, string a, CancellationToken c)
        {
            var m = new TenantRawRetrievalModel(Guid.NewGuid(), i.SourceEntryId, i.IdempotencyKey, i.FinalUri,
                RawContentDigest.Compute(i.RawBytes), i.RawBytes.Length, i.RetrievedAtUtc, DateTime.UtcNow);
            Items.Add((m, i.RawBytes));
            return Task.FromResult(new RawRetrievalRecordResult(RawRetrievalRecordOutcome.Recorded, m));
        }
        public Task<TenantRawRetrievalModel?> GetAsync(Guid t, Guid id, CancellationToken c) =>
            Task.FromResult(Items.Select(x => x.Model).FirstOrDefault(x => x.Id == id));
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)) };
    private static HttpResponseMessage Redirect(string to, HttpStatusCode code = HttpStatusCode.Found)
    {
        var r = new HttpResponseMessage(code);
        r.Headers.Location = new Uri(to);
        return r;
    }

    [TestMethod]
    public async Task StoresUnparsedBytesAndRecordsASuccessfulAttempt_WithoutAnyCredentials()
    {
        var rig = new Rig();
        var result = await rig.Fetch();

        Assert.AreEqual(RawSourceFetchStatus.Stored, result.Status);
        Assert.AreEqual(RawContentDigest.Compute(Encoding.UTF8.GetBytes("<p>act</p>")), result.ContentSha256);
        Assert.AreEqual(1, rig.Raw.Items.Count);
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("<p>act</p>"), rig.Raw.Items[0].Bytes);
        Assert.AreEqual(FetchAttemptOutcome.Succeeded, rig.Attempts.Items.Single().Outcome);
        Assert.AreEqual(result.RawRetrievalId, rig.Attempts.Items.Single().RawRetrievalId);
        var request = rig.Sent.Single();
        Assert.IsNull(request.Headers.Authorization);
        Assert.IsFalse(request.Headers.Contains("Cookie"));
        Assert.AreEqual(HttpMethod.Get, request.Method);
    }

    [TestMethod]
    public async Task IdenticalBytesAreDeduplicated_DifferentBytesCreateANewRecord()
    {
        var rig = new Rig();
        var first = await rig.Fetch(key: "k1");
        var second = await rig.Fetch(key: "k2");
        Assert.AreEqual(RawSourceFetchStatus.Duplicate, second.Status);
        Assert.AreEqual(first.RawRetrievalId, second.RawRetrievalId);
        Assert.AreEqual(1, rig.Raw.Items.Count);
        Assert.AreEqual(2, rig.Attempts.Items.Count);

        rig.Respond = _ => Task.FromResult(Ok("<p>amended act</p>"));
        var third = await rig.Fetch(key: "k3");
        Assert.AreEqual(RawSourceFetchStatus.Stored, third.Status);
        Assert.AreNotEqual(first.RawRetrievalId, third.RawRetrievalId);
        Assert.AreEqual(2, rig.Raw.Items.Count);
        Assert.AreEqual(3, rig.Attempts.Items.Count);
    }

    [TestMethod]
    public async Task FollowsAnAllowedRedirectManually_AndRecordsTheFinalUri()
    {
        var rig = new Rig();
        rig.Respond = r => Task.FromResult(r.RequestUri!.Host == "legislation.example.test"
            ? Redirect("https://mirror.example.test/act/1") : Ok("<p>act</p>"));
        var result = await rig.Fetch();
        Assert.AreEqual(RawSourceFetchStatus.Stored, result.Status);
        Assert.AreEqual(2, rig.Sent.Count);
        Assert.AreEqual("https://mirror.example.test/act/1", rig.Raw.Items.Single().Model.FinalUri);
    }

    [TestMethod]
    public async Task UnsafeRedirectsAreRefusedWithARecordedReasonAndNoRawRecord()
    {
        var cases = new (string Name, SourceFetchPolicy Policy, string Target, string Reason)[]
        {
            ("private", Policy(), "https://internal.example.test/x", "forbidden_address"),
            ("loopback-at-connect", Policy(), "https://rebind.example.test/x", "forbidden_address"),
            ("not-allowed-host", Policy(), "https://evil.example.test/x", "host_not_allowed"),
            ("downgrade", Policy(plaintext: true), "http://mirror.example.test/x", "scheme_downgrade"),
            ("plain-http", Policy(), "http://mirror.example.test/x", "scheme_not_allowed"),
            ("bad-port", Policy(), "https://mirror.example.test:8443/x", "port_not_allowed"),
            ("credentials", Policy(), "https://user:secret@mirror.example.test/x", "credentials_in_uri"),
        };
        foreach (var c in cases)
        {
            var rig = new Rig(c.Policy);
            rig.Respond = r => Task.FromResult(r.RequestUri!.Host == "legislation.example.test" ? Redirect(c.Target) : Ok("leaked"));
            var result = await rig.Fetch();
            Assert.AreEqual(RawSourceFetchStatus.Refused, result.Status, c.Name);
            Assert.AreEqual(c.Reason, result.Reason, c.Name);
            Assert.AreEqual(1, rig.Sent.Count, c.Name + ": nothing may be requested from the refused hop");
            Assert.AreEqual(0, rig.Raw.Items.Count, c.Name);
            var attempt = rig.Attempts.Items.Single();
            Assert.AreEqual(FetchAttemptOutcome.Failed, attempt.Outcome, c.Name);
            Assert.AreEqual(FetchFailureClass.PolicyDenied, attempt.FailureClass, c.Name);
            Assert.IsFalse(attempt.RequestedUri.Contains("secret"), c.Name);
        }
    }

    [TestMethod]
    public async Task RedirectChainOverTheLimitIsRefused()
    {
        var rig = new Rig(Policy(maxRedirects: 2));
        rig.Respond = r => Task.FromResult(Redirect(r.RequestUri!.AbsolutePath.EndsWith("/a") ? "https://mirror.example.test/b" : "https://legislation.example.test/a"));
        var result = await rig.Fetch();
        Assert.AreEqual("too_many_redirects", result.Reason);
        Assert.AreEqual(3, rig.Sent.Count);
        Assert.AreEqual(0, rig.Raw.Items.Count);
    }

    [TestMethod]
    public async Task HostNotInTheTenantListIsRefusedBeforeAnyRequestOrLookup()
    {
        var rig = new Rig();
        var result = await rig.Fetch("https://evil.example.test/act");
        Assert.AreEqual("host_not_allowed", result.Reason);
        Assert.AreEqual(0, rig.Sent.Count);
        Assert.AreEqual(FetchFailureClass.PolicyDenied, rig.Attempts.Items.Single().FailureClass);
    }

    [TestMethod]
    public async Task HostnameResolvingToADeniedRangeIsRefusedBeforeAnyRequest()
    {
        var rig = new Rig();
        var result = await rig.Fetch("https://rebind.example.test/act");
        Assert.AreEqual("forbidden_address", result.Reason);
        Assert.AreEqual(0, rig.Sent.Count);
    }

    [TestMethod]
    public async Task ConnectTimeGuardRefusesADeniedAddress_WhateverAnEarlierCheckSaw()
    {
        var resolver = new FakeResolver(new() { ["rebind.example.test"] = [IPAddress.Parse("127.0.0.1")], ["mix.example.test"] = [IPAddress.Parse("93.184.216.34"), IPAddress.Parse("169.254.169.254")] });
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            GuardedConnect.ConnectAsync(new DnsEndPoint("rebind.example.test", 443), resolver, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            GuardedConnect.ConnectAsync(new DnsEndPoint("mix.example.test", 443), resolver, CancellationToken.None));
    }

    [TestMethod]
    public async Task OversizeAndTooSlowResponsesAreRefused()
    {
        var rig = new Rig();
        var declared = await rig.Fetch(limits: new SourceFetchLimits(4, TimeSpan.FromSeconds(5)));
        Assert.AreEqual("content_length_exceeds_limit", declared.Reason);
        Assert.AreEqual(FetchFailureClass.SizeLimit, rig.Attempts.Items.Last().FailureClass);

        rig.Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnknownLengthContent(new byte[64]) });
        var streamed = await rig.Fetch(key: "k2", limits: new SourceFetchLimits(4, TimeSpan.FromSeconds(5)));
        Assert.AreEqual("response_too_large", streamed.Reason);

        rig.Respond = async _ => { await Task.Delay(Timeout.Infinite); return Ok("never"); };
        var slow = await rig.Fetch(key: "k3", limits: new SourceFetchLimits(1024, TimeSpan.FromSeconds(1)));
        Assert.AreEqual("fetch_timeout", slow.Reason);
        Assert.AreEqual(FetchFailureClass.Timeout, rig.Attempts.Items.Last().FailureClass);
        Assert.AreEqual(0, rig.Raw.Items.Count);
    }

    [TestMethod]
    public async Task PinnedDigestMismatchIsRefusedAndAMatchIsStored()
    {
        var rig = new Rig();
        var wrong = await rig.Fetch(pin: new string('0', 64));
        Assert.AreEqual(RawSourceFetchStatus.Refused, wrong.Status);
        Assert.AreEqual("digest_mismatch", wrong.Reason);
        Assert.AreEqual(0, rig.Raw.Items.Count);

        var right = await rig.Fetch(key: "k2", pin: RawContentDigest.Compute(Encoding.UTF8.GetBytes("<p>act</p>")));
        Assert.AreEqual(RawSourceFetchStatus.Stored, right.Status);
    }

    [TestMethod]
    public async Task RetryAfterOn429Or503GivesARecordedDueTimeAndNoRawRecord_OtherStatusesDoNot()
    {
        var rig = new Rig();
        rig.Respond = _ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            r.Headers.TryAddWithoutValidation("Retry-After", "30");
            return Task.FromResult(r);
        };
        var before = DateTimeOffset.UtcNow;
        var busy = await rig.Fetch();
        Assert.AreEqual(RawSourceFetchStatus.RetryLater, busy.Status);
        Assert.IsTrue(busy.RetryDueAtUtc >= before.AddSeconds(30), "due time honours Retry-After");
        Assert.AreEqual(503, rig.Attempts.Items.Single().HttpStatus);
        Assert.AreEqual(FetchFailureClass.HttpStatus, rig.Attempts.Items.Single().FailureClass);
        Assert.AreEqual(0, rig.Raw.Items.Count);

        rig.Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        var missing = await rig.Fetch(key: "k2");
        Assert.AreEqual(RawSourceFetchStatus.Refused, missing.Status);
        Assert.IsNull(missing.RetryDueAtUtc);
    }

    [TestMethod]
    public async Task RetriesAreBounded()
    {
        var rig = new Rig();
        rig.Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var statuses = new List<RawSourceFetchStatus>();
        for (var i = 1; i <= 4; i++) statuses.Add((await rig.Fetch(key: "k" + i)).Status);
        CollectionAssert.AreEqual(
            new[] { RawSourceFetchStatus.RetryLater, RawSourceFetchStatus.RetryLater, RawSourceFetchStatus.Refused, RawSourceFetchStatus.Refused },
            statuses);
    }

    [TestMethod]
    public async Task UnknownSourceOrMissingPolicyIsNotFound_AndNothingIsRequested()
    {
        var rig = new Rig();
        rig.Policies.Current = null;
        Assert.AreEqual(RawSourceFetchStatus.SourceNotFound, (await rig.Fetch()).Status);
        Assert.AreEqual(0, rig.Sent.Count);

        var rig2 = new Rig();
        var otherTenant = await rig2.Fetcher().FetchAsync(
            Guid.Parse("b2222222-2222-4222-8222-222222222222"),
            new RawSourceFetchRequest(Source, "k", Start, Expect, Limits), Actor, CancellationToken.None);
        Assert.AreEqual(RawSourceFetchStatus.SourceNotFound, otherTenant.Status);
        Assert.AreEqual(0, rig2.Sent.Count);
        Assert.AreEqual(0, rig2.Attempts.Items.Count);
    }

    [TestMethod]
    public async Task PolicyDriftAndWrongClassificationAreRefused()
    {
        var rig = new Rig();
        var result = await rig.Fetcher().FetchAsync(Tenant,
            new RawSourceFetchRequest(Source, "k", Start, new FetchExpectation("v0", "gb", "statute"), Limits), Actor, CancellationToken.None);
        Assert.AreEqual("policy_version_drift", result.Reason);
        Assert.AreEqual(0, rig.Sent.Count);
    }

    [TestMethod]
    public async Task ATransportThatFollowedARedirectItselfIsRefused()
    {
        var rig = new Rig();
        rig.Respond = r =>
        {
            var ok = Ok("<p>act</p>");
            ok.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://mirror.example.test/elsewhere");
            return Task.FromResult(ok);
        };
        var result = await rig.Fetch();
        Assert.AreEqual("unexpected_redirect", result.Reason);
        Assert.AreEqual(0, rig.Raw.Items.Count);
    }

    [TestMethod]
    public async Task NetworkFailureIsRecordedAsNetwork()
    {
        var rig = new Rig();
        rig.Respond = _ => throw new HttpRequestException("boom https://user:secret@x.test/");
        var result = await rig.Fetch();
        Assert.AreEqual("network_error", result.Reason);
        Assert.AreEqual(FetchFailureClass.Network, rig.Attempts.Items.Single().FailureClass);
        Assert.IsFalse(result.Reason!.Contains("secret"));
    }

    [TestMethod]
    public void AttemptUriKeepsNoUserInfoQueryOrFragment()
    {
        Assert.AreEqual("https://legislation.example.test/act/1", HttpRawSourceFetcher.Sanitize("https://u:p@legislation.example.test/act/1?token=s#frag"));
        Assert.AreEqual("invalid:uri", HttpRawSourceFetcher.Sanitize("not a uri"));
    }

    // ---- real PostgreSQL: the whole composition against the real ledgers --------------------

    [TestMethod]
    public async Task PersistsThroughTheRealLedgers_DedupsAndIsTenantScoped()
    {
        if (Database() is not { } connectionString) return;
        var options = new DbContextOptionsBuilder<VeritasAtlasDbContext>().UseNpgsql(connectionString).Options;
        await using (var reset = new VeritasAtlasDbContext(options))
        {
            await reset.Database.EnsureDeletedAsync();
            await reset.Database.MigrateAsync();
        }
        await using var db = new VeritasAtlasDbContext(options);
        var tenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
        var source = (await new EfTenantSourceRegistry(db, TimeProvider.System).CreateAsync(Tenant, "https://legislation.example.test/act/1", "synthetic-seeder", CancellationToken.None))!.Id;
        var policies = new EfTenantSourceFetchPolicyRegistry(db, TimeProvider.System);
        var proposed = await policies.ProposeAsync(Tenant, source, new SourceFetchPolicy("v1", "gb", "statute", ["legislation.example.test", "mirror.example.test"], ["https"], [443], 3), true, "synthetic-seeder", CancellationToken.None);
        Assert.AreEqual(SourceFetchPolicyProposalOutcome.Created, proposed.Outcome, string.Join(",", proposed.Reasons));

        var rig = new Rig();
        var fetcher = new HttpRawSourceFetcher(
            new HttpClient(new Handler(rig)) { Timeout = Timeout.InfiniteTimeSpan }, new FakeResolver(rig.Dns), policies,
            new EfTenantFetchAttemptLedger(db, TimeProvider.System), new EfTenantRawRetrievalLedger(db, TimeProvider.System), TimeProvider.System);

        RawSourceFetchRequest Req(string key) => new(source, key, Start, Expect, Limits);
        var first = await fetcher.FetchAsync(Tenant, Req("pg1"), Actor, CancellationToken.None);
        var second = await fetcher.FetchAsync(Tenant, Req("pg2"), Actor, CancellationToken.None);
        Assert.AreEqual(RawSourceFetchStatus.Stored, first.Status);
        Assert.AreEqual(RawSourceFetchStatus.Duplicate, second.Status);
        Assert.AreEqual(first.RawRetrievalId, second.RawRetrievalId);
        Assert.AreEqual(1, await db.TenantRawRetrievals.AsNoTracking().CountAsync());
        Assert.AreEqual(2, await db.TenantFetchAttempts.AsNoTracking().CountAsync());

        var foreign = await fetcher.FetchAsync(tenantB, Req("pg3"), Actor, CancellationToken.None);
        Assert.AreEqual(RawSourceFetchStatus.SourceNotFound, foreign.Status);
        Assert.AreEqual(2, await db.TenantFetchAttempts.AsNoTracking().CountAsync());
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
        builder.Database += "_rawfetcher";
        return builder.ConnectionString;
    }
}
