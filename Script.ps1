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
Git-Checkpoint -Message ("checkpoint before phase 6.32 - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying Phase 6.32 - Executive Readout, Portfolio View, and Final Ops Surfaces pack..." -ForegroundColor Cyan

$web = Join-Path $RootDir "apps\web\veritas-atlas-web\src"

Write-File (Join-Path $web "components\ExecutiveReadoutPanel.tsx") @'
type ExecutiveReadoutItem = {
  label: string;
  value: string;
};

export function ExecutiveReadoutPanel({
  items,
}: {
  items: ExecutiveReadoutItem[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Executive Readout</h3>
      <ul style={{ marginBottom: 0 }}>
        {items.map((item) => (
          <li key={item.label}>
            <strong>{item.label}</strong>: {item.value}
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

Write-File (Join-Path $web "components\PortfolioRollupPanel.tsx") @'
type PortfolioMetric = {
  label: string;
  value: number;
};

export function PortfolioRollupPanel({
  metrics,
}: {
  metrics: PortfolioMetric[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Portfolio Rollup</h3>
      <div style={gridStyle}>
        {metrics.map((metric) => (
          <div key={metric.label} style={cardStyle}>
            <span style={{ color: "#666" }}>{metric.label}</span>
            <strong style={{ fontSize: 28 }}>{metric.value}</strong>
          </div>
        ))}
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
  gridTemplateColumns: "repeat(auto-fit, minmax(160px, 1fr))",
  gap: 12,
};

const cardStyle: React.CSSProperties = {
  border: "1px solid #eee",
  borderRadius: 12,
  padding: 12,
  display: "grid",
  gap: 8,
};
'@

Write-File (Join-Path $web "components\OpsFinalizationPanel.tsx") @'
type FinalizationItem = {
  label: string;
  status: string;
};

export function OpsFinalizationPanel({
  items,
}: {
  items: FinalizationItem[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Ops Finalization</h3>
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

Write-File (Join-Path $web "pages\ExecutiveReadoutWorkspacePage.tsx") @'
import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";
import { useContradictions } from "../hooks/useContradictions";
import { ExecutiveReadoutPanel } from "../components/ExecutiveReadoutPanel";

export function ExecutiveReadoutWorkspacePage() {
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  const items = [
    { label: "Claims visible", value: String(claimsQuery.data?.items.length ?? 0) },
    { label: "Contradictions visible", value: String(contradictionsQuery.data?.items.length ?? 0) },
    { label: "Delivery state", value: "Operational prototype" },
    { label: "Readiness mode", value: "Expansion + stabilization" },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Executive Readout Workspace</h1>
        <p style={{ color: "#555" }}>
          High-level rollup across the current Veritas Atlas operational system.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/executive-readout-workspace">Executive Readout</Link>
          <Link to="/release-readiness-hub">Release Readiness Hub</Link>
          <Link to="/ops-coordination-center">Ops Coordination</Link>
        </nav>
      </header>

      <ExecutiveReadoutPanel items={items} />
    </div>
  );
}
'@

Write-File (Join-Path $web "pages\OperationalPortfolioPage.tsx") @'
import { Link } from "react-router-dom";
import { useCases } from "../hooks/useCases";
import { useClaims } from "../hooks/useClaims";
import { useContradictions } from "../hooks/useContradictions";
import { PortfolioRollupPanel } from "../components/PortfolioRollupPanel";

export function OperationalPortfolioPage() {
  const casesQuery = useCases();
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  const metrics = [
    { label: "Cases", value: casesQuery.data?.items.length ?? 0 },
    { label: "Claims", value: claimsQuery.data?.items.length ?? 0 },
    { label: "Contradictions", value: contradictionsQuery.data?.items.length ?? 0 },
    { label: "Open Review Work", value: contradictionsQuery.data?.items.filter(x => x.status !== "Resolved").length ?? 0 },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Operational Portfolio</h1>
        <p style={{ color: "#555" }}>
          Rollup across the major operational entities in the platform.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/operational-portfolio">Operational Portfolio</Link>
          <Link to="/case-explorer">Case Explorer</Link>
          <Link to="/knowledge-graph-hub">Knowledge Graph Hub</Link>
        </nav>
      </header>

      <PortfolioRollupPanel metrics={metrics} />
    </div>
  );
}
'@

Write-File (Join-Path $web "pages\OpsFinalizationWorkspacePage.tsx") @'
import { Link } from "react-router-dom";
import { OpsFinalizationPanel } from "../components/OpsFinalizationPanel";

export function OpsFinalizationWorkspacePage() {
  const items = [
    { label: "Core entity pages", status: "Ready" },
    { label: "Case explorer surfaces", status: "Ready" },
    { label: "Contradiction workflow surfaces", status: "Ready" },
    { label: "Review and publication shells", status: "Ready" },
    { label: "Decision and readiness layers", status: "Ready" },
    { label: "Final business-depth pass", status: "Pending" },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Ops Finalization Workspace</h1>
        <p style={{ color: "#555" }}>
          Final surface for consolidating operational completion before deeper backend and business logic passes.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/ops-finalization-workspace">Ops Finalization</Link>
          <Link to="/delivery-closeout">Delivery Closeout</Link>
          <Link to="/release-readiness-hub">Release Readiness Hub</Link>
        </nav>
      </header>

      <OpsFinalizationPanel items={items} />
    </div>
  );
}
'@

$main = Join-Path $web "main.tsx"
$content = Get-Content $main -Raw

$content = Ensure-ImportLine -Content $content -Anchor 'import { DashboardPage } from "./pages/DashboardPage";' -ImportLine 'import { ExecutiveReadoutWorkspacePage } from "./pages/ExecutiveReadoutWorkspacePage";'
$content = Ensure-ImportLine -Content $content -Anchor 'import { ExecutiveReadoutWorkspacePage } from "./pages/ExecutiveReadoutWorkspacePage";' -ImportLine 'import { OperationalPortfolioPage } from "./pages/OperationalPortfolioPage";'
$content = Ensure-ImportLine -Content $content -Anchor 'import { OperationalPortfolioPage } from "./pages/OperationalPortfolioPage";' -ImportLine 'import { OpsFinalizationWorkspacePage } from "./pages/OpsFinalizationWorkspacePage";'

$content = Ensure-NavBlock -Content $content -Anchor '<Link to="/release-readiness-hub">Release Readiness Hub</Link>' -NavBlock '<Link to="/executive-readout-workspace">Executive Readout</Link>
          <Link to="/operational-portfolio">Operational Portfolio</Link>
          <Link to="/ops-finalization-workspace">Ops Finalization</Link>' -PresencePattern 'to="/executive-readout-workspace"'

$content = Ensure-RouteBlock -Content $content -AnchorRoute '{ path: "/release-readiness-hub", element: <ReleaseReadinessHubPage /> },' -RouteBlock '{ path: "/executive-readout-workspace", element: <ExecutiveReadoutWorkspacePage /> },
  { path: "/operational-portfolio", element: <OperationalPortfolioPage /> },
  { path: "/ops-finalization-workspace", element: <OpsFinalizationWorkspacePage /> },' -PresencePattern 'path: "/executive-readout-workspace"'

Write-File $main $content

Write-Host "Building..." -ForegroundColor Cyan
Build-All -RootDir $RootDir

Write-Host "Phase 6.32 DONE" -ForegroundColor Green
