namespace VeritasAtlas.Infrastructure.Services;

public sealed class WorkflowIntegrityService
{
    private static readonly Dictionary<string, Dictionary<string, string[]>> AllowedTransitions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Case"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Open"] = new[] { "InReview" },
                ["Draft"] = new[] { "InReview" },
                ["InReview"] = new[] { "Approved", "Rejected", "ReadyForPublication", "Published", "OnHold" },
                ["Approved"] = new[] { "InReview", "ReadyForPublication", "Published", "OnHold" },
                ["Rejected"] = new[] { "InReview" },
                ["ReadyForPublication"] = new[] { "Published", "OnHold" },
                ["OnHold"] = new[] { "ReadyForPublication", "InReview", "Published" },
                ["Published"] = new[] { "Published" }
            },
            ["Claim"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Draft"] = new[] { "Draft", "InReview", "Open" },
                ["InReview"] = new[] { "Draft", "InReview", "Open" },
                ["Open"] = new[] { "Draft", "InReview", "Open" }
            },
            ["Contradiction"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Draft"] = new[] { "Draft", "Resolved", "Open", "UnderReview" },
                ["Open"] = new[] { "Resolved", "Open", "UnderReview" },
                ["UnderReview"] = new[] { "Resolved", "Open", "UnderReview" },
                ["Resolved"] = new[] { "Resolved" }
            },
            ["Review"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Created"] = new[] { "Completed", "Created", "Open" },
                ["Open"] = new[] { "Completed", "Created", "Open" },
                ["Completed"] = new[] { "Completed", "Created", "Open" }
            }
        };

    private static readonly Dictionary<string, string[]> RolePolicies =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["SubmitCase"] = new[] { "operator", "reviewer", "publisher", "admin" },
            ["ApproveCase"] = new[] { "reviewer", "publisher", "admin" },
            ["RejectCase"] = new[] { "reviewer", "publisher", "admin" },
            ["ResolveContradiction"] = new[] { "reviewer", "publisher", "admin" },
            ["CompleteReview"] = new[] { "reviewer", "publisher", "admin" },
            ["SendClaimToReview"] = new[] { "reviewer", "publisher", "admin" },
            ["ReturnClaimForEdit"] = new[] { "reviewer", "publisher", "admin" },
            ["EscalateContradiction"] = new[] { "reviewer", "publisher", "admin" },
            ["ReopenReview"] = new[] { "reviewer", "publisher", "admin" },
            ["PreparePublication"] = new[] { "reviewer", "publisher", "admin" },
            ["PublishCase"] = new[] { "publisher", "admin", "reviewer" },
            ["HoldCase"] = new[] { "publisher", "admin", "reviewer" },
            ["SeedLifecycle"] = new[] { "operator", "admin", "reviewer", "publisher" }
        };

    public void EnsureRoleAllowed(string actionName, string? role)
    {
        var normalizedRole = string.IsNullOrWhiteSpace(role) ? "anonymous" : role.Trim().ToLowerInvariant();

        if (!RolePolicies.TryGetValue(actionName, out var roles))
        {
            return;
        }

        if (!roles.Contains(normalizedRole, StringComparer.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"Role '{normalizedRole}' is not allowed to execute '{actionName}'.");
        }
    }

    public void EnsureTransitionAllowed(string entityType, string? currentStatus, string nextStatus)
    {
        var current = string.IsNullOrWhiteSpace(currentStatus) ? "Open" : currentStatus.Trim();

        if (!AllowedTransitions.TryGetValue(entityType, out var map))
        {
            return;
        }

        if (!map.TryGetValue(current, out var allowed))
        {
            throw new InvalidOperationException($"No transition rules are defined for {entityType} status '{current}'.");
        }

        if (!allowed.Contains(nextStatus, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{entityType} cannot transition from '{current}' to '{nextStatus}'.");
        }
    }

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string[]>> GetRules()
    {
        return AllowedTransitions.ToDictionary(
            x => x.Key,
            x => (IReadOnlyDictionary<string, string[]>)x.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, string[]> GetRolePolicies()
    {
        return RolePolicies.ToDictionary(
            x => x.Key,
            x => x.Value,
            StringComparer.OrdinalIgnoreCase);
    }
}