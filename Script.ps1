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
Git-Checkpoint -Message ("checkpoint before phase 6.19 - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying Phase 6.19 - Large Scope - Executive Oversight and Delivery Control Pack..."

$webRoot = Join-Path $RootDir "apps\web\veritas-atlas-web\src"

$executiveOverviewPath = Join-Path $webRoot "pages\ExecutiveOverviewPage.tsx"
$deliveryControlTowerPath = Join-Path $webRoot "pages\DeliveryControlTowerPage.tsx"
$workstreamBoardPath = Join-Path $webRoot "pages\WorkstreamBoardPage.tsx"
$escalationCenterPath = Join-Path $webRoot "pages\EscalationCenterPage.tsx"
$executiveSummaryPanelPath = Join-Path $webRoot "components\ExecutiveSummaryPanel.tsx"
$deliveryHealthPanelPath = Join-Path $webRoot "components\DeliveryHealthPanel.tsx"
$mainPath = Join-Path $webRoot "main.tsx"

Write-Utf8File -Path $executiveSummaryPanelPath -Content @'
export function ExecutiveSummaryPanel() {
  return (
    <div style={{ border: "1px solid #ddd", borderRadius: 12, padding: 16 }}>
      <h3 style={{ marginTop: 0 }}>Executive Summary</h3>
      <ul style={{ marginBottom: 0 }}>
        <li>Total operational surfaces expanded: placeholder</li>
        <li>Key review bottlenecks: placeholder</li>
        <li>Top publication risks: placeholder</li>
      </ul>
    </div>
  );
}
'@

Write-Utf8File -Path $deliveryHealthPanelPath -Content @'
export function DeliveryHealthPanel() {
  return (
    <div style={{ border: "1px solid #ddd", borderRadius: 12, padding: 16 }}>
      <h3 style={{ marginTop: 0 }}>Delivery Health</h3>
      <ul style={{ marginBottom: 0 }}>
        <li>Build health: placeholder</li>
        <li>Workflow continuity: placeholder</li>
        <li>Escalation pressure: placeholder</li>
      </ul>
    </div>
  );
}
'@

Write-Utf8File -Path $executiveOverviewPath -Content @'
import { Link } from "react-router-dom";
import { ExecutiveSummaryPanel } from "../components/ExecutiveSummaryPanel";
import { DeliveryHealthPanel } from "../components/DeliveryHealthPanel";

export function ExecutiveOverviewPage() {
  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Executive Overview</h1>
      <p>High-level operational oversight across governance, review, publication, and delivery execution.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/operations-intelligence">Operations Intelligence</Link>
        <Link to="/governance-console">Governance Console</Link>
        <Link to="/delivery-control-tower">Delivery Control Tower</Link>
        <Link to="/escalation-center">Escalation Center</Link>
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16, marginBottom: 24 }}>
        <ExecutiveSummaryPanel />
        <DeliveryHealthPanel />
      </div>

      <section style={panelStyle}>
        <h2 style={{ marginTop: 0 }}>Executive priorities</h2>
        <ul style={{ marginBottom: 0 }}>
          <li>Reduce blocked publication items</li>
          <li>Improve review-to-publication flow</li>
          <li>Track unresolved escalations and operational friction</li>
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

Write-Utf8File -Path $deliveryControlTowerPath -Content @'
import { Link } from "react-router-dom";

export function DeliveryControlTowerPage() {
  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Delivery Control Tower</h1>
      <p>Central delivery surface for active workstreams, sequencing, and operational follow-through.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/executive-overview">Executive Overview</Link>
        <Link to="/workstream-board">Workstream Board</Link>
        <Link to="/escalation-center">Escalation Center</Link>
      </div>

      <section style={panelStyle}>
        <h2 style={{ marginTop: 0 }}>Control priorities</h2>
        <ul style={{ marginBottom: 0 }}>
          <li>Monitor active operational workstreams</li>
          <li>Keep blockers visible</li>
          <li>Route issues to escalation center when needed</li>
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

Write-Utf8File -Path $workstreamBoardPath -Content @'
import { Link } from "react-router-dom";

export function WorkstreamBoardPage() {
  const lanes = {
    Planning: ["Governance refinement", "Review routing"],
    Active: ["Claim operations", "Contradiction preparation", "Publication readiness"],
    Blocked: ["Escalation policy placeholder"],
    Done: ["Ingestion workspace expansion"]
  };

  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Workstream Board</h1>
      <p>Visual organization of operational workstreams and current execution state.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/delivery-control-tower">Delivery Control Tower</Link>
        <Link to="/executive-overview">Executive Overview</Link>
      </div>

      <div style={gridStyle}>
        {Object.entries(lanes).map(([lane, items]) => (
          <div key={lane} style={laneStyle}>
            <h3 style={{ marginTop: 0 }}>{lane}</h3>
            <ul style={{ marginBottom: 0 }}>
              {items.map((item) => (
                <li key={item}>{item}</li>
              ))}
            </ul>
          </div>
        ))}
      </div>
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))",
  gap: 16,
};

const laneStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  minHeight: 220,
};
'@

Write-Utf8File -Path $escalationCenterPath -Content @'
import { Link } from "react-router-dom";

export function EscalationCenterPage() {
  const escalations = [
    "Review bottleneck placeholder",
    "Publication blocker placeholder",
    "Contradiction severity escalation placeholder",
    "Operational routing issue placeholder"
  ];

  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Escalation Center</h1>
      <p>Central point for operational blockers, high-risk items, and unresolved workflow issues.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/delivery-control-tower">Delivery Control Tower</Link>
        <Link to="/executive-overview">Executive Overview</Link>
        <Link to="/governance-console">Governance Console</Link>
      </div>

      <section style={panelStyle}>
        <h2 style={{ marginTop: 0 }}>Current escalations</h2>
        <ul style={{ marginBottom: 0 }}>
          {escalations.map((item, index) => (
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

$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { DashboardPage } from "./pages/DashboardPage";' -ImportLine 'import { ExecutiveOverviewPage } from "./pages/ExecutiveOverviewPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { ExecutiveOverviewPage } from "./pages/ExecutiveOverviewPage";' -ImportLine 'import { DeliveryControlTowerPage } from "./pages/DeliveryControlTowerPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { DeliveryControlTowerPage } from "./pages/DeliveryControlTowerPage";' -ImportLine 'import { WorkstreamBoardPage } from "./pages/WorkstreamBoardPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { WorkstreamBoardPage } from "./pages/WorkstreamBoardPage";' -ImportLine 'import { EscalationCenterPage } from "./pages/EscalationCenterPage";'

$mainContent = Ensure-NavBlock -Content $mainContent -Anchor '<Link to="/dashboard">Dashboard</Link>' -NavBlock '<Link to="/executive-overview">Executive Overview</Link>
          <Link to="/delivery-control-tower">Delivery Control Tower</Link>
          <Link to="/workstream-board">Workstream Board</Link>
          <Link to="/escalation-center">Escalation Center</Link>' -PresencePattern 'to="/executive-overview"'

$mainContent = Ensure-RouteBlock -Content $mainContent -AnchorRoute '{ path: "/dashboard", element: <DashboardPage /> },' -RouteBlock '{ path: "/executive-overview", element: <ExecutiveOverviewPage /> },
  { path: "/delivery-control-tower", element: <DeliveryControlTowerPage /> },
  { path: "/workstream-board", element: <WorkstreamBoardPage /> },
  { path: "/escalation-center", element: <EscalationCenterPage /> },' -PresencePattern 'path: "/executive-overview"'

Write-Utf8File -Path $mainPath -Content $mainContent

Write-Host "Building backend..."
Build-Backend -RootDir $RootDir

Write-Host "Building frontend..."
Build-Frontend -RootDir $RootDir

Write-Host "Phase 6.19 applied successfully."
