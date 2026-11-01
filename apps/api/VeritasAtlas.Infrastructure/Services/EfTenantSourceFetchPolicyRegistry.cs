using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// PostgreSQL-backed fetch policy registry (VER-P0-04 slice 5b, #121) over the
/// append-only <c>tenant_source_fetch_policies</c> table (#119). Every query
/// is filtered by the caller's tenant. A revision and its audit event commit in
/// one SaveChanges; the per-source unique revision, policy version and audit
/// event key make a concurrent writer of the same revision fail, which is
/// reported as <see cref="SourceFetchPolicyProposalOutcome.Conflict"/>.
/// </summary>
public sealed class EfTenantSourceFetchPolicyRegistry : ITenantSourceFetchPolicyRegistry
{
    private const string UniqueViolation = "23505";
    public const string RegisteredAction = "tenant_source.fetch_policy_registered";
    public const string RevisedAction = "tenant_source.fetch_policy_revised";
    private readonly VeritasAtlasDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfTenantSourceFetchPolicyRegistry(VeritasAtlasDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<SourceFetchPolicyProposalResult> ProposeAsync(
        Guid tenantId,
        Guid sourceRegistryEntryId,
        SourceFetchPolicy proposed,
        bool wideningApproved,
        string actorSubject,
        CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        // Same rule as the audit ledger's actor check: a typed refusal before
        // the database, never a raw DbUpdateException.
        if (string.IsNullOrWhiteSpace(actorSubject)
            || actorSubject.Length > TenantAuditEventLimits.MaxSubjectLength
            || actorSubject.Any(char.IsControl))
        {
            throw new ArgumentException("Invalid actor subject.", nameof(actorSubject));
        }

        var sourceExists = await _db.TenantSourceRegistryEntries
            .AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId && x.Id == sourceRegistryEntryId, cancellationToken);
        if (!sourceExists) return new(SourceFetchPolicyProposalOutcome.SourceNotFound, null, []);

        var current = await Revisions(tenantId, sourceRegistryEntryId)
            .OrderByDescending(x => x.Revision)
            .FirstOrDefaultAsync(cancellationToken);

        bool widens;
        if (current is null)
        {
            var admission = SourceFetchPolicyAdmission.Validate(proposed);
            if (!admission.Admitted) return Refused(admission.Reasons);
            // A first policy widens nothing: there is no earlier surface.
            widens = false;
        }
        else
        {
            var previousVersions = await Revisions(tenantId, sourceRegistryEntryId)
                .Select(x => x.PolicyVersion)
                .ToListAsync(cancellationToken);
            var change = SourceFetchPolicyChange.Evaluate(
                ToDomain(current), proposed, previousVersions, wideningApproved);
            if (!change.Admitted) return Refused(change.Reasons);
            if (!change.Changed) return new(SourceFetchPolicyProposalOutcome.Unchanged, ToModel(current), []);
            widens = change.Widenings.Count > 0;
        }

        var revision = new TenantSourceFetchPolicy
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            SourceRegistryEntryId = sourceRegistryEntryId,
            Revision = (current?.Revision ?? 0) + 1,
            PolicyVersion = proposed.PolicyVersion,
            Jurisdiction = proposed.Jurisdiction,
            SourceType = proposed.SourceType,
            AllowedHosts = proposed.AllowedHosts.ToArray(),
            AllowedSchemes = proposed.AllowedSchemes.ToArray(),
            AllowedPorts = proposed.AllowedPorts.ToArray(),
            MaxRedirects = proposed.MaxRedirects,
            PlaintextHttpException = proposed.PlaintextHttpException,
            // True only when this revision widens the surface (which the
            // change policy admitted because the caller approved it).
            WideningApproved = widens,
            CreatedAtUtc = ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime)
        };

        var action = revision.Revision == 1 ? RegisteredAction : RevisedAction;
        var audit = new TenantAuditEvent
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            // One event per source and revision: a second guard against two
            // writers committing the same revision.
            EventKey = $"tenant_source.fetch_policy:{sourceRegistryEntryId:D}:{revision.Revision}",
            Action = action,
            EntityType = EfTenantSourceRegistry.AuditEntityType,
            EntityId = sourceRegistryEntryId.ToString("D"),
            ActorSubject = actorSubject,
            OccurredAtUtc = revision.CreatedAtUtc
        };

        _db.TenantSourceFetchPolicies.Add(revision);
        _db.TenantAuditEvents.Add(audit);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            _db.Entry(revision).State = EntityState.Detached;
            _db.Entry(audit).State = EntityState.Detached;
            return new(SourceFetchPolicyProposalOutcome.Conflict, null, []);
        }

        return new(SourceFetchPolicyProposalOutcome.Created, ToModel(revision), []);
    }

    public async Task<TenantSourceFetchPolicyModel?> GetCurrentAsync(
        Guid tenantId, Guid sourceRegistryEntryId, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        var current = await Revisions(tenantId, sourceRegistryEntryId)
            .OrderByDescending(x => x.Revision)
            .FirstOrDefaultAsync(cancellationToken);
        return current is null ? null : ToModel(current);
    }

    private IQueryable<TenantSourceFetchPolicy> Revisions(Guid tenantId, Guid sourceRegistryEntryId) =>
        _db.TenantSourceFetchPolicies
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.SourceRegistryEntryId == sourceRegistryEntryId);

    private static SourceFetchPolicyProposalResult Refused(IReadOnlyList<string> reasons) =>
        new(SourceFetchPolicyProposalOutcome.Refused, null, reasons);

    private static void RequireTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant is required.", nameof(tenantId));
        }
    }

    private static DateTime ToPostgresPrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);

    private static SourceFetchPolicy ToDomain(TenantSourceFetchPolicy row) =>
        new(row.PolicyVersion, row.Jurisdiction, row.SourceType,
            row.AllowedHosts.ToArray(), row.AllowedSchemes.ToArray(), row.AllowedPorts.ToArray(),
            row.MaxRedirects, row.PlaintextHttpException);

    private static TenantSourceFetchPolicyModel ToModel(TenantSourceFetchPolicy row) =>
        new(row.Id, row.SourceRegistryEntryId, row.Revision, ToDomain(row), row.WideningApproved,
            DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc));
}
