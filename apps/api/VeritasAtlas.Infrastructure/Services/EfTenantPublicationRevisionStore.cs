using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// PostgreSQL publication revision store (VER-P0-16 slice 4, #196). The write is one transaction: a guarded UPDATE conditional on
/// the exact version that was read plus an append-only audit row, so racing writers cannot both apply. Triggers repeat the
/// one-step/immutability rules as a second line of defence.
/// </summary>
public sealed class EfTenantPublicationRevisionStore : ITenantPublicationRevisionStore
{
    private const string UniqueViolation = "23505";
    private const int MaxText = 200;
    private readonly VeritasAtlasDbContext _db;
    private readonly TimeProvider _time;

    public EfTenantPublicationRevisionStore(VeritasAtlasDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<TenantPublicationRevisionModel> CreateDraftAsync(
        Guid tenantId, string authorId, string contentDigest, bool policyPermitsPublicDisplay, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        RequireText(authorId, nameof(authorId));
        RequireText(contentDigest, nameof(contentDigest));
        var row = new TenantPublicationRevision
        {
            TenantId = tenantId, Id = Guid.NewGuid(), AuthorId = authorId, ContentDigest = contentDigest,
            PolicyPermitsPublicDisplay = policyPermitsPublicDisplay, State = nameof(LifecycleState.Draft), Version = 0
        };
        _db.TenantPublicationRevisions.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        return ToModel(row);
    }

    public async Task<TenantPublicationRevisionModel?> GetAsync(Guid tenantId, Guid revisionId, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        var row = await _db.TenantPublicationRevisions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == revisionId, cancellationToken);
        return row is null ? null : ToModel(row);
    }

    public async Task<PublicationTransitionResult> TransitionAsync(
        Guid tenantId, Guid revisionId, string actorId, LifecycleRole actorRole, LifecycleAction action,
        long expectedVersion, string operationId, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        var row = await _db.TenantPublicationRevisions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == revisionId, cancellationToken);
        if (row is null)
        {
            return new(PublicationTransitionOutcome.NotFound, null, null);
        }

        if (!Enum.TryParse<LifecycleState>(row.State, out var state) ||
            (row.LastAction is not null && !Enum.TryParse<LifecycleAction>(row.LastAction, out _)))
        {
            return Denied("stored_state_invalid", row);
        }

        LifecycleAction? priorAction = row.LastAction is null ? null : Enum.Parse<LifecycleAction>(row.LastAction);
        var guard = PublicationVersionGuard.Evaluate(new GuardRequest(
            expectedVersion, row.Version, operationId, action, row.LastOperationId, priorAction,
            row.ApprovedDigest, row.ContentDigest));
        if (guard.Outcome == GuardOutcome.Deny)
        {
            return Denied(guard.DenialCode!, row);
        }

        if (guard.Outcome == GuardOutcome.Replay)
        {
            // An operation id is only replayable by the actor and role that committed it.
            var audit = await _db.TenantPublicationTransitions.AsNoTracking().SingleOrDefaultAsync(
                x => x.TenantId == tenantId && x.RevisionId == revisionId && x.Version == row.Version, cancellationToken);
            return audit is not null && audit.OperationId == operationId && audit.ActorId == actorId &&
                   audit.ActorRole == actorRole.ToString()
                ? new(PublicationTransitionOutcome.Replayed, null, ToModel(row))
                : Denied(GuardDenials.OperationConflict, row);
        }

        var decision = PublicationLifecycle.Transition(new LifecycleRequest(
            tenantId.ToString(), tenantId.ToString(), actorId, actorRole, row.AuthorId, state, action,
            row.PolicyPermitsPublicDisplay));
        if (!decision.Allowed)
        {
            return Denied(decision.DenialCode!, row);
        }

        var next = decision.NewState!.Value;
        // Approval binds the exact content; leaving the approved line for Draft drops it.
        var approved = next switch
        {
            LifecycleState.Approved => row.ContentDigest,
            LifecycleState.Published or LifecycleState.Withdrawn => row.ApprovedDigest,
            _ => null
        };
        var newVersion = row.Version + 1;
        var now = DateTime.SpecifyKind(_time.GetUtcNow().UtcDateTime, DateTimeKind.Utc);
        var nextName = next.ToString();
        var actionName = action.ToString();

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var changed = await _db.TenantPublicationRevisions
                .Where(x => x.TenantId == tenantId && x.Id == revisionId && x.Version == row.Version)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.State, nextName)
                    .SetProperty(x => x.Version, newVersion)
                    .SetProperty(x => x.ApprovedDigest, approved)
                    .SetProperty(x => x.LastOperationId, operationId)
                    .SetProperty(x => x.LastAction, actionName), cancellationToken);
            if (changed == 0)
            {
                return new(PublicationTransitionOutcome.ConcurrentUpdate, null, null);
            }

            _db.TenantPublicationTransitions.Add(new TenantPublicationTransition
            {
                TenantId = tenantId, RevisionId = revisionId, Version = newVersion, OperationId = operationId,
                Action = actionName, FromState = row.State, ToState = nextName, ActorId = actorId,
                ActorRole = actorRole.ToString(), OccurredAtUtc = now
            });
            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            _db.ChangeTracker.Clear();
            return new(PublicationTransitionOutcome.ConcurrentUpdate, null, null);
        }

        return new(PublicationTransitionOutcome.Applied, null,
            new TenantPublicationRevisionModel(row.Id, row.AuthorId, row.ContentDigest, approved,
                row.PolicyPermitsPublicDisplay, next, newVersion));
    }

    private static PublicationTransitionResult Denied(string code, TenantPublicationRevision row) =>
        new(PublicationTransitionOutcome.Denied, code, ToModel(row));

    private static void RequireTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant is required.", nameof(tenantId));
        }
    }

    private static void RequireText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxText)
        {
            throw new ArgumentException("A non-blank value of at most 200 characters is required.", name);
        }
    }

    private static TenantPublicationRevisionModel ToModel(TenantPublicationRevision row) =>
        new(row.Id, row.AuthorId, row.ContentDigest, row.ApprovedDigest, row.PolicyPermitsPublicDisplay,
            Enum.TryParse<LifecycleState>(row.State, out var s) ? s : LifecycleState.Draft, row.Version);
}
