param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir
)

$ErrorActionPreference = "Stop"

function Ensure-Dir {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Ensure-Dir received an empty path."
    }
    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Write-File {
    param(
        [string]$Path,
        [string]$Content
    )
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Write-File received an empty path."
    }
    $parent = Split-Path -Parent $Path
    Ensure-Dir $parent
    $enc = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $enc)
    Write-Host "Wrote: $Path"
}

function Git-Checkpoint {
    param([string]$Message)

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
    }
    finally {
        Pop-Location
    }
}

function Build-All {
    param([string]$RootDir)

    Push-Location $RootDir
    try {
        dotnet build
        if ($LASTEXITCODE -ne 0) { throw "Backend failed" }
    }
    finally {
        Pop-Location
    }

    $web = Join-Path $RootDir "apps\web\veritas-atlas-web"
    Push-Location $web
    try {
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "Frontend failed" }
    }
    finally {
        Pop-Location
    }
}

function Ensure-ImportLine {
    param(
        [string]$Content,
        [string]$Anchor,
        [string]$ImportLine
    )

    if ($Content -match [regex]::Escape($ImportLine)) {
        return $Content
    }

    return $Content -replace [regex]::Escape($Anchor), ($Anchor + [Environment]::NewLine + $ImportLine)
}

function Ensure-NavBlock {
    param(
        [string]$Content,
        [string]$Anchor,
        [string]$NavBlock,
        [string]$PresencePattern
    )

    if ($Content -match $PresencePattern) {
        return $Content
    }

    return $Content -replace [regex]::Escape($Anchor), ($Anchor + [Environment]::NewLine + $NavBlock)
}

function Ensure-RouteBlock {
    param(
        [string]$Content,
        [string]$AnchorRoute,
        [string]$RouteBlock,
        [string]$PresencePattern
    )

    if ($Content -match $PresencePattern) {
        return $Content
    }

    return $Content -replace [regex]::Escape($AnchorRoute), ($AnchorRoute + [Environment]::NewLine + $RouteBlock)
}

Write-Host "Checkpointing current code with git..." -ForegroundColor Cyan
Git-Checkpoint -Message ("checkpoint before phase 6.33 - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying Phase 6.33 - Final control surfaces, readiness map, and operator cockpit..." -ForegroundColor Cyan

$web = Join-Path $RootDir "apps\web\veritas-atlas-web\src"

Write-File (Join-Path $web "components\SystemReadinessMapPanel.tsx") @'
type ReadinessNode = {
  label: string;
  status: string;
};

export function SystemReadinessMapPanel({
  nodes,
}: {
  nodes: ReadinessNode[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>System Readiness Map</h3>
      <ul style={{ marginBottom: 0 }}>
        {nodes.map((node) => (
          <li key={node.label}>
            {node.label} - {node.status}
          </li>
        ))}
      </ul>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
'@

Write-File (Join-Path $web "components\OperatorCockpitPanel.tsx") @'
import { Link } from "react-router-dom";

export function OperatorCockpitPanel() {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Operator Cockpit</h3>
      <div style={{ display: "flex", gap: 12, flexWrap: "wrap" }}>
        <Link to="/case-explorer">Case Explorer</Link>
        <Link to="/truth-review-studio">Truth Review Studio</Link>
        <Link to="/publication-pipeline">Publication Pipeline</Link>
        <Link to="/decision-intelligence">Decision Intelligence</Link>
        <Link to="/release-readiness-hub">Release Readiness Hub</Link>
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

Write-File (Join-Path $web "components\FinalControlSummaryPanel.tsx") @'
type FinalControlMetric = {
  label: string;
  value: string;
};

export function FinalControlSummaryPanel({
  metrics,
}: {
  metrics: FinalControlMetric[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Final Control Summary</h3>
      <ul style={{ marginBottom: 0 }}>
        {metrics.map((metric) => (
          <li key={metric.label}>
            <strong>{metric.label}</strong>: {metric.value}
          </li>
        ))}
      </ul>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
'@

Write-File (Join-Path $web "pages\SystemReadinessMapPage.tsx") @'
import { Link } from "react-router-dom";
import { SystemReadinessMapPanel } from "../components/SystemReadinessMapPanel";

export function SystemReadinessMapPage() {
  const nodes = [
    { label: "Core entity workflows", status: "Ready" },
    { label: "Case explorer", status: "Ready" },
    { label: "Contradiction workflow", status: "Ready" },
    { label: "Review surfaces", status: "Ready" },
    { label: "Publication governance", status: "Partial" },
    { label: "Deep business logic", status: "Pending" },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>System Readiness Map</h1>
        <p style={{ color: "#555" }}>
          Final readiness map across the major Veritas Atlas operational surfaces.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/system-readiness-map">System Readiness Map</Link>
          <Link to="/release-readiness-hub">Release Readiness Hub</Link>
          <Link to="/ops-finalization-workspace">Ops Finalization</Link>
        </nav>
      </header>

      <SystemReadinessMapPanel nodes={nodes} />
    </div>
  );
}
'@

Write-File (Join-Path $web "pages\OperatorCockpitPage.tsx") @'
import { Link } from "react-router-dom";
import { OperatorCockpitPanel } from "../components/OperatorCockpitPanel";

export function OperatorCockpitPage() {
  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Operator Cockpit</h1>
        <p style={{ color: "#555" }}>
          Central operator surface for jumping across all major execution workspaces.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/operator-cockpit">Operator Cockpit</Link>
          <Link to="/ops-coordination-center">Ops Coordination</Link>
          <Link to="/delivery-closeout">Delivery Closeout</Link>
        </nav>
      </header>

      <OperatorCockpitPanel />
    </div>
  );
}
'@

Write-File (Join-Path $web "pages\FinalControlCenterPage.tsx") @'
import { Link } from "react-router-dom";
import { FinalControlSummaryPanel } from "../components/FinalControlSummaryPanel";

export function FinalControlCenterPage() {
  const metrics = [
    { label: "Operational breadth", value: "High" },
    { label: "Core workflows", value: "Implemented" },
    { label: "Governance and publication depth", value: "Partial" },
    { label: "Next focus", value: "Deeper backend + business logic" },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Final Control Center</h1>
        <p style={{ color: "#555" }}>
          Final high-level control surface summarizing the current Veritas Atlas state.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/final-control-center">Final Control Center</Link>
          <Link to="/executive-readout-workspace">Executive Readout</Link>
          <Link to="/system-readiness-map">System Readiness Map</Link>
        </nav>
      </header>

      <FinalControlSummaryPanel metrics={metrics} />
    </div>
  );
}
'@

$main = Join-Path $web "main.tsx"
$content = Get-Content $main -Raw

$content = Ensure-ImportLine -Content $content -Anchor 'import { DashboardPage } from "./pages/DashboardPage";' -ImportLine 'import { SystemReadinessMapPage } from "./pages/SystemReadinessMapPage";'
$content = Ensure-ImportLine -Content $content -Anchor 'import { SystemReadinessMapPage } from "./pages/SystemReadinessMapPage";' -ImportLine 'import { OperatorCockpitPage } from "./pages/OperatorCockpitPage";'
$content = Ensure-ImportLine -Content $content -Anchor 'import { OperatorCockpitPage } from "./pages/OperatorCockpitPage";' -ImportLine 'import { FinalControlCenterPage } from "./pages/FinalControlCenterPage";'

$content = Ensure-NavBlock -Content $content -Anchor '<Link to="/release-readiness-hub">Release Readiness Hub</Link>' -NavBlock '<Link to="/system-readiness-map">System Readiness Map</Link>
          <Link to="/operator-cockpit">Operator Cockpit</Link>
          <Link to="/final-control-center">Final Control Center</Link>' -PresencePattern 'to="/system-readiness-map"'

$content = Ensure-RouteBlock -Content $content -AnchorRoute '{ path: "/release-readiness-hub", element: <ReleaseReadinessHubPage /> },' -RouteBlock '{ path: "/system-readiness-map", element: <SystemReadinessMapPage /> },
  { path: "/operator-cockpit", element: <OperatorCockpitPage /> },
  { path: "/final-control-center", element: <FinalControlCenterPage /> },' -PresencePattern 'path: "/system-readiness-map"'

Write-File $main $content

Write-Host "Building..." -ForegroundColor Cyan
Build-All -RootDir $RootDir

Write-Host "Phase 6.33 DONE" -ForegroundColor Green
