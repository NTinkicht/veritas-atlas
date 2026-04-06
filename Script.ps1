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
Git-Checkpoint -Message ("checkpoint before phase 6.31 - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying Phase 6.31 - Delivery Closeout, Release Readiness, and Ops Coordination pack..." -ForegroundColor Cyan

$web = Join-Path $RootDir "apps\web\veritas-atlas-web\src"

Write-File (Join-Path $web "components\ReleaseReadinessPanel.tsx") @'
type ReleaseReadinessItem = {
  label: string;
  status: string;
};

export function ReleaseReadinessPanel({
  items,
}: {
  items: ReleaseReadinessItem[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Release Readiness</h3>
      <ul style={{ marginBottom: 0 }}>
        {items.map((item) => (
          <li key={item.label}>
            {item.label} - {item.status}
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

Write-File (Join-Path $web "components\DeliveryCheckpointPanel.tsx") @'
type DeliveryCheckpoint = {
  label: string;
  detail: string;
};

export function DeliveryCheckpointPanel({
  items,
}: {
  items: DeliveryCheckpoint[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Delivery Checkpoints</h3>
      <ul style={{ marginBottom: 0 }}>
        {items.map((item) => (
          <li key={item.label}>
            <strong>{item.label}</strong>: {item.detail}
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

Write-File (Join-Path $web "components\OpsCoordinationMatrixPanel.tsx") @'
type OpsCoordinationItem = {
  workspace: string;
  nextAction: string;
};

export function OpsCoordinationMatrixPanel({
  items,
}: {
  items: OpsCoordinationItem[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Ops Coordination Matrix</h3>
      <ul style={{ marginBottom: 0 }}>
        {items.map((item) => (
          <li key={item.workspace}>
            {item.workspace} - {item.nextAction}
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

Write-File (Join-Path $web "pages\ReleaseReadinessHubPage.tsx") @'
import { Link } from "react-router-dom";
import { ReleaseReadinessPanel } from "../components/ReleaseReadinessPanel";

export function ReleaseReadinessHubPage() {
  const items = [
    { label: "Claims workflow", status: "Ready" },
    { label: "Contradiction workflow", status: "Ready" },
    { label: "Review surfaces", status: "Ready" },
    { label: "Publication governance", status: "In progress" },
    { label: "Narrative layer", status: "In progress" },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Release Readiness Hub</h1>
        <p style={{ color: "#555" }}>
          Central release-readiness surface across operational, review, and publication layers.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/release-readiness-hub">Release Readiness Hub</Link>
          <Link to="/publication-governance">Publication Governance</Link>
          <Link to="/readiness-radar-workspace">Readiness Radar</Link>
        </nav>
      </header>

      <ReleaseReadinessPanel items={items} />
    </div>
  );
}
'@

Write-File (Join-Path $web "pages\DeliveryCloseoutWorkspacePage.tsx") @'
import { Link } from "react-router-dom";
import { DeliveryCheckpointPanel } from "../components/DeliveryCheckpointPanel";

export function DeliveryCloseoutWorkspacePage() {
  const items = [
    { label: "Core entity workflows", detail: "Operational" },
    { label: "Case explorer and workbench", detail: "Operational" },
    { label: "Review and publication pack", detail: "Operational shell ready" },
    { label: "Decision intelligence layer", detail: "Operational shell ready" },
    { label: "Release governance", detail: "Needs final business wiring" },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Delivery Closeout Workspace</h1>
        <p style={{ color: "#555" }}>
          Workspace for tracking implementation closeout and remaining readiness gaps.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/delivery-closeout">Delivery Closeout</Link>
          <Link to="/delivery-control-tower">Delivery Control Tower</Link>
          <Link to="/release-readiness-hub">Release Readiness Hub</Link>
        </nav>
      </header>

      <DeliveryCheckpointPanel items={items} />
    </div>
  );
}
'@

Write-File (Join-Path $web "pages\OpsCoordinationCenterPage.tsx") @'
import { Link } from "react-router-dom";
import { OpsCoordinationMatrixPanel } from "../components/OpsCoordinationMatrixPanel";

export function OpsCoordinationCenterPage() {
  const items = [
    { workspace: "Evidence Flow Studio", nextAction: "Advance evidence into statements and claims" },
    { workspace: "Truth Review Studio", nextAction: "Validate contradictions and review readiness" },
    { workspace: "Publication Pipeline", nextAction: "Prepare narrative and governance checks" },
    { workspace: "Decision Intelligence", nextAction: "Review confidence and explanation surfaces" },
    { workspace: "Release Readiness Hub", nextAction: "Track final release state" },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Ops Coordination Center</h1>
        <p style={{ color: "#555" }}>
          Coordination view for moving work cleanly between operational surfaces.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/ops-coordination-center">Ops Coordination Center</Link>
          <Link to="/operational-handoff">Operational Handoff</Link>
          <Link to="/release-readiness-hub">Release Readiness Hub</Link>
        </nav>
      </header>

      <OpsCoordinationMatrixPanel items={items} />
    </div>
  );
}
'@

$main = Join-Path $web "main.tsx"
$content = Get-Content $main -Raw

$content = Ensure-ImportLine -Content $content -Anchor 'import { DashboardPage } from "./pages/DashboardPage";' -ImportLine 'import { ReleaseReadinessHubPage } from "./pages/ReleaseReadinessHubPage";'
$content = Ensure-ImportLine -Content $content -Anchor 'import { ReleaseReadinessHubPage } from "./pages/ReleaseReadinessHubPage";' -ImportLine 'import { DeliveryCloseoutWorkspacePage } from "./pages/DeliveryCloseoutWorkspacePage";'
$content = Ensure-ImportLine -Content $content -Anchor 'import { DeliveryCloseoutWorkspacePage } from "./pages/DeliveryCloseoutWorkspacePage";' -ImportLine 'import { OpsCoordinationCenterPage } from "./pages/OpsCoordinationCenterPage";'

$content = Ensure-NavBlock -Content $content -Anchor '<Link to="/knowledge-graph-hub">Knowledge Graph Hub</Link>' -NavBlock '<Link to="/release-readiness-hub">Release Readiness Hub</Link>
          <Link to="/delivery-closeout">Delivery Closeout</Link>
          <Link to="/ops-coordination-center">Ops Coordination</Link>' -PresencePattern 'to="/release-readiness-hub"'

$content = Ensure-RouteBlock -Content $content -AnchorRoute '{ path: "/knowledge-graph-hub", element: <KnowledgeGraphHubPage /> },' -RouteBlock '{ path: "/release-readiness-hub", element: <ReleaseReadinessHubPage /> },
  { path: "/delivery-closeout", element: <DeliveryCloseoutWorkspacePage /> },
  { path: "/ops-coordination-center", element: <OpsCoordinationCenterPage /> },' -PresencePattern 'path: "/release-readiness-hub"'

Write-File $main $content

Write-Host "Building..." -ForegroundColor Cyan
Build-All -RootDir $RootDir

Write-Host "Phase 6.31 DONE" -ForegroundColor Green
