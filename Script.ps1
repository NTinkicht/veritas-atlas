param(
    [string]$RootDir = "C:\Projects\veritas-atlas",
    [string]$ApiProject = "apps\api\VeritasAtlas.Api\VeritasAtlas.Api.csproj",
    [string]$WebAppDir = "apps\web\veritas-atlas-web",
    [string]$BaseUrl = "http://localhost:5091"
)

$ErrorActionPreference = "Stop"

function Ensure-Directory {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Force -Path $Path | Out-Null
    }
}

function Write-Utf8File {
    param(
        [string]$Path,
        [string]$Content
    )
    $parent = Split-Path -Parent $Path
    Ensure-Directory $parent
    Set-Content -Path $Path -Value $Content -Encoding UTF8
    Write-Host "Wrote: $Path" -ForegroundColor Green
}

function Add-Section {
    param(
        [string]$OutputPath,
        [string]$Title,
        [string]$Content
    )
    Add-Content -Path $OutputPath -Value ""
    Add-Content -Path $OutputPath -Value ("=" * 120)
    Add-Content -Path $OutputPath -Value $Title
    Add-Content -Path $OutputPath -Value ("=" * 120)
    Add-Content -Path $OutputPath -Value $Content
}

function Wait-ForApi {
    param(
        [string]$HealthUrl,
        [int]$MaxAttempts = 40
    )

    for ($i = 1; $i -le $MaxAttempts; $i++) {
        try {
            $null = Invoke-RestMethod -Method Get -Uri $HealthUrl -TimeoutSec 2
            return $true
        }
        catch {
            Start-Sleep -Milliseconds 750
        }
    }

    return $false
}

$solutionPath = Join-Path $RootDir "VeritasAtlas.slnx"
$apiProjectPath = Join-Path $RootDir $ApiProject
$webRoot = Join-Path $RootDir $WebAppDir
$apiDir = Join-Path $RootDir "apps\api\VeritasAtlas.Api"
$appDir = Join-Path $RootDir "apps\api\VeritasAtlas.Application"
$infraDir = Join-Path $RootDir "apps\api\VeritasAtlas.Infrastructure"

if (-not (Test-Path $solutionPath)) {
    throw "Solution file not found: $solutionPath"
}
if (-not (Test-Path $apiProjectPath)) {
    throw "API project not found: $apiProjectPath"
}
if (-not (Test-Path $webRoot)) {
    throw "Web app folder not found: $webRoot"
}

$sourceInterfacePath = Join-Path $appDir "Interfaces\ISourceService.cs"
$documentInterfacePath = Join-Path $appDir "Interfaces\IDocumentService.cs"
$evidenceInterfacePath = Join-Path $appDir "Interfaces\IEvidenceService.cs"

$sourceServicePath = Join-Path $infraDir "Services\SourceService.cs"
$documentServicePath = Join-Path $infraDir "Services\DocumentService.cs"
$evidenceServicePath = Join-Path $infraDir "Services\EvidenceService.cs"

$sourcesControllerPath = Join-Path $apiDir "Controllers\SourcesController.cs"
$documentsControllerPath = Join-Path $apiDir "Controllers\DocumentsController.cs"
$evidenceControllerPath = Join-Path $apiDir "Controllers\EvidenceController.cs"

$sourcesApiPath = Join-Path $webRoot "src\api\sources.ts"
$documentsApiPath = Join-Path $webRoot "src\api\documents.ts"
$evidenceApiPath = Join-Path $webRoot "src\api\evidenceList.ts"

$useSourcesHookPath = Join-Path $webRoot "src\hooks\useSources.ts"
$useDocumentsHookPath = Join-Path $webRoot "src\hooks\useDocuments.ts"
$useEvidenceHookPath = Join-Path $webRoot "src\hooks\useEvidenceList.ts"

$sourcesPagePath = Join-Path $webRoot "src\pages\SourcesPage.tsx"
$documentsPagePath = Join-Path $webRoot "src\pages\DocumentsPage.tsx"
$evidencePagePath = Join-Path $webRoot "src\pages\EvidencePage.tsx"

Push-Location $RootDir

Write-Host ""
Write-Host "Checkpointing current code with git..." -ForegroundColor Cyan

git add -A
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "git add failed."
}

$commitMessage = "checkpoint before phase 6.4 - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
git commit -m $commitMessage
if ($LASTEXITCODE -ne 0) {
    Write-Host "No new commit created. Continuing with phase 6.4." -ForegroundColor Yellow
}
else {
    Write-Host "Created git commit: $commitMessage" -ForegroundColor Green
}

Pop-Location

$sourceInterface = @'
using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;

namespace VeritasAtlas.Application.Interfaces;

public interface ISourceService
{
    Task<Source> RegisterSourceAsync(
        string name,
        SourceType type,
        string reference,
        string? createdBy = null,
        CancellationToken cancellationToken = default);

    Task<PagedListResult<Source>> GetSourcesAsync(
        int page,
        int pageSize,
        string? search = null,
        SourceType? type = null,
        SourceStatus? status = null,
        CancellationToken cancellationToken = default);
}
'@

$documentInterface = @'
using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;

namespace VeritasAtlas.Application.Interfaces;

public interface IDocumentService
{
    Task<Document> AddDocumentAsync(
        Guid sourceId,
        string title,
        string content,
        string? externalReference = null,
        string? createdBy = null,
        CancellationToken cancellationToken = default);

    Task<PagedListResult<Document>> GetDocumentsAsync(
        int page,
        int pageSize,
        Guid? sourceId = null,
        DocumentStatus? status = null,
        CancellationToken cancellationToken = default);
}
'@

$evidenceInterface = @'
using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;

namespace VeritasAtlas.Application.Interfaces;

public interface IEvidenceService
{
    Task<Evidence> AddEvidenceAsync(
        Guid documentId,
        string quote,
        int? startOffset = null,
        int? endOffset = null,
        string? context = null,
        string? createdBy = null,
        CancellationToken cancellationToken = default);

    Task<PagedListResult<Evidence>> GetEvidenceAsync(
        int page,
        int pageSize,
        Guid? documentId = null,
        EvidenceStatus? status = null,
        CancellationToken cancellationToken = default);
}
'@

$sourceService = @'
using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Domain.ValueObjects;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class SourceService : ISourceService
{
    private readonly VeritasAtlasDbContext _dbContext;

    public SourceService(VeritasAtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Source> RegisterSourceAsync(
        string name,
        SourceType type,
        string reference,
        string? createdBy = null,
        CancellationToken cancellationToken = default)
    {
        var entity = new Source
        {
            Name = name,
            Type = type,
            Status = default,
            TrustTier = default,
            Reference = BuildReference(reference)
        };

        _dbContext.Sources.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    public async Task<PagedListResult<Source>> GetSourcesAsync(
        int page,
        int pageSize,
        string? search = null,
        SourceType? type = null,
        SourceStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : pageSize;

        IQueryable<Source> query = _dbContext.Sources.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();

            query = query.Where(x =>
                x.Name.ToLower().Contains(term) ||
                (x.Reference != null && (
                    (x.Reference.ExternalId != null && x.Reference.ExternalId.ToLower().Contains(term)) ||
                    (x.Reference.Url != null && x.Reference.Url.ToLower().Contains(term)) ||
                    (x.Reference.Domain != null && x.Reference.Domain.ToLower().Contains(term)) ||
                    (x.Reference.LanguageCode != null && x.Reference.LanguageCode.ToLower().Contains(term))
                )));
        }

        if (type.HasValue)
        {
            query = query.Where(x => x.Type == type.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        query = query.OrderByDescending(x => x.CreatedAtUtc);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedListResult<Source>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    private static SourceReference? BuildReference(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        if (Uri.TryCreate(reference, UriKind.Absolute, out var uri))
        {
            return new SourceReference(
                null,
                uri.ToString(),
                uri.Host,
                null);
        }

        return new SourceReference(
            reference,
            null,
            null,
            null);
    }
}
'@

$documentService = @'
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class DocumentService : IDocumentService
{
    private readonly VeritasAtlasDbContext _dbContext;

    public DocumentService(VeritasAtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Document> AddDocumentAsync(
        Guid sourceId,
        string title,
        string content,
        string? externalReference = null,
        string? createdBy = null,
        CancellationToken cancellationToken = default)
    {
        string? externalId = null;
        string? url = null;

        if (!string.IsNullOrWhiteSpace(externalReference))
        {
            if (Uri.TryCreate(externalReference, UriKind.Absolute, out var uri))
            {
                url = uri.ToString();
            }
            else
            {
                externalId = externalReference;
            }
        }

        var entity = new Document
        {
            SourceId = sourceId,
            Title = title,
            Type = default,
            Status = default,
            ExternalId = externalId,
            Url = url,
            ContentHash = ComputeSha256(content),
            RetrievedAtUtc = DateTime.UtcNow
        };

        _dbContext.Documents.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    public async Task<PagedListResult<Document>> GetDocumentsAsync(
        int page,
        int pageSize,
        Guid? sourceId = null,
        DocumentStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : pageSize;

        IQueryable<Document> query = _dbContext.Documents.AsNoTracking();

        if (sourceId.HasValue)
        {
            query = query.Where(x => x.SourceId == sourceId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        query = query.OrderByDescending(x => x.CreatedAtUtc);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedListResult<Document>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    private static string? ComputeSha256(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
'@

$evidenceService = @'
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Domain.ValueObjects;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class EvidenceService : IEvidenceService
{
    private readonly VeritasAtlasDbContext _dbContext;

    public EvidenceService(VeritasAtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Evidence> AddEvidenceAsync(
        Guid documentId,
        string quote,
        int? startOffset = null,
        int? endOffset = null,
        string? context = null,
        string? createdBy = null,
        CancellationToken cancellationToken = default)
    {
        var document = await _dbContext.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == documentId, cancellationToken);

        if (document is null)
        {
            throw new InvalidOperationException($"Document '{documentId}' was not found.");
        }

        var content = string.IsNullOrWhiteSpace(context)
            ? quote
            : quote + Environment.NewLine + Environment.NewLine + context;

        var entity = new Evidence
        {
            SourceId = document.SourceId,
            DocumentId = documentId,
            Type = default,
            Status = default,
            Content = content,
            ContentHash = ComputeSha256(content),
            LanguageCode = document.LanguageCode,
            Span = (startOffset.HasValue && endOffset.HasValue)
                ? new DocumentSpan(startOffset.Value, endOffset.Value)
                : null,
            CapturedAtUtc = DateTime.UtcNow
        };

        _dbContext.Evidences.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    public async Task<PagedListResult<Evidence>> GetEvidenceAsync(
        int page,
        int pageSize,
        Guid? documentId = null,
        EvidenceStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : pageSize;

        IQueryable<Evidence> query = _dbContext.Evidences.AsNoTracking();

        if (documentId.HasValue)
        {
            query = query.Where(x => x.DocumentId == documentId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        query = query.OrderByDescending(x => x.CreatedAtUtc);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedListResult<Evidence>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    private static string? ComputeSha256(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
'@

$sourcesController = @'
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Sources;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Domain.ValueObjects;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/sources")]
public sealed class SourcesController : ControllerBase
{
    private readonly ISourceService _sourceService;

    public SourcesController(ISourceService sourceService)
    {
        _sourceService = sourceService;
    }

    [HttpGet]
    public async Task<ActionResult<GetSourcesResponse>> GetSources(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? type = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        SourceType? parsedType = null;
        if (!string.IsNullOrWhiteSpace(type))
        {
            if (!Enum.TryParse<SourceType>(type, true, out var sourceType))
            {
                return BadRequest(new { message = "Invalid source type filter." });
            }
            parsedType = sourceType;
        }

        SourceStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<SourceStatus>(status, true, out var sourceStatus))
            {
                return BadRequest(new { message = "Invalid source status filter." });
            }
            parsedStatus = sourceStatus;
        }

        var result = await _sourceService.GetSourcesAsync(
            page,
            pageSize,
            search,
            parsedType,
            parsedStatus,
            cancellationToken);

        var items = result.Items
            .Select(entity => new GetSourcesItemResponse(
                entity.Id,
                entity.Name,
                entity.Type.ToString(),
                MapReference(entity.Reference),
                entity.Status.ToString(),
                entity.CreatedAtUtc,
                entity.UpdatedAtUtc))
            .ToArray();

        return Ok(new GetSourcesResponse(
            items,
            result.Page,
            result.PageSize,
            result.TotalCount,
            result.TotalPages));
    }

    [HttpPost]
    public async Task<ActionResult<CreateSourceResponse>> CreateSource(
        [FromBody] CreateSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<SourceType>(request.Type, true, out var sourceType))
        {
            return BadRequest(new { message = "Invalid source type." });
        }

        var entity = await _sourceService.RegisterSourceAsync(
            request.Name,
            sourceType,
            request.Reference ?? string.Empty,
            request.CreatedBy,
            cancellationToken);

        var response = new CreateSourceResponse(
            entity.Id,
            entity.Name,
            entity.Type.ToString(),
            MapReference(entity.Reference),
            entity.Status.ToString(),
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);

        return Ok(response);
    }

    private static SourceReferenceResponse? MapReference(SourceReference? reference)
    {
        if (reference is null)
        {
            return null;
        }

        return new SourceReferenceResponse(
            reference.ExternalId,
            reference.Url,
            reference.Domain,
            reference.LanguageCode);
    }
}
'@

$documentsController = @'
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Documents;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Enums;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/documents")]
public sealed class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;

    public DocumentsController(IDocumentService documentService)
    {
        _documentService = documentService;
    }

    [HttpGet]
    public async Task<ActionResult<GetDocumentsResponse>> GetDocuments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? sourceId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        DocumentStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<DocumentStatus>(status, true, out var documentStatus))
            {
                return BadRequest(new { message = "Invalid document status filter." });
            }
            parsedStatus = documentStatus;
        }

        var result = await _documentService.GetDocumentsAsync(
            page,
            pageSize,
            sourceId,
            parsedStatus,
            cancellationToken);

        var items = result.Items
            .Select(entity => new GetDocumentsItemResponse(
                entity.Id,
                entity.SourceId,
                entity.Title,
                entity.Status.ToString(),
                entity.CreatedAtUtc,
                entity.UpdatedAtUtc))
            .ToArray();

        return Ok(new GetDocumentsResponse(
            items,
            result.Page,
            result.PageSize,
            result.TotalCount,
            result.TotalPages));
    }

    [HttpPost]
    public async Task<ActionResult<CreateDocumentResponse>> CreateDocument(
        [FromBody] CreateDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _documentService.AddDocumentAsync(
            request.SourceId,
            request.Title,
            request.Content,
            request.ExternalReference,
            request.CreatedBy,
            cancellationToken);

        var response = new CreateDocumentResponse(
            entity.Id,
            entity.SourceId,
            entity.Title,
            entity.Status.ToString(),
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);

        return Ok(response);
    }
}
'@

$evidenceController = @'
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Evidence;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Enums;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/evidence")]
public sealed class EvidenceController : ControllerBase
{
    private readonly IEvidenceService _evidenceService;

    public EvidenceController(IEvidenceService evidenceService)
    {
        _evidenceService = evidenceService;
    }

    [HttpGet]
    public async Task<ActionResult<GetEvidenceResponse>> GetEvidence(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? documentId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        EvidenceStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<EvidenceStatus>(status, true, out var evidenceStatus))
            {
                return BadRequest(new { message = "Invalid evidence status filter." });
            }
            parsedStatus = evidenceStatus;
        }

        var result = await _evidenceService.GetEvidenceAsync(
            page,
            pageSize,
            documentId,
            parsedStatus,
            cancellationToken);

        var items = result.Items
            .Select(entity => new GetEvidenceItemResponse(
                entity.Id,
                entity.DocumentId,
                entity.Status.ToString(),
                entity.CreatedAtUtc,
                entity.UpdatedAtUtc))
            .ToArray();

        return Ok(new GetEvidenceResponse(
            items,
            result.Page,
            result.PageSize,
            result.TotalCount,
            result.TotalPages));
    }

    [HttpPost]
    public async Task<ActionResult<CreateEvidenceResponse>> CreateEvidence(
        [FromBody] CreateEvidenceRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _evidenceService.AddEvidenceAsync(
            request.DocumentId,
            request.Quote,
            request.StartOffset,
            request.EndOffset,
            request.Context,
            request.CreatedBy,
            cancellationToken);

        var response = new CreateEvidenceResponse(
            entity.Id,
            entity.DocumentId,
            request.Quote,
            entity.Status.ToString(),
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);

        return Ok(response);
    }
}
'@

$sourcesApi = @'
import { apiGet } from "./client";

export type SourceReferenceResponse = {
  externalId: string | null;
  url: string | null;
  domain: string | null;
  languageCode: string | null;
};

export type SourceItem = {
  id: string;
  name: string;
  type: string;
  reference: SourceReferenceResponse | null;
  status: string;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type SourcesResponse = {
  items: SourceItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export type SourcesQuery = {
  search?: string;
  type?: string;
  status?: string;
};

export async function getSources(query?: SourcesQuery): Promise<SourcesResponse> {
  const params = new URLSearchParams({
    page: "1",
    pageSize: "50",
  });

  if (query?.search) params.set("search", query.search);
  if (query?.type && query.type !== "All") params.set("type", query.type);
  if (query?.status && query.status !== "All") params.set("status", query.status);

  return apiGet<SourcesResponse>(`/api/v1/sources?${params.toString()}`);
}
'@

$documentsApi = @'
import { apiGet } from "./client";

export type DocumentItem = {
  id: string;
  sourceId: string;
  title: string;
  status: string;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type DocumentsResponse = {
  items: DocumentItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export type DocumentsQuery = {
  sourceId?: string;
  status?: string;
};

export async function getDocuments(query?: DocumentsQuery): Promise<DocumentsResponse> {
  const params = new URLSearchParams({
    page: "1",
    pageSize: "50",
  });

  if (query?.sourceId) params.set("sourceId", query.sourceId);
  if (query?.status && query.status !== "All") params.set("status", query.status);

  return apiGet<DocumentsResponse>(`/api/v1/documents?${params.toString()}`);
}
'@

$evidenceApi = @'
import { apiGet } from "./client";

export type EvidenceItem = {
  id: string;
  documentId: string | null;
  status: string;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type EvidenceResponse = {
  items: EvidenceItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export type EvidenceQuery = {
  documentId?: string;
  status?: string;
};

export async function getEvidenceList(query?: EvidenceQuery): Promise<EvidenceResponse> {
  const params = new URLSearchParams({
    page: "1",
    pageSize: "50",
  });

  if (query?.documentId) params.set("documentId", query.documentId);
  if (query?.status && query.status !== "All") params.set("status", query.status);

  return apiGet<EvidenceResponse>(`/api/v1/evidence?${params.toString()}`);
}
'@

$useSourcesHook = @'
import { useQuery } from "@tanstack/react-query";
import { getSources, type SourcesQuery } from "../api/sources";

export function useSources(query?: SourcesQuery) {
  return useQuery({
    queryKey: ["sources", query ?? {}],
    queryFn: () => getSources(query),
  });
}
'@

$useDocumentsHook = @'
import { useQuery } from "@tanstack/react-query";
import { getDocuments, type DocumentsQuery } from "../api/documents";

export function useDocuments(query?: DocumentsQuery) {
  return useQuery({
    queryKey: ["documents", query ?? {}],
    queryFn: () => getDocuments(query),
  });
}
'@

$useEvidenceHook = @'
import { useQuery } from "@tanstack/react-query";
import { getEvidenceList, type EvidenceQuery } from "../api/evidenceList";

export function useEvidenceList(query?: EvidenceQuery) {
  return useQuery({
    queryKey: ["evidence", query ?? {}],
    queryFn: () => getEvidenceList(query),
  });
}
'@

$sourcesPage = @'
import { Link } from "react-router-dom";
import { useMemo, useState, type CSSProperties } from "react";
import { useSources } from "../hooks/useSources";
import type { SourceItem, SourceReferenceResponse } from "../api/sources";

export function SourcesPage() {
  const [search, setSearch] = useState("");
  const [typeFilter, setTypeFilter] = useState("All");
  const [statusFilter, setStatusFilter] = useState("All");

  const sourcesQuery = useSources({
    search: search || undefined,
    type: typeFilter,
    status: statusFilter,
  });

  const items = sourcesQuery.data?.items ?? [];

  const types = useMemo(
    () => ["All", ...Array.from(new Set(items.map((x) => x.type))).sort()],
    [items]
  );

  const statuses = useMemo(
    () => ["All", ...Array.from(new Set(items.map((x) => x.status))).sort()],
    [items]
  );

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Sources</h1>
        <p style={{ color: "#555" }}>Live source catalog from Veritas Atlas API</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/dashboard">Dashboard</Link>
          <Link to="/ingestion">Ingestion</Link>
          <Link to="/sources">Sources</Link>
          <Link to="/documents">Documents</Link>
          <Link to="/evidence">Evidence</Link>
          <Link to="/statements/new">New Statement</Link>
        </nav>
      </header>

      <div style={{ marginBottom: "16px", display: "flex", gap: "12px", flexWrap: "wrap" }}>
        <Link to="/sources/new" style={actionLinkStyle}>Create New Source</Link>
        <Link to="/ingestion" style={actionLinkStyle}>Open Ingestion Workspace</Link>
      </div>

      <section style={filterPanelStyle}>
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search name, type, status, domain, url, or external id"
          style={inputStyle}
        />
        <select value={typeFilter} onChange={(e) => setTypeFilter(e.target.value)} style={selectStyle}>
          {types.map((value) => (
            <option key={value} value={value}>{value}</option>
          ))}
        </select>
        <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} style={selectStyle}>
          {statuses.map((value) => (
            <option key={value} value={value}>{value}</option>
          ))}
        </select>
      </section>

      {sourcesQuery.isLoading && <p>Loading sources...</p>}

      {sourcesQuery.isError && (
        <p style={{ color: "crimson" }}>
          Failed to load sources: {(sourcesQuery.error as Error).message}
        </p>
      )}

      {sourcesQuery.isSuccess && (
        <>
          <p>
            Showing {items.length} of {sourcesQuery.data.totalCount} sources
          </p>

          {items.length === 0 ? (
            <div style={emptyStateStyle}>
              <p style={{ margin: 0 }}>No sources match the current filters.</p>
            </div>
          ) : (
            <div style={{ overflowX: "auto" }}>
              <table style={tableStyle}>
                <thead>
                  <tr>
                    <th style={thStyle}>Name</th>
                    <th style={thStyle}>Type</th>
                    <th style={thStyle}>Reference</th>
                    <th style={thStyle}>Status</th>
                    <th style={thStyle}>Created</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((item: SourceItem) => (
                    <tr key={item.id}>
                      <td style={tdStyle}>{item.name}</td>
                      <td style={tdStyle}>{item.type}</td>
                      <td style={tdStyle}>{renderReference(item.reference)}</td>
                      <td style={tdStyle}>{item.status}</td>
                      <td style={tdStyle}>{formatDate(item.createdAtUtc)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </div>
  );
}

function renderReference(reference: SourceReferenceResponse | null) {
  if (!reference) {
    return "N/A";
  }

  return (
    <div style={{ display: "grid", gap: "4px" }}>
      {reference.url && (
        <a href={reference.url} target="_blank" rel="noreferrer">
          {reference.url}
        </a>
      )}
      {reference.domain && <span>Domain: {reference.domain}</span>}
      {reference.externalId && <span>ExternalId: {reference.externalId}</span>}
      {reference.languageCode && <span>Lang: {reference.languageCode}</span>}
    </div>
  );
}

function formatDate(value: string) {
  return new Date(value).toLocaleString();
}

const filterPanelStyle: CSSProperties = {
  display: "grid",
  gridTemplateColumns: "minmax(260px, 1fr) 180px 180px",
  gap: "12px",
  marginBottom: "16px",
};

const tableStyle: CSSProperties = {
  width: "100%",
  borderCollapse: "collapse",
  marginTop: "16px",
};

const thStyle: CSSProperties = {
  textAlign: "left",
  borderBottom: "1px solid #ccc",
  padding: "10px",
};

const tdStyle: CSSProperties = {
  borderBottom: "1px solid #eee",
  padding: "10px",
  verticalAlign: "top",
};

const actionLinkStyle: CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
  display: "inline-flex",
  alignItems: "center",
};

const inputStyle: CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  padding: "10px 12px",
  borderRadius: "8px",
  border: "1px solid #ccc",
  font: "inherit",
};

const selectStyle: CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  padding: "10px 12px",
  borderRadius: "8px",
  border: "1px solid #ccc",
  font: "inherit",
};

const emptyStateStyle: CSSProperties = {
  border: "1px solid #eee",
  borderRadius: "12px",
  padding: "20px",
  color: "#666",
};
'@

$documentsPage = @'
import { Link } from "react-router-dom";
import { useMemo, useState, type CSSProperties } from "react";
import { useDocuments } from "../hooks/useDocuments";
import type { DocumentItem } from "../api/documents";

export function DocumentsPage() {
  const [sourceIdFilter, setSourceIdFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("All");

  const documentsQuery = useDocuments({
    sourceId: sourceIdFilter || undefined,
    status: statusFilter,
  });

  const items = documentsQuery.data?.items ?? [];

  const statuses = useMemo(
    () => ["All", ...Array.from(new Set(items.map((x) => x.status))).sort()],
    [items]
  );

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Documents</h1>
        <p style={{ color: "#555" }}>Live document catalog from Veritas Atlas API</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/dashboard">Dashboard</Link>
          <Link to="/ingestion">Ingestion</Link>
          <Link to="/sources">Sources</Link>
          <Link to="/documents">Documents</Link>
          <Link to="/evidence">Evidence</Link>
          <Link to="/statements/new">New Statement</Link>
        </nav>
      </header>

      <div style={{ marginBottom: "16px", display: "flex", gap: "12px", flexWrap: "wrap" }}>
        <Link to="/documents/new" style={actionLinkStyle}>Create New Document</Link>
        <Link to="/ingestion" style={actionLinkStyle}>Open Ingestion Workspace</Link>
      </div>

      <section style={filterPanelStyle}>
        <input
          value={sourceIdFilter}
          onChange={(e) => setSourceIdFilter(e.target.value)}
          placeholder="Filter by source id"
          style={inputStyle}
        />
        <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} style={selectStyle}>
          {statuses.map((value) => (
            <option key={value} value={value}>{value}</option>
          ))}
        </select>
      </section>

      {documentsQuery.isLoading && <p>Loading documents...</p>}

      {documentsQuery.isError && (
        <p style={{ color: "crimson" }}>
          Failed to load documents: {(documentsQuery.error as Error).message}
        </p>
      )}

      {documentsQuery.isSuccess && (
        <>
          <p>
            Showing {items.length} of {documentsQuery.data.totalCount} documents
          </p>

          {items.length === 0 ? (
            <div style={emptyStateStyle}>
              <p style={{ margin: 0 }}>No documents match the current filters.</p>
            </div>
          ) : (
            <div style={{ overflowX: "auto" }}>
              <table style={tableStyle}>
                <thead>
                  <tr>
                    <th style={thStyle}>Id</th>
                    <th style={thStyle}>Source Id</th>
                    <th style={thStyle}>Title</th>
                    <th style={thStyle}>Status</th>
                    <th style={thStyle}>Created</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((item: DocumentItem) => (
                    <tr key={item.id}>
                      <td style={tdStyle}>{item.id}</td>
                      <td style={tdStyle}>{item.sourceId}</td>
                      <td style={tdStyle}>{item.title}</td>
                      <td style={tdStyle}>{item.status}</td>
                      <td style={tdStyle}>{formatDate(item.createdAtUtc)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </div>
  );
}

function formatDate(value: string) {
  return new Date(value).toLocaleString();
}

const filterPanelStyle: CSSProperties = {
  display: "grid",
  gridTemplateColumns: "minmax(260px, 1fr) 180px",
  gap: "12px",
  marginBottom: "16px",
};

const tableStyle: CSSProperties = {
  width: "100%",
  borderCollapse: "collapse",
  marginTop: "16px",
};

const thStyle: CSSProperties = {
  textAlign: "left",
  borderBottom: "1px solid #ccc",
  padding: "10px",
};

const tdStyle: CSSProperties = {
  borderBottom: "1px solid #eee",
  padding: "10px",
  verticalAlign: "top",
};

const actionLinkStyle: CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
  display: "inline-flex",
  alignItems: "center",
};

const inputStyle: CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  padding: "10px 12px",
  borderRadius: "8px",
  border: "1px solid #ccc",
  font: "inherit",
};

const selectStyle: CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  padding: "10px 12px",
  borderRadius: "8px",
  border: "1px solid #ccc",
  font: "inherit",
};

const emptyStateStyle: CSSProperties = {
  border: "1px solid #eee",
  borderRadius: "12px",
  padding: "20px",
  color: "#666",
};
'@

$evidencePage = @'
import { Link } from "react-router-dom";
import { useMemo, useState, type CSSProperties } from "react";
import { useEvidenceList } from "../hooks/useEvidenceList";
import type { EvidenceItem } from "../api/evidenceList";

export function EvidencePage() {
  const [documentIdFilter, setDocumentIdFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("All");

  const evidenceQuery = useEvidenceList({
    documentId: documentIdFilter || undefined,
    status: statusFilter,
  });

  const items = evidenceQuery.data?.items ?? [];

  const statuses = useMemo(
    () => ["All", ...Array.from(new Set(items.map((x) => x.status))).sort()],
    [items]
  );

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Evidence</h1>
        <p style={{ color: "#555" }}>Live evidence catalog from Veritas Atlas API</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/dashboard">Dashboard</Link>
          <Link to="/ingestion">Ingestion</Link>
          <Link to="/sources">Sources</Link>
          <Link to="/documents">Documents</Link>
          <Link to="/evidence">Evidence</Link>
          <Link to="/statements/new">New Statement</Link>
        </nav>
      </header>

      <div style={{ marginBottom: "16px", display: "flex", gap: "12px", flexWrap: "wrap" }}>
        <Link to="/evidence/new" style={actionLinkStyle}>Create New Evidence</Link>
        <Link to="/ingestion" style={actionLinkStyle}>Open Ingestion Workspace</Link>
      </div>

      <section style={filterPanelStyle}>
        <input
          value={documentIdFilter}
          onChange={(e) => setDocumentIdFilter(e.target.value)}
          placeholder="Filter by document id"
          style={inputStyle}
        />
        <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} style={selectStyle}>
          {statuses.map((value) => (
            <option key={value} value={value}>{value}</option>
          ))}
        </select>
      </section>

      {evidenceQuery.isLoading && <p>Loading evidence...</p>}

      {evidenceQuery.isError && (
        <p style={{ color: "crimson" }}>
          Failed to load evidence: {(evidenceQuery.error as Error).message}
        </p>
      )}

      {evidenceQuery.isSuccess && (
        <>
          <p>
            Showing {items.length} of {evidenceQuery.data.totalCount} evidence items
          </p>

          {items.length === 0 ? (
            <div style={emptyStateStyle}>
              <p style={{ margin: 0 }}>No evidence items match the current filters.</p>
            </div>
          ) : (
            <div style={{ overflowX: "auto" }}>
              <table style={tableStyle}>
                <thead>
                  <tr>
                    <th style={thStyle}>Id</th>
                    <th style={thStyle}>Document Id</th>
                    <th style={thStyle}>Status</th>
                    <th style={thStyle}>Created</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((item: EvidenceItem) => (
                    <tr key={item.id}>
                      <td style={tdStyle}>{item.id}</td>
                      <td style={tdStyle}>{item.documentId ?? "N/A"}</td>
                      <td style={tdStyle}>{item.status}</td>
                      <td style={tdStyle}>{formatDate(item.createdAtUtc)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </div>
  );
}

function formatDate(value: string) {
  return new Date(value).toLocaleString();
}

const filterPanelStyle: CSSProperties = {
  display: "grid",
  gridTemplateColumns: "minmax(260px, 1fr) 180px",
  gap: "12px",
  marginBottom: "16px",
};

const tableStyle: CSSProperties = {
  width: "100%",
  borderCollapse: "collapse",
  marginTop: "16px",
};

const thStyle: CSSProperties = {
  textAlign: "left",
  borderBottom: "1px solid #ccc",
  padding: "10px",
};

const tdStyle: CSSProperties = {
  borderBottom: "1px solid #eee",
  padding: "10px",
  verticalAlign: "top",
};

const actionLinkStyle: CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
  display: "inline-flex",
  alignItems: "center",
};

const inputStyle: CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  padding: "10px 12px",
  borderRadius: "8px",
  border: "1px solid #ccc",
  font: "inherit",
};

const selectStyle: CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  padding: "10px 12px",
  borderRadius: "8px",
  border: "1px solid #ccc",
  font: "inherit",
};

const emptyStateStyle: CSSProperties = {
  border: "1px solid #eee",
  borderRadius: "12px",
  padding: "20px",
  color: "#666",
};
'@

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$diagDir = Join-Path $RootDir "_diagnostics\phase-6-4-$timestamp"
Ensure-Directory $diagDir
$reportPath = Join-Path $diagDir "phase-6-4-report.txt"
$stdoutPath = Join-Path $diagDir "api-stdout.log"
$stderrPath = Join-Path $diagDir "api-stderr.log"

Set-Content -Path $reportPath -Value "Phase 6.4 cross-entity filtering verification`r`nGenerated: $(Get-Date -Format s)`r`nRoot: $RootDir" -Encoding UTF8

Write-Host ""
Write-Host "Applying Phase 6.4 - cross-entity filtering..." -ForegroundColor Cyan

Write-Utf8File -Path $sourceInterfacePath -Content $sourceInterface
Write-Utf8File -Path $documentInterfacePath -Content $documentInterface
Write-Utf8File -Path $evidenceInterfacePath -Content $evidenceInterface

Write-Utf8File -Path $sourceServicePath -Content $sourceService
Write-Utf8File -Path $documentServicePath -Content $documentService
Write-Utf8File -Path $evidenceServicePath -Content $evidenceService

Write-Utf8File -Path $sourcesControllerPath -Content $sourcesController
Write-Utf8File -Path $documentsControllerPath -Content $documentsController
Write-Utf8File -Path $evidenceControllerPath -Content $evidenceController

Write-Utf8File -Path $sourcesApiPath -Content $sourcesApi
Write-Utf8File -Path $documentsApiPath -Content $documentsApi
Write-Utf8File -Path $evidenceApiPath -Content $evidenceApi

Write-Utf8File -Path $useSourcesHookPath -Content $useSourcesHook
Write-Utf8File -Path $useDocumentsHookPath -Content $useDocumentsHook
Write-Utf8File -Path $useEvidenceHookPath -Content $useEvidenceHook

Write-Utf8File -Path $sourcesPagePath -Content $sourcesPage
Write-Utf8File -Path $documentsPagePath -Content $documentsPage
Write-Utf8File -Path $evidencePagePath -Content $evidencePage

Write-Host ""
Write-Host "Running clean / restore / build..." -ForegroundColor Cyan

Push-Location $RootDir

dotnet clean $solutionPath
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "dotnet clean failed."
}

dotnet restore $solutionPath
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "dotnet restore failed."
}

dotnet build $solutionPath
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "dotnet build failed."
}

Push-Location $webRoot
npm run build
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    Pop-Location
    throw "npm run build failed."
}
Pop-Location

Write-Host ""
Write-Host "Starting API for phase 6.4 smoke test..." -ForegroundColor Cyan

$apiProcess = Start-Process `
    -FilePath "dotnet" `
    -ArgumentList @("run", "--project", $apiProjectPath, "--no-build") `
    -WorkingDirectory $RootDir `
    -RedirectStandardOutput $stdoutPath `
    -RedirectStandardError $stderrPath `
    -PassThru

try {
    $healthOk = Wait-ForApi -HealthUrl "$BaseUrl/health"
    if (-not $healthOk) {
        throw "API did not become ready."
    }

    $sourceBody = @{
        name = "Filter Smoke Source"
        type = "Article"
        reference = "https://example.com/filter-test"
        createdBy = "phase-6-4"
    } | ConvertTo-Json -Depth 5

    $sourceResponse = Invoke-RestMethod `
        -Method Post `
        -Uri "$BaseUrl/api/v1/sources" `
        -ContentType "application/json" `
        -Body $sourceBody

    Add-Section -OutputPath $reportPath -Title "POST /api/v1/sources" -Content (($sourceResponse | ConvertTo-Json -Depth 10))

    $documentBody = @{
        sourceId = $sourceResponse.id
        title = "Filter Smoke Document"
        content = "Document content for filtering."
        externalReference = "filter-doc-001"
        createdBy = "phase-6-4"
    } | ConvertTo-Json -Depth 5

    $documentResponse = Invoke-RestMethod `
        -Method Post `
        -Uri "$BaseUrl/api/v1/documents" `
        -ContentType "application/json" `
        -Body $documentBody

    Add-Section -OutputPath $reportPath -Title "POST /api/v1/documents" -Content (($documentResponse | ConvertTo-Json -Depth 10))

    $evidenceBody = @{
        documentId = $documentResponse.id
        quote = "Evidence snippet for filtering."
        startOffset = 0
        endOffset = 30
        context = "Evidence context"
        createdBy = "phase-6-4"
    } | ConvertTo-Json -Depth 5

    $evidenceResponse = Invoke-RestMethod `
        -Method Post `
        -Uri "$BaseUrl/api/v1/evidence" `
        -ContentType "application/json" `
        -Body $evidenceBody

    Add-Section -OutputPath $reportPath -Title "POST /api/v1/evidence" -Content (($evidenceResponse | ConvertTo-Json -Depth 10))

    $filteredSources = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/sources?page=1&pageSize=10&search=filter&type=Article&status=Draft"
    Add-Section -OutputPath $reportPath -Title "GET /api/v1/sources filtered" -Content (($filteredSources | ConvertTo-Json -Depth 10))

    $filteredDocuments = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/documents?page=1&pageSize=10&sourceId=$($sourceResponse.id)&status=Draft"
    Add-Section -OutputPath $reportPath -Title "GET /api/v1/documents filtered" -Content (($filteredDocuments | ConvertTo-Json -Depth 10))

    $evidenceStatus = [uri]::EscapeDataString($evidenceResponse.status)
    $filteredEvidence = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/evidence?page=1&pageSize=10&documentId=$($documentResponse.id)&status=$evidenceStatus"
    Add-Section -OutputPath $reportPath -Title "GET /api/v1/evidence filtered" -Content (($filteredEvidence | ConvertTo-Json -Depth 10))

    Write-Host ""
    Write-Host "Phase 6.4 completed successfully." -ForegroundColor Green
    Write-Host "Verification report: $reportPath" -ForegroundColor Cyan
}
finally {
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Write-Host ""
        Write-Host "Stopping API..." -ForegroundColor Cyan
        Stop-Process -Id $apiProcess.Id -Force
    }

    Pop-Location
}