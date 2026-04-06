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
Git-Checkpoint -Message ("checkpoint before phase 6.30 - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying Phase 6.30 - Knowledge Graph, Publication Governance, and Readiness Pack..." -ForegroundColor Cyan

$web = Join-Path $RootDir "apps\web\veritas-atlas-web\src"

Write-File (Join-Path $web "components\KnowledgeGraphSummaryPanel.tsx") @'
type GraphMetric = {
  label: string;
  value: number;
};

export function KnowledgeGraphSummaryPanel({
  metrics,
}: {
  metrics: GraphMetric[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Knowledge Graph Summary</h3>
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

Write-File (Join-Path $web "components\PublicationGovernancePanel.tsx") @'
type GovernanceCheck = {
  label: string;
  status: string;
};

export function PublicationGovernancePanel({
  checks,
}: {
  checks: GovernanceCheck[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Publication Governance</h3>
      <ul style={{ marginBottom: 0 }}>
        {checks.map((check) => (
          <li key={check.label}>
            {check.label} - {check.status}
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

Write-File (Join-Path $web "components\ReadinessRadarPanel.tsx") @'
type ReadinessItem = {
  label: string;
  score: number;
};

export function ReadinessRadarPanel({
  items,
}: {
  items: ReadinessItem[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Readiness Radar</h3>
      <ul style={{ marginBottom: 0 }}>
        {items.map((item) => (
          <li key={item.label}>
            {item.label} - {item.score}%
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

Write-File (Join-Path $web "pages\KnowledgeGraphHubPage.tsx") @'
import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";
import { useStatements } from "../hooks/useStatements";
import { useContradictions } from "../hooks/useContradictions";
import { KnowledgeGraphSummaryPanel } from "../components/KnowledgeGraphSummaryPanel";

export function KnowledgeGraphHubPage() {
  const claimsQuery = useClaims();
  const statementsQuery = useStatements();
  const contradictionsQuery = useContradictions();

  const metrics = [
    { label: "Claims", value: claimsQuery.data?.items.length ?? 0 },
    { label: "Statements", value: statementsQuery.data?.items.length ?? 0 },
    { label: "Contradictions", value: contradictionsQuery.data?.items.length ?? 0 },
    { label: "Links", value: (claimsQuery.data?.items.length ?? 0) + (contradictionsQuery.data?.items.length ?? 0) },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Knowledge Graph Hub</h1>
        <p style={{ color: "#555" }}>
          Relationship-centered operational view across statements, claims, contradictions, and graph density.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/knowledge-graph-hub">Knowledge Graph Hub</Link>
          <Link to="/entity-graph">Entity Graph</Link>
          <Link to="/case-explorer">Case Explorer</Link>
        </nav>
      </header>

      <KnowledgeGraphSummaryPanel metrics={metrics} />
    </div>
  );
}
'@

Write-File (Join-Path $web "pages\PublicationGovernanceWorkspacePage.tsx") @'
import { Link } from "react-router-dom";
import { PublicationGovernancePanel } from "../components/PublicationGovernancePanel";

export function PublicationGovernanceWorkspacePage() {
  const checks = [
    { label: "Review outcome documented", status: "Ready" },
    { label: "Contradiction workflow completed", status: "Ready" },
    { label: "Narrative prepared", status: "Pending" },
    { label: "Decision logged", status: "Pending" },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Publication Governance Workspace</h1>
        <p style={{ color: "#555" }}>
          Final governance surface before publication routing and release.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/publication-governance">Publication Governance</Link>
          <Link to="/publication-pipeline">Publication Pipeline</Link>
          <Link to="/decision-log">Decision Log</Link>
        </nav>
      </header>

      <PublicationGovernancePanel checks={checks} />
    </div>
  );
}
'@

Write-File (Join-Path $web "pages\ReadinessRadarWorkspacePage.tsx") @'
import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";
import { useContradictions } from "../hooks/useContradictions";
import { ReadinessRadarPanel } from "../components/ReadinessRadarPanel";

export function ReadinessRadarWorkspacePage() {
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  const items = [
    { label: "Claim review coverage", score: claimsQuery.data?.items.length ? 78 : 0 },
    { label: "Contradiction resolution", score: contradictionsQuery.data?.items.length ? 64 : 0 },
    { label: "Publication readiness", score: 55 },
    { label: "Decision traceability", score: 72 },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Readiness Radar Workspace</h1>
        <p style={{ color: "#555" }}>
          Operational readiness view across claims, contradictions, decision, and publication stages.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/readiness-radar-workspace">Readiness Radar</Link>
          <Link to="/decision-intelligence">Decision Intelligence</Link>
          <Link to="/publication-governance">Publication Governance</Link>
        </nav>
      </header>

      <ReadinessRadarPanel items={items} />
    </div>
  );
}
'@

$main = Join-Path $web "main.tsx"
$content = Get-Content $main -Raw

$content = Ensure-ImportLine -Content $content -Anchor 'import { DashboardPage } from "./pages/DashboardPage";' -ImportLine 'import { KnowledgeGraphHubPage } from "./pages/KnowledgeGraphHubPage";'
$content = Ensure-ImportLine -Content $content -Anchor 'import { KnowledgeGraphHubPage } from "./pages/KnowledgeGraphHubPage";' -ImportLine 'import { PublicationGovernanceWorkspacePage } from "./pages/PublicationGovernanceWorkspacePage";'
$content = Ensure-ImportLine -Content $content -Anchor 'import { PublicationGovernanceWorkspacePage } from "./pages/PublicationGovernanceWorkspacePage";' -ImportLine 'import { ReadinessRadarWorkspacePage } from "./pages/ReadinessRadarWorkspacePage";'

$content = Ensure-NavBlock -Content $content -Anchor '<Link to="/decision-intelligence">Decision Intelligence</Link>' -NavBlock '<Link to="/knowledge-graph-hub">Knowledge Graph Hub</Link>
          <Link to="/publication-governance">Publication Governance</Link>
          <Link to="/readiness-radar-workspace">Readiness Radar</Link>' -PresencePattern 'to="/knowledge-graph-hub"'

$content = Ensure-RouteBlock -Content $content -AnchorRoute '{ path: "/decision-intelligence", element: <DecisionIntelligencePage /> },' -RouteBlock '{ path: "/knowledge-graph-hub", element: <KnowledgeGraphHubPage /> },
  { path: "/publication-governance", element: <PublicationGovernanceWorkspacePage /> },
  { path: "/readiness-radar-workspace", element: <ReadinessRadarWorkspacePage /> },' -PresencePattern 'path: "/knowledge-graph-hub"'

Write-File $main $content

Write-Host "Building..." -ForegroundColor Cyan
Build-All -RootDir $RootDir

Write-Host "Phase 6.30 DONE" -ForegroundColor Green
