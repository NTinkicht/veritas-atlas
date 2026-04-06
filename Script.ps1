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
Git-Checkpoint -Message ("checkpoint before phase 6.34 - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying Phase 6.34 - platform atlas, workspace map, and route registry pack..." -ForegroundColor Cyan

$web = Join-Path $RootDir "apps\web\veritas-atlas-web\src"

Write-File (Join-Path $web "components\PlatformAtlasSummaryPanel.tsx") @'
type AtlasMetric = {
  label: string;
  value: string;
};

export function PlatformAtlasSummaryPanel({
  metrics,
}: {
  metrics: AtlasMetric[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Platform Atlas Summary</h3>
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

Write-File (Join-Path $web "components\WorkspaceMapPanel.tsx") @'
type WorkspaceMapItem = {
  label: string;
  route: string;
};

export function WorkspaceMapPanel({
  items,
}: {
  items: WorkspaceMapItem[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Workspace Map</h3>
      <ul style={{ marginBottom: 0 }}>
        {items.map((item) => (
          <li key={item.route}>
            {item.label} - {item.route}
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

Write-File (Join-Path $web "components\RouteRegistryPanel.tsx") @'
type RouteRegistryItem = {
  category: string;
  count: number;
};

export function RouteRegistryPanel({
  items,
}: {
  items: RouteRegistryItem[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Route Registry</h3>
      <ul style={{ marginBottom: 0 }}>
        {items.map((item) => (
          <li key={item.category}>
            {item.category}: {item.count}
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

Write-File (Join-Path $web "pages\PlatformAtlasPage.tsx") @'
import { Link } from "react-router-dom";
import { PlatformAtlasSummaryPanel } from "../components/PlatformAtlasSummaryPanel";

export function PlatformAtlasPage() {
  const metrics = [
    { label: "Current state", value: "Operational prototype" },
    { label: "Core vertical slices", value: "Claims, contradictions, review shell" },
    { label: "UI surface breadth", value: "High" },
    { label: "Next depth focus", value: "Backend and business logic" },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Platform Atlas</h1>
        <p style={{ color: "#555" }}>
          Consolidated picture of the Veritas Atlas platform and its current operational footprint.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/platform-atlas">Platform Atlas</Link>
          <Link to="/system-readiness-map">System Readiness Map</Link>
          <Link to="/executive-readout-workspace">Executive Readout</Link>
        </nav>
      </header>

      <PlatformAtlasSummaryPanel metrics={metrics} />
    </div>
  );
}
'@

Write-File (Join-Path $web "pages\WorkspaceMapPage.tsx") @'
import { Link } from "react-router-dom";
import { WorkspaceMapPanel } from "../components/WorkspaceMapPanel";

export function WorkspaceMapPage() {
  const items = [
    { label: "Case Explorer", route: "/case-explorer" },
    { label: "Truth Review Studio", route: "/truth-review-studio" },
    { label: "Publication Pipeline", route: "/publication-pipeline" },
    { label: "Decision Intelligence", route: "/decision-intelligence" },
    { label: "Release Readiness Hub", route: "/release-readiness-hub" },
    { label: "Operator Cockpit", route: "/operator-cockpit" },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Workspace Map</h1>
        <p style={{ color: "#555" }}>
          Quick navigation map across the major operational workspaces already present in the product.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/workspace-map">Workspace Map</Link>
          <Link to="/operator-cockpit">Operator Cockpit</Link>
          <Link to="/ops-coordination-center">Ops Coordination</Link>
        </nav>
      </header>

      <WorkspaceMapPanel items={items} />
    </div>
  );
}
'@

Write-File (Join-Path $web "pages\RouteRegistryPage.tsx") @'
import { Link } from "react-router-dom";
import { RouteRegistryPanel } from "../components/RouteRegistryPanel";

export function RouteRegistryPage() {
  const items = [
    { category: "Core entity pages", count: 12 },
    { category: "Review and contradiction pages", count: 10 },
    { category: "Publication and governance pages", count: 10 },
    { category: "Executive and readiness pages", count: 10 },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Route Registry</h1>
        <p style={{ color: "#555" }}>
          Registry-style summary of the current operational route families inside the frontend.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/route-registry">Route Registry</Link>
          <Link to="/platform-atlas">Platform Atlas</Link>
          <Link to="/workspace-map">Workspace Map</Link>
        </nav>
      </header>

      <RouteRegistryPanel items={items} />
    </div>
  );
}
'@

$main = Join-Path $web "main.tsx"
$content = Get-Content $main -Raw

$content = Ensure-ImportLine -Content $content -Anchor 'import { DashboardPage } from "./pages/DashboardPage";' -ImportLine 'import { PlatformAtlasPage } from "./pages/PlatformAtlasPage";'
$content = Ensure-ImportLine -Content $content -Anchor 'import { PlatformAtlasPage } from "./pages/PlatformAtlasPage";' -ImportLine 'import { WorkspaceMapPage } from "./pages/WorkspaceMapPage";'
$content = Ensure-ImportLine -Content $content -Anchor 'import { WorkspaceMapPage } from "./pages/WorkspaceMapPage";' -ImportLine 'import { RouteRegistryPage } from "./pages/RouteRegistryPage";'

$content = Ensure-NavBlock -Content $content -Anchor '<Link to="/system-readiness-map">System Readiness Map</Link>' -NavBlock '<Link to="/platform-atlas">Platform Atlas</Link>
          <Link to="/workspace-map">Workspace Map</Link>
          <Link to="/route-registry">Route Registry</Link>' -PresencePattern 'to="/platform-atlas"'

$content = Ensure-RouteBlock -Content $content -AnchorRoute '{ path: "/system-readiness-map", element: <SystemReadinessMapPage /> },' -RouteBlock '{ path: "/platform-atlas", element: <PlatformAtlasPage /> },
  { path: "/workspace-map", element: <WorkspaceMapPage /> },
  { path: "/route-registry", element: <RouteRegistryPage /> },' -PresencePattern 'path: "/platform-atlas"'

Write-File $main $content

Write-Host "Building..." -ForegroundColor Cyan
Build-All -RootDir $RootDir

Write-Host "Phase 6.34 DONE" -ForegroundColor Green
