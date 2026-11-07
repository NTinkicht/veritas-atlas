using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private const string ResetOperationPrefix = "transition:";
    private static readonly JsonSerializerOptions FactsJson = new(JsonSerializerDefaults.Web);
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
        // Leaving the Approved/Published line returns the public-reader facts to the restrictive defaults (audited as a Reset).
        var resetFacts = next is not (LifecycleState.Approved or LifecycleState.Published) && !HasRestrictiveDefaults(row);
        var newFactsVersion = resetFacts ? row.FactsVersion + 1 : row.FactsVersion;
        var now = DateTime.SpecifyKind(_time.GetUtcNow().UtcDateTime, DateTimeKind.Utc);
        var nextName = next.ToString();
        var actionName = action.ToString();

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var changed = await _db.TenantPublicationRevisions
                .Where(x => x.TenantId == tenantId && x.Id == revisionId && x.Version == row.Version
                            && x.FactsVersion == row.FactsVersion)
                .ExecuteUpdateAsync(s =>
                {
                    s.SetProperty(x => x.State, nextName)
                        .SetProperty(x => x.Version, newVersion)
                        .SetProperty(x => x.ApprovedDigest, approved)
                        .SetProperty(x => x.LastOperationId, operationId)
                        .SetProperty(x => x.LastAction, actionName);
                    if (resetFacts)
                    {
                        s.SetProperty(x => x.Restricted, true)
                            .SetProperty(x => x.Superseded, false)
                            .SetProperty(x => x.ExpiresAtUtc, (DateTime?)null)
                            .SetProperty(x => x.FreshnessRequired, true)
                            .SetProperty(x => x.EvidenceCurrency, nameof(Domain.Provenance.EvidenceCurrency.Unknown))
                            .SetProperty(x => x.HistoricalLabelPresent, false)
                            .SetProperty(x => x.PublicCitationsJson, (string?)null)
                            .SetProperty(x => x.FactsVersion, newFactsVersion);
                    }
                }, cancellationToken);
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
            if (resetFacts)
            {
                _db.TenantPublicationVisibilityChanges.Add(new TenantPublicationVisibilityChange
                {
                    TenantId = tenantId, RevisionId = revisionId, FactsVersion = newFactsVersion, RevisionVersion = newVersion,
                    OperationId = ResetOperationPrefix + operationId, Kind = "Reset", RequestDigest = Sha256Hex("reset"),
                    ActorId = actorId, ActorRole = actorRole.ToString(), OccurredAtUtc = now
                });
            }

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
                row.PolicyPermitsPublicDisplay, next, newVersion, newFactsVersion));
    }

    public async Task<VisibilityChangeResult> SetVisibilityAsync(
        Guid tenantId, Guid revisionId, string actorId, LifecycleRole actorRole, PublicVisibilityFactsInput facts,
        long expectedFactsVersion, string operationId, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        RequireText(actorId, nameof(actorId));
        RequireText(operationId, nameof(operationId));
        ArgumentNullException.ThrowIfNull(facts);
        var row = await _db.TenantPublicationRevisions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == revisionId, cancellationToken);
        if (row is null)
        {
            return new(VisibilityOutcome.NotFound, null, null);
        }

        if (actorRole != LifecycleRole.Publisher)
        {
            return VisibilityDenied(LifecycleDenials.RoleNotPermitted, row);
        }

        var digest = Sha256Hex(JsonSerializer.Serialize(new
        {
            facts.Restricted, facts.Superseded, ExpiresAt = facts.ExpiresAt?.UtcDateTime, facts.FreshnessRequired,
            EvidenceCurrency = facts.EvidenceCurrency.ToString(), facts.HistoricalLabelPresent, facts.Citations
        }, FactsJson));

        // An operation id is only replayable by the same actor, role and exact facts that committed it.
        var prior = await _db.TenantPublicationVisibilityChanges.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.RevisionId == revisionId && x.OperationId == operationId, cancellationToken);
        if (prior is not null)
        {
            return prior.Kind == "Set" && prior.ActorId == actorId && prior.ActorRole == actorRole.ToString()
                   && prior.RequestDigest == digest && prior.FactsVersion == expectedFactsVersion + 1
                ? new(VisibilityOutcome.Replayed, null, ToModel(row))
                : VisibilityDenied(GuardDenials.OperationConflict, row);
        }

        if (expectedFactsVersion < 0 || expectedFactsVersion != row.FactsVersion)
        {
            return VisibilityDenied(GuardDenials.StaleVersion, row);
        }

        if (!Enum.TryParse<LifecycleState>(row.State, ignoreCase: false, out var state))
        {
            return VisibilityDenied("stored_state_invalid", row);
        }

        var denial = PublicVisibilityRules.Validate(facts, row.PolicyPermitsPublicDisplay, state);
        if (denial is not null)
        {
            return VisibilityDenied(denial, row);
        }

        var citationsJson = JsonSerializer.Serialize(facts.Citations, FactsJson);
        DateTime? expires = facts.ExpiresAt is { } e ? DateTime.SpecifyKind(e.UtcDateTime, DateTimeKind.Utc) : null;
        var currency = facts.EvidenceCurrency.ToString();
        var newFactsVersion = row.FactsVersion + 1;
        var now = DateTime.SpecifyKind(_time.GetUtcNow().UtcDateTime, DateTimeKind.Utc);
        var stateName = row.State;

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var changed = await _db.TenantPublicationRevisions
                .Where(x => x.TenantId == tenantId && x.Id == revisionId && x.Version == row.Version
                            && x.FactsVersion == row.FactsVersion && x.State == stateName)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Restricted, facts.Restricted)
                    .SetProperty(x => x.Superseded, facts.Superseded)
                    .SetProperty(x => x.ExpiresAtUtc, expires)
                    .SetProperty(x => x.FreshnessRequired, facts.FreshnessRequired)
                    .SetProperty(x => x.EvidenceCurrency, currency)
                    .SetProperty(x => x.HistoricalLabelPresent, facts.HistoricalLabelPresent)
                    .SetProperty(x => x.PublicCitationsJson, citationsJson)
                    .SetProperty(x => x.FactsVersion, newFactsVersion), cancellationToken);
            if (changed == 0)
            {
                return new(VisibilityOutcome.ConcurrentUpdate, null, null);
            }

            _db.TenantPublicationVisibilityChanges.Add(new TenantPublicationVisibilityChange
            {
                TenantId = tenantId, RevisionId = revisionId, FactsVersion = newFactsVersion, RevisionVersion = row.Version,
                OperationId = operationId, Kind = "Set", RequestDigest = digest, ActorId = actorId,
                ActorRole = actorRole.ToString(), OccurredAtUtc = now
            });
            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            _db.ChangeTracker.Clear();
            return new(VisibilityOutcome.ConcurrentUpdate, null, null);
        }

        return new(VisibilityOutcome.Applied, null, new TenantPublicationRevisionModel(
            row.Id, row.AuthorId, row.ContentDigest, row.ApprovedDigest, row.PolicyPermitsPublicDisplay,
            state, row.Version, newFactsVersion));
    }

    private static bool HasRestrictiveDefaults(TenantPublicationRevision row) =>
        row.Restricted && !row.Superseded && row.ExpiresAtUtc is null && row.FreshnessRequired
        && row.EvidenceCurrency == nameof(Domain.Provenance.EvidenceCurrency.Unknown) && !row.HistoricalLabelPresent
        && row.PublicCitationsJson is null;

    private static VisibilityChangeResult VisibilityDenied(string code, TenantPublicationRevision row) =>
        new(VisibilityOutcome.Denied, code, ToModel(row));

    private static string Sha256Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

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
            Enum.TryParse<LifecycleState>(row.State, out var s) ? s : LifecycleState.Draft, row.Version, row.FactsVersion);
}
