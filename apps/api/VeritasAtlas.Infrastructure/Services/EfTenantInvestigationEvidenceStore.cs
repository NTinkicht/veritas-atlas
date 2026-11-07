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
