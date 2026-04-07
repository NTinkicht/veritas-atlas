namespace VeritasAtlas.Api.Contracts.Workflow;

public sealed record WorkflowTransitionResponse(
    string EntityType,
    Guid EntityId,
    string Status,
    DateTime TimestampUtc,
    string Message);

public sealed record WorkflowSeedResponse(
    Guid CaseId,
    Guid PrimaryClaimId,
    Guid SecondaryClaimId,
    Guid ContradictionId,
    string CaseStatus,
    string ContradictionStatus,
    DateTime TimestampUtc);