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

Write-Host "Checkpointing current code with git..."
Git-Checkpoint -Message ("checkpoint before phase 6.18 - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying Phase 6.18 - Large Scope - Governance and Publication Console Pack..."

$webRoot = Join-Path $RootDir "apps\web\veritas-atlas-web\src"

$governanceConsolePath = Join-Path $webRoot "pages\GovernanceConsolePage.tsx"
$publicationReadinessBoardPath = Join-Path $webRoot "pages\PublicationReadinessBoardPage.tsx"
$decisionLogPath = Join-Path $webRoot "pages\DecisionLogPage.tsx"
$reviewMetricsPanelPath = Join-Path $webRoot "components\ReviewMetricsPanel.tsx"
$publicationStatusPanelPath = Join-Path $webRoot "components\PublicationStatusPanel.tsx"
$mainPath = Join-Path $webRoot "main.tsx"

Write-Utf8File -Path $reviewMetricsPanelPath -Content @'
export function ReviewMetricsPanel() {
  return (
    <div style={{ border: "1px solid #ddd", borderRadius: 12, padding: 16 }}>
      <h3 style={{ marginTop: 0 }}>Review Metrics</h3>
      <ul style={{ marginBottom: 0 }}>
        <li>Items in review: placeholder</li>
        <li>Items escalated: placeholder</li>
        <li>Average review turnaround: placeholder</li>
      </ul>
    </div>
  );
}
'@

Write-Utf8File -Path $publicationStatusPanelPath -Content @'
export function PublicationStatusPanel() {
  return (
    <div style={{ border: "1px solid #ddd", borderRadius: 12, padding: 16 }}>
      <h3 style={{ marginTop: 0 }}>Publication Status</h3>
      <ul style={{ marginBottom: 0 }}>
        <li>Ready to publish: placeholder</li>
        <li>Blocked by review: placeholder</li>
        <li>Pending contradiction notes: placeholder</li>
      </ul>
    </div>
  );
}
'@

Write-Utf8File -Path $governanceConsolePath -Content @'
import { Link } from "react-router-dom";
import { ReviewMetricsPanel } from "../components/ReviewMetricsPanel";
import { PublicationStatusPanel } from "../components/PublicationStatusPanel";

export function GovernanceConsolePage() {
  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Governance Console</h1>
      <p>Operational surface for review governance, escalation tracking, and publication readiness.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/review-queue">Review Queue</Link>
        <Link to="/review-workspace">Review Workspace</Link>
        <Link to="/publication-desk">Publication Desk</Link>
        <Link to="/publication-readiness">Publication Readiness</Link>
        <Link to="/decision-log">Decision Log</Link>
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16, marginBottom: 24 }}>
        <ReviewMetricsPanel />
        <PublicationStatusPanel />
      </div>

      <section style={panelStyle}>
        <h2 style={{ marginTop: 0 }}>Governance priorities</h2>
        <ul style={{ marginBottom: 0 }}>
          <li>Keep publication decisions traceable</li>
          <li>Escalate unresolved contradiction candidates</li>
          <li>Route review-ready items into publication desk</li>
        </ul>
      </section>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
'@

Write-Utf8File -Path $publicationReadinessBoardPath -Content @'
import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";

export function PublicationReadinessBoardPage() {
  const claimsQuery = useClaims();
  const items = (claimsQuery.data?.items ?? []).slice(0, 10);

  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Publication Readiness Board</h1>
      <p>Board view for items approaching publication after review and contradiction preparation.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/governance-console">Governance Console</Link>
        <Link to="/publication-desk">Publication Desk</Link>
        <Link to="/review-queue">Review Queue</Link>
      </div>

      <section style={gridStyle}>
        <BoardColumn title="Needs Review" items={items.slice(0, 3)} />
        <BoardColumn title="Needs Contradiction Check" items={items.slice(3, 6)} />
        <BoardColumn title="Ready to Publish" items={items.slice(6, 10)} />
      </section>
    </div>
  );
}

function BoardColumn({
  title,
  items,
}: {
  title: string;
  items: Array<{ id: string; topic: string; type: string; status: string }>;
}) {
  return (
    <div style={columnStyle}>
      <h3 style={{ marginTop: 0 }}>{title}</h3>
      {items.length === 0 && <p>No items.</p>}
      {items.length > 0 && (
        <ul style={{ marginBottom: 0 }}>
          {items.map((item) => (
            <li key={item.id}>
              <Link to={`/claims/${item.id}`}>{item.topic}</Link> - {item.type} - {item.status}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(260px, 1fr))",
  gap: 16,
};

const columnStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  minHeight: 220,
};
'@

Write-Utf8File -Path $decisionLogPath -Content @'
import { Link } from "react-router-dom";

export function DecisionLogPage() {
  const decisions = [
    "Review queue policy placeholder",
    "Publication routing placeholder",
    "Escalation rule placeholder",
    "Contradiction severity placeholder",
  ];

  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Decision Log</h1>
      <p>Central place for governance and publication decisions.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/governance-console">Governance Console</Link>
        <Link to="/publication-readiness">Publication Readiness</Link>
        <Link to="/publication-desk">Publication Desk</Link>
      </div>

      <section style={panelStyle}>
        <h2 style={{ marginTop: 0 }}>Current decisions</h2>
        <ul style={{ marginBottom: 0 }}>
          {decisions.map((item, index) => (
            <li key={index}>{item}</li>
          ))}
        </ul>
      </section>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
'@

$mainContent = Get-Content $mainPath -Raw

$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { DashboardPage } from "./pages/DashboardPage";' -ImportLine 'import { GovernanceConsolePage } from "./pages/GovernanceConsolePage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { GovernanceConsolePage } from "./pages/GovernanceConsolePage";' -ImportLine 'import { PublicationReadinessBoardPage } from "./pages/PublicationReadinessBoardPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { PublicationReadinessBoardPage } from "./pages/PublicationReadinessBoardPage";' -ImportLine 'import { DecisionLogPage } from "./pages/DecisionLogPage";'

$mainContent = Ensure-NavBlock -Content $mainContent -Anchor '<Link to="/reviews">Reviews</Link>' -NavBlock '<Link to="/governance-console">Governance Console</Link>
          <Link to="/publication-readiness">Publication Readiness</Link>
          <Link to="/decision-log">Decision Log</Link>' -PresencePattern 'to="/governance-console"'

$mainContent = Ensure-RouteBlock -Content $mainContent -AnchorRoute '{ path: "/dashboard", element: <DashboardPage /> },' -RouteBlock '{ path: "/governance-console", element: <GovernanceConsolePage /> },
  { path: "/publication-readiness", element: <PublicationReadinessBoardPage /> },
  { path: "/decision-log", element: <DecisionLogPage /> },' -PresencePattern 'path: "/governance-console"'

Write-Utf8File -Path $mainPath -Content $mainContent

Write-Host "Building backend..."
Build-Backend -RootDir $RootDir

Write-Host "Building frontend..."
Build-Frontend -RootDir $RootDir

Write-Host "Phase 6.18 applied successfully."
