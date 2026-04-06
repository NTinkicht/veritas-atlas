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
Git-Checkpoint -Message ("checkpoint before phase 6.35 - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying Phase 6.35 - Final UI completion pack..." -ForegroundColor Cyan

$web = Join-Path $RootDir "apps\web\veritas-atlas-web\src"

Write-File (Join-Path $web "components\AppSurfaceCatalogPanel.tsx") @'
import { Link } from "react-router-dom";

type CatalogItem = {
  label: string;
  route: string;
  description: string;
};

export function AppSurfaceCatalogPanel({
  items,
}: {
  items: CatalogItem[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>App Surface Catalog</h3>
      <div style={gridStyle}>
        {items.map((item) => (
          <Link key={item.route} to={item.route} style={cardStyle}>
            <strong>{item.label}</strong>
            <span>{item.route}</span>
            <p style={{ margin: 0, color: "#555" }}>{item.description}</p>
          </Link>
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
  gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
  gap: 12,
};

const cardStyle: React.CSSProperties = {
  border: "1px solid #eee",
  borderRadius: 12,
  padding: 14,
  display: "grid",
  gap: 6,
  textDecoration: "none",
  color: "inherit",
};
'@

Write-File (Join-Path $web "components\CompletionChecklistPanel.tsx") @'
type ChecklistItem = {
  label: string;
  status: string;
};

export function CompletionChecklistPanel({
  items,
}: {
  items: ChecklistItem[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Completion Checklist</h3>
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

Write-File (Join-Path $web "components\ReferenceLinksPanel.tsx") @'
import { Link } from "react-router-dom";

type RefItem = {
  label: string;
  route: string;
};

export function ReferenceLinksPanel({
  items,
}: {
  items: RefItem[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Reference Links</h3>
      <div style={{ display: "flex", gap: 12, flexWrap: "wrap" }}>
        {items.map((item) => (
          <Link key={item.route} to={item.route}>
            {item.label}
          </Link>
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
'@

Write-File (Join-Path $web "pages\AppSurfaceCatalogPage.tsx") @'
import { AppSurfaceCatalogPanel } from "../components/AppSurfaceCatalogPanel";

export function AppSurfaceCatalogPage() {
  const items = [
    { label: "Case Explorer", route: "/case-explorer", description: "Primary case navigation and workbench entry." },
    { label: "Truth Review Studio", route: "/truth-review-studio", description: "Claims and contradictions review surface." },
    { label: "Publication Pipeline", route: "/publication-pipeline", description: "Publication preparation and routing shell." },
    { label: "Decision Intelligence", route: "/decision-intelligence", description: "Confidence and decision explanation surface." },
    { label: "Release Readiness Hub", route: "/release-readiness-hub", description: "Release and readiness overview." },
    { label: "Operator Cockpit", route: "/operator-cockpit", description: "Central operator jump-off surface." },
    { label: "Platform Atlas", route: "/platform-atlas", description: "Platform-level orientation and state summary." },
    { label: "Workspace Map", route: "/workspace-map", description: "Route-level map of major workspaces." },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>App Surface Catalog</h1>
      <p style={{ color: "#555" }}>
        Final catalog of the major frontend surfaces now present in Veritas Atlas.
      </p>
      <AppSurfaceCatalogPanel items={items} />
    </div>
  );
}
'@

Write-File (Join-Path $web "pages\UICompletionCenterPage.tsx") @'
import { CompletionChecklistPanel } from "../components/CompletionChecklistPanel";
import { ReferenceLinksPanel } from "../components/ReferenceLinksPanel";

export function UICompletionCenterPage() {
  const checklist = [
    { label: "Core entity pages", status: "Complete" },
    { label: "Case explorer surfaces", status: "Complete" },
    { label: "Contradiction and review surfaces", status: "Complete" },
    { label: "Publication and governance shells", status: "Complete" },
    { label: "Readiness and executive surfaces", status: "Complete" },
    { label: "Deep business logic wiring", status: "Pending deeper pass" },
  ];

  const links = [
    { label: "App Surface Catalog", route: "/app-surface-catalog" },
    { label: "Release Readiness Hub", route: "/release-readiness-hub" },
    { label: "System Readiness Map", route: "/system-readiness-map" },
    { label: "Final Control Center", route: "/final-control-center" },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>UI Completion Center</h1>
      <p style={{ color: "#555" }}>
        Final consolidation view for the completed frontend shell and the remaining deeper implementation work.
      </p>

      <div style={gridStyle}>
        <CompletionChecklistPanel items={checklist} />
        <ReferenceLinksPanel items={links} />
      </div>
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "1fr 1fr",
  gap: 16,
};
'@

Write-File (Join-Path $web "pages\NavigationIndexPage.tsx") @'
import { Link } from "react-router-dom";

export function NavigationIndexPage() {
  const groups = [
    {
      title: "Core Work",
      items: [
        { label: "Cases", route: "/cases" },
        { label: "Case Explorer", route: "/case-explorer" },
        { label: "Claims", route: "/claims" },
        { label: "Contradictions", route: "/contradictions" },
      ],
    },
    {
      title: "Review and Publication",
      items: [
        { label: "Truth Review Studio", route: "/truth-review-studio" },
        { label: "Review Decision Board", route: "/review-decision-board" },
        { label: "Publication Pipeline", route: "/publication-pipeline" },
        { label: "Publication Governance", route: "/publication-governance" },
      ],
    },
    {
      title: "Oversight and Readiness",
      items: [
        { label: "Decision Intelligence", route: "/decision-intelligence" },
        { label: "Release Readiness Hub", route: "/release-readiness-hub" },
        { label: "System Readiness Map", route: "/system-readiness-map" },
        { label: "Executive Readout", route: "/executive-readout-workspace" },
      ],
    },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Navigation Index</h1>
      <p style={{ color: "#555" }}>
        Organized route index across the current Veritas Atlas frontend.
      </p>

      <div style={gridStyle}>
        {groups.map((group) => (
          <div key={group.title} style={panelStyle}>
            <h3 style={{ marginTop: 0 }}>{group.title}</h3>
            <ul style={{ marginBottom: 0 }}>
              {group.items.map((item) => (
                <li key={item.route}>
                  <Link to={item.route}>{item.label}</Link>
                </li>
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
  gridTemplateColumns: "repeat(auto-fit, minmax(260px, 1fr))",
  gap: 16,
};

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
'@

Write-File (Join-Path $web "pages\FrontendClosurePage.tsx") @'
import { CompletionChecklistPanel } from "../components/CompletionChecklistPanel";

export function FrontendClosurePage() {
  const items = [
    { label: "UI breadth across workspaces", status: "Locked" },
    { label: "Operator and executive navigation", status: "Locked" },
    { label: "Review, contradiction, publication shells", status: "Locked" },
    { label: "Final frontend consolidation", status: "Locked" },
    { label: "Future priority", status: "Backend depth and business logic" },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Frontend Closure</h1>
      <p style={{ color: "#555" }}>
        Final UI closure surface marking the transition from broad UI expansion into deeper implementation work.
      </p>

      <CompletionChecklistPanel items={items} />
    </div>
  );
}
'@

$main = Join-Path $web "main.tsx"
$content = Get-Content $main -Raw

$content = Ensure-ImportLine -Content $content -Anchor 'import { DashboardPage } from "./pages/DashboardPage";' -ImportLine 'import { AppSurfaceCatalogPage } from "./pages/AppSurfaceCatalogPage";'
$content = Ensure-ImportLine -Content $content -Anchor 'import { AppSurfaceCatalogPage } from "./pages/AppSurfaceCatalogPage";' -ImportLine 'import { UICompletionCenterPage } from "./pages/UICompletionCenterPage";'
$content = Ensure-ImportLine -Content $content -Anchor 'import { UICompletionCenterPage } from "./pages/UICompletionCenterPage";' -ImportLine 'import { NavigationIndexPage } from "./pages/NavigationIndexPage";'
$content = Ensure-ImportLine -Content $content -Anchor 'import { NavigationIndexPage } from "./pages/NavigationIndexPage";' -ImportLine 'import { FrontendClosurePage } from "./pages/FrontendClosurePage";'

$content = Ensure-NavBlock -Content $content -Anchor '<Link to="/platform-atlas">Platform Atlas</Link>' -NavBlock '<Link to="/app-surface-catalog">App Surface Catalog</Link>
          <Link to="/ui-completion-center">UI Completion Center</Link>
          <Link to="/navigation-index">Navigation Index</Link>
          <Link to="/frontend-closure">Frontend Closure</Link>' -PresencePattern 'to="/app-surface-catalog"'

$content = Ensure-RouteBlock -Content $content -AnchorRoute '{ path: "/platform-atlas", element: <PlatformAtlasPage /> },' -RouteBlock '{ path: "/app-surface-catalog", element: <AppSurfaceCatalogPage /> },
  { path: "/ui-completion-center", element: <UICompletionCenterPage /> },
  { path: "/navigation-index", element: <NavigationIndexPage /> },
  { path: "/frontend-closure", element: <FrontendClosurePage /> },' -PresencePattern 'path: "/app-surface-catalog"'

Write-File $main $content

Write-Host "Building..." -ForegroundColor Cyan
Build-All -RootDir $RootDir

Write-Host "Phase 6.35 DONE" -ForegroundColor Green
