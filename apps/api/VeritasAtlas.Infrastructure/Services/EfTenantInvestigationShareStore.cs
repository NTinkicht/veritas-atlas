using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// PostgreSQL-backed investigation sharing (VER-P0-10 slice 6). The investigation is resolved
/// inside the caller tenant and must have been created by the caller before anything is read or
/// written, so another tenant's id, an unknown id and a non-creator are indistinguishable; the
/// database guard trigger enforces the same rule. Each effective grant, reactivation or
/// revocation writes its audit event in the same transaction; a replay or no-op writes none.
/// </summary>
public sealed class EfTenantInvestigationShareStore : ITenantInvestigationShareStore
{
    private const string UniqueViolation = "23505";
    private const string GuardViolation = "23514";
    public const string GrantedAction = "investigation.share_granted";
    public const string RevokedAction = "investigation.share_revoked";
    public const string AuditEntityType = "investigation_share";
    private readonly VeritasAtlasDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfTenantInvestigationShareStore(VeritasAtlasDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<ShareGrantResult> GrantAsync(
        Guid tenantId, Guid investigationId, string owner, string grantee, CancellationToken cancellationToken)
    {
        Require(tenantId, owner);
        RequireGrantee(grantee);
        if (!await IsCreatorAsync(tenantId, investigationId, owner, cancellationToken))
        {
            return new ShareGrantResult(ShareGrantOutcome.NotFound, null);
        }

        if (string.Equals(owner, grantee, StringComparison.Ordinal))
        {
            return new ShareGrantResult(ShareGrantOutcome.SelfGrant, null);
        }

        var now = ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime);
        var existing = await FindAsync(tenantId, investigationId, grantee, cancellationToken);
        if (existing is { RevokedAtUtc: null })
        {
            return new ShareGrantResult(ShareGrantOutcome.Replayed, ToModel(existing));
        }

        if (existing is not null)
        {
            return await ReactivateAsync(tenantId, investigationId, owner, grantee, now, cancellationToken);
        }

        var row = new TenantInvestigationShare
        {
            TenantId = tenantId,
            InvestigationId = investigationId,
            GranteeSubject = grantee,
            GrantedBySubject = owner,
            GrantedAtUtc = now
        };
        var audit = NewAudit(GrantedAction, row, owner, now);
        _db.TenantInvestigationShares.Add(row);
        _db.TenantAuditEvents.Add(audit);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return new ShareGrantResult(ShareGrantOutcome.Granted, ToModel(row));
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: GuardViolation })
        {
            Detach(row, audit);
            return new ShareGrantResult(ShareGrantOutcome.NotFound, null);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // A concurrent identical grant won; its row (never deleted) is the replayed result.
            Detach(row, audit);
            var winner = await FindAsync(tenantId, investigationId, grantee, cancellationToken)
                ?? throw new InvalidOperationException("A shared row cannot disappear: shares are never deleted.");
            return winner.RevokedAtUtc is null
                ? new ShareGrantResult(ShareGrantOutcome.Replayed, ToModel(winner))
                : await ReactivateAsync(tenantId, investigationId, owner, grantee, now, cancellationToken);
        }
    }

    public async Task<ShareRevokeOutcome> RevokeAsync(
        Guid tenantId, Guid investigationId, string owner, string grantee, CancellationToken cancellationToken)
    {
        Require(tenantId, owner);
        RequireGrantee(grantee);
        if (!await IsCreatorAsync(tenantId, investigationId, owner, cancellationToken))
        {
            return ShareRevokeOutcome.NotFound;
        }

        var now = ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime);
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        // The conditional update makes concurrent revocations race safely: exactly one wins.
        var changed = await _db.TenantInvestigationShares
            .Where(x => x.TenantId == tenantId && x.InvestigationId == investigationId
                        && x.GranteeSubject == grantee && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.RevokedAtUtc, now)
                .SetProperty(x => x.RevokedBySubject, owner), cancellationToken);
        if (changed == 0)
        {
            return ShareRevokeOutcome.NotActive;
        }

        _db.TenantAuditEvents.Add(NewAudit(RevokedAction, tenantId, investigationId, grantee, owner, now));
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return ShareRevokeOutcome.Revoked;
    }

    public async Task<IReadOnlyList<TenantInvestigationShareModel>?> ListActiveAsync(
        Guid tenantId, Guid investigationId, string owner, CancellationToken cancellationToken)
    {
        Require(tenantId, owner);
        if (!await IsCreatorAsync(tenantId, investigationId, owner, cancellationToken))
        {
            return null;
        }

        var rows = await _db.TenantInvestigationShares.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.InvestigationId == investigationId && x.RevokedAtUtc == null)
            .OrderBy(x => x.GrantedAtUtc).ThenBy(x => x.GranteeSubject)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    public async Task<bool> IsAuthorizedAsync(
        Guid tenantId, Guid investigationId, string subject, CancellationToken cancellationToken)
    {
        Require(tenantId, subject);
        if (await IsCreatorAsync(tenantId, investigationId, subject, cancellationToken))
        {
            return true;
        }

        // A grant row can only exist for an investigation of this tenant (guard trigger; investigations are never deleted).
        return await _db.TenantInvestigationShares.AsNoTracking().AnyAsync(
            x => x.TenantId == tenantId && x.InvestigationId == investigationId
                 && x.GranteeSubject == subject && x.RevokedAtUtc == null, cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> ListActiveGrantedInvestigationIdsAsync(
        Guid tenantId, string subject, CancellationToken cancellationToken)
    {
        Require(tenantId, subject);
        var ids = await _db.TenantInvestigationShares.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.GranteeSubject == subject && x.RevokedAtUtc == null)
            .Select(x => x.InvestigationId)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    private async Task<ShareGrantResult> ReactivateAsync(
        Guid tenantId, Guid investigationId, string owner, string grantee, DateTime now, CancellationToken cancellationToken)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        var changed = await _db.TenantInvestigationShares
            .Where(x => x.TenantId == tenantId && x.InvestigationId == investigationId
                        && x.GranteeSubject == grantee && x.RevokedAtUtc != null)
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.GrantedAtUtc, now)
                .SetProperty(x => x.RevokedAtUtc, (DateTime?)null)
                .SetProperty(x => x.RevokedBySubject, (string?)null), cancellationToken);
        if (changed == 0)
        {
            // Another caller reactivated it first: report their result as a replay.
            var winner = await FindAsync(tenantId, investigationId, grantee, cancellationToken);
            return new ShareGrantResult(ShareGrantOutcome.Replayed, winner is null ? null : ToModel(winner));
        }

        _db.TenantAuditEvents.Add(NewAudit(GrantedAction, tenantId, investigationId, grantee, owner, now));
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        var row = await FindAsync(tenantId, investigationId, grantee, cancellationToken);
        return new ShareGrantResult(ShareGrantOutcome.Granted, row is null ? null : ToModel(row));
    }

    private Task<bool> IsCreatorAsync(Guid tenantId, Guid investigationId, string subject, CancellationToken cancellationToken) =>
        _db.TenantInvestigations.AsNoTracking().AnyAsync(
            x => x.TenantId == tenantId && x.Id == investigationId && x.CreatedBySubject == subject,
            cancellationToken);

    private Task<TenantInvestigationShare?> FindAsync(
        Guid tenantId, Guid investigationId, string grantee, CancellationToken cancellationToken) =>
        _db.TenantInvestigationShares.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.InvestigationId == investigationId && x.GranteeSubject == grantee,
            cancellationToken);

    private void Detach(TenantInvestigationShare row, TenantAuditEvent audit)
    {
        _db.Entry(row).State = EntityState.Detached;
        _db.Entry(audit).State = EntityState.Detached;
    }

    private static TenantAuditEvent NewAudit(string action, TenantInvestigationShare row, string actor, DateTime at) =>
        NewAudit(action, row.TenantId, row.InvestigationId, row.GranteeSubject, actor, at);

    private static TenantAuditEvent NewAudit(
        string action, Guid tenantId, Guid investigationId, string grantee, string actor, DateTime at)
    {
        var id = Guid.NewGuid();
        return new TenantAuditEvent
        {
            TenantId = tenantId,
            Id = id,
            EventKey = $"{action}:{id:D}",
            Action = action,
            EntityType = AuditEntityType,
            // The grantee is part of the audited entity so a trail shows who was let in or out.
            EntityId = $"{investigationId:D}/{grantee}",
            ActorSubject = actor,
            OccurredAtUtc = at
        };
    }

    private static void Require(Guid tenantId, string subject)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant is required.", nameof(tenantId));
        }

        RequireSubject(subject, nameof(subject));
    }

    private static void RequireGrantee(string grantee)
    {
        RequireSubject(grantee, nameof(grantee));
        // Fail closed with the same rule as the controller: no surrounding whitespace, so
        // " bob" can never become a distinct grantee from "bob".
        if (!TenantInvestigationShareLimits.IsValidGrantee(grantee))
        {
            throw new ArgumentException("A valid grantee is required.", nameof(grantee));
        }
    }

    private static void RequireSubject(string subject, string name)
    {
        if (string.IsNullOrWhiteSpace(subject)
            || subject.Length > TenantInvestigationLimits.MaxSubjectLength
            || subject.Length > TenantAuditEventLimits.MaxSubjectLength
            || subject.Any(char.IsControl))
        {
            throw new ArgumentException("A valid subject is required.", name);
        }
    }

    // PostgreSQL stores microseconds: keep a created result identical to a later read.
    private static DateTime ToPostgresPrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);

    private static TenantInvestigationShareModel ToModel(TenantInvestigationShare row) =>
        new(row.InvestigationId, row.GranteeSubject, row.GrantedBySubject,
            DateTime.SpecifyKind(row.GrantedAtUtc, DateTimeKind.Utc));
}
