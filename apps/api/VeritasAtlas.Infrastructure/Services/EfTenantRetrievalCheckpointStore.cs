using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// PostgreSQL-backed retrieval checkpoint store (VER-P0-06 follow-up, #167). The decision is made
/// by <see cref="RetrievalCheckpointAdvance"/>; the write is a single guarded statement (insert of
/// sequence 1, or an update conditional on the exact checkpoint that was read), so two racing
/// writers cannot both advance. The database repeats the rules (one-step advance, no delete,
/// same-tenant same-source raw retrieval) as a second line of defence.
/// </summary>
public sealed class EfTenantRetrievalCheckpointStore : ITenantRetrievalCheckpointStore
{
    private const string UniqueViolation = "23505";
    private readonly VeritasAtlasDbContext _db;

    public EfTenantRetrievalCheckpointStore(VeritasAtlasDbContext db) => _db = db;

    public async Task<TenantRetrievalCheckpointModel?> GetAsync(
        Guid tenantId, Guid sourceEntryId, CancellationToken cancellationToken)
    {
        RequireIds(tenantId, sourceEntryId);
        var row = await _db.TenantRetrievalCheckpoints.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.SourceEntryId == sourceEntryId, cancellationToken);
        return row is null ? null : ToModel(row);
    }

    public async Task<RetrievalCheckpointAdvanceResult> AdvanceAsync(
        Guid tenantId, Guid sourceEntryId, long sequence, Guid rawRetrievalId, DateTime completedAtUtc,
        CancellationToken cancellationToken)
    {
        RequireIds(tenantId, sourceEntryId);
        if (rawRetrievalId == Guid.Empty)
        {
            throw new ArgumentException("A raw retrieval is required.", nameof(rawRetrievalId));
        }

        // Persistence check: the retrieval must exist for this tenant AND this source.
        var key = await _db.TenantRawRetrievals.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.SourceEntryId == sourceEntryId && x.Id == rawRetrievalId)
            .Select(x => x.IdempotencyKey)
            .SingleOrDefaultAsync(cancellationToken);
        if (key is null)
        {
            return new(RetrievalCheckpointAdvanceOutcome.RetrievalNotFound, null, null);
        }

        var stored = await _db.TenantRetrievalCheckpoints.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.SourceEntryId == sourceEntryId, cancellationToken);
        var current = stored is null ? null : new RetrievalCheckpoint(
            stored.Sequence, stored.Key, DateTime.SpecifyKind(stored.CompletedAtUtc, DateTimeKind.Utc));
        var completed = completedAtUtc.Kind == DateTimeKind.Utc
            ? new DateTime(completedAtUtc.Ticks - completedAtUtc.Ticks % 10, DateTimeKind.Utc)
            : completedAtUtc;

        var decision = RetrievalCheckpointAdvance.Decide(
            current, new RetrievalCheckpointProposal(sequence, key, completed, RawRecordPersisted: true));
        switch (decision.Action)
        {
            case RetrievalCheckpointAction.Refused:
                return new(RetrievalCheckpointAdvanceOutcome.Refused, decision.Reason, stored is null ? null : ToModel(stored));
            case RetrievalCheckpointAction.Replay:
                // The pure rule compares key and time only; the same sequence must also be the same retrieval.
                return stored!.RawRetrievalId == rawRetrievalId
                    ? new(RetrievalCheckpointAdvanceOutcome.Replayed, null, ToModel(stored))
                    : new(RetrievalCheckpointAdvanceOutcome.Refused, "sequence_key_conflict", ToModel(stored));
        }

        try
        {
            if (stored is null)
            {
                _db.TenantRetrievalCheckpoints.Add(new TenantRetrievalCheckpoint
                {
                    TenantId = tenantId, SourceEntryId = sourceEntryId, Sequence = sequence,
                    RawRetrievalId = rawRetrievalId, Key = key, CompletedAtUtc = completed
                });
                await _db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                var expected = stored.Sequence;
                var changed = await _db.TenantRetrievalCheckpoints
                    .Where(x => x.TenantId == tenantId && x.SourceEntryId == sourceEntryId && x.Sequence == expected)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.Sequence, sequence)
                        .SetProperty(x => x.RawRetrievalId, rawRetrievalId)
                        .SetProperty(x => x.Key, key)
                        .SetProperty(x => x.CompletedAtUtc, completed), cancellationToken);
                if (changed == 0)
                {
                    return new(RetrievalCheckpointAdvanceOutcome.ConcurrentUpdate, null, null);
                }
            }
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            _db.ChangeTracker.Clear();
            return new(RetrievalCheckpointAdvanceOutcome.ConcurrentUpdate, null, null);
        }

        return new(RetrievalCheckpointAdvanceOutcome.Advanced, null,
            new TenantRetrievalCheckpointModel(sourceEntryId, sequence, rawRetrievalId, key, completed));
    }

    private static void RequireIds(Guid tenantId, Guid sourceEntryId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant is required.", nameof(tenantId));
        }

        if (sourceEntryId == Guid.Empty)
        {
            throw new ArgumentException("A source is required.", nameof(sourceEntryId));
        }
    }

    private static TenantRetrievalCheckpointModel ToModel(TenantRetrievalCheckpoint row) =>
        new(row.SourceEntryId, row.Sequence, row.RawRetrievalId, row.Key,
            DateTime.SpecifyKind(row.CompletedAtUtc, DateTimeKind.Utc));
}
