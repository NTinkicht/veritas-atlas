using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// PostgreSQL-backed append-only collection of verified evidence into investigations
/// (VER-P0-10 slice 2, #210). Existence of both references is checked in the caller tenant
/// first so refusals are typed; the database guard trigger enforces the same rule. Each
/// collected item writes its <c>investigation.evidence_collected</c> audit event in the same
/// transaction.
/// </summary>
public sealed class EfTenantInvestigationEvidenceStore : ITenantInvestigationEvidenceStore
{
    private const string UniqueViolation = "23505";
    public const string CollectedAction = "investigation.evidence_collected";
    public const string AuditEntityType = "investigation_evidence_item";
    private readonly VeritasAtlasDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfTenantInvestigationEvidenceStore(VeritasAtlasDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<EvidenceItemCollectResult> CollectAsync(
        Guid tenantId, Guid investigationId, EvidenceItemInput input, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        ArgumentNullException.ThrowIfNull(input);
        if (!TenantInvestigationEvidenceLimits.IsValidNote(input.Note)
            || string.IsNullOrWhiteSpace(input.CollectedBySubject)
            || input.CollectedBySubject.Length > TenantInvestigationLimits.MaxSubjectLength
            || input.CollectedBySubject.Any(char.IsControl))
        {
            throw new ArgumentException("Invalid evidence item input.", nameof(input));
        }

        if (!await _db.TenantInvestigations.AsNoTracking()
                .AnyAsync(x => x.TenantId == tenantId && x.Id == investigationId, cancellationToken))
        {
            return new EvidenceItemCollectResult(EvidenceItemCollectOutcome.InvestigationNotFound, null);
        }

        if (await InvestigationStateQueries.IsClosedAsync(_db, tenantId, investigationId, cancellationToken))
        {
            return new EvidenceItemCollectResult(EvidenceItemCollectOutcome.InvestigationClosed, null);
        }

        if (!await _db.TenantEvidenceVerifications.AsNoTracking()
                .AnyAsync(x => x.TenantId == tenantId && x.Id == input.EvidenceVerificationId, cancellationToken))
        {
            return new EvidenceItemCollectResult(EvidenceItemCollectOutcome.EvidenceNotFound, null);
        }

        var existing = await FindAsync(tenantId, investigationId, input.EvidenceVerificationId, cancellationToken);
        if (existing is not null)
        {
            return Replay(existing, input);
        }

        var row = new TenantInvestigationEvidenceItem
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            InvestigationId = investigationId,
            EvidenceVerificationId = input.EvidenceVerificationId,
            Note = input.Note,
            CollectedBySubject = input.CollectedBySubject,
            CreatedAtUtc = ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime)
        };
        var audit = new TenantAuditEvent
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            EventKey = $"{CollectedAction}:{row.Id:D}",
            Action = CollectedAction,
            EntityType = AuditEntityType,
            EntityId = row.Id.ToString("D"),
            ActorSubject = row.CollectedBySubject,
            OccurredAtUtc = row.CreatedAtUtc
        };

        _db.TenantInvestigationEvidenceItems.Add(row);
        _db.TenantAuditEvents.Add(audit);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (InvestigationStateQueries.IsClosedViolation(ex))
        {
            _db.Entry(row).State = EntityState.Detached;
            _db.Entry(audit).State = EntityState.Detached;
            return new EvidenceItemCollectResult(EvidenceItemCollectOutcome.InvestigationClosed, null);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            _db.Entry(row).State = EntityState.Detached;
            _db.Entry(audit).State = EntityState.Detached;
            var winner = await FindAsync(tenantId, investigationId, input.EvidenceVerificationId, cancellationToken)
                ?? throw new InvalidOperationException("Collected item winner is not visible.", ex);
            return Replay(winner, input);
        }

        return new EvidenceItemCollectResult(EvidenceItemCollectOutcome.Collected, ToModel(row));
    }

    public async Task<EvidenceItemBatchResult> CollectBatchAsync(
        Guid tenantId, Guid investigationId, IReadOnlyList<EvidenceItemInput> inputs, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count is < 1 or > TenantInvestigationEvidenceLimits.MaxBatchSize
            || inputs.Select(x => x.EvidenceVerificationId).Distinct().Count() != inputs.Count
            || inputs.Any(x => x is null
                || !TenantInvestigationEvidenceLimits.IsValidNote(x.Note)
                || string.IsNullOrWhiteSpace(x.CollectedBySubject)
                || x.CollectedBySubject.Length > TenantInvestigationLimits.MaxSubjectLength
                || x.CollectedBySubject.Any(char.IsControl)))
        {
            throw new ArgumentException("Invalid evidence batch input.", nameof(inputs));
        }

        // One retry: a concurrent collect of the same evidence loses the unique race, and the
        // second pass then sees the winner and replays it.
        for (var attempt = 0; ; attempt++)
        {
            var added = new List<object>();
            try
            {
                return await TryCollectBatchAsync(tenantId, investigationId, inputs, added, cancellationToken);
            }
            catch (DbUpdateException ex) when (InvestigationStateQueries.IsClosedViolation(ex))
            {
                Detach(added);
                return new EvidenceItemBatchResult(EvidenceItemCollectOutcome.InvestigationClosed, null, null);
            }
            catch (DbUpdateException ex) when (attempt == 0 && ex.InnerException is PostgresException { SqlState: UniqueViolation })
            {
                Detach(added);
            }
        }
    }

    private async Task<EvidenceItemBatchResult> TryCollectBatchAsync(
        Guid tenantId, Guid investigationId, IReadOnlyList<EvidenceItemInput> inputs, List<object> added,
        CancellationToken cancellationToken)
    {
        if (!await _db.TenantInvestigations.AsNoTracking()
                .AnyAsync(x => x.TenantId == tenantId && x.Id == investigationId, cancellationToken))
        {
            return new EvidenceItemBatchResult(EvidenceItemCollectOutcome.InvestigationNotFound, null, null);
        }

        if (await InvestigationStateQueries.IsClosedAsync(_db, tenantId, investigationId, cancellationToken))
        {
            return new EvidenceItemBatchResult(EvidenceItemCollectOutcome.InvestigationClosed, null, null);
        }

        var ids = inputs.Select(x => x.EvidenceVerificationId).ToList();
        var known = (await _db.TenantEvidenceVerifications.AsNoTracking()
            .Where(x => x.TenantId == tenantId && ids.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken)).ToHashSet();
        var missing = inputs.ToList().FindIndex(x => !known.Contains(x.EvidenceVerificationId));
        if (missing >= 0)
        {
            return new EvidenceItemBatchResult(EvidenceItemCollectOutcome.EvidenceNotFound, null, missing);
        }

        var existing = (await _db.TenantInvestigationEvidenceItems.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.InvestigationId == investigationId
                && ids.Contains(x.EvidenceVerificationId))
            .ToListAsync(cancellationToken)).ToDictionary(x => x.EvidenceVerificationId);
        for (var i = 0; i < inputs.Count; i++)
        {
            if (existing.TryGetValue(inputs[i].EvidenceVerificationId, out var row) && row.Note != inputs[i].Note)
            {
                return new EvidenceItemBatchResult(EvidenceItemCollectOutcome.NoteConflict, null, i);
            }
        }

        var now = ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime);
        var entries = new List<EvidenceItemBatchEntry>(inputs.Count);
        foreach (var input in inputs)
        {
            if (existing.TryGetValue(input.EvidenceVerificationId, out var replayed))
            {
                entries.Add(new EvidenceItemBatchEntry(ToModel(replayed), true));
                continue;
            }

            var row = new TenantInvestigationEvidenceItem
            {
                TenantId = tenantId,
                Id = Guid.NewGuid(),
                InvestigationId = investigationId,
                EvidenceVerificationId = input.EvidenceVerificationId,
                Note = input.Note,
                CollectedBySubject = input.CollectedBySubject,
                CreatedAtUtc = now
            };
            var audit = new TenantAuditEvent
            {
                TenantId = tenantId,
                Id = Guid.NewGuid(),
                EventKey = $"{CollectedAction}:{row.Id:D}",
                Action = CollectedAction,
                EntityType = AuditEntityType,
                EntityId = row.Id.ToString("D"),
                ActorSubject = row.CollectedBySubject,
                OccurredAtUtc = now
            };
            _db.TenantInvestigationEvidenceItems.Add(row);
            _db.TenantAuditEvents.Add(audit);
            added.Add(row);
            added.Add(audit);
            entries.Add(new EvidenceItemBatchEntry(ToModel(row), false));
        }

        if (added.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new EvidenceItemBatchResult(EvidenceItemCollectOutcome.Collected, entries, null);
    }

    private void Detach(List<object> added)
    {
        foreach (var entity in added)
        {
            _db.Entry(entity).State = EntityState.Detached;
        }
    }

    public async Task<IReadOnlyList<TenantInvestigationEvidenceItemModel>?> ListAsync(
        Guid tenantId, Guid investigationId, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        if (!await _db.TenantInvestigations.AsNoTracking()
                .AnyAsync(x => x.TenantId == tenantId && x.Id == investigationId, cancellationToken))
        {
            return null;
        }

        var rows = await _db.TenantInvestigationEvidenceItems.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.InvestigationId == investigationId)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    public async Task<TenantInvestigationEvidenceComparisonModel?> CompareAsync(
        Guid tenantId, Guid investigationId, Guid leftItemId, Guid rightItemId, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        if (leftItemId == rightItemId)
        {
            throw new ArgumentException("Two distinct items are required.", nameof(rightItemId));
        }

        var rows = await (
            from item in _db.TenantInvestigationEvidenceItems.AsNoTracking()
            join v in _db.TenantEvidenceVerifications.AsNoTracking()
                on new { item.TenantId, Id = item.EvidenceVerificationId } equals new { v.TenantId, v.Id }
            where item.TenantId == tenantId && item.InvestigationId == investigationId
                && (item.Id == leftItemId || item.Id == rightItemId)
            select new ComparedEvidenceModel(
                item.Id, v.Id, item.Note, v.ParentEvidenceRef, v.RawRetrievalId, v.AttestationId,
                v.ParseRunId, v.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var left = rows.SingleOrDefault(x => x.ItemId == leftItemId);
        var right = rows.SingleOrDefault(x => x.ItemId == rightItemId);
        if (left is null || right is null)
        {
            return null;
        }

        left = left with { VerifiedAtUtc = DateTime.SpecifyKind(left.VerifiedAtUtc, DateTimeKind.Utc) };
        right = right with { VerifiedAtUtc = DateTime.SpecifyKind(right.VerifiedAtUtc, DateTimeKind.Utc) };
        return new TenantInvestigationEvidenceComparisonModel(
            left, right,
            string.Equals(left.ParentEvidenceRef, right.ParentEvidenceRef, StringComparison.Ordinal),
            left.RawRetrievalId == right.RawRetrievalId,
            left.AttestationId == right.AttestationId,
            left.ParseRunId == right.ParseRunId);
    }

    private Task<TenantInvestigationEvidenceItem?> FindAsync(
        Guid tenantId, Guid investigationId, Guid verificationId, CancellationToken cancellationToken) =>
        _db.TenantInvestigationEvidenceItems.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.InvestigationId == investigationId
                && x.EvidenceVerificationId == verificationId,
            cancellationToken);

    private static EvidenceItemCollectResult Replay(TenantInvestigationEvidenceItem existing, EvidenceItemInput input) =>
        existing.Note == input.Note
            ? new EvidenceItemCollectResult(EvidenceItemCollectOutcome.Replayed, ToModel(existing))
            : new EvidenceItemCollectResult(EvidenceItemCollectOutcome.NoteConflict, null);

    private static void RequireTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant is required.", nameof(tenantId));
        }
    }

    // PostgreSQL stores microseconds: keep a created result identical to a later read.
    private static DateTime ToPostgresPrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);

    private static TenantInvestigationEvidenceItemModel ToModel(TenantInvestigationEvidenceItem row) =>
        new(row.Id, row.InvestigationId, row.EvidenceVerificationId, row.Note, row.CollectedBySubject,
            DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc));
}
