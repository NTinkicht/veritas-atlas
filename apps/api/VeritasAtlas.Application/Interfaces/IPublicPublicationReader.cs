using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// VER-P0-16 slice 6 (#200): anonymous public-reader read path. Visibility is decided only by <see cref="PublicReaderGate"/>
/// over trusted server-side facts; a hidden revision, a foreign tenant's revision and an unknown id all return null so the
/// cause is not inferable.
/// </summary>
public interface IPublicPublicationReader
{
    Task<PublicPublicationView?> GetAsync(Guid revisionId, CancellationToken cancellationToken);
}

/// <summary>The only fields an anonymous reader may see. No tenant, digest, author, reviewer or internal reason.</summary>
public sealed record PublicPublicationView(Guid Id, string FreshnessLabel, IReadOnlyList<PublicCitationProjection> Citations);
