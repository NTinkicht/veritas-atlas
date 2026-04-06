using VeritasAtlas.Domain.Entities;

namespace VeritasAtlas.Application.Interfaces;

public interface IStatementService
{
    Task<Statement> ExtractStatementAsync(
        Guid evidenceId,
        string text,
        string? createdBy = null,
        CancellationToken cancellationToken = default);
}
