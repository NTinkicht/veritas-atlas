namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// Column limits of <c>tenant_retrieval_attestations</c> (VER-P0-03 slice 12,
/// #107), enforced by <see cref="ITenantRetrievalAttestationLedger"/> (#109).
/// </summary>
public static class TenantRetrievalAttestationLimits
{
    public const int MaxAttestationKeyLength = 200;
    public const int MaxIdentityLength = 200;
    public const int MaxBindingReferenceLength = 500;
    public const int MaxJurisdictionLength = 64;
}
