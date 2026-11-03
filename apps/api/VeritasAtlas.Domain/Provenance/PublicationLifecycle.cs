namespace VeritasAtlas.Domain.Provenance;

public enum LifecycleState { Draft, Submitted, ChangesRequested, Approved, Published, Withdrawn }

public enum LifecycleAction { Submit, RequestChanges, Approve, Revise, Publish, Withdraw }

public enum LifecycleRole { None, Analyst, Reviewer, Publisher }

public static class LifecycleDenials
{
    public const string TenantMissing = "tenant_missing";
    public const string ActorMissing = "actor_missing";
    public const string CrossTenant = "cross_tenant";
    public const string RoleNotPermitted = "role_not_permitted";
    public const string NotAuthor = "not_author";
    public const string SelfApprovalDenied = "self_approval_denied";
    public const string InvalidTransition = "invalid_transition";
    public const string PublicDisplayNotPermitted = "public_display_not_permitted";
}

/// <summary>Facts about one revision and the actor requesting a transition. All facts are supplied by the caller from trusted server-side state.</summary>
public sealed record LifecycleRequest(
    string RevisionTenantId, string ActorTenantId, string ActorId, LifecycleRole ActorRole,
    string AuthorId, LifecycleState State, LifecycleAction Action, bool PolicyPermitsPublicDisplay);

public sealed record LifecycleResult(bool Allowed, LifecycleState? NewState, string? DenialCode);

/// <summary>Pure deterministic, fail-closed review/publication transition rules (VER-P0-16 slice 1). Approved is never public: only Publish with an explicit policy permit reaches Published.</summary>
public static class PublicationLifecycle
{
    public static LifecycleResult Transition(LifecycleRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (string.IsNullOrWhiteSpace(r.RevisionTenantId) || string.IsNullOrWhiteSpace(r.ActorTenantId)) return Deny(LifecycleDenials.TenantMissing);
        if (string.IsNullOrWhiteSpace(r.ActorId) || string.IsNullOrWhiteSpace(r.AuthorId)) return Deny(LifecycleDenials.ActorMissing);
        if (!string.Equals(r.RevisionTenantId, r.ActorTenantId, StringComparison.Ordinal)) return Deny(LifecycleDenials.CrossTenant);

        LifecycleState? next = (r.State, r.Action) switch
        {
            (LifecycleState.Draft, LifecycleAction.Submit) => LifecycleState.Submitted,
            (LifecycleState.Submitted, LifecycleAction.RequestChanges) => LifecycleState.ChangesRequested,
            (LifecycleState.Submitted, LifecycleAction.Approve) => LifecycleState.Approved,
            (LifecycleState.ChangesRequested, LifecycleAction.Revise) => LifecycleState.Draft,
            (LifecycleState.Approved, LifecycleAction.Publish) => LifecycleState.Published,
            (LifecycleState.Approved, LifecycleAction.Withdraw) => LifecycleState.Withdrawn,
            (LifecycleState.Published, LifecycleAction.Withdraw) => LifecycleState.Withdrawn,
            _ => null,
        };
        if (next is null) return Deny(LifecycleDenials.InvalidTransition);

        switch (r.Action)
        {
            case LifecycleAction.Submit:
            case LifecycleAction.Revise:
                if (r.ActorRole != LifecycleRole.Analyst) return Deny(LifecycleDenials.RoleNotPermitted);
                if (!string.Equals(r.ActorId, r.AuthorId, StringComparison.Ordinal)) return Deny(LifecycleDenials.NotAuthor);
                break;
            case LifecycleAction.Approve:
            case LifecycleAction.RequestChanges:
                if (r.ActorRole != LifecycleRole.Reviewer) return Deny(LifecycleDenials.RoleNotPermitted);
                if (string.Equals(r.ActorId, r.AuthorId, StringComparison.Ordinal)) return Deny(LifecycleDenials.SelfApprovalDenied);
                break;
            case LifecycleAction.Publish:
            case LifecycleAction.Withdraw:
                if (r.ActorRole != LifecycleRole.Publisher) return Deny(LifecycleDenials.RoleNotPermitted);
                if (r.Action == LifecycleAction.Publish && !r.PolicyPermitsPublicDisplay) return Deny(LifecycleDenials.PublicDisplayNotPermitted);
                break;
            default:
                return Deny(LifecycleDenials.InvalidTransition);
        }
        return new(true, next, null);
    }

    private static LifecycleResult Deny(string code) => new(false, null, code);
}
