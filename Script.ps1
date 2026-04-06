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
Git-Checkpoint -Message ("checkpoint before phase 6.22 - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying Phase 6.22 - UI one pass - Case Explorer and Contradiction Workbench..."

$webRoot = Join-Path $RootDir "apps\web\veritas-atlas-web\src"

$apiPath = Join-Path $webRoot "api\caseExplorer.ts"
$hookPath = Join-Path $webRoot "hooks\useCaseExplorer.ts"
$detailHookPath = Join-Path $webRoot "hooks\useCaseWorkbench.ts"
$explorerPagePath = Join-Path $webRoot "pages\CaseExplorerPage.tsx"
$workbenchPagePath = Join-Path $webRoot "pages\CaseWorkbenchPage.tsx"
$summaryPanelPath = Join-Path $webRoot "components\CaseExplorerSummaryPanel.tsx"
$queuePanelPath = Join-Path $webRoot "components\ContradictionQueuePanel.tsx"
$mainPath = Join-Path $webRoot "main.tsx"

Write-Utf8File -Path $apiPath -Content @'
export type CaseExplorerItem = {
  id: string;
  status: string;
  createdAtUtc: string;
  createdBy?: string | null;
};

export type CaseExplorerResponse = {
  items: CaseExplorerItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export async function getCaseExplorer(page = 1, pageSize = 50): Promise<CaseExplorerResponse> {
  const response = await fetch(`/api/v1/cases?page=${page}&pageSize=${pageSize}`);

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<CaseExplorerResponse>;
}
'@

Write-Utf8File -Path $hookPath -Content @'
import { useQuery } from "@tanstack/react-query";
import { getCaseExplorer } from "../api/caseExplorer";

export function useCaseExplorer(page = 1, pageSize = 50) {
  return useQuery({
    queryKey: ["case-explorer", page, pageSize],
    queryFn: () => getCaseExplorer(page, pageSize),
  });
}
'@

Write-Utf8File -Path $detailHookPath -Content @'
import { useMemo } from "react";
import { useCaseDetail } from "./useCaseDetail";
import { useClaims } from "./useClaims";
import { useContradictions } from "./useContradictions";

export function useCaseWorkbench(caseId?: string) {
  const caseQuery = useCaseDetail(caseId);
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions(undefined, caseId);

  const linkedClaims = useMemo(() => {
    const items = claimsQuery.data?.items ?? [];
    if (!caseId) return [];
    return items.filter((x) => x.caseId === caseId);
  }, [claimsQuery.data, caseId]);

  const contradictionItems = contradictionsQuery.data?.items ?? [];

  return {
    caseQuery,
    claimsQuery,
    contradictionsQuery,
    linkedClaims,
    contradictionItems,
  };
}
'@

Write-Utf8File -Path $summaryPanelPath -Content @'
export function CaseExplorerSummaryPanel({
  totalCases,
  openCases,
  totalClaims,
  totalContradictions,
}: {
  totalCases: number;
  openCases: number;
  totalClaims: number;
  totalContradictions: number;
}) {
  return (
    <div style={gridStyle}>
      <Card title="Cases" value={totalCases} />
      <Card title="Open Cases" value={openCases} />
      <Card title="Claims" value={totalClaims} />
      <Card title="Contradictions" value={totalContradictions} />
    </div>
  );
}

function Card({ title, value }: { title: string; value: number }) {
  return (
    <div style={cardStyle}>
      <span style={{ color: "#666" }}>{title}</span>
      <strong style={{ fontSize: 28 }}>{value}</strong>
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))",
  gap: 16,
};

const cardStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  display: "grid",
  gap: 8,
};
'@

Write-Utf8File -Path $queuePanelPath -Content @'
import { Link } from "react-router-dom";
import type { ContradictionItem } from "../api/contradictions";

export function ContradictionQueuePanel({ items }: { items: ContradictionItem[] }) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Contradiction Queue</h3>
      {items.length === 0 && <p>No contradiction items for this case.</p>}
      {items.length > 0 && (
        <ul style={{ marginBottom: 0 }}>
          {items.map((item) => (
            <li key={item.id}>
              <Link to={`/contradictions/${item.id}`}>{item.topic}</Link> - {item.severity} - {item.status}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
'@

Write-Utf8File -Path $explorerPagePath -Content @'
import { Link } from "react-router-dom";
import { useMemo } from "react";
import { useCaseExplorer } from "../hooks/useCaseExplorer";
import { useClaims } from "../hooks/useClaims";
import { useContradictions } from "../hooks/useContradictions";
import { CaseExplorerSummaryPanel } from "../components/CaseExplorerSummaryPanel";

export function CaseExplorerPage() {
  const casesQuery = useCaseExplorer();
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  const totalCases = casesQuery.data?.items.length ?? 0;
  const totalClaims = claimsQuery.data?.items.length ?? 0;
  const totalContradictions = contradictionsQuery.data?.items.length ?? 0;

  const openCases = useMemo(() => {
    const items = casesQuery.data?.items ?? [];
    return items.filter((x) => x.status !== "Closed" && x.status !== "Resolved").length;
  }, [casesQuery.data]);

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Case Explorer</h1>
        <p style={{ color: "#555" }}>
          Main working surface for cases, claims, and contradictions.
        </p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/cases">Cases</Link>
          <Link to="/case-explorer">Case Explorer</Link>
          <Link to="/contradictions">Contradictions</Link>
          <Link to="/resolution-board">Resolution Board</Link>
        </nav>
      </header>

      <CaseExplorerSummaryPanel
        totalCases={totalCases}
        openCases={openCases}
        totalClaims={totalClaims}
        totalContradictions={totalContradictions}
      />

      <section style={{ marginTop: 24 }}>
        {casesQuery.isLoading && <p>Loading case explorer...</p>}
        {casesQuery.isError && <p style={{ color: "crimson" }}>Failed to load cases: {(casesQuery.error as Error).message}</p>}

        {casesQuery.isSuccess && (
          <div style={{ overflowX: "auto" }}>
            <table style={tableStyle}>
              <thead>
                <tr>
                  <th style={thStyle}>Case</th>
                  <th style={thStyle}>Status</th>
                  <th style={thStyle}>Created</th>
                  <th style={thStyle}>Workbench</th>
                </tr>
              </thead>
              <tbody>
                {casesQuery.data.items.map((item) => (
                  <tr key={item.id}>
                    <td style={tdStyle}>
                      <Link to={`/cases/${item.id}`}>{item.id}</Link>
                    </td>
                    <td style={tdStyle}>{item.status}</td>
                    <td style={tdStyle}>{new Date(item.createdAtUtc).toLocaleString()}</td>
                    <td style={tdStyle}>
                      <Link to={`/case-explorer/${item.id}`}>Open Workbench</Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}

const tableStyle: React.CSSProperties = {
  width: "100%",
  borderCollapse: "collapse",
  marginTop: 16,
};

const thStyle: React.CSSProperties = {
  textAlign: "left",
  borderBottom: "1px solid #ccc",
  padding: 10,
};

const tdStyle: React.CSSProperties = {
  borderBottom: "1px solid #eee",
  padding: 10,
  verticalAlign: "top",
};
'@

Write-Utf8File -Path $workbenchPagePath -Content @'
import { Link, useParams } from "react-router-dom";
import { useCaseWorkbench } from "../hooks/useCaseWorkbench";
import { ContradictionQueuePanel } from "../components/ContradictionQueuePanel";

export function CaseWorkbenchPage() {
  const { id } = useParams();
  const { caseQuery, linkedClaims, contradictionItems, claimsQuery, contradictionsQuery } = useCaseWorkbench(id);

  if (caseQuery.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Loading case workbench...</div>;
  }

  if (caseQuery.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24, color: "crimson" }}>Failed to load case: {(caseQuery.error as Error).message}</div>;
  }

  if (!caseQuery.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Case not found.</div>;
  }

  const item = caseQuery.data;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <nav style={{ display: "flex", gap: 16, marginBottom: 20, flexWrap: "wrap" }}>
        <Link to="/case-explorer">Back to Case Explorer</Link>
        <Link to={`/cases/${item.id}`}>Case Detail</Link>
        <Link to={`/contradictions?caseId=${item.id}`}>Contradictions for Case</Link>
      </nav>

      <h1 style={{ marginTop: 0 }}>Case Workbench</h1>

      <div style={panelStyle}>
        <Row label="Case Id" value={item.id} />
        <Row label="Status" value={item.status} />
        <Row label="Created" value={new Date(item.createdAtUtc).toLocaleString()} />
      </div>

      <div style={gridStyle}>
        <div style={panelStyle}>
          <h3 style={{ marginTop: 0 }}>Linked Claims</h3>
          {claimsQuery.isLoading && <p>Loading claims...</p>}
          {!claimsQuery.isLoading && linkedClaims.length === 0 && <p>No linked claims.</p>}
          {linkedClaims.length > 0 && (
            <ul style={{ marginBottom: 0 }}>
              {linkedClaims.map((claim) => (
                <li key={claim.id}>
                  <Link to={`/claims/${claim.id}`}>{claim.topic}</Link> - {claim.type} - {claim.status}
                </li>
              ))}
            </ul>
          )}
        </div>

        <ContradictionQueuePanel items={contradictionItems} />
      </div>

      <div style={{ ...panelStyle, marginTop: 20 }}>
        <h3 style={{ marginTop: 0 }}>Operational Actions</h3>
        <div style={{ display: "flex", gap: 12, flexWrap: "wrap" }}>
          <Link to="/claims">Open Claims Catalog</Link>
          <Link to={`/contradictions?caseId=${item.id}`}>Open Contradictions Catalog</Link>
          <Link to="/resolution-board">Open Resolution Board</Link>
        </div>
        {contradictionsQuery.isLoading && <p style={{ marginTop: 12 }}>Refreshing contradictions...</p>}
      </div>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "180px 1fr", gap: 12, padding: "6px 0" }}>
      <strong>{label}</strong>
      <span>{value}</span>
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "1fr 1fr",
  gap: 16,
};

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
'@

$mainContent = Get-Content $mainPath -Raw

$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { DashboardPage } from "./pages/DashboardPage";' -ImportLine 'import { CaseExplorerPage } from "./pages/CaseExplorerPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { CaseExplorerPage } from "./pages/CaseExplorerPage";' -ImportLine 'import { CaseWorkbenchPage } from "./pages/CaseWorkbenchPage";'

$mainContent = Ensure-NavBlock -Content $mainContent -Anchor '<Link to="/cases">Cases</Link>' -NavBlock '<Link to="/case-explorer">Case Explorer</Link>' -PresencePattern 'to="/case-explorer"'

$mainContent = Ensure-RouteBlock -Content $mainContent -AnchorRoute '{ path: "/cases", element: <CasesPage /> },' -RouteBlock '{ path: "/case-explorer", element: <CaseExplorerPage /> },
  { path: "/case-explorer/:id", element: <CaseWorkbenchPage /> },' -PresencePattern 'path: "/case-explorer"'

Write-Utf8File -Path $mainPath -Content $mainContent

Write-Host "Building backend..."
Build-Backend -RootDir $RootDir

Write-Host "Building frontend..."
Build-Frontend -RootDir $RootDir

Write-Host "Phase 6.22 completed successfully." -ForegroundColor Green
