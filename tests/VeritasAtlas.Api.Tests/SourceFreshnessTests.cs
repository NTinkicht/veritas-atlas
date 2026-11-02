using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-08 (#158): the .NET evaluator must return exactly the states, eligibility and reasons of
/// tests/fixtures/source_freshness_vectors_v1.json and the projections of
/// tests/fixtures/source_freshness_query_vectors_v1.json, the same files the Python tests run.
/// </summary>
[TestClass]
public sealed class SourceFreshnessTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VeritasAtlas.slnx")))
            directory = directory.Parent;
        Assert.IsNotNull(directory, "repository root (VeritasAtlas.slnx) not found");
        return directory.FullName;
    }

    private static JsonElement Load(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "tests", "fixtures", name))).RootElement;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Flag(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>Strict JSON typing: a number where a boolean belongs is NOT a boolean (no 1 == true).</summary>
    private static SourceFreshnessPolicy ToPolicy(JsonElement p) => new(
        Str(p, "policy_id"),
        Str(p, "policy_version"),
        Str(p, "source_match"),
        Bool(p, "freshness_required_for_current_claims"),
        Long(p, "max_retrieval_age_seconds"),
        Str(p, "revision_signal"),
        Bool(p, "revision_signal_required"),
        Str(p, "replacement_authority"));

    private static bool? Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;

    private static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;

    /// <summary>Applies a policy_override object the way the Python reference does (type-exact).</summary>
    private static SourceFreshnessPolicy Override(JsonElement canonical, JsonElement? overrides)
    {
        var map = new Dictionary<string, JsonElement>();
        foreach (var property in canonical.EnumerateObject()) map[property.Name] = property.Value;
        if (overrides is { ValueKind: JsonValueKind.Object } o)
            foreach (var property in o.EnumerateObject()) map[property.Name] = property.Value;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(map));
        return ToPolicy(doc.RootElement);
    }

    private static string Name(FreshnessState s) => s.ToString().ToUpperInvariant();

    [TestMethod]
    public void EveryClassificationVectorMatchesTheReferenceVerifier()
    {
        var doc = Load("source_freshness_vectors_v1.json");
        Assert.AreEqual("source-freshness-vectors/v1", doc.GetProperty("schema_version").GetString());
        var cases = doc.GetProperty("cases").EnumerateArray().ToList();
        Assert.IsTrue(cases.Count >= 19);
        Assert.AreEqual(cases.Count, cases.Select(c => c.GetProperty("id").GetString()).Distinct().Count());

        foreach (var c in cases)
        {
            var id = c.GetProperty("id").GetString();
            var canonical = doc.GetProperty("policies").GetProperty(c.GetProperty("policy").GetString()!);
            var registry = ToPolicy(canonical);
            var asserted = Override(canonical, c.TryGetProperty("policy_override", out var po) ? po : null);
            var evidence = new SourceFreshnessEvidence(
                Str(c, "retrieved_at"), Flag(c, "revision_verified"), Flag(c, "replacement_proven"),
                Flag(c, "replacement_authority_verified"));
            SourceFreshnessClaimPolicy? claim = null;
            if (c.TryGetProperty("claim_policy", out var cp))
                claim = new(Flag(cp, "historical_evidence_allowed"), Flag(cp, "historical_time_context_preserved"));

            var d = SourceFreshnessEvaluator.Evaluate(registry, asserted, evidence, Str(c, "evaluated_at"), claim);

            Assert.AreEqual(c.GetProperty("expected_state").GetString(), Name(d.State), id);
            Assert.AreEqual(c.GetProperty("expected_eligible").GetBoolean(), d.Eligible, id);
            Assert.AreEqual(Str(c, "reason"), d.Reason, id);
        }
    }

    [TestMethod]
    public void EveryQueryProjectionVectorMatchesTheReferenceVerifier()
    {
        var source = Load("source_freshness_vectors_v1.json");
        var doc = Load("source_freshness_query_vectors_v1.json");
        Assert.AreEqual("source-freshness-query-v1", doc.GetProperty("schema_version").GetString());
        var cases = doc.GetProperty("cases").EnumerateArray().ToList();
        Assert.IsTrue(cases.Count >= 10);

        foreach (var c in cases)
        {
            var id = c.GetProperty("id").GetString();
            var unverified = Strings(c, "revision_unverified_ids");
            var superseded = Strings(c, "superseded_ids");
            var records = doc.GetProperty("records").EnumerateArray().Select(r =>
            {
                var canonical = source.GetProperty("policies").GetProperty(r.GetProperty("policy").GetString()!);
                JsonElement? overrides = null;
                if (c.TryGetProperty("policy_version_override", out var pv) && pv.ValueKind == JsonValueKind.String)
                    overrides = JsonDocument.Parse(JsonSerializer.Serialize(new { policy_version = pv.GetString() })).RootElement;
                var rid = r.GetProperty("id").GetString()!;
                return new SourceFreshnessQueryRecord(
                    rid, r.GetProperty("category").GetString()!,
                    new SourceFreshnessEvidence(
                        Str(r, "retrieved_at"), !unverified.Contains(rid), superseded.Contains(rid), superseded.Contains(rid)),
                    ToPolicy(canonical), Override(canonical, overrides));
            }).ToList();

            FreshnessState? filter = Str(c, "freshness_filter") is { } f
                ? Enum.Parse<FreshnessState>(f, ignoreCase: true)
                : null;
            var pageSize = c.TryGetProperty("page_size", out var ps) ? ps.GetInt32() : Math.Max(records.Count, 1);
            var pageIndex = c.TryGetProperty("page_index", out var pi) ? pi.GetInt32() : 0;

            var result = SourceFreshnessQueryProjector.Project(records, Str(c, "evaluated_at"), filter, pageSize, pageIndex);

            CollectionAssert.AreEqual(Strings(c, "expected_ids"), result.Ids.ToList(), id);
            Assert.AreEqual(c.GetProperty("expected_total").GetInt32(), result.Total, id);
            var expectedFacets = c.GetProperty("expected_facets").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());
            CollectionAssert.AreEquivalent(expectedFacets, result.Facets.ToDictionary(p => p.Key, p => p.Value), id);
            if (c.TryGetProperty("expected_states", out var es))
                foreach (var p in es.EnumerateObject())
                    Assert.AreEqual(p.Value.GetString(), Name(result.States[p.Name]), $"{id}/{p.Name}");
        }
    }

    private static List<string> Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.EnumerateArray().Select(x => x.GetString()!).ToList() : new();

    private static readonly SourceFreshnessPolicy P1 = new(
        "public-record-source-policy", "1", "registry:public-records", true, 86400, "source-version-id", true,
        "adapter:public-records");

    private const string Retrieved = "2026-10-01T00:00:00Z";

    private static SourceFreshnessEvidence Verified(string? retrieved = Retrieved) => new(retrieved, true, false, false);

    private static FreshnessState At(string evaluated, SourceFreshnessEvidence? e = null) =>
        SourceFreshnessEvaluator.Evaluate(P1, P1, e ?? Verified(), evaluated).State;

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [TestMethod]
    public void ServiceTakesTheEvaluationInstantFromTheServerClockOnly()
    {
        var clock = new FixedClock(DateTimeOffset.Parse("2026-10-02T00:00:00Z"));
        var service = new SourceFreshnessService(clock);

        var atBoundary = service.Evaluate(P1, P1, Verified());
        Assert.AreEqual(FreshnessState.Current, atBoundary.State);
        Assert.AreEqual(86400, atBoundary.RetrievalAgeSeconds);
        Assert.AreEqual("2026-10-02T00:00:00.0000000Z", atBoundary.EvaluatedAt);

        clock.Now = clock.Now.AddSeconds(1);
        Assert.AreEqual(FreshnessState.Stale, service.Evaluate(P1, P1, Verified()).State);

        // A non-UTC server offset is canonicalized, never trusted as local time.
        clock.Now = new DateTimeOffset(2026, 10, 2, 2, 0, 0, TimeSpan.FromHours(2));
        Assert.AreEqual(FreshnessState.Current, service.Evaluate(P1, P1, Verified()).State);
    }

    [TestMethod]
    public void ReplayUsesTheRecordedInstantNotTheAmbientClock()
    {
        var recorded = SourceFreshnessEvaluator.Evaluate(P1, P1, Verified(), "2026-10-01T12:00:00Z");
        Assert.AreEqual(FreshnessState.Current, recorded.State);
        var replay = SourceFreshnessService.Replay(P1, P1, Verified(), recorded.EvaluatedAt!);
        Assert.AreEqual(recorded, replay);
    }

    [TestMethod]
    public void MissingOrUnparseableEvaluationInstantIsUnknown()
    {
        foreach (var bad in new string?[] { null, "", "2026-10-02", "2026-10-02T00:00:00+01:00", "2026-10-02T00:00:00z", "2026-10-02T00:00:00Z\n", "２０２６-10-02T00:00:00Z" })
            Assert.AreEqual(FreshnessState.Unknown, At(bad!), bad ?? "null");
    }

    [TestMethod]
    public void AgeIsMonotonicallyDegradingAsTimeAdvances()
    {
        // Sweep one second at a time across the boundary: Current is a prefix, then Stale forever.
        var start = DateTimeOffset.Parse(Retrieved);
        var sawStale = false;
        for (var s = 0; s <= 86400 + 600; s += s is >= 86390 and <= 86410 ? 1 : 977)
        {
            var state = At(SourceFreshnessEvaluator.Canonicalize(start.AddSeconds(s)));
            if (state == FreshnessState.Stale) sawStale = true;
            Assert.AreEqual(s <= 86400 ? FreshnessState.Current : FreshnessState.Stale, state, $"t+{s}");
            Assert.IsFalse(sawStale && state == FreshnessState.Current, $"regressed to Current at t+{s}");
        }
        Assert.IsTrue(sawStale);
    }

    [TestMethod]
    public void NoInputProducesCurrentWithoutAVerifiedRevision()
    {
        var retrievedValues = new string?[] { Retrieved, null, "bad", "2026-10-01T00:00:00.9Z", "2026-10-02T00:00:00Z" };
        var evaluatedValues = new string?[] { Retrieved, "2026-10-01T00:00:00.5Z", "2026-10-01T12:00:00Z", "2026-10-02T00:00:00Z", "2026-10-03T00:00:00Z", null };
        foreach (var retrieved in retrievedValues)
        foreach (var evaluated in evaluatedValues)
        foreach (var authorityVerified in new[] { true, false })
        {
            // Revision NOT verified, replacement not proven: must never be Current.
            var unverified = new SourceFreshnessEvidence(retrieved, false, false, authorityVerified);
            Assert.AreNotEqual(FreshnessState.Current, At(evaluated!, unverified), $"{retrieved}|{evaluated}");
            // A replacement proven by an UNVERIFIED authority never supersedes, and never yields Current
            // without revision verification either.
            var claimedOnly = new SourceFreshnessEvidence(retrieved, false, true, false);
            Assert.AreNotEqual(FreshnessState.Current, At(evaluated!, claimedOnly));
            Assert.AreNotEqual(FreshnessState.Superseded, At(evaluated!, claimedOnly));
        }
    }

    [TestMethod]
    public void UnsupportedOrIncompletePolicyNeverClassifiesAsCurrentAndFailsClosed()
    {
        var versions = new[] { P1 with { PolicyVersion = "2" }, P1 with { MaxRetrievalAgeSeconds = 999999 }, P1 with { PolicyId = null }, P1 with { FreshnessRequiredForCurrentClaims = null } };
        foreach (var asserted in versions)
        {
            var d = SourceFreshnessEvaluator.Evaluate(P1, asserted, Verified(), "2026-10-01T01:00:00Z");
            Assert.AreEqual(FreshnessState.Unknown, d.State);
            Assert.IsFalse(d.Eligible);
            // A replacement claimed under an unsupported policy cannot supersede.
            var withReplacement = SourceFreshnessEvaluator.Evaluate(
                P1, asserted, new(Retrieved, true, true, true), "2026-10-01T01:00:00Z");
            Assert.AreEqual(FreshnessState.Unknown, withReplacement.State);
        }

        var noRegistry = SourceFreshnessEvaluator.Evaluate(null, P1, Verified(), "2026-10-01T01:00:00Z");
        Assert.AreEqual(FreshnessState.Unknown, noRegistry.State);
        Assert.AreEqual(SourceFreshnessReasons.FreshnessRequiredNonCurrent, noRegistry.Reason);

        var negative = P1 with { MaxRetrievalAgeSeconds = -1 };
        Assert.AreEqual(FreshnessState.Unknown,
            SourceFreshnessEvaluator.Evaluate(negative, negative, Verified(), "2026-10-01T01:00:00Z").State);
    }

    [TestMethod]
    public void HistoricalEvidenceCarriesItsAsOfContextAndNeedsBothFlags()
    {
        var both = new SourceFreshnessClaimPolicy(true, true);
        var stale = SourceFreshnessEvaluator.Evaluate(P1, P1, Verified(), "2026-10-02T00:00:01Z", both);
        Assert.AreEqual(FreshnessState.Stale, stale.State);
        Assert.IsTrue(stale.Eligible);
        Assert.AreEqual("2026-10-02T00:00:01Z", stale.HistoricalAsOf);

        foreach (var partial in new[] { new SourceFreshnessClaimPolicy(true, false), new SourceFreshnessClaimPolicy(false, true), SourceFreshnessClaimPolicy.CurrentOnly })
        {
            var d = SourceFreshnessEvaluator.Evaluate(P1, P1, Verified(), "2026-10-02T00:00:01Z", partial);
            Assert.IsFalse(d.Eligible);
            Assert.IsNull(d.HistoricalAsOf);
        }

        // Current evidence is not "historical": no as-of marker is attached.
        Assert.IsNull(SourceFreshnessEvaluator.Evaluate(P1, P1, Verified(), "2026-10-01T01:00:00Z", both).HistoricalAsOf);

        // Without a valid recorded instant there is no as-of context, so history is not returned.
        var noInstant = SourceFreshnessEvaluator.Evaluate(P1, P1, Verified(), null, both);
        Assert.IsFalse(noInstant.Eligible);
        Assert.AreEqual(SourceFreshnessReasons.HistoricalContextRequired, noInstant.Reason);
    }

    [TestMethod]
    public void HostileTimestampsAreBoundedAndDoNotThrow()
    {
        var huge = "2026-10-01T00:00:00." + new string('9', 1001) + "Z";
        Assert.AreEqual(FreshnessState.Unknown, At("2026-10-02T00:00:00Z", Verified(huge)));
        var wide = "2026-10-01T00:00:00." + new string('0', 999) + "1Z";
        Assert.AreEqual(FreshnessState.Current, At("2026-10-02T00:00:00Z", Verified(wide)));
        Assert.AreEqual(FreshnessState.Unknown, At("2026-13-45T00:00:00Z"));
        Assert.AreEqual(FreshnessState.Unknown, At("0000-01-01T00:00:00Z"));
    }
}
