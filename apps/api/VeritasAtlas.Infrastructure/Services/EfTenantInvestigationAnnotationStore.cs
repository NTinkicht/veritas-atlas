using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// PostgreSQL-backed append-only annotations on collected evidence items (VER-P0-10 slice 4).
/// The item is resolved inside the caller tenant first so another tenant's ids are
/// indistinguishable from unknown ones; the database guard trigger enforces the same rule. Each
/// annotation writes its <c>investigation.annotation_added</c> audit event in the same transaction.
/// </summary>
public sealed class EfTenantInvestigationAnnotationStore : ITenantInvestigationAnnotationStore
{
    private const string UniqueViolation = "23505";
    public const string AddedAction = "investigation.annotation_added";
    public const string AuditEntityType = "investigation_annotation";
    private readonly VeritasAtlasDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfTenantInvestigationAnnotationStore(VeritasAtlasDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<AnnotationAddResult> AddAsync(
        Guid tenantId, Guid investigationId, Guid evidenceItemId, AnnotationInput input,
        CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        ArgumentNullException.ThrowIfNull(input);
        if (!TenantInvestigationAnnotationLimits.IsValidText(input.Text)
            || string.IsNullOrWhiteSpace(input.AuthorSubject)
            || input.AuthorSubject.Length > TenantInvestigationLimits.MaxSubjectLength
            || input.AuthorSubject.Any(char.IsControl))
        {
            throw new ArgumentException("Invalid annotation input.", nameof(input));
        }

        if (!await ItemExistsAsync(tenantId, investigationId, evidenceItemId, cancellationToken))
        {
            return new AnnotationAddResult(AnnotationAddOutcome.NotFound, null);
        }

        var existing = await FindAsync(tenantId, evidenceItemId, input, cancellationToken);
        if (existing is not null)
        {
            return new AnnotationAddResult(AnnotationAddOutcome.Replayed, ToModel(existing));
        }

        var row = new TenantInvestigationAnnotation
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            InvestigationId = investigationId,
            EvidenceItemId = evidenceItemId,
            Text = input.Text,
            AuthorSubject = input.AuthorSubject,
            CreatedAtUtc = ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime)
        };
        var audit = new TenantAuditEvent
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            EventKey = $"{AddedAction}:{row.Id:D}",
            Action = AddedAction,
            EntityType = AuditEntityType,
            EntityId = row.Id.ToString("D"),
            ActorSubject = row.AuthorSubject,
            OccurredAtUtc = row.CreatedAtUtc
        };

        _db.TenantInvestigationAnnotations.Add(row);
        _db.TenantAuditEvents.Add(audit);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            _db.Entry(row).State = EntityState.Detached;
            _db.Entry(audit).State = EntityState.Detached;
            var winner = await FindAsync(tenantId, evidenceItemId, input, cancellationToken)
                ?? throw new InvalidOperationException("Annotation winner is not visible.", ex);
            return new AnnotationAddResult(AnnotationAddOutcome.Replayed, ToModel(winner));
        }

        return new AnnotationAddResult(AnnotationAddOutcome.Added, ToModel(row));
    }

    public async Task<IReadOnlyList<TenantInvestigationAnnotationModel>?> ListAsync(
        Guid tenantId, Guid investigationId, Guid evidenceItemId, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        if (!await ItemExistsAsync(tenantId, investigationId, evidenceItemId, cancellationToken))
        {
            return null;
        }

        var rows = await _db.TenantInvestigationAnnotations.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EvidenceItemId == evidenceItemId)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    private Task<bool> ItemExistsAsync(
        Guid tenantId, Guid investigationId, Guid evidenceItemId, CancellationToken cancellationToken) =>
        _db.TenantInvestigationEvidenceItems.AsNoTracking().AnyAsync(
            x => x.TenantId == tenantId && x.InvestigationId == investigationId && x.Id == evidenceItemId,
            cancellationToken);

    private Task<TenantInvestigationAnnotation?> FindAsync(
        Guid tenantId, Guid evidenceItemId, AnnotationInput input, CancellationToken cancellationToken) =>
        _db.TenantInvestigationAnnotations.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.EvidenceItemId == evidenceItemId
                && x.AuthorSubject == input.AuthorSubject && x.Text == input.Text,
            cancellationToken);

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

    private static TenantInvestigationAnnotationModel ToModel(TenantInvestigationAnnotation row) =>
        new(row.Id, row.InvestigationId, row.EvidenceItemId, row.Text, row.AuthorSubject,
            DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc));
}
