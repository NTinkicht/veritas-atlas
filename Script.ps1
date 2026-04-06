param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir
)

$ErrorActionPreference = "Stop"

function Remove-FileIfExists {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (Test-Path $Path) {
        Remove-Item -Path $Path -Force
        Write-Host "Removed: $Path"
    }
    else {
        Write-Host "Already absent: $Path"
    }
}

function Write-Utf8File {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $dir = Split-Path -Parent $Path
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
    Write-Host "Wrote: $Path"
}

function Build-Backend {
    param([Parameter(Mandatory = $true)][string]$RootDir)

    $solutionPath = Join-Path $RootDir "VeritasAtlas.slnx"
    $apiDir = Join-Path $RootDir "apps\api"

    if (Test-Path $solutionPath) {
        Push-Location $RootDir
        try {
            dotnet build $solutionPath
            if ($LASTEXITCODE -ne 0) {
                throw "Backend build failed."
            }
        }
        finally {
            Pop-Location
        }
        return
    }

    if (Test-Path $apiDir) {
        Push-Location $apiDir
        try {
            dotnet build
            if ($LASTEXITCODE -ne 0) {
                throw "Backend build failed."
            }
        }
        finally {
            Pop-Location
        }
        return
    }

    throw "Could not find solution or api directory."
}

$apiRoot = Join-Path $RootDir "apps\api"

# These files are duplicates and must be removed because the real definitions already live
# in DomainEntities.cs and DomainEnums.cs.
$duplicateEntityPath = Join-Path $apiRoot "VeritasAtlas.Domain\Entities\Contradiction.cs"
$duplicateTypeEnumPath = Join-Path $apiRoot "VeritasAtlas.Domain\Enums\ContradictionType.cs"
$duplicateSeverityEnumPath = Join-Path $apiRoot "VeritasAtlas.Domain\Enums\ContradictionSeverity.cs"
$duplicateStatusEnumPath = Join-Path $apiRoot "VeritasAtlas.Domain\Enums\ContradictionStatus.cs"

$servicePath = Join-Path $apiRoot "VeritasAtlas.Infrastructure\Services\ContradictionSliceService.cs"
$controllerPath = Join-Path $apiRoot "VeritasAtlas.Api\Controllers\ContradictionsController.cs"

Write-Host "Applying contradiction debug alignment fix..." -ForegroundColor Cyan

Remove-FileIfExists -Path $duplicateEntityPath
Remove-FileIfExists -Path $duplicateTypeEnumPath
Remove-FileIfExists -Path $duplicateSeverityEnumPath
Remove-FileIfExists -Path $duplicateStatusEnumPath

Write-Utf8File -Path $servicePath -Content @'
using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class ContradictionSliceService
{
    private readonly VeritasAtlasDbContext _dbContext;

    public ContradictionSliceService(VeritasAtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Contradiction> CreateContradictionAsync(
        Guid primaryClaimId,
        Guid secondaryClaimId,
        string topic,
        string summary,
        string? contradictionType,
        string? severity,
        Guid? caseId,
        CancellationToken cancellationToken = default)
    {
        var primaryClaim = await _dbContext.Claims
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == primaryClaimId, cancellationToken);

        var secondaryClaim = await _dbContext.Claims
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == secondaryClaimId, cancellationToken);

        if (primaryClaim is null)
        {
            throw new InvalidOperationException($"Primary claim '{primaryClaimId}' was not found.");
        }

        if (secondaryClaim is null)
        {
            throw new InvalidOperationException($"Secondary claim '{secondaryClaimId}' was not found.");
        }

        var parsedType = Enum.TryParse<ContradictionType>(contradictionType, true, out var contradictionTypeValue)
            ? contradictionTypeValue
            : ContradictionType.Direct;

        var parsedSeverity = Enum.TryParse<ContradictionSeverity>(severity, true, out var contradictionSeverityValue)
            ? contradictionSeverityValue
            : ContradictionSeverity.Medium;

        var resolvedCaseId = caseId ?? primaryClaim.CaseId ?? secondaryClaim.CaseId;
        if (!resolvedCaseId.HasValue)
        {
            throw new InvalidOperationException("A contradiction requires a CaseId. Provide one explicitly or use claims already linked to a case.");
        }

        var entity = new Contradiction
        {
            CaseId = resolvedCaseId.Value,
            LeftClaimId = primaryClaimId,
            RightClaimId = secondaryClaimId,
            Type = parsedType,
            Severity = parsedSeverity,
            Status = ContradictionStatus.Draft,
            Summary = string.IsNullOrWhiteSpace(summary) ? topic.Trim() : summary.Trim(),
            Rationale = null,
            ConfidenceScoreId = null
        };

        _dbContext.Contradictions.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    public async Task<(int Total, List<Contradiction> Items)> GetContradictionsAsync(
        int page,
        int pageSize,
        Guid? claimId = null,
        Guid? caseId = null,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : pageSize;

        IQueryable<Contradiction> query = _dbContext.Contradictions.AsNoTracking();

        if (claimId.HasValue)
        {
            query = query.Where(x => x.LeftClaimId == claimId.Value || x.RightClaimId == claimId.Value);
        }

        if (caseId.HasValue)
        {
            query = query.Where(x => x.CaseId == caseId.Value);
        }

        query = query.OrderByDescending(x => x.CreatedAtUtc);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (total, items);
    }

    public async Task<Contradiction?> GetContradictionByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Contradictions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }
}
'@

Write-Utf8File -Path $controllerPath -Content @'
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Contradictions;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/contradictions")]
public sealed class ContradictionsController : ControllerBase
{
    private readonly ContradictionSliceService _contradictionSliceService;

    public ContradictionsController(ContradictionSliceService contradictionSliceService)
    {
        _contradictionSliceService = contradictionSliceService;
    }

    [HttpPost]
    public async Task<ActionResult<CreateContradictionResponse>> CreateContradiction(
        [FromBody] CreateContradictionRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _contradictionSliceService.CreateContradictionAsync(
            request.PrimaryClaimId,
            request.SecondaryClaimId,
            request.Topic,
            request.Summary,
            request.ContradictionType,
            request.Severity,
            request.CaseId,
            cancellationToken);

        var response = new CreateContradictionResponse(
            entity.Id,
            entity.LeftClaimId,
            entity.RightClaimId,
            entity.CaseId,
            request.Topic,
            entity.Summary,
            entity.Type.ToString(),
            entity.Severity.ToString(),
            entity.Status.ToString(),
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);

        return Ok(response);
    }

    [HttpGet]
    public async Task<ActionResult<GetContradictionsResponse>> GetContradictions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? claimId = null,
        [FromQuery] Guid? caseId = null,
        CancellationToken cancellationToken = default)
    {
        var (total, items) = await _contradictionSliceService.GetContradictionsAsync(page, pageSize, claimId, caseId, cancellationToken);

        var mapped = items.Select(entity => new GetContradictionsItemResponse(
            entity.Id,
            entity.LeftClaimId,
            entity.RightClaimId,
            entity.CaseId,
            entity.Summary,
            entity.Summary,
            entity.Type.ToString(),
            entity.Severity.ToString(),
            entity.Status.ToString(),
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc
        )).ToArray();

        return Ok(new GetContradictionsResponse(
            mapped,
            page,
            pageSize,
            total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize)
        ));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetContradictionResponse>> GetContradiction(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _contradictionSliceService.GetContradictionByIdAsync(id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        return Ok(new GetContradictionResponse(
            entity.Id,
            entity.LeftClaimId,
            entity.RightClaimId,
            entity.CaseId,
            entity.Summary,
            entity.Summary,
            entity.Type.ToString(),
            entity.Severity.ToString(),
            entity.Status.ToString(),
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc
        ));
    }
}
'@

Write-Host "Building backend..." -ForegroundColor Cyan
Build-Backend -RootDir $RootDir

Write-Host "Contradiction debug alignment fix completed successfully." -ForegroundColor Green
