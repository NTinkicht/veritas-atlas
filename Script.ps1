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

if (-not (Test-Path $solutionPath)) {
    throw "Solution file not found: $solutionPath"
}
if (-not (Test-Path $apiProjectPath)) {
    throw "API project not found: $apiProjectPath"
}
if (-not (Test-Path $webRoot)) {
    throw "Web app folder not found: $webRoot"
}

Push-Location $RootDir

Write-Host ""
Write-Host "Checkpointing current code with git..." -ForegroundColor Cyan

git add -A
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "git add failed."
}

$commitMessage = "checkpoint before phase 6.5 - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
git commit -m $commitMessage
if ($LASTEXITCODE -ne 0) {
    Write-Host "No new commit created. Continuing with phase 6.5." -ForegroundColor Yellow
}
else {
    Write-Host "Created git commit: $commitMessage" -ForegroundColor Green
}

Pop-Location

$sourcesContractsPath = Join-Path $apiDir "Contracts\SourcesContracts.cs"
$documentsContractsPath = Join-Path $apiDir "Contracts\DocumentsContracts.cs"
$evidenceContractsPath = Join-Path $apiDir "Contracts\EvidenceContracts.cs"

$sourcesControllerPath = Join-Path $apiDir "Controllers\SourcesController.cs"
$documentsControllerPath = Join-Path $apiDir "Controllers\DocumentsController.cs"
$evidenceControllerPath = Join-Path $apiDir "Controllers\EvidenceController.cs"

$sourcesApiPath = Join-Path $webRoot "src\api\sources.ts"
$documentsApiPath = Join-Path $webRoot "src\api\documents.ts"
$evidenceApiPath = Join-Path $webRoot "src\api\evidenceList.ts"

$useSourceDetailHookPath = Join-Path $webRoot "src\hooks\useSourceDetail.ts"
$useDocumentDetailHookPath = Join-Path $webRoot "src\hooks\useDocumentDetail.ts"
$useEvidenceDetailHookPath = Join-Path $webRoot "src\hooks\useEvidenceDetail.ts"

$sourceDetailPagePath = Join-Path $webRoot "src\pages\SourceDetailPage.tsx"
$documentDetailPagePath = Join-Path $webRoot "src\pages\DocumentDetailPage.tsx"
$evidenceDetailPagePath = Join-Path $webRoot "src\pages\EvidenceDetailPage.tsx"

$sourcesPagePath = Join-Path $webRoot "src\pages\SourcesPage.tsx"
$documentsPagePath = Join-Path $webRoot "src\pages\DocumentsPage.tsx"
$evidencePagePath = Join-Path $webRoot "src\pages\EvidencePage.tsx"
$mainTsxPath = Join-Path $webRoot "src\main.tsx"

$sourcesContracts = @'
namespace VeritasAtlas.Api.Contracts.Sources;

public sealed record SourceReferenceResponse(
    string? ExternalId,
    string? Url,
    string? Domain,
    string? LanguageCode);

public sealed record CreateSourceRequest(
    string Name,
    string Type,
    string? Reference,
    string? CreatedBy);

public sealed record CreateSourceResponse(
    Guid Id,
    string Name,
    string Type,
    SourceReferenceResponse? Reference,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetSourcesItemResponse(
    Guid Id,
    string Name,
    string Type,
    SourceReferenceResponse? Reference,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetSourceResponse(
    Guid Id,
    string Name,
    string? Description,
    string Type,
    string Status,
    string TrustTier,
    SourceReferenceResponse? Reference,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetSourcesResponse(
    IReadOnlyCollection<GetSourcesItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
'@

$documentsContracts = @'
namespace VeritasAtlas.Api.Contracts.Documents;

public sealed record CreateDocumentRequest(
    Guid SourceId,
    string Title,
    string Content,
    string? ExternalReference,
    string? CreatedBy);

public sealed record CreateDocumentResponse(
    Guid Id,
    Guid SourceId,
    string Title,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetDocumentsItemResponse(
    Guid Id,
    Guid SourceId,
    string Title,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetDocumentResponse(
    Guid Id,
    Guid SourceId,
    string Title,
    string Type,
    string Status,
    string? LanguageCode,
    string? ExternalId,
    string? Url,
    string? ContentHash,
    DateTime? PublishedAtUtc,
    DateTime? RetrievedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetDocumentsResponse(
    IReadOnlyCollection<GetDocumentsItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
'@

$evidenceContracts = @'
namespace VeritasAtlas.Api.Contracts.Evidence;

public sealed record CreateEvidenceRequest(
    Guid DocumentId,
    string Quote,
    int? StartOffset,
    int? EndOffset,
    string? Context,
    string? CreatedBy);

public sealed record CreateEvidenceResponse(
    Guid Id,
    Guid? DocumentId,
    string Quote,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetEvidenceItemResponse(
    Guid Id,
    Guid? DocumentId,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetEvidenceResponseItemSpan(
    int StartOffset,
    int EndOffset);

public sealed record GetEvidenceResponse(
    Guid Id,
    Guid SourceId,
    Guid? DocumentId,
    string Type,
    string Status,
    string Content,
    string? ContentHash,
    string? LanguageCode,
    GetEvidenceResponseItemSpan? Span,
    DateTime? CapturedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetEvidenceListResponse(
    IReadOnlyCollection<GetEvidenceItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
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

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetSourceResponse>> GetSourceById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await _sourceService.GetSourcesAsync(1, int.MaxValue, null, null, null, cancellationToken);
        var entity = result.Items.FirstOrDefault(x => x.Id == id);

        if (entity is null)
        {
            return NotFound();
        }

        return Ok(new GetSourceResponse(
            entity.Id,
            entity.Name,
            entity.Description,
            entity.Type.ToString(),
            entity.Status.ToString(),
            entity.TrustTier.ToString(),
            MapReference(entity.Reference),
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc));
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

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetDocumentResponse>> GetDocumentById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await _documentService.GetDocumentsAsync(1, int.MaxValue, null, null, cancellationToken);
        var entity = result.Items.FirstOrDefault(x => x.Id == id);

        if (entity is null)
        {
            return NotFound();
        }

        return Ok(new GetDocumentResponse(
            entity.Id,
            entity.SourceId,
            entity.Title,
            entity.Type.ToString(),
            entity.Status.ToString(),
            entity.LanguageCode,
            entity.ExternalId,
            entity.Url,
            entity.ContentHash,
            entity.PublishedAtUtc,
            entity.RetrievedAtUtc,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc));
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
    public async Task<ActionResult<GetEvidenceListResponse>> GetEvidence(
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

        return Ok(new GetEvidenceListResponse(
            items,
            result.Page,
            result.PageSize,
            result.TotalCount,
            result.TotalPages));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetEvidenceResponse>> GetEvidenceById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await _evidenceService.GetEvidenceAsync(1, int.MaxValue, null, null, cancellationToken);
        var entity = result.Items.FirstOrDefault(x => x.Id == id);

        if (entity is null)
        {
            return NotFound();
        }

        return Ok(new GetEvidenceResponse(
            entity.Id,
            entity.SourceId,
            entity.DocumentId,
            entity.Type.ToString(),
            entity.Status.ToString(),
            entity.Content,
            entity.ContentHash,
            entity.LanguageCode,
            entity.Span.HasValue ? new GetEvidenceResponseItemSpan(entity.Span.Value.StartOffset, entity.Span.Value.EndOffset) : null,
            entity.CapturedAtUtc,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc));
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

export type SourceDetail = {
  id: string;
  name: string;
  description: string | null;
  type: string;
  status: string;
  trustTier: string;
  reference: SourceReferenceResponse | null;
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

export async function getSourceById(id: string): Promise<SourceDetail> {
  return apiGet<SourceDetail>(`/api/v1/sources/${id}`);
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

export type DocumentDetail = {
  id: string;
  sourceId: string;
  title: string;
  type: string;
  status: string;
  languageCode: string | null;
  externalId: string | null;
  url: string | null;
  contentHash: string | null;
  publishedAtUtc: string | null;
  retrievedAtUtc: string | null;
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

export async function getDocumentById(id: string): Promise<DocumentDetail> {
  return apiGet<DocumentDetail>(`/api/v1/documents/${id}`);
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

export type EvidenceSpan = {
  startOffset: number;
  endOffset: number;
};

export type EvidenceDetail = {
  id: string;
  sourceId: string;
  documentId: string | null;
  type: string;
  status: string;
  content: string;
  contentHash: string | null;
  languageCode: string | null;
  span: EvidenceSpan | null;
  capturedAtUtc: string | null;
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

export async function getEvidenceById(id: string): Promise<EvidenceDetail> {
  return apiGet<EvidenceDetail>(`/api/v1/evidence/${id}`);
}
'@

$useSourceDetailHook = @'
import { useQuery } from "@tanstack/react-query";
import { getSourceById } from "../api/sources";

export function useSourceDetail(id?: string) {
  return useQuery({
    queryKey: ["source-detail", id],
    queryFn: () => getSourceById(id!),
    enabled: !!id,
  });
}
'@

$useDocumentDetailHook = @'
import { useQuery } from "@tanstack/react-query";
import { getDocumentById } from "../api/documents";

export function useDocumentDetail(id?: string) {
  return useQuery({
    queryKey: ["document-detail", id],
    queryFn: () => getDocumentById(id!),
    enabled: !!id,
  });
}
'@

$useEvidenceDetailHook = @'
import { useQuery } from "@tanstack/react-query";
import { getEvidenceById } from "../api/evidenceList";

export function useEvidenceDetail(id?: string) {
  return useQuery({
    queryKey: ["evidence-detail", id],
    queryFn: () => getEvidenceById(id!),
    enabled: !!id,
  });
}
'@

$sourceDetailPage = @'
import { Link, useParams } from "react-router-dom";
import { useSourceDetail } from "../hooks/useSourceDetail";

export function SourceDetailPage() {
  const { id } = useParams();
  const query = useSourceDetail(id);

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Loading source...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", color: "crimson" }}>Failed to load source: {(query.error as Error).message}</div>;
  }

  if (!query.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Source not found.</div>;
  }

  const item = query.data;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <nav style={{ display: "flex", gap: "16px", marginBottom: "20px", flexWrap: "wrap" }}>
        <Link to="/sources">Back to Sources</Link>
        <Link to="/documents?sourceIdPlaceholder=1">Documents</Link>
        <Link to="/documents/new">New Document</Link>
      </nav>

      <h1 style={{ marginTop: 0 }}>{item.name}</h1>

      <div style={cardStyle}>
        <Row label="Id" value={item.id} />
        <Row label="Type" value={item.type} />
        <Row label="Status" value={item.status} />
        <Row label="Trust Tier" value={item.trustTier} />
        <Row label="Description" value={item.description ?? "N/A"} />
        <Row label="Created" value={formatDate(item.createdAtUtc)} />
        <Row label="Updated" value={formatDate(item.updatedAtUtc)} />
      </div>

      <div style={cardStyle}>
        <h3 style={{ marginTop: 0 }}>Reference</h3>
        <Row label="ExternalId" value={item.reference?.externalId ?? "N/A"} />
        <Row label="Url" value={item.reference?.url ?? "N/A"} />
        <Row label="Domain" value={item.reference?.domain ?? "N/A"} />
        <Row label="LanguageCode" value={item.reference?.languageCode ?? "N/A"} />
      </div>

      <div style={{ display: "flex", gap: "12px", flexWrap: "wrap" }}>
        <Link to={`/documents?sourceId=${item.id}`} style={actionLinkStyle}>View Documents For This Source</Link>
        <Link to="/documents/new" style={actionLinkStyle}>Create Document</Link>
      </div>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "180px 1fr", gap: "12px", padding: "6px 0" }}>
      <strong>{label}</strong>
      <span>{value}</span>
    </div>
  );
}

function formatDate(value: string) {
  return new Date(value).toLocaleString();
}

const cardStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: "12px",
  padding: "16px",
  marginBottom: "16px",
};

const actionLinkStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
};
'@

$documentDetailPage = @'
import { Link, useParams } from "react-router-dom";
import { useDocumentDetail } from "../hooks/useDocumentDetail";

export function DocumentDetailPage() {
  const { id } = useParams();
  const query = useDocumentDetail(id);

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Loading document...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", color: "crimson" }}>Failed to load document: {(query.error as Error).message}</div>;
  }

  if (!query.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Document not found.</div>;
  }

  const item = query.data;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <nav style={{ display: "flex", gap: "16px", marginBottom: "20px", flexWrap: "wrap" }}>
        <Link to="/documents">Back to Documents</Link>
        <Link to={`/sources/${item.sourceId}`}>Source</Link>
        <Link to="/evidence/new">New Evidence</Link>
      </nav>

      <h1 style={{ marginTop: 0 }}>{item.title}</h1>

      <div style={cardStyle}>
        <Row label="Id" value={item.id} />
        <Row label="Source Id" value={item.sourceId} />
        <Row label="Type" value={item.type} />
        <Row label="Status" value={item.status} />
        <Row label="LanguageCode" value={item.languageCode ?? "N/A"} />
        <Row label="ExternalId" value={item.externalId ?? "N/A"} />
        <Row label="Url" value={item.url ?? "N/A"} />
        <Row label="ContentHash" value={item.contentHash ?? "N/A"} />
        <Row label="Published" value={item.publishedAtUtc ? formatDate(item.publishedAtUtc) : "N/A"} />
        <Row label="Retrieved" value={item.retrievedAtUtc ? formatDate(item.retrievedAtUtc) : "N/A"} />
        <Row label="Created" value={formatDate(item.createdAtUtc)} />
        <Row label="Updated" value={formatDate(item.updatedAtUtc)} />
      </div>

      <div style={{ display: "flex", gap: "12px", flexWrap: "wrap" }}>
        <Link to={`/sources/${item.sourceId}`} style={actionLinkStyle}>Open Source</Link>
        <Link to={`/evidence?documentId=${item.id}`} style={actionLinkStyle}>View Evidence For This Document</Link>
        <Link to="/evidence/new" style={actionLinkStyle}>Create Evidence</Link>
      </div>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "180px 1fr", gap: "12px", padding: "6px 0" }}>
      <strong>{label}</strong>
      <span>{value}</span>
    </div>
  );
}

function formatDate(value: string) {
  return new Date(value).toLocaleString();
}

const cardStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: "12px",
  padding: "16px",
  marginBottom: "16px",
};

const actionLinkStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
};
'@

$evidenceDetailPage = @'
import { Link, useParams } from "react-router-dom";
import { useEvidenceDetail } from "../hooks/useEvidenceDetail";

export function EvidenceDetailPage() {
  const { id } = useParams();
  const query = useEvidenceDetail(id);

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Loading evidence...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", color: "crimson" }}>Failed to load evidence: {(query.error as Error).message}</div>;
  }

  if (!query.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Evidence not found.</div>;
  }

  const item = query.data;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <nav style={{ display: "flex", gap: "16px", marginBottom: "20px", flexWrap: "wrap" }}>
        <Link to="/evidence">Back to Evidence</Link>
        {item.documentId && <Link to={`/documents/${item.documentId}`}>Document</Link>}
      </nav>

      <h1 style={{ marginTop: 0 }}>Evidence {item.id}</h1>

      <div style={cardStyle}>
        <Row label="Id" value={item.id} />
        <Row label="Source Id" value={item.sourceId} />
        <Row label="Document Id" value={item.documentId ?? "N/A"} />
        <Row label="Type" value={item.type} />
        <Row label="Status" value={item.status} />
        <Row label="LanguageCode" value={item.languageCode ?? "N/A"} />
        <Row label="ContentHash" value={item.contentHash ?? "N/A"} />
        <Row label="Captured" value={item.capturedAtUtc ? formatDate(item.capturedAtUtc) : "N/A"} />
        <Row label="Span" value={item.span ? `${item.span.startOffset} - ${item.span.endOffset}` : "N/A"} />
        <Row label="Created" value={formatDate(item.createdAtUtc)} />
        <Row label="Updated" value={formatDate(item.updatedAtUtc)} />
      </div>

      <div style={cardStyle}>
        <h3 style={{ marginTop: 0 }}>Content</h3>
        <pre style={{ whiteSpace: "pre-wrap", margin: 0, fontFamily: "inherit" }}>{item.content}</pre>
      </div>

      <div style={{ display: "flex", gap: "12px", flexWrap: "wrap" }}>
        {item.documentId && <Link to={`/documents/${item.documentId}`} style={actionLinkStyle}>Open Document</Link>}
        <Link to="/statements/new" style={actionLinkStyle}>Create Statement</Link>
      </div>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "180px 1fr", gap: "12px", padding: "6px 0" }}>
      <strong>{label}</strong>
      <span>{value}</span>
    </div>
  );
}

function formatDate(value: string) {
  return new Date(value).toLocaleString();
}

const cardStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: "12px",
  padding: "16px",
  marginBottom: "16px",
};

const actionLinkStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
};
'@

$sourcesPage = @'
import { Link, useSearchParams } from "react-router-dom";
import { useMemo, useState, type CSSProperties } from "react";
import { useSources } from "../hooks/useSources";
import type { SourceItem, SourceReferenceResponse } from "../api/sources";

export function SourcesPage() {
  const [params] = useSearchParams();
  const [search, setSearch] = useState("");
  const [typeFilter, setTypeFilter] = useState("All");
  const [statusFilter, setStatusFilter] = useState("All");

  const forcedSearch = params.get("search") ?? undefined;

  const sourcesQuery = useSources({
    search: forcedSearch ?? (search || undefined),
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
          value={forcedSearch ?? search}
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
                      <td style={tdStyle}><Link to={`/sources/${item.id}`}>{item.name}</Link></td>
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
import { Link, useSearchParams } from "react-router-dom";
import { useMemo, useState, type CSSProperties } from "react";
import { useDocuments } from "../hooks/useDocuments";
import type { DocumentItem } from "../api/documents";

export function DocumentsPage() {
  const [params] = useSearchParams();
  const forcedSourceId = params.get("sourceId") ?? "";
  const [sourceIdFilter, setSourceIdFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("All");

  const documentsQuery = useDocuments({
    sourceId: forcedSourceId || sourceIdFilter || undefined,
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
          value={forcedSourceId || sourceIdFilter}
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
                      <td style={tdStyle}><Link to={`/documents/${item.id}`}>{item.id}</Link></td>
                      <td style={tdStyle}><Link to={`/sources/${item.sourceId}`}>{item.sourceId}</Link></td>
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
import { Link, useSearchParams } from "react-router-dom";
import { useMemo, useState, type CSSProperties } from "react";
import { useEvidenceList } from "../hooks/useEvidenceList";
import type { EvidenceItem } from "../api/evidenceList";

export function EvidencePage() {
  const [params] = useSearchParams();
  const forcedDocumentId = params.get("documentId") ?? "";
  const [documentIdFilter, setDocumentIdFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("All");

  const evidenceQuery = useEvidenceList({
    documentId: forcedDocumentId || documentIdFilter || undefined,
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
          value={forcedDocumentId || documentIdFilter}
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
                      <td style={tdStyle}><Link to={`/evidence/${item.id}`}>{item.id}</Link></td>
                      <td style={tdStyle}>{item.documentId ? <Link to={`/documents/${item.documentId}`}>{item.documentId}</Link> : "N/A"}</td>
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

$mainTsx = @'
import React from "react";
import ReactDOM from "react-dom/client";
import {
  createBrowserRouter,
  RouterProvider,
  Link,
} from "react-router-dom";
import {
  QueryClient,
  QueryClientProvider,
  useQuery,
} from "@tanstack/react-query";
import { getDatabaseHealth, getHealth } from "./api/health";
import { PersonsPage } from "./pages/PersonsPage";
import { PersonDetailPage } from "./pages/PersonDetailPage";
import { CasesPage } from "./pages/CasesPage";
import { CreateCasePage } from "./pages/CreateCasePage";
import { CreatePersonPage } from "./pages/CreatePersonPage";
import { CreateStatementPage } from "./pages/CreateStatementPage";
import { CreateDocumentPage } from "./pages/CreateDocumentPage";
import { CreateEvidencePage } from "./pages/CreateEvidencePage";
import { CreateSourcePage } from "./pages/CreateSourcePage";
import { IngestionWorkspacePage } from "./pages/IngestionWorkspacePage";
import { SourcesPage } from "./pages/SourcesPage";
import { SourceDetailPage } from "./pages/SourceDetailPage";
import { DocumentsPage } from "./pages/DocumentsPage";
import { DocumentDetailPage } from "./pages/DocumentDetailPage";
import { EvidencePage } from "./pages/EvidencePage";
import { EvidenceDetailPage } from "./pages/EvidenceDetailPage";
import { CaseDetailPage } from "./pages/CaseDetailPage";
import { ReviewsPage } from "./pages/ReviewsPage";
import { DashboardPage } from "./pages/DashboardPage";
import "./index.css";

const queryClient = new QueryClient();

function Layout({ children }: { children: React.ReactNode }) {
  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Veritas Atlas</h1>
        <p style={{ color: "#555" }}>Frontend connected to live API</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/dashboard">Dashboard</Link>
          <Link to="/health">Health</Link>
          <Link to="/persons">Persons</Link>
          <Link to="/persons/new">New Person</Link>
          <Link to="/cases">Cases</Link>
          <Link to="/cases/new">New Case</Link>
          <Link to="/reviews">Reviews</Link>
          <Link to="/ingestion">Ingestion</Link>
          <Link to="/sources">Sources</Link>
          <Link to="/documents">Documents</Link>
          <Link to="/evidence">Evidence</Link>
          <Link to="/sources/new">New Source</Link>
          <Link to="/documents/new">New Document</Link>
          <Link to="/evidence/new">New Evidence</Link>
          <Link to="/statements/new">New Statement</Link>
        </nav>
      </header>
      <main>{children}</main>
    </div>
  );
}

function HomePage() {
  return (
    <Layout>
      <h2>Home</h2>
      <p>Detail pages and cross-links are now available for sources, documents, and evidence.</p>
    </Layout>
  );
}

function HealthPage() {
  const healthQuery = useQuery({
    queryKey: ["health"],
    queryFn: getHealth,
  });

  const dbHealthQuery = useQuery({
    queryKey: ["health-db"],
    queryFn: getDatabaseHealth,
  });

  return (
    <Layout>
      <h2>Health</h2>

      <section style={{ marginBottom: "24px" }}>
        <h3>API</h3>
        {healthQuery.isLoading && <p>Loading API health...</p>}
        {healthQuery.isError && (
          <p style={{ color: "crimson" }}>
            API health failed: {(healthQuery.error as Error).message}
          </p>
        )}
        {healthQuery.isSuccess && (
          <pre>{JSON.stringify(healthQuery.data, null, 2)}</pre>
        )}
      </section>

      <section>
        <h3>Database</h3>
        {dbHealthQuery.isLoading && <p>Loading DB health...</p>}
        {dbHealthQuery.isError && (
          <p style={{ color: "crimson" }}>
            DB health failed: {(dbHealthQuery.error as Error).message}
          </p>
        )}
        {dbHealthQuery.isSuccess && (
          <pre>{JSON.stringify(dbHealthQuery.data, null, 2)}</pre>
        )}
      </section>
    </Layout>
  );
}

const router = createBrowserRouter([
  { path: "/", element: <HomePage /> },
  { path: "/dashboard", element: <DashboardPage /> },
  { path: "/health", element: <HealthPage /> },
  { path: "/persons", element: <PersonsPage /> },
  { path: "/persons/new", element: <CreatePersonPage /> },
  { path: "/persons/:id", element: <PersonDetailPage /> },
  { path: "/cases", element: <CasesPage /> },
  { path: "/cases/new", element: <CreateCasePage /> },
  { path: "/cases/:id", element: <CaseDetailPage /> },
  { path: "/reviews", element: <ReviewsPage /> },
  { path: "/ingestion", element: <IngestionWorkspacePage /> },
  { path: "/sources", element: <SourcesPage /> },
  { path: "/sources/new", element: <CreateSourcePage /> },
  { path: "/sources/:id", element: <SourceDetailPage /> },
  { path: "/documents", element: <DocumentsPage /> },
  { path: "/documents/new", element: <CreateDocumentPage /> },
  { path: "/documents/:id", element: <DocumentDetailPage /> },
  { path: "/evidence", element: <EvidencePage /> },
  { path: "/evidence/new", element: <CreateEvidencePage /> },
  { path: "/evidence/:id", element: <EvidenceDetailPage /> },
  { path: "/statements/new", element: <CreateStatementPage /> },
]);

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </React.StrictMode>
);
'@

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$diagDir = Join-Path $RootDir "_diagnostics\phase-6-5-$timestamp"
Ensure-Directory $diagDir
$reportPath = Join-Path $diagDir "phase-6-5-report.txt"
$stdoutPath = Join-Path $diagDir "api-stdout.log"
$stderrPath = Join-Path $diagDir "api-stderr.log"

Set-Content -Path $reportPath -Value "Phase 6.5 detail pages verification`r`nGenerated: $(Get-Date -Format s)`r`nRoot: $RootDir" -Encoding UTF8

Write-Host ""
Write-Host "Applying Phase 6.5 - detail pages + cross-links..." -ForegroundColor Cyan

Write-Utf8File -Path $sourcesContractsPath -Content $sourcesContracts
Write-Utf8File -Path $documentsContractsPath -Content $documentsContracts
Write-Utf8File -Path $evidenceContractsPath -Content $evidenceContracts

Write-Utf8File -Path $sourcesControllerPath -Content $sourcesController
Write-Utf8File -Path $documentsControllerPath -Content $documentsController
Write-Utf8File -Path $evidenceControllerPath -Content $evidenceController

Write-Utf8File -Path $sourcesApiPath -Content $sourcesApi
Write-Utf8File -Path $documentsApiPath -Content $documentsApi
Write-Utf8File -Path $evidenceApiPath -Content $evidenceApi

Write-Utf8File -Path $useSourceDetailHookPath -Content $useSourceDetailHook
Write-Utf8File -Path $useDocumentDetailHookPath -Content $useDocumentDetailHook
Write-Utf8File -Path $useEvidenceDetailHookPath -Content $useEvidenceDetailHook

Write-Utf8File -Path $sourceDetailPagePath -Content $sourceDetailPage
Write-Utf8File -Path $documentDetailPagePath -Content $documentDetailPage
Write-Utf8File -Path $evidenceDetailPagePath -Content $evidenceDetailPage

Write-Utf8File -Path $sourcesPagePath -Content $sourcesPage
Write-Utf8File -Path $documentsPagePath -Content $documentsPage
Write-Utf8File -Path $evidencePagePath -Content $evidencePage
Write-Utf8File -Path $mainTsxPath -Content $mainTsx

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
Write-Host "Starting API for phase 6.5 smoke test..." -ForegroundColor Cyan

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
        name = "Detail Smoke Source"
        type = "Article"
        reference = "https://example.com/detail-test"
        createdBy = "phase-6-5"
    } | ConvertTo-Json -Depth 5

    $sourceResponse = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/sources" -ContentType "application/json" -Body $sourceBody
    Add-Section -OutputPath $reportPath -Title "POST /api/v1/sources" -Content (($sourceResponse | ConvertTo-Json -Depth 10))

    $documentBody = @{
        sourceId = $sourceResponse.id
        title = "Detail Smoke Document"
        content = "Document content for detail test."
        externalReference = "detail-doc-001"
        createdBy = "phase-6-5"
    } | ConvertTo-Json -Depth 5

    $documentResponse = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/documents" -ContentType "application/json" -Body $documentBody
    Add-Section -OutputPath $reportPath -Title "POST /api/v1/documents" -Content (($documentResponse | ConvertTo-Json -Depth 10))

    $evidenceBody = @{
        documentId = $documentResponse.id
        quote = "Evidence snippet for detail test."
        startOffset = 0
        endOffset = 32
        context = "Evidence context for detail test."
        createdBy = "phase-6-5"
    } | ConvertTo-Json -Depth 5

    $evidenceResponse = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/evidence" -ContentType "application/json" -Body $evidenceBody
    Add-Section -OutputPath $reportPath -Title "POST /api/v1/evidence" -Content (($evidenceResponse | ConvertTo-Json -Depth 10))

    $sourceDetail = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/sources/$($sourceResponse.id)"
    Add-Section -OutputPath $reportPath -Title "GET /api/v1/sources/{id}" -Content (($sourceDetail | ConvertTo-Json -Depth 10))

    $documentDetail = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/documents/$($documentResponse.id)"
    Add-Section -OutputPath $reportPath -Title "GET /api/v1/documents/{id}" -Content (($documentDetail | ConvertTo-Json -Depth 10))

    $evidenceDetail = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/evidence/$($evidenceResponse.id)"
    Add-Section -OutputPath $reportPath -Title "GET /api/v1/evidence/{id}" -Content (($evidenceDetail | ConvertTo-Json -Depth 10))

    Write-Host ""
    Write-Host "Phase 6.5 completed successfully." -ForegroundColor Green
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