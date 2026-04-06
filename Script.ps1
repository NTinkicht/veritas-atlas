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
Git-Checkpoint -Message ("checkpoint before phase 6.20 - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying Phase 6.20 - Large Scope - Analytics and AI Operations Control Pack..."

$webRoot = Join-Path $RootDir "apps\web\veritas-atlas-web\src"

$analyticsCenterPath = Join-Path $webRoot "pages\AnalyticsCenterPage.tsx"
$agentRunsBoardPath = Join-Path $webRoot "pages\AgentRunsBoardPage.tsx"
$caseFlowMapPath = Join-Path $webRoot "pages\CaseFlowMapPage.tsx"
$qualityRadarPath = Join-Path $webRoot "pages\QualityRadarPage.tsx"
$analyticsSummaryPanelPath = Join-Path $webRoot "components\AnalyticsSummaryPanel.tsx"
$agentUtilizationPanelPath = Join-Path $webRoot "components\AgentUtilizationPanel.tsx"
$mainPath = Join-Path $webRoot "main.tsx"

Write-Utf8File -Path $analyticsSummaryPanelPath -Content @'
export function AnalyticsSummaryPanel() {
  return (
    <div style={{ border: "1px solid #ddd", borderRadius: 12, padding: 16 }}>
      <h3 style={{ marginTop: 0 }}>Analytics Summary</h3>
      <ul style={{ marginBottom: 0 }}>
        <li>Operational throughput: placeholder</li>
        <li>Review load trend: placeholder</li>
        <li>Publication readiness trend: placeholder</li>
      </ul>
    </div>
  );
}
'@

Write-Utf8File -Path $agentUtilizationPanelPath -Content @'
export function AgentUtilizationPanel() {
  return (
    <div style={{ border: "1px solid #ddd", borderRadius: 12, padding: 16 }}>
      <h3 style={{ marginTop: 0 }}>Agent Utilization</h3>
      <ul style={{ marginBottom: 0 }}>
        <li>Extraction workload: placeholder</li>
        <li>Contradiction workload: placeholder</li>
        <li>Confidence workload: placeholder</li>
      </ul>
    </div>
  );
}
'@

Write-Utf8File -Path $analyticsCenterPath -Content @'
import { Link } from "react-router-dom";
import { AnalyticsSummaryPanel } from "../components/AnalyticsSummaryPanel";
import { AgentUtilizationPanel } from "../components/AgentUtilizationPanel";

export function AnalyticsCenterPage() {
  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Analytics Center</h1>
      <p>Central analytics surface for operational throughput, review pressure, and AI workload visibility.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/operations-intelligence">Operations Intelligence</Link>
        <Link to="/executive-overview">Executive Overview</Link>
        <Link to="/agent-runs-board">Agent Runs Board</Link>
        <Link to="/quality-radar">Quality Radar</Link>
        <Link to="/case-flow-map">Case Flow Map</Link>
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16, marginBottom: 24 }}>
        <AnalyticsSummaryPanel />
        <AgentUtilizationPanel />
      </div>

      <section style={panelStyle}>
        <h2 style={{ marginTop: 0 }}>Analytics priorities</h2>
        <ul style={{ marginBottom: 0 }}>
          <li>Surface throughput bottlenecks early</li>
          <li>Track review versus publication readiness balance</li>
          <li>Monitor AI workload concentration and idle capacity</li>
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

Write-Utf8File -Path $agentRunsBoardPath -Content @'
import { Link } from "react-router-dom";

export function AgentRunsBoardPage() {
  const runs = [
    { name: "Extraction Agent", status: "Idle", detail: "Awaiting new evidence" },
    { name: "Contradiction Agent", status: "Running", detail: "Comparing active claims" },
    { name: "Confidence Agent", status: "Idle", detail: "No pending recalculations" },
    { name: "Review Support Agent", status: "Placeholder", detail: "Future workflow expansion" }
  ];

  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Agent Runs Board</h1>
      <p>Board view for current and upcoming AI operations across the system.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/analytics-center">Analytics Center</Link>
        <Link to="/operations-intelligence">Operations Intelligence</Link>
      </div>

      <div style={gridStyle}>
        {runs.map((run) => (
          <div key={run.name} style={cardStyle}>
            <h3 style={{ marginTop: 0 }}>{run.name}</h3>
            <p><strong>Status:</strong> {run.status}</p>
            <p style={{ marginBottom: 0 }}>{run.detail}</p>
          </div>
        ))}
      </div>
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
  gap: 16,
};

const cardStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
'@

Write-Utf8File -Path $caseFlowMapPath -Content @'
import { Link } from "react-router-dom";

export function CaseFlowMapPage() {
  const steps = [
    "Source registration",
    "Document intake",
    "Evidence extraction",
    "Statement creation",
    "Claim formulation",
    "Contradiction preparation",
    "Review routing",
    "Publication readiness",
  ];

  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Case Flow Map</h1>
      <p>Visual sequence of how information moves through the Veritas Atlas operational system.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/analytics-center">Analytics Center</Link>
        <Link to="/operations">Operations Hub</Link>
        <Link to="/review-queue">Review Queue</Link>
      </div>

      <section style={panelStyle}>
        <ol style={{ marginBottom: 0, paddingLeft: 20 }}>
          {steps.map((step) => (
            <li key={step} style={{ marginBottom: 8 }}>{step}</li>
          ))}
        </ol>
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

Write-Utf8File -Path $qualityRadarPath -Content @'
import { Link } from "react-router-dom";

export function QualityRadarPage() {
  const dimensions = [
    "Evidence quality",
    "Statement clarity",
    "Claim quality",
    "Contradiction readiness",
    "Review traceability",
    "Publication readiness"
  ];

  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Quality Radar</h1>
      <p>Operational quality dimensions for reviewing system maturity and readiness.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/analytics-center">Analytics Center</Link>
        <Link to="/governance-console">Governance Console</Link>
        <Link to="/publication-readiness">Publication Readiness</Link>
      </div>

      <section style={panelStyle}>
        <ul style={{ marginBottom: 0 }}>
          {dimensions.map((dimension) => (
            <li key={dimension}>{dimension} - placeholder</li>
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

$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { DashboardPage } from "./pages/DashboardPage";' -ImportLine 'import { AnalyticsCenterPage } from "./pages/AnalyticsCenterPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { AnalyticsCenterPage } from "./pages/AnalyticsCenterPage";' -ImportLine 'import { AgentRunsBoardPage } from "./pages/AgentRunsBoardPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { AgentRunsBoardPage } from "./pages/AgentRunsBoardPage";' -ImportLine 'import { CaseFlowMapPage } from "./pages/CaseFlowMapPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { CaseFlowMapPage } from "./pages/CaseFlowMapPage";' -ImportLine 'import { QualityRadarPage } from "./pages/QualityRadarPage";'

$mainContent = Ensure-NavBlock -Content $mainContent -Anchor '<Link to="/dashboard">Dashboard</Link>' -NavBlock '<Link to="/analytics-center">Analytics Center</Link>
          <Link to="/agent-runs-board">Agent Runs Board</Link>
          <Link to="/case-flow-map">Case Flow Map</Link>
          <Link to="/quality-radar">Quality Radar</Link>' -PresencePattern 'to="/analytics-center"'

$mainContent = Ensure-RouteBlock -Content $mainContent -AnchorRoute '{ path: "/dashboard", element: <DashboardPage /> },' -RouteBlock '{ path: "/analytics-center", element: <AnalyticsCenterPage /> },
  { path: "/agent-runs-board", element: <AgentRunsBoardPage /> },
  { path: "/case-flow-map", element: <CaseFlowMapPage /> },
  { path: "/quality-radar", element: <QualityRadarPage /> },' -PresencePattern 'path: "/analytics-center"'

Write-Utf8File -Path $mainPath -Content $mainContent

Write-Host "Building backend..."
Build-Backend -RootDir $RootDir

Write-Host "Building frontend..."
Build-Frontend -RootDir $RootDir

Write-Host "Phase 6.20 applied successfully."
