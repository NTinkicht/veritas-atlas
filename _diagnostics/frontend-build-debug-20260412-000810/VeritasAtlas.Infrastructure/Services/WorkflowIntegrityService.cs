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
                ["InReview"] = new[] { "Approved", "Rejected", "ReadyForPublication", "OnHold" },
                ["Approved"] = new[] { "ReadyForPublication", "OnHold" },
                ["Rejected"] = new[] { "InReview" },
                ["ReadyForPublication"] = new[] { "Published", "OnHold" },
                ["OnHold"] = new[] { "ReadyForPublication", "InReview" },
                ["Published"] = Array.Empty<string>()
            },
            ["Claim"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Draft"] = new[] { "InReview" },
                ["InReview"] = new[] { "Draft" }
            },
            ["Contradiction"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Draft"] = new[] { "UnderReview", "Resolved" },
                ["UnderReview"] = new[] { "Resolved" },
                ["Resolved"] = Array.Empty<string>()
            },
            ["Review"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Open"] = new[] { "Completed" },
                ["Completed"] = new[] { "Open" }
            }
        };

    private static readonly Dictionary<string, string[]> RolePolicies =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["SubmitCase"] = new[] { "operator", "reviewer", "admin" },
            ["ApproveCase"] = new[] { "reviewer", "admin" },
            ["RejectCase"] = new[] { "reviewer", "admin" },
            ["ResolveContradiction"] = new[] { "reviewer", "admin" },
            ["CompleteReview"] = new[] { "reviewer", "admin" },
            ["SendClaimToReview"] = new[] { "reviewer", "admin" },
            ["ReturnClaimForEdit"] = new[] { "reviewer", "admin" },
            ["EscalateContradiction"] = new[] { "reviewer", "admin" },
            ["ReopenReview"] = new[] { "reviewer", "admin" },
            ["PreparePublication"] = new[] { "reviewer", "publisher", "admin" },
            ["PublishCase"] = new[] { "publisher", "admin" },
            ["HoldCase"] = new[] { "publisher", "admin" },
            ["SeedLifecycle"] = new[] { "operator", "admin" }
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