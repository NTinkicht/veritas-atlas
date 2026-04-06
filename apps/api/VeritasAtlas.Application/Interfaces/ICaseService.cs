using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Entities;

namespace VeritasAtlas.Application.Interfaces;

public interface ICaseService
{
    Task<PagedResult<Case>> GetCasesAsync(CaseListFilters filters, CancellationToken cancellationToken = default);
    Task<Case> GetCaseByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Case> CreateCaseAsync(
        string title,
        string? summary = null,
        Guid? subjectPersonId = null,
        string? createdBy = null,
        CancellationToken cancellationToken = default);
    Task AddClaimToCaseAsync(Guid caseId, Guid claimId, string? createdBy = null, CancellationToken cancellationToken = default);
    Task ApproveCaseAsync(Guid caseId, string? approvedBy = null, string? notes = null, CancellationToken cancellationToken = default);
    Task RejectCaseAsync(Guid caseId, string? rejectedBy = null, string? notes = null, CancellationToken cancellationToken = default);
}
