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
    Push-Location (Join-Path $RootDir "apps/api")
    try {
        dotnet build
        if ($LASTEXITCODE -ne 0) {
            throw "Backend build failed."
        }
    }
    finally {
        Pop-Location
    }
}

function Build-Frontend {
    Push-Location (Join-Path $RootDir "apps/web/veritas-atlas-web")
    try {
        npm run build
        if ($LASTEXITCODE -ne 0) {
            throw "Frontend build failed."
        }
    }
    finally {
        Pop-Location
    }
}

Write-Host "Checkpointing current code with git..."
Git-Checkpoint -Message ("checkpoint before phase 6.16 - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying Phase 6.16 - Large Scope - Operations Intelligence Layer..."

$webRoot = Join-Path $RootDir "apps/web/veritas-atlas-web/src"

$operationsIntelligencePath = Join-Path $webRoot "pages/OperationsIntelligencePage.tsx"
$investigationNavigatorPath = Join-Path $webRoot "pages/InvestigationNavigatorPage.tsx"
$agentActivityPanelPath = Join-Path $webRoot "components/AgentActivityPanel.tsx"
$systemTimelinePanelPath = Join-Path $webRoot "components/SystemTimelinePanel.tsx"
$mainPath = Join-Path $webRoot "main.tsx"

Write-Utf8File -Path $operationsIntelligencePath -Content @'
import React from "react";
import { Link } from "react-router-dom";
import { AgentActivityPanel } from "../components/AgentActivityPanel";
import { SystemTimelinePanel } from "../components/SystemTimelinePanel";

export function OperationsIntelligencePage() {
  return (
    <div style={{ padding: 24 }}>
      <h1>Operations Intelligence</h1>
      <p>System-wide monitoring of cases, claims, contradictions, and agent activity.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/dashboard">Dashboard</Link>
        <Link to="/operations">Operations Hub</Link>
        <Link to="/navigator">Navigator</Link>
        <Link to="/claims">Claims</Link>
        <Link to="/contradictions/workspace">Contradictions Workspace</Link>
      </div>

      <section style={{ marginBottom: 24 }}>
        <h2>Live Metrics</h2>
        <ul>
          <li>Total Cases (placeholder)</li>
          <li>Active Investigations (placeholder)</li>
          <li>Pending Reviews (placeholder)</li>
          <li>Agent Runs Today (placeholder)</li>
        </ul>
      </section>

      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16 }}>
        <AgentActivityPanel />
        <SystemTimelinePanel />
      </div>

      <section style={{ marginTop: 24 }}>
        <h2>Activity Streams</h2>
        <p>Recent claims, contradictions, and system events will appear here.</p>
      </section>
    </div>
  );
}
'@

Write-Utf8File -Path $investigationNavigatorPath -Content @'
import React from "react";
import { Link } from "react-router-dom";

export function InvestigationNavigatorPage() {
  return (
    <div style={{ padding: 24 }}>
      <h1>Investigation Navigator</h1>
      <p>Unified navigation across Cases, Claims, Statements, and Contradictions.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/operations-intelligence">Operations Intelligence</Link>
        <Link to="/operations">Operations Hub</Link>
        <Link to="/statements">Statements</Link>
        <Link to="/claims">Claims</Link>
        <Link to="/contradictions/workspace">Contradictions Workspace</Link>
      </div>

      <section style={{ marginBottom: 24 }}>
        <h2>Quick Access</h2>
        <ul>
          <li>Latest Claims</li>
          <li>Latest Statements</li>
          <li>Recent Contradictions</li>
        </ul>
      </section>

      <section>
        <h2>Cross-Linking</h2>
        <p>Jump between related entities to follow the investigation graph.</p>
      </section>
    </div>
  );
}
'@

Write-Utf8File -Path $agentActivityPanelPath -Content @'
import React from "react";

export function AgentActivityPanel() {
  return (
    <div style={{ border: "1px solid #ccc", padding: 12, borderRadius: 8 }}>
      <h3>Agent Activity</h3>
      <ul>
        <li>Extraction Agent - idle</li>
        <li>Contradiction Agent - running</li>
        <li>Confidence Agent - idle</li>
      </ul>
    </div>
  );
}
'@

Write-Utf8File -Path $systemTimelinePanelPath -Content @'
import React from "react";

export function SystemTimelinePanel() {
  return (
    <div style={{ border: "1px solid #ccc", padding: 12, borderRadius: 8 }}>
      <h3>System Timeline</h3>
      <p>Chronological events across the system.</p>
    </div>
  );
}
'@

$mainContent = Get-Content $mainPath -Raw

if ($mainContent -notmatch 'OperationsIntelligencePage') {
    $mainContent = $mainContent -replace 'import \{ DashboardPage \} from "\./pages/DashboardPage";', @'
import { DashboardPage } from "./pages/DashboardPage";
import { OperationsIntelligencePage } from "./pages/OperationsIntelligencePage";
import { InvestigationNavigatorPage } from "./pages/InvestigationNavigatorPage";
'@
}

if ($mainContent -notmatch 'path: "/operations-intelligence"') {
    $mainContent = $mainContent -replace '\{ path: "/dashboard", element: <DashboardPage /> \},', @'
{ path: "/dashboard", element: <DashboardPage /> },
  { path: "/operations-intelligence", element: <OperationsIntelligencePage /> },
  { path: "/investigation-navigator", element: <InvestigationNavigatorPage /> },
'@
}

if ($mainContent -notmatch 'to="/operations-intelligence"') {
    $mainContent = $mainContent -replace '<Link to="/dashboard">Dashboard</Link>', @'
<Link to="/dashboard">Dashboard</Link>
          <Link to="/operations-intelligence">Operations Intelligence</Link>
          <Link to="/investigation-navigator">Investigation Navigator</Link>
'@
}

Write-Utf8File -Path $mainPath -Content $mainContent

Write-Host "Building backend..."
Build-Backend

Write-Host "Building frontend..."
Build-Frontend

Write-Host "Phase 6.16 applied successfully."
