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
Git-Checkpoint -Message ("checkpoint before phase 6.23 - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying Phase 6.23 - Truth Review Studio and Contradiction Resolution UI pack..."

$webRoot = Join-Path $RootDir "apps\web\veritas-atlas-web\src"

$studioPagePath = Join-Path $webRoot "pages\TruthReviewStudioPage.tsx"
$resolutionWorkspacePath = Join-Path $webRoot "pages\ContradictionResolutionWorkspacePage.tsx"
$scoreboardPath = Join-Path $webRoot "pages\CaseScoreboardPage.tsx"
$truthMatrixPath = Join-Path $webRoot "components\TruthMatrixPanel.tsx"
$resolutionActionsPath = Join-Path $webRoot "components\ResolutionActionsPanel.tsx"
$mainPath = Join-Path $webRoot "main.tsx"

Write-Utf8File -Path $truthMatrixPath -Content @'
type TruthMatrixItem = {
  id: string;
  label: string;
  status: string;
};

export function TruthMatrixPanel({
  claims,
  contradictions,
}: {
  claims: TruthMatrixItem[];
  contradictions: TruthMatrixItem[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Truth Matrix</h3>

      <div style={gridStyle}>
        <div>
          <h4>Claims</h4>
          {claims.length === 0 && <p>No claims.</p>}
          {claims.length > 0 && (
            <ul style={{ marginBottom: 0 }}>
              {claims.map((item) => (
                <li key={item.id}>{item.label} - {item.status}</li>
              ))}
            </ul>
          )}
        </div>

        <div>
          <h4>Contradictions</h4>
          {contradictions.length === 0 && <p>No contradictions.</p>}
          {contradictions.length > 0 && (
            <ul style={{ marginBottom: 0 }}>
              {contradictions.map((item) => (
                <li key={item.id}>{item.label} - {item.status}</li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "1fr 1fr",
  gap: 16,
};
'@

Write-Utf8File -Path $resolutionActionsPath -Content @'
import { Link } from "react-router-dom";

export function ResolutionActionsPanel({
  contradictionId,
  caseId,
}: {
  contradictionId?: string;
  caseId?: string;
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Resolution Actions</h3>
      <div style={{ display: "flex", gap: 12, flexWrap: "wrap" }}>
        {contradictionId && <Link to={`/contradictions/${contradictionId}`}>Open Contradiction</Link>}
        {caseId && <Link to={`/case-explorer/${caseId}`}>Open Case Workbench</Link>}
        <Link to="/review-queue">Send to Review Queue</Link>
        <Link to="/publication-desk">Open Publication Desk</Link>
        <Link to="/resolution-board">Open Resolution Board</Link>
      </div>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
'@

Write-Utf8File -Path $studioPagePath -Content @'
import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";
import { useContradictions } from "../hooks/useContradictions";
import { TruthMatrixPanel } from "../components/TruthMatrixPanel";

export function TruthReviewStudioPage() {
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  const claimItems = (claimsQuery.data?.items ?? []).slice(0, 8).map((item) => ({
    id: item.id,
    label: item.topic,
    status: item.status,
  }));

  const contradictionItems = (contradictionsQuery.data?.items ?? []).slice(0, 8).map((item) => ({
    id: item.id,
    label: item.topic,
    status: item.status,
  }));

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Truth Review Studio</h1>
        <p style={{ color: "#555" }}>
          Unified review surface for claims, contradictions, and resolution readiness.
        </p>

        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/case-explorer">Case Explorer</Link>
          <Link to="/truth-review-studio">Truth Review Studio</Link>
          <Link to="/resolution-board">Resolution Board</Link>
          <Link to="/publication-desk">Publication Desk</Link>
        </nav>
      </header>

      <TruthMatrixPanel claims={claimItems} contradictions={contradictionItems} />

      <div style={{ ...panelStyle, marginTop: 20 }}>
        <h3 style={{ marginTop: 0 }}>Studio Summary</h3>
        <ul style={{ marginBottom: 0 }}>
          <li>Total visible claims: {claimsQuery.data?.items.length ?? 0}</li>
          <li>Total visible contradictions: {contradictionsQuery.data?.items.length ?? 0}</li>
          <li>Ready for deeper resolution workflow: yes</li>
        </ul>
      </div>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
'@

Write-Utf8File -Path $resolutionWorkspacePath -Content @'
import { Link, useParams } from "react-router-dom";
import { useContradictionDetail } from "../hooks/useContradictionDetail";
import { ResolutionActionsPanel } from "../components/ResolutionActionsPanel";

export function ContradictionResolutionWorkspacePage() {
  const { id } = useParams();
  const query = useContradictionDetail(id);

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Loading contradiction workspace...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24, color: "crimson" }}>Failed to load contradiction: {(query.error as Error).message}</div>;
  }

  if (!query.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Contradiction not found.</div>;
  }

  const item = query.data;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <nav style={{ display: "flex", gap: 16, marginBottom: 20, flexWrap: "wrap" }}>
        <Link to="/truth-review-studio">Back to Truth Review Studio</Link>
        <Link to={`/contradictions/${item.id}`}>Contradiction Detail</Link>
        <Link to={`/case-explorer/${item.caseId}`}>Case Workbench</Link>
      </nav>

      <h1 style={{ marginTop: 0 }}>Contradiction Resolution Workspace</h1>

      <div style={panelStyle}>
        <Row label="Contradiction Id" value={item.id} />
        <Row label="Topic" value={item.topic} />
        <Row label="Summary" value={item.summary} />
        <Row label="Severity" value={item.severity} />
        <Row label="Status" value={item.status} />
        <Row label="Case Id" value={item.caseId ?? "N/A"} />
      </div>

      <div style={{ ...panelStyle, marginTop: 20 }}>
        <h3 style={{ marginTop: 0 }}>Resolution Notes</h3>
        <ul style={{ marginBottom: 0 }}>
          <li>Confirm opposing claims belong to the same decision space</li>
          <li>Assess whether contradiction is direct, temporal, or contextual</li>
          <li>Route unresolved contradiction to review queue</li>
          <li>Route resolved contradiction toward publication decision</li>
        </ul>
      </div>

      <div style={{ marginTop: 20 }}>
        <ResolutionActionsPanel contradictionId={item.id} caseId={item.caseId ?? undefined} />
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

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
'@

Write-Utf8File -Path $scoreboardPath -Content @'
import { Link } from "react-router-dom";
import { useCaseExplorer } from "../hooks/useCaseExplorer";
import { useClaims } from "../hooks/useClaims";
import { useContradictions } from "../hooks/useContradictions";

export function CaseScoreboardPage() {
  const casesQuery = useCaseExplorer();
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  const cases = casesQuery.data?.items ?? [];
  const claims = claimsQuery.data?.items ?? [];
  const contradictions = contradictionsQuery.data?.items ?? [];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Case Scoreboard</h1>
        <p style={{ color: "#555" }}>
          Roll-up scoreboard across cases, claims, and contradictions.
        </p>

        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/case-explorer">Case Explorer</Link>
          <Link to="/truth-review-studio">Truth Review Studio</Link>
          <Link to="/case-scoreboard">Case Scoreboard</Link>
        </nav>
      </header>

      <div style={gridStyle}>
        <MetricCard title="Cases" value={cases.length} />
        <MetricCard title="Claims" value={claims.length} />
        <MetricCard title="Contradictions" value={contradictions.length} />
        <MetricCard title="Open Resolution Work" value={contradictions.filter(x => x.status !== "Resolved").length} />
      </div>

      <div style={{ ...panelStyle, marginTop: 20 }}>
        <h3 style={{ marginTop: 0 }}>Top Case Signals</h3>
        {cases.length === 0 && <p>No cases available.</p>}
        {cases.length > 0 && (
          <ul style={{ marginBottom: 0 }}>
            {cases.slice(0, 10).map((item) => (
              <li key={item.id}>
                <Link to={`/case-explorer/${item.id}`}>{item.id}</Link> - {item.status}
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}

function MetricCard({ title, value }: { title: string; value: number }) {
  return (
    <div style={metricStyle}>
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

const metricStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  display: "grid",
  gap: 8,
};

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
'@

$mainContent = Get-Content $mainPath -Raw

$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { DashboardPage } from "./pages/DashboardPage";' -ImportLine 'import { TruthReviewStudioPage } from "./pages/TruthReviewStudioPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { TruthReviewStudioPage } from "./pages/TruthReviewStudioPage";' -ImportLine 'import { ContradictionResolutionWorkspacePage } from "./pages/ContradictionResolutionWorkspacePage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { ContradictionResolutionWorkspacePage } from "./pages/ContradictionResolutionWorkspacePage";' -ImportLine 'import { CaseScoreboardPage } from "./pages/CaseScoreboardPage";'

$mainContent = Ensure-NavBlock -Content $mainContent -Anchor '<Link to="/case-explorer">Case Explorer</Link>' -NavBlock '<Link to="/truth-review-studio">Truth Review Studio</Link>
          <Link to="/case-scoreboard">Case Scoreboard</Link>' -PresencePattern 'to="/truth-review-studio"'

$mainContent = Ensure-RouteBlock -Content $mainContent -AnchorRoute '{ path: "/case-explorer", element: <CaseExplorerPage /> },' -RouteBlock '{ path: "/truth-review-studio", element: <TruthReviewStudioPage /> },
  { path: "/contradiction-resolution/:id", element: <ContradictionResolutionWorkspacePage /> },
  { path: "/case-scoreboard", element: <CaseScoreboardPage /> },' -PresencePattern 'path: "/truth-review-studio"'

Write-Utf8File -Path $mainPath -Content $mainContent

Write-Host "Building backend..."
Build-Backend -RootDir $RootDir

Write-Host "Building frontend..."
Build-Frontend -RootDir $RootDir

Write-Host "Phase 6.23 completed successfully." -ForegroundColor Green
