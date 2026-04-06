param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir
)

$ErrorActionPreference = "Stop"

function Ensure-Directory {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Ensure-Directory received an empty path."
    }

    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Write-Utf8File {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Write-Utf8File received an empty path."
    }

    $dir = Split-Path -Parent $Path
    Ensure-Directory -Path $dir

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
    Write-Host "Wrote: $Path"
}

function Git-Checkpoint {
    param([Parameter(Mandatory = $true)][string]$Message)

    Push-Location $RootDir
    try {
        if (Test-Path ".git") {
            git add -A | Out-Null
            git commit -m $Message 2>$null | Out-Null
            if ($LASTEXITCODE -eq 0) {
                Write-Host "Created git commit: $Message"
            }
            else {
                Write-Host "No new commit created. Continuing."
            }
        }
        else {
            Write-Host "No git repo detected, skipping checkpoint."
        }
    }
    finally {
        Pop-Location
    }
}

function Build-Backend {
    param([Parameter(Mandatory = $true)][string]$RootDir)

    $solutionPath = Join-Path $RootDir "VeritasAtlas.slnx"
    $apiDir = Join-Path $RootDir "apps\api"

    if (Test-Path $solutionPath) {
        Push-Location $RootDir
        try {
            dotnet build $solutionPath
            if ($LASTEXITCODE -ne 0) { throw "Backend build failed." }
        }
        finally { Pop-Location }
        return
    }

    if (Test-Path $apiDir) {
        Push-Location $apiDir
        try {
            dotnet build
            if ($LASTEXITCODE -ne 0) { throw "Backend build failed." }
        }
        finally { Pop-Location }
        return
    }

    throw "Could not find solution or api directory."
}

function Build-Frontend {
    param([Parameter(Mandatory = $true)][string]$RootDir)

    $frontendDir = Join-Path $RootDir "apps\web\veritas-atlas-web"
    if (-not (Test-Path $frontendDir)) {
        throw "Frontend directory not found: $frontendDir"
    }

    Push-Location $frontendDir
    try {
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "Frontend build failed." }
    }
    finally { Pop-Location }
}

function Ensure-ImportLine {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$Anchor,
        [Parameter(Mandatory = $true)][string]$ImportLine
    )

    if ($Content -match [regex]::Escape($ImportLine)) {
        return $Content
    }

    return $Content -replace [regex]::Escape($Anchor), ($Anchor + [Environment]::NewLine + $ImportLine)
}

function Ensure-NavBlock {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$Anchor,
        [Parameter(Mandatory = $true)][string]$NavBlock,
        [Parameter(Mandatory = $true)][string]$PresencePattern
    )

    if ($Content -match $PresencePattern) {
        return $Content
    }

    return $Content -replace [regex]::Escape($Anchor), ($Anchor + [Environment]::NewLine + $NavBlock)
}

function Ensure-RouteBlock {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$AnchorRoute,
        [Parameter(Mandatory = $true)][string]$RouteBlock,
        [Parameter(Mandatory = $true)][string]$PresencePattern
    )

    if ($Content -match $PresencePattern) {
        return $Content
    }

    return $Content -replace [regex]::Escape($AnchorRoute), ($AnchorRoute + [Environment]::NewLine + $RouteBlock)
}

Write-Host "Checkpointing current code with git..."
Git-Checkpoint -Message ("checkpoint before phase 6.21 - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying Phase 6.21 - Large Scope - Contradiction Vertical Slice and Resolution Operations..."

$apiRoot = Join-Path $RootDir "apps\api"
$webRoot = Join-Path $RootDir "apps\web\veritas-atlas-web\src"

$contractsPath = Join-Path $apiRoot "VeritasAtlas.Api\Contracts\ContradictionsContracts.cs"
$controllerPath = Join-Path $apiRoot "VeritasAtlas.Api\Controllers\ContradictionsController.cs"
$servicePath = Join-Path $apiRoot "VeritasAtlas.Infrastructure\Services\ContradictionSliceService.cs"
$diPath = Join-Path $apiRoot "VeritasAtlas.Infrastructure\Extensions\ServiceCollectionExtensions.cs"

$apiTsPath = Join-Path $webRoot "api\contradictions.ts"
$useListPath = Join-Path $webRoot "hooks\useContradictions.ts"
$useDetailPath = Join-Path $webRoot "hooks\useContradictionDetail.ts"
$useCreatePath = Join-Path $webRoot "hooks\useCreateContradiction.ts"
$listPagePath = Join-Path $webRoot "pages\ContradictionsPage.tsx"
$detailPagePath = Join-Path $webRoot "pages\ContradictionDetailPage.tsx"
$resolutionBoardPath = Join-Path $webRoot "pages\ResolutionBoardPage.tsx"
$signalPanelPath = Join-Path $webRoot "components\ContradictionSignalPanel.tsx"
$mainPath = Join-Path $webRoot "main.tsx"

Write-Utf8File -Path $contractsPath -Content @'
namespace VeritasAtlas.Api.Contracts.Contradictions;

public sealed record CreateContradictionRequest(
    Guid PrimaryClaimId,
    Guid SecondaryClaimId,
    string Topic,
    string Summary,
    string? ContradictionType,
    string? Severity,
    Guid? CaseId);

public sealed record CreateContradictionResponse(
    Guid Id,
    Guid PrimaryClaimId,
    Guid SecondaryClaimId,
    Guid? CaseId,
    string Topic,
    string Summary,
    string ContradictionType,
    string Severity,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetContradictionsItemResponse(
    Guid Id,
    Guid PrimaryClaimId,
    Guid SecondaryClaimId,
    Guid? CaseId,
    string Topic,
    string Summary,
    string ContradictionType,
    string Severity,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetContradictionResponse(
    Guid Id,
    Guid PrimaryClaimId,
    Guid SecondaryClaimId,
    Guid? CaseId,
    string Topic,
    string Summary,
    string ContradictionType,
    string Severity,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetContradictionsResponse(
    IReadOnlyCollection<GetContradictionsItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
'@

Write-Utf8File -Path $servicePath -Content @'
using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Domain.Entities;
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

        var entity = new Contradiction
        {
            ClaimAId = primaryClaimId,
            ClaimBId = secondaryClaimId,
            CaseId = caseId,
            Type = contradictionType?.Trim() ?? "Direct",
            Severity = severity?.Trim() ?? "Medium",
            Status = "Draft",
            Summary = string.IsNullOrWhiteSpace(summary) ? topic.Trim() : summary.Trim()
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
            query = query.Where(x => x.ClaimAId == claimId.Value || x.ClaimBId == claimId.Value);
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
            entity.ClaimAId,
            entity.ClaimBId,
            entity.CaseId,
            request.Topic,
            entity.Summary,
            entity.Type,
            entity.Severity,
            entity.Status,
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
            entity.ClaimAId,
            entity.ClaimBId,
            entity.CaseId,
            entity.Summary,
            entity.Summary,
            entity.Type,
            entity.Severity,
            entity.Status,
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
            entity.ClaimAId,
            entity.ClaimBId,
            entity.CaseId,
            entity.Summary,
            entity.Summary,
            entity.Type,
            entity.Severity,
            entity.Status,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc
        ));
    }
}
'@

$diContent = Get-Content $diPath -Raw
if ($diContent -notmatch 'ContradictionSliceService') {
    $diContent = $diContent -replace 'services\.AddScoped<IClaimService,\s*ClaimService>\(\);', 'services.AddScoped<IClaimService, ClaimService>();
        services.AddScoped<ContradictionSliceService>();'
}
Write-Utf8File -Path $diPath -Content $diContent

Write-Utf8File -Path $apiTsPath -Content @'
import { apiGet } from "./client";

export type ContradictionItem = {
  id: string;
  primaryClaimId: string;
  secondaryClaimId: string;
  caseId: string | null;
  topic: string;
  summary: string;
  contradictionType: string;
  severity: string;
  status: string;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type ContradictionDetail = ContradictionItem;

export type ContradictionsResponse = {
  items: ContradictionItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export type CreateContradictionRequest = {
  primaryClaimId: string;
  secondaryClaimId: string;
  topic: string;
  summary: string;
  contradictionType?: string;
  severity?: string;
  caseId?: string;
};

export async function getContradictions(claimId?: string, caseId?: string): Promise<ContradictionsResponse> {
  const params = new URLSearchParams({
    page: "1",
    pageSize: "50",
  });

  if (claimId) {
    params.set("claimId", claimId);
  }

  if (caseId) {
    params.set("caseId", caseId);
  }

  return apiGet<ContradictionsResponse>(`/api/v1/contradictions?${params.toString()}`);
}

export async function getContradictionById(id: string): Promise<ContradictionDetail> {
  return apiGet<ContradictionDetail>(`/api/v1/contradictions/${id}`);
}

export async function createContradiction(request: CreateContradictionRequest): Promise<ContradictionItem> {
  const response = await fetch("/api/v1/contradictions", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<ContradictionItem>;
}
'@

Write-Utf8File -Path $useListPath -Content @'
import { useQuery } from "@tanstack/react-query";
import { getContradictions } from "../api/contradictions";

export function useContradictions(claimId?: string, caseId?: string) {
  return useQuery({
    queryKey: ["contradictions", claimId ?? "", caseId ?? ""],
    queryFn: () => getContradictions(claimId, caseId),
  });
}
'@

Write-Utf8File -Path $useDetailPath -Content @'
import { useQuery } from "@tanstack/react-query";
import { getContradictionById } from "../api/contradictions";

export function useContradictionDetail(id?: string) {
  return useQuery({
    queryKey: ["contradiction-detail", id],
    queryFn: () => getContradictionById(id!),
    enabled: !!id,
  });
}
'@

Write-Utf8File -Path $useCreatePath -Content @'
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { createContradiction, type CreateContradictionRequest } from "../api/contradictions";

export function useCreateContradiction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: CreateContradictionRequest) => createContradiction(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["contradictions"] });
    },
  });
}
'@

Write-Utf8File -Path $signalPanelPath -Content @'
export function ContradictionSignalPanel() {
  return (
    <div style={{ border: "1px solid #ddd", borderRadius: 12, padding: 16 }}>
      <h3 style={{ marginTop: 0 }}>Contradiction Signals</h3>
      <ul style={{ marginBottom: 0 }}>
        <li>Direct opposition signal: placeholder</li>
        <li>Temporal drift signal: placeholder</li>
        <li>Attribution conflict signal: placeholder</li>
      </ul>
    </div>
  );
}
'@

Write-Utf8File -Path $listPagePath -Content @'
import { Link, useSearchParams } from "react-router-dom";
import { useContradictions } from "../hooks/useContradictions";

export function ContradictionsPage() {
  const [params] = useSearchParams();
  const claimId = params.get("claimId") ?? undefined;
  const caseId = params.get("caseId") ?? undefined;
  const query = useContradictions(claimId, caseId);

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Contradictions</h1>
        <p style={{ color: "#555" }}>Live contradiction catalog from Veritas Atlas API</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/claims">Claims</Link>
          <Link to="/contradictions">Contradictions</Link>
          <Link to="/resolution-board">Resolution Board</Link>
          <Link to="/contradictions/workspace">Contradictions Workspace</Link>
        </nav>
      </header>

      {claimId && (
        <div style={hintCardStyle}>
          <strong>Claim scope:</strong> {claimId}
        </div>
      )}

      {caseId && (
        <div style={hintCardStyle}>
          <strong>Case scope:</strong> {caseId}
        </div>
      )}

      {query.isLoading && <p>Loading contradictions...</p>}

      {query.isError && (
        <p style={{ color: "crimson" }}>
          Failed to load contradictions: {(query.error as Error).message}
        </p>
      )}

      {query.isSuccess && (
        <>
          <p>Showing {query.data.items.length} of {query.data.totalCount} contradictions</p>

          {query.data.items.length === 0 ? (
            <div style={emptyStateStyle}>
              <p style={{ margin: 0 }}>No contradictions found.</p>
            </div>
          ) : (
            <div style={{ overflowX: "auto" }}>
              <table style={tableStyle}>
                <thead>
                  <tr>
                    <th style={thStyle}>Topic</th>
                    <th style={thStyle}>Type</th>
                    <th style={thStyle}>Severity</th>
                    <th style={thStyle}>Status</th>
                    <th style={thStyle}>Claims</th>
                  </tr>
                </thead>
                <tbody>
                  {query.data.items.map((item) => (
                    <tr key={item.id}>
                      <td style={tdStyle}><Link to={`/contradictions/${item.id}`}>{item.topic}</Link></td>
                      <td style={tdStyle}>{item.contradictionType}</td>
                      <td style={tdStyle}>{item.severity}</td>
                      <td style={tdStyle}>{item.status}</td>
                      <td style={tdStyle}>
                        <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
                          <Link to={`/claims/${item.primaryClaimId}`}>Primary</Link>
                          <Link to={`/claims/${item.secondaryClaimId}`}>Secondary</Link>
                        </div>
                      </td>
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

Write-Utf8File -Path $detailPagePath -Content @'
import { Link, useParams } from "react-router-dom";
import { useContradictionDetail } from "../hooks/useContradictionDetail";
import { ContradictionSignalPanel } from "../components/ContradictionSignalPanel";

export function ContradictionDetailPage() {
  const { id } = useParams();
  const query = useContradictionDetail(id);

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Loading contradiction...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", color: "crimson" }}>Failed to load contradiction: {(query.error as Error).message}</div>;
  }

  if (!query.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Contradiction not found.</div>;
  }

  const item = query.data;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <nav style={{ display: "flex", gap: "16px", marginBottom: "20px", flexWrap: "wrap" }}>
        <Link to="/contradictions">Back to Contradictions</Link>
        <Link to={`/claims/${item.primaryClaimId}`}>Primary Claim</Link>
        <Link to={`/claims/${item.secondaryClaimId}`}>Secondary Claim</Link>
      </nav>

      <h1 style={{ marginTop: 0 }}>Contradiction</h1>

      <div style={cardStyle}>
        <Row label="Id" value={item.id} />
        <Row label="Topic" value={item.topic} />
        <Row label="Summary" value={item.summary} />
        <Row label="Type" value={item.contradictionType} />
        <Row label="Severity" value={item.severity} />
        <Row label="Status" value={item.status} />
        <Row label="Primary Claim" value={item.primaryClaimId} />
        <Row label="Secondary Claim" value={item.secondaryClaimId} />
        <Row label="Case Id" value={item.caseId ?? "N/A"} />
      </div>

      <ContradictionSignalPanel />
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

Write-Utf8File -Path $resolutionBoardPath -Content @'
import { Link } from "react-router-dom";
import { useContradictions } from "../hooks/useContradictions";

export function ResolutionBoardPage() {
  const query = useContradictions();

  const items = query.data?.items ?? [];
  const lanes = {
    Draft: items.filter(x => x.status === "Draft"),
    Active: items.filter(x => x.status === "Active"),
    Resolved: items.filter(x => x.status === "Resolved")
  };

  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Resolution Board</h1>
      <p>Operational board for contradiction handling and resolution flow.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/contradictions">Contradictions</Link>
        <Link to="/contradictions/workspace">Contradictions Workspace</Link>
        <Link to="/review-queue">Review Queue</Link>
      </div>

      {query.isLoading && <p>Loading contradiction board...</p>}
      {query.isError && <p style={{ color: "crimson" }}>Failed to load contradiction board.</p>}

      {query.isSuccess && (
        <div style={gridStyle}>
          {Object.entries(lanes).map(([lane, laneItems]) => (
            <div key={lane} style={laneStyle}>
              <h3 style={{ marginTop: 0 }}>{lane}</h3>
              {laneItems.length === 0 && <p>No items.</p>}
              {laneItems.length > 0 && (
                <ul style={{ marginBottom: 0 }}>
                  {laneItems.map((item) => (
                    <li key={item.id}>
                      <Link to={`/contradictions/${item.id}`}>{item.topic}</Link> - {item.severity}
                    </li>
                  ))}
                </ul>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
  gap: 16,
};

const laneStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  minHeight: 220,
};
'@

$mainContent = Get-Content $mainPath -Raw

$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { DashboardPage } from "./pages/DashboardPage";' -ImportLine 'import { ContradictionsPage } from "./pages/ContradictionsPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { ContradictionsPage } from "./pages/ContradictionsPage";' -ImportLine 'import { ContradictionDetailPage } from "./pages/ContradictionDetailPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { ContradictionDetailPage } from "./pages/ContradictionDetailPage";' -ImportLine 'import { ResolutionBoardPage } from "./pages/ResolutionBoardPage";'

$mainContent = Ensure-NavBlock -Content $mainContent -Anchor '<Link to="/claims">Claims</Link>' -NavBlock '<Link to="/contradictions">Contradictions</Link>
          <Link to="/resolution-board">Resolution Board</Link>' -PresencePattern 'to="/contradictions"'

$mainContent = Ensure-RouteBlock -Content $mainContent -AnchorRoute '{ path: "/claims", element: <ClaimsPage /> },' -RouteBlock '{ path: "/contradictions", element: <ContradictionsPage /> },
  { path: "/contradictions/:id", element: <ContradictionDetailPage /> },
  { path: "/resolution-board", element: <ResolutionBoardPage /> },' -PresencePattern 'path: "/contradictions"'

Write-Utf8File -Path $mainPath -Content $mainContent

Write-Host "Building backend..."
Build-Backend -RootDir $RootDir

Write-Host "Building frontend..."
Build-Frontend -RootDir $RootDir

Write-Host "Phase 6.21 applied successfully."
