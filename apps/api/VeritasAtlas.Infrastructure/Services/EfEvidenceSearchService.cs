using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// VER-P0-09 (#159): tenant-scoped PostgreSQL full-text search over VERIFIED evidence only. Candidate
/// references come from parsed chunks that carry an append-only verification row of the caller's
/// tenant, so raw or unparsed records are never reachable. Freshness is re-evaluated for every hit
/// with the server clock (<see cref="IEvidenceFreshnessService"/>) before any freshness filter is
/// applied. Pages are keyset-paged on the evidence reference; the cursor binds the tenant and the
/// exact filter set, and any mismatch is the same uniform invalid-request outcome as tampering.
/// The cursor carries no authority: it only chooses a position inside the caller's own tenant.
/// </summary>
public sealed class EfEvidenceSearchService : IEvidenceSearchService
{
    private const int MaxBatches = 8;

    private readonly VeritasAtlasDbContext _db;
    private readonly IEvidenceFreshnessService _freshness;

    public EfEvidenceSearchService(VeritasAtlasDbContext db, IEvidenceFreshnessService freshness)
    {
        _db = db;
        _freshness = freshness;
    }

    public async Task<EvidenceSearchPage> SearchAsync(
        Guid tenantId, EvidenceSearchRequest request, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (!IsValid(request)) return Invalid();

        var fingerprint = Fingerprint(tenantId, request);
        string? after = null;
        if (request.Cursor is not null && !TryReadCursor(request.Cursor, fingerprint, out after)) return Invalid();

        var query = request.Query.Trim();
        var hits = new List<EvidenceSearchHit>();
        var batchSize = Math.Min(EvidenceSearchLimits.MaxPageSize, request.PageSize * 2);
        string? lastScanned = after;
        var exhausted = false;

        for (var batch = 0; batch < MaxBatches && hits.Count <= request.PageSize; batch++)
        {
            var refs = await (
                from c in _db.TenantEvidenceChunks.AsNoTracking()
                join v in _db.TenantEvidenceVerifications.AsNoTracking()
                    on new { c.TenantId, c.ParseRunId } equals new { v.TenantId, v.ParseRunId }
                where c.TenantId == tenantId
                    && EF.Functions.ToTsVector("simple", c.Text).Matches(EF.Functions.PlainToTsQuery("simple", query))
                    && (lastScanned == null || v.ParentEvidenceRef.CompareTo(lastScanned) > 0)
                select v.ParentEvidenceRef)
                .Distinct().OrderBy(r => r).Take(batchSize)
                .ToListAsync(cancellationToken);
            if (refs.Count < batchSize) exhausted = true;
            if (refs.Count == 0) break;

            var latest = await LoadLatestAsync(tenantId, refs, cancellationToken);
            foreach (var evidenceRef in refs)
            {
                lastScanned = evidenceRef;
                if (!latest.TryGetValue(evidenceRef, out var row) || !MatchesFilters(request, row)) continue;

                // Freshness is always re-evaluated now, never read from an index or an earlier page.
                var evaluated = await _freshness.EvaluateAsync(tenantId, evidenceRef, null, cancellationToken);
                if (evaluated.Outcome != EvidenceFreshnessOutcome.Evaluated || evaluated.Decision is null) continue;
                var decision = evaluated.Decision;
                if (request.Freshness is { } wanted && decision.State != wanted) continue;

                hits.Add(new EvidenceSearchHit(
                    evidenceRef, row.FinalUri, row.SourceEntryId, row.Jurisdiction, row.SourceType,
                    row.AuthenticationResult, DateTime.SpecifyKind(row.RetrievedAtUtc, DateTimeKind.Utc),
                    decision.State, decision.Eligible, decision.Reason, decision.EvaluatedAt));
                if (hits.Count > request.PageSize) break;
            }

            if (exhausted) break;
        }

        string? next = null;
        if (hits.Count > request.PageSize)
        {
            hits.RemoveAt(hits.Count - 1);
            next = WriteCursor(fingerprint, hits[^1].EvidenceRef);
        }
        else if (!exhausted && lastScanned is not null)
        {
            next = WriteCursor(fingerprint, lastScanned);
        }

        return new EvidenceSearchPage(EvidenceSearchOutcome.Ok, hits, next);
    }

    private sealed record LatestRow(
        string FinalUri, Guid SourceEntryId, string Jurisdiction, string SourceType,
        string AuthenticationResult, DateTime RetrievedAtUtc);

    private async Task<Dictionary<string, LatestRow>> LoadLatestAsync(
        Guid tenantId, List<string> refs, CancellationToken cancellationToken)
    {
        var rows = await (
            from v in _db.TenantEvidenceVerifications.AsNoTracking()
            join r in _db.TenantRawRetrievals.AsNoTracking()
                on new { v.TenantId, Id = v.RawRetrievalId } equals new { r.TenantId, r.Id }
            join a in _db.TenantRetrievalAttestations.AsNoTracking()
                on new { v.TenantId, Id = v.AttestationId } equals new { a.TenantId, a.Id }
            where v.TenantId == tenantId && refs.Contains(v.ParentEvidenceRef)
            select new
            {
                v.ParentEvidenceRef, r.Id, r.FinalUri, r.SourceEntryId, r.RetrievedAtUtc,
                a.Jurisdiction, a.SourceType, a.AuthenticationResult
            }).ToListAsync(cancellationToken);

        // Same rule as the freshness service: the most recently retrieved verified copy is authoritative.
        return rows
            .GroupBy(x => x.ParentEvidenceRef)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var best = g.OrderByDescending(x => x.RetrievedAtUtc).ThenBy(x => x.Id).First();
                    return new LatestRow(
                        best.FinalUri, best.SourceEntryId, best.Jurisdiction, best.SourceType,
                        best.AuthenticationResult, best.RetrievedAtUtc);
                });
    }

    private static bool MatchesFilters(EvidenceSearchRequest request, LatestRow row) =>
        (request.SourceEntryId is null || row.SourceEntryId == request.SourceEntryId)
        && (request.Jurisdiction is null || string.Equals(row.Jurisdiction, request.Jurisdiction, StringComparison.OrdinalIgnoreCase))
        && (request.SourceType is null || string.Equals(row.SourceType, request.SourceType, StringComparison.OrdinalIgnoreCase))
        && (request.RetrievedFromUtc is null || row.RetrievedAtUtc >= request.RetrievedFromUtc)
        && (request.RetrievedToUtc is null || row.RetrievedAtUtc <= request.RetrievedToUtc);

    private static bool IsValid(EvidenceSearchRequest r) =>
        !string.IsNullOrWhiteSpace(r.Query)
        && r.Query.Length <= EvidenceSearchLimits.MaxQueryLength
        && !r.Query.Any(char.IsControl)
        && r.PageSize is >= 1 and <= EvidenceSearchLimits.MaxPageSize
        && ValidFilter(r.Jurisdiction) && ValidFilter(r.SourceType)
        && (r.RetrievedFromUtc is null || r.RetrievedToUtc is null || r.RetrievedFromUtc <= r.RetrievedToUtc)
        && (r.Freshness is null || Enum.IsDefined(r.Freshness.Value))
        && (r.Cursor is null || (r.Cursor.Length <= EvidenceSearchLimits.MaxCursorLength && r.Cursor.Length > 0));

    private static bool ValidFilter(string? value) =>
        value is null || (value.Length is >= 1 and <= EvidenceSearchLimits.MaxFilterLength && !value.Any(char.IsControl));

    private static string Fingerprint(Guid tenantId, EvidenceSearchRequest r)
    {
        var text = string.Join(
            "\u001f",
            tenantId.ToString("D"), r.Query.Trim(), r.SourceEntryId?.ToString("D") ?? "",
            r.Jurisdiction?.ToLowerInvariant() ?? "", r.SourceType?.ToLowerInvariant() ?? "",
            r.RetrievedFromUtc?.ToString("O") ?? "", r.RetrievedToUtc?.ToString("O") ?? "",
            r.Freshness?.ToString() ?? "", r.PageSize);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static string WriteCursor(string fingerprint, string evidenceRef) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(fingerprint + "|" + evidenceRef))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryReadCursor(string cursor, string fingerprint, out string? evidenceRef)
    {
        evidenceRef = null;
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var parts = text.Split('|', 2);
            if (parts.Length != 2 || parts[0] != fingerprint) return false;
            if (parts[1].Length is 0 or > 512 || parts[1].Any(char.IsControl)) return false;
            evidenceRef = parts[1];
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static EvidenceSearchPage Invalid() => new(EvidenceSearchOutcome.InvalidRequest, [], null);
}
