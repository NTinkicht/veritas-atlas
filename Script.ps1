param(
    [string]$RootDir = "C:\Projects\veritas-atlas"
)

$ErrorActionPreference = "Stop"

function Ensure-Directory {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Ensure-Directory received an empty path."
    }

    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Force -Path $Path | Out-Null
    }
}

function Write-Utf8File {
    param(
        [string]$Path,
        [string]$Content
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Write-Utf8File received an empty path."
    }

    $parent = Split-Path -Parent $Path
    if ([string]::IsNullOrWhiteSpace($parent)) {
        throw "Could not resolve parent directory for path: $Path"
    }

    Ensure-Directory $parent
    Set-Content -Path $Path -Value $Content -Encoding UTF8
    Write-Host "Wrote: $Path" -ForegroundColor Green
}

$solutionPath = Join-Path $RootDir "VeritasAtlas.slnx"
$apiRoot = Join-Path $RootDir "apps\api"
$webRoot = Join-Path $RootDir "apps\web\veritas-atlas-web"

$claimsContractsPath = Join-Path $apiRoot "VeritasAtlas.Api\Contracts\ClaimsContracts.cs"
$claimsControllerPath = Join-Path $apiRoot "VeritasAtlas.Api\Controllers\ClaimsController.cs"
$claimSliceServicePath = Join-Path $apiRoot "VeritasAtlas.Infrastructure\Services\ClaimSliceService.cs"
$diPath = Join-Path $apiRoot "VeritasAtlas.Infrastructure\Extensions\ServiceCollectionExtensions.cs"

$claimsApiPath = Join-Path $webRoot "src\api\claims.ts"
$useClaimsPath = Join-Path $webRoot "src\hooks\useClaims.ts"
$useClaimDetailPath = Join-Path $webRoot "src\hooks\useClaimDetail.ts"
$useCreateClaimPath = Join-Path $webRoot "src\hooks\useCreateClaim.ts"
$claimsPagePath = Join-Path $webRoot "src\pages\ClaimsPage.tsx"
$claimDetailPagePath = Join-Path $webRoot "src\pages\ClaimDetailPage.tsx"
$claimsWorkspacePagePath = Join-Path $webRoot "src\pages\ClaimsWorkspacePage.tsx"
$mainTsxPath = Join-Path $webRoot "src\main.tsx"

Push-Location $RootDir

Write-Host ""
Write-Host "Checkpointing current code with git..." -ForegroundColor Cyan

git add -A
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "git add failed."
}

$commitMessage = "checkpoint before phase 6.12 - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
git commit -m $commitMessage 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "No new commit created. Continuing with phase 6.12." -ForegroundColor Yellow
}
else {
    Write-Host "Created git commit: $commitMessage" -ForegroundColor Green
}

Pop-Location

$claimsContractsContent = @'
namespace VeritasAtlas.Api.Contracts.Claims;

public sealed record CreateClaimRequest(
    Guid StatementId,
    string Topic,
    string NormalizedText,
    string? Type,
    Guid? PersonId,
    Guid? CaseId,
    bool IsMaterial);

public sealed record CreateClaimResponse(
    Guid Id,
    Guid StatementId,
    Guid? PersonId,
    Guid? CaseId,
    string Type,
    string Status,
    string Topic,
    string NormalizedText,
    bool IsMaterial,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetClaimsItemResponse(
    Guid Id,
    Guid StatementId,
    Guid? PersonId,
    Guid? CaseId,
    string Type,
    string Status,
    string Topic,
    string NormalizedText,
    bool IsMaterial,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetClaimResponse(
    Guid Id,
    Guid StatementId,
    Guid? PersonId,
    Guid? CaseId,
    string Type,
    string Status,
    string Topic,
    string NormalizedText,
    bool IsMaterial,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetClaimsResponse(
    IReadOnlyCollection<GetClaimsItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
'@

$claimSliceServiceContent = @'
using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class ClaimSliceService
{
    private readonly VeritasAtlasDbContext _dbContext;

    public ClaimSliceService(VeritasAtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Claim> CreateClaimAsync(
        Guid statementId,
        string topic,
        string normalizedText,
        string? type,
        Guid? personId,
        Guid? caseId,
        bool isMaterial,
        CancellationToken cancellationToken = default)
    {
        var statement = await _dbContext.Statements
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == statementId, cancellationToken);

        if (statement is null)
        {
            throw new InvalidOperationException($"Statement '{statementId}' was not found.");
        }

        ClaimType parsedType = ClaimType.Factual;
        if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<ClaimType>(type, true, out var explicitType))
        {
            parsedType = explicitType;
        }

        var entity = new Claim
        {
            StatementId = statementId,
            PersonId = personId,
            CaseId = caseId,
            Type = parsedType,
            Status = ClaimStatus.Draft,
            Topic = topic.Trim(),
            NormalizedText = normalizedText.Trim(),
            IsMaterial = isMaterial
        };

        _dbContext.Claims.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    public async Task<(int Total, List<Claim> Items)> GetClaimsAsync(
        int page,
        int pageSize,
        Guid? statementId = null,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : pageSize;

        IQueryable<Claim> query = _dbContext.Claims.AsNoTracking();

        if (statementId.HasValue)
        {
            query = query.Where(x => x.StatementId == statementId.Value);
        }

        query = query.OrderByDescending(x => x.CreatedAtUtc);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (total, items);
    }

    public async Task<Claim?> GetClaimByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Claims
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }
}
'@

$claimsControllerContent = @'
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Claims;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/claims")]
public sealed class ClaimsController : ControllerBase
{
    private readonly ClaimSliceService _claimSliceService;

    public ClaimsController(ClaimSliceService claimSliceService)
    {
        _claimSliceService = claimSliceService;
    }

    [HttpPost]
    public async Task<ActionResult<CreateClaimResponse>> CreateClaim(
        [FromBody] CreateClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _claimSliceService.CreateClaimAsync(
            request.StatementId,
            request.Topic,
            request.NormalizedText,
            request.Type,
            request.PersonId,
            request.CaseId,
            request.IsMaterial,
            cancellationToken);

        var response = new CreateClaimResponse(
            entity.Id,
            entity.StatementId,
            entity.PersonId,
            entity.CaseId,
            entity.Type.ToString(),
            entity.Status.ToString(),
            entity.Topic,
            entity.NormalizedText,
            entity.IsMaterial,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);

        return Ok(response);
    }

    [HttpGet]
    public async Task<ActionResult<GetClaimsResponse>> GetClaims(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? statementId = null,
        CancellationToken cancellationToken = default)
    {
        var (total, items) = await _claimSliceService.GetClaimsAsync(page, pageSize, statementId, cancellationToken);

        var mapped = items.Select(entity => new GetClaimsItemResponse(
            entity.Id,
            entity.StatementId,
            entity.PersonId,
            entity.CaseId,
            entity.Type.ToString(),
            entity.Status.ToString(),
            entity.Topic,
            entity.NormalizedText,
            entity.IsMaterial,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc
        )).ToArray();

        return Ok(new GetClaimsResponse(
            mapped,
            page,
            pageSize,
            total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize)
        ));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetClaimResponse>> GetClaim(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _claimSliceService.GetClaimByIdAsync(id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        return Ok(new GetClaimResponse(
            entity.Id,
            entity.StatementId,
            entity.PersonId,
            entity.CaseId,
            entity.Type.ToString(),
            entity.Status.ToString(),
            entity.Topic,
            entity.NormalizedText,
            entity.IsMaterial,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc
        ));
    }
}
'@

$diOriginal = Get-Content $diPath -Raw
if ($diOriginal -notmatch 'AddScoped<ClaimSliceService>\(\);') {
    $diUpdated = $diOriginal -replace 'services\.AddScoped<IClaimService,\s*ClaimService>\(\);', "services.AddScoped<IClaimService, ClaimService>();`r`n        services.AddScoped<ClaimSliceService>();"
} else {
    $diUpdated = $diOriginal
}

$claimsApiContent = @'
import { apiGet, apiPost } from "./client";

export type ClaimItem = {
  id: string;
  statementId: string;
  personId: string | null;
  caseId: string | null;
  type: string;
  status: string;
  topic: string;
  normalizedText: string;
  isMaterial: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type ClaimDetail = ClaimItem;

export type ClaimsResponse = {
  items: ClaimItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export type CreateClaimRequest = {
  statementId: string;
  topic: string;
  normalizedText: string;
  type?: string;
  personId?: string;
  caseId?: string;
  isMaterial: boolean;
};

export async function getClaims(statementId?: string): Promise<ClaimsResponse> {
  const params = new URLSearchParams({
    page: "1",
    pageSize: "50",
  });

  if (statementId) {
    params.set("statementId", statementId);
  }

  return apiGet<ClaimsResponse>(`/api/v1/claims?${params.toString()}`);
}

export async function getClaimById(id: string): Promise<ClaimDetail> {
  return apiGet<ClaimDetail>(`/api/v1/claims/${id}`);
}

export async function createClaim(request: CreateClaimRequest) {
  return apiPost<ClaimItem>("/api/v1/claims", request);
}
'@

$useClaimsContent = @'
import { useQuery } from "@tanstack/react-query";
import { getClaims } from "../api/claims";

export function useClaims(statementId?: string) {
  return useQuery({
    queryKey: ["claims", statementId ?? ""],
    queryFn: () => getClaims(statementId),
  });
}
'@

$useClaimDetailContent = @'
import { useQuery } from "@tanstack/react-query";
import { getClaimById } from "../api/claims";

export function useClaimDetail(id?: string) {
  return useQuery({
    queryKey: ["claim-detail", id],
    queryFn: () => getClaimById(id!),
    enabled: !!id,
  });
}
'@

$useCreateClaimContent = @'
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { createClaim, type CreateClaimRequest } from "../api/claims";

export function useCreateClaim() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: CreateClaimRequest) => createClaim(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["claims"] });
    },
  });
}
'@

$claimsPageContent = @'
import { Link, useSearchParams } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";

export function ClaimsPage() {
  const [params] = useSearchParams();
  const statementId = params.get("statementId") ?? undefined;
  const query = useClaims(statementId);

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Claims</h1>
        <p style={{ color: "#555" }}>Live claim catalog from Veritas Atlas API</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/statements">Statements</Link>
          <Link to="/claims">Claims</Link>
          <Link to="/claims/workspace">Claims Workspace</Link>
        </nav>
      </header>

      {statementId && (
        <div style={hintCardStyle}>
          <strong>Statement scope:</strong> {statementId}
        </div>
      )}

      {query.isLoading && <p>Loading claims...</p>}

      {query.isError && (
        <p style={{ color: "crimson" }}>
          Failed to load claims: {(query.error as Error).message}
        </p>
      )}

      {query.isSuccess && (
        <>
          <p>Showing {query.data.items.length} of {query.data.totalCount} claims</p>

          {query.data.items.length === 0 ? (
            <div style={emptyStateStyle}>
              <p style={{ margin: 0 }}>No claims found.</p>
            </div>
          ) : (
            <div style={{ overflowX: "auto" }}>
              <table style={tableStyle}>
                <thead>
                  <tr>
                    <th style={thStyle}>Topic</th>
                    <th style={thStyle}>Type</th>
                    <th style={thStyle}>Status</th>
                    <th style={thStyle}>Material</th>
                    <th style={thStyle}>Statement</th>
                    <th style={thStyle}>Created</th>
                  </tr>
                </thead>
                <tbody>
                  {query.data.items.map((item) => (
                    <tr key={item.id}>
                      <td style={tdStyle}><Link to={`/claims/${item.id}`}>{item.topic}</Link></td>
                      <td style={tdStyle}>{item.type}</td>
                      <td style={tdStyle}>{item.status}</td>
                      <td style={tdStyle}>{item.isMaterial ? "Yes" : "No"}</td>
                      <td style={tdStyle}><Link to={`/statements/${item.statementId}`}>{item.statementId}</Link></td>
                      <td style={tdStyle}>{new Date(item.createdAtUtc).toLocaleString()}</td>
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

const tableStyle: React.CSSProperties = {
  width: "100%",
  borderCollapse: "collapse",
  marginTop: "16px",
};

const thStyle: React.CSSProperties = {
  textAlign: "left",
  borderBottom: "1px solid #ccc",
  padding: "10px",
};

const tdStyle: React.CSSProperties = {
  borderBottom: "1px solid #eee",
  padding: "10px",
  verticalAlign: "top",
};

const emptyStateStyle: React.CSSProperties = {
  border: "1px solid #eee",
  borderRadius: "12px",
  padding: "20px",
  color: "#666",
};

const hintCardStyle: React.CSSProperties = {
  marginBottom: "16px",
  padding: "12px 14px",
  border: "1px solid #d9e6ff",
  borderRadius: "12px",
  background: "#f8fbff",
};
'@

$claimDetailPageContent = @'
import { Link, useParams } from "react-router-dom";
import { useClaimDetail } from "../hooks/useClaimDetail";

export function ClaimDetailPage() {
  const { id } = useParams();
  const query = useClaimDetail(id);

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Loading claim...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", color: "crimson" }}>Failed to load claim: {(query.error as Error).message}</div>;
  }

  if (!query.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Claim not found.</div>;
  }

  const item = query.data;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <nav style={{ display: "flex", gap: "16px", marginBottom: "20px", flexWrap: "wrap" }}>
        <Link to="/claims">Back to Claims</Link>
        <Link to={`/statements/${item.statementId}`}>Statement</Link>
      </nav>

      <h1 style={{ marginTop: 0 }}>Claim</h1>

      <div style={cardStyle}>
        <Row label="Id" value={item.id} />
        <Row label="Statement Id" value={item.statementId} />
        <Row label="Topic" value={item.topic} />
        <Row label="Normalized Text" value={item.normalizedText} />
        <Row label="Type" value={item.type} />
        <Row label="Status" value={item.status} />
        <Row label="Material" value={item.isMaterial ? "Yes" : "No"} />
        <Row label="Person Id" value={item.personId ?? "N/A"} />
        <Row label="Case Id" value={item.caseId ?? "N/A"} />
        <Row label="Created" value={new Date(item.createdAtUtc).toLocaleString()} />
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

const cardStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: "12px",
  padding: "16px",
  marginBottom: "16px",
};
'@

$claimsWorkspacePageContent = @'
import { Link, useSearchParams } from "react-router-dom";
import { useState } from "react";
import { useStatementDetail } from "../hooks/useStatementDetail";
import { useClaims } from "../hooks/useClaims";
import { useCreateClaim } from "../hooks/useCreateClaim";

export function ClaimsWorkspacePage() {
  const [params] = useSearchParams();
  const statementId = params.get("statementId") ?? "";

  const statementQuery = useStatementDetail(statementId || undefined);
  const claimsQuery = useClaims(statementId || undefined);
  const createClaimMutation = useCreateClaim();

  const [topic, setTopic] = useState("");
  const [normalizedText, setNormalizedText] = useState("");
  const [type, setType] = useState("Factual");
  const [isMaterial, setIsMaterial] = useState(true);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();

    await createClaimMutation.mutateAsync({
      statementId,
      topic,
      normalizedText,
      type,
      isMaterial,
    });

    setTopic("");
    setNormalizedText("");
  };

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", maxWidth: "980px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Claims Workspace</h1>
        <p style={{ color: "#555" }}>Create and inspect claims for a statement.</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/statements">Statements</Link>
          <Link to="/claims">Claims</Link>
          {statementId && <Link to={`/statements/${statementId}`}>Statement</Link>}
        </nav>
      </header>

      <div style={cardStyle}>
        <Row label="Statement Id" value={statementId || "N/A"} />
      </div>

      {statementId.length > 0 && statementQuery.isSuccess && statementQuery.data && (
        <div style={cardStyle}>
          <h3 style={{ marginTop: 0 }}>Statement Context</h3>
          <Row label="Text" value={statementQuery.data.text} />
          <Row label="Topic" value={statementQuery.data.topic ?? "N/A"} />
          <Row label="Polarity" value={statementQuery.data.polarity} />
          <Row label="Status" value={statementQuery.data.status} />
        </div>
      )}

      {statementId.length > 0 && (
        <div style={cardStyle}>
          <h3 style={{ marginTop: 0 }}>Create Claim</h3>
          <form onSubmit={submit} style={{ display: "grid", gap: "16px" }}>
            <label style={labelStyle}>
              <span>Topic</span>
              <input value={topic} onChange={(e) => setTopic(e.target.value)} style={inputStyle} required />
            </label>

            <label style={labelStyle}>
              <span>Normalized Text</span>
              <textarea value={normalizedText} onChange={(e) => setNormalizedText(e.target.value)} style={{ ...inputStyle, minHeight: "120px", resize: "vertical" }} required />
            </label>

            <label style={labelStyle}>
              <span>Type</span>
              <select value={type} onChange={(e) => setType(e.target.value)} style={inputStyle}>
                <option value="Factual">Factual</option>
                <option value="Temporal">Temporal</option>
                <option value="Quantitative">Quantitative</option>
                <option value="Categorical">Categorical</option>
                <option value="Attribution">Attribution</option>
              </select>
            </label>

            <label style={{ display: "flex", gap: "10px", alignItems: "center" }}>
              <input type="checkbox" checked={isMaterial} onChange={(e) => setIsMaterial(e.target.checked)} />
              <span>Material claim</span>
            </label>

            <div style={{ display: "flex", gap: "12px", flexWrap: "wrap" }}>
              <button type="submit" style={buttonStyle} disabled={createClaimMutation.isPending}>
                {createClaimMutation.isPending ? "Creating..." : "Create Claim"}
              </button>
              <Link to={statementId ? `/claims?statementId=${statementId}` : "/claims"} style={linkButtonStyle}>Open Claims</Link>
            </div>
          </form>
        </div>
      )}

      <div style={cardStyle}>
        <h3 style={{ marginTop: 0 }}>Claims for this Statement</h3>

        {claimsQuery.isLoading && <p>Loading claims...</p>}
        {claimsQuery.isError && <p style={{ color: "crimson" }}>Failed to load claims: {(claimsQuery.error as Error).message}</p>}
        {claimsQuery.isSuccess && claimsQuery.data.items.length === 0 && <p>No claims yet.</p>}
        {claimsQuery.isSuccess && claimsQuery.data.items.length > 0 && (
          <ul>
            {claimsQuery.data.items.map((claim) => (
              <li key={claim.id}>
                <Link to={`/claims/${claim.id}`}>{claim.topic}</Link> - {claim.type} - {claim.status}
              </li>
            ))}
          </ul>
        )}
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

const cardStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: "12px",
  padding: "16px",
  marginBottom: "16px",
};

const labelStyle: React.CSSProperties = {
  display: "grid",
  gap: "8px",
};

const inputStyle: React.CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  padding: "10px 12px",
  borderRadius: "8px",
  border: "1px solid #ccc",
  font: "inherit",
};

const buttonStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  background: "#1976d2",
  color: "white",
  cursor: "pointer",
};

const linkButtonStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
  display: "inline-flex",
  alignItems: "center",
};
'@

$mainTsxContent = @'
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
import { StatementsPage } from "./pages/StatementsPage";
import { StatementDetailPage } from "./pages/StatementDetailPage";
import { ClaimsPage } from "./pages/ClaimsPage";
import { ClaimDetailPage } from "./pages/ClaimDetailPage";
import { ClaimsWorkspacePage } from "./pages/ClaimsWorkspacePage";
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
          <Link to="/statements">Statements</Link>
          <Link to="/claims">Claims</Link>
          <Link to="/claims/workspace">Claims Workspace</Link>
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
      <p>Claim vertical slice is now available.</p>
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
  { path: "/statements", element: <StatementsPage /> },
  { path: "/statements/new", element: <CreateStatementPage /> },
  { path: "/statements/:id", element: <StatementDetailPage /> },
  { path: "/claims", element: <ClaimsPage /> },
  { path: "/claims/:id", element: <ClaimDetailPage /> },
  { path: "/claims/workspace", element: <ClaimsWorkspacePage /> },
]);

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </React.StrictMode>
);
'@

Write-Host ""
Write-Host "Applying Phase 6.12..." -ForegroundColor Cyan

Write-Utf8File -Path $claimsContractsPath -Content $claimsContractsContent
Write-Utf8File -Path $claimSliceServicePath -Content $claimSliceServiceContent
Write-Utf8File -Path $claimsControllerPath -Content $claimsControllerContent
Write-Utf8File -Path $diPath -Content $diUpdated

Write-Utf8File -Path $claimsApiPath -Content $claimsApiContent
Write-Utf8File -Path $useClaimsPath -Content $useClaimsContent
Write-Utf8File -Path $useClaimDetailPath -Content $useClaimDetailContent
Write-Utf8File -Path $useCreateClaimPath -Content $useCreateClaimContent
Write-Utf8File -Path $claimsPagePath -Content $claimsPageContent
Write-Utf8File -Path $claimDetailPagePath -Content $claimDetailPageContent
Write-Utf8File -Path $claimsWorkspacePagePath -Content $claimsWorkspacePageContent
Write-Utf8File -Path $mainTsxPath -Content $mainTsxContent

Push-Location $RootDir
Write-Host ""
Write-Host "Building backend..." -ForegroundColor Cyan
dotnet build $solutionPath
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "dotnet build failed."
}
Pop-Location

Push-Location $webRoot
Write-Host ""
Write-Host "Building frontend..." -ForegroundColor Cyan
npm run build
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "npm run build failed."
}
Pop-Location

Write-Host ""
Write-Host "Phase 6.12 completed successfully." -ForegroundColor Green