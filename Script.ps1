param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir
)

$ErrorActionPreference = "Stop"

function Ensure-Directory {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Ensure-Directory received an empty path."
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

    $dir = Split-Path -Parent $Path
    Ensure-Directory $dir

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
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
Git-Checkpoint -Message ("checkpoint before phase 6.27 - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying Phase 6.27 - Search, Graph, Source Intelligence, and Review Audit UI pack..." -ForegroundColor Cyan

$web = Join-Path $RootDir "apps\web\veritas-atlas-web\src"

Write-File (Join-Path $web "components\SourceIntelligencePanel.tsx") @'
type SourceIntelligenceItem = {
  id: string;
  name: string;
  status: string;
};

export function SourceIntelligencePanel({
  items,
}: {
  items: SourceIntelligenceItem[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Source Intelligence</h3>
      {items.length === 0 && <p>No sources available.</p>}
      {items.length > 0 && (
        <ul style={{ marginBottom: 0 }}>
          {items.map((item) => (
            <li key={item.id}>
              {item.name} - {item.status}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
'@

Write-File (Join-Path $web "components\EntityGraphPanel.tsx") @'
type EntityGraphNode = {
  id: string;
  label: string;
  type: string;
};

export function EntityGraphPanel({
  nodes,
}: {
  nodes: EntityGraphNode[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Entity Graph</h3>
      {nodes.length === 0 && <p>No graph nodes available.</p>}
      {nodes.length > 0 && (
        <ul style={{ marginBottom: 0 }}>
          {nodes.map((node) => (
            <li key={node.id}>
              {node.label} - {node.type}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
'@

Write-File (Join-Path $web "components\ReviewAuditPanel.tsx") @'
type ReviewAuditItem = {
  id: string;
  label: string;
  outcome: string;
};

export function ReviewAuditPanel({
  items,
}: {
  items: ReviewAuditItem[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Review Audit</h3>
      {items.length === 0 && <p>No audit entries available.</p>}
      {items.length > 0 && (
        <ul style={{ marginBottom: 0 }}>
          {items.map((item) => (
            <li key={item.id}>
              {item.label} - {item.outcome}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
'@

Write-File (Join-Path $web "pages\UnifiedSearchWorkspacePage.tsx") @'
import { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";
import { useStatements } from "../hooks/useStatements";
import { useContradictions } from "../hooks/useContradictions";

export function UnifiedSearchWorkspacePage() {
  const [query, setQuery] = useState("");
  const claimsQuery = useClaims();
  const statementsQuery = useStatements();
  const contradictionsQuery = useContradictions();

  const normalized = query.trim().toLowerCase();

  const claimResults = useMemo(() => {
    const items = claimsQuery.data?.items ?? [];
    if (!normalized) return items.slice(0, 8);
    return items.filter((x) =>
      x.topic.toLowerCase().includes(normalized) ||
      x.normalizedText.toLowerCase().includes(normalized)
    ).slice(0, 8);
  }, [claimsQuery.data, normalized]);

  const statementResults = useMemo(() => {
    const items = statementsQuery.data?.items ?? [];
    if (!normalized) return items.slice(0, 8);
    return items.filter((x) =>
      x.text.toLowerCase().includes(normalized) ||
      (x.topic ?? "").toLowerCase().includes(normalized)
    ).slice(0, 8);
  }, [statementsQuery.data, normalized]);

  const contradictionResults = useMemo(() => {
    const items = contradictionsQuery.data?.items ?? [];
    if (!normalized) return items.slice(0, 8);
    return items.filter((x) =>
      x.topic.toLowerCase().includes(normalized) ||
      x.summary.toLowerCase().includes(normalized)
    ).slice(0, 8);
  }, [contradictionsQuery.data, normalized]);

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Unified Search Workspace</h1>
        <p style={{ color: "#555" }}>
          Cross-search statements, claims, and contradictions from one operational surface.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/global-search-workspace">Global Search Workspace</Link>
          <Link to="/truth-review-studio">Truth Review Studio</Link>
          <Link to="/case-explorer">Case Explorer</Link>
        </nav>
      </header>

      <input
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        placeholder="Search across claims, statements, contradictions..."
        style={inputStyle}
      />

      <div style={gridStyle}>
        <ResultPanel title="Claims" emptyText="No claim results.">
          {claimResults.map((item) => (
            <li key={item.id}>
              <Link to={`/claims/${item.id}`}>{item.topic}</Link> - {item.status}
            </li>
          ))}
        </ResultPanel>

        <ResultPanel title="Statements" emptyText="No statement results.">
          {statementResults.map((item) => (
            <li key={item.id}>
              <Link to={`/statements/${item.id}`}>{item.text}</Link>
            </li>
          ))}
        </ResultPanel>

        <ResultPanel title="Contradictions" emptyText="No contradiction results.">
          {contradictionResults.map((item) => (
            <li key={item.id}>
              <Link to={`/contradictions/${item.id}`}>{item.topic}</Link> - {item.status}
            </li>
          ))}
        </ResultPanel>
      </div>
    </div>
  );
}

function ResultPanel({
  title,
  emptyText,
  children,
}: {
  title: string;
  emptyText: string;
  children: React.ReactNode;
}) {
  const count = Array.isArray(children) ? children.length : 0;

  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>{title}</h3>
      {count === 0 ? <p>{emptyText}</p> : <ul style={{ marginBottom: 0 }}>{children}</ul>}
    </div>
  );
}

const inputStyle: React.CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  padding: "12px 14px",
  borderRadius: 10,
  border: "1px solid #ccc",
  font: "inherit",
  marginBottom: 20,
};

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
  gap: 16,
};

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
'@

Write-File (Join-Path $web "pages\SourceIntelligencePage.tsx") @'
import { Link } from "react-router-dom";
import { useSources } from "../hooks/useSources";
import { SourceIntelligencePanel } from "../components/SourceIntelligencePanel";

export function SourceIntelligencePage() {
  const sourcesQuery = useSources();

  const items = (sourcesQuery.data?.items ?? []).slice(0, 12).map((item) => ({
    id: item.id,
    name: item.name,
    status: item.status,
  }));

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Source Intelligence</h1>
        <p style={{ color: "#555" }}>
          Source-focused operational view for ingestion quality and source coverage.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/sources">Sources</Link>
          <Link to="/source-intelligence">Source Intelligence</Link>
          <Link to="/evidence-trace">Evidence Trace</Link>
        </nav>
      </header>

      <SourceIntelligencePanel items={items} />
    </div>
  );
}
'@

Write-File (Join-Path $web "pages\EntityGraphWorkspacePage.tsx") @'
import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";
import { useContradictions } from "../hooks/useContradictions";
import { EntityGraphPanel } from "../components/EntityGraphPanel";

export function EntityGraphWorkspacePage() {
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  const claimNodes = (claimsQuery.data?.items ?? []).slice(0, 6).map((item) => ({
    id: item.id,
    label: item.topic,
    type: "Claim",
  }));

  const contradictionNodes = (contradictionsQuery.data?.items ?? []).slice(0, 6).map((item) => ({
    id: item.id,
    label: item.topic,
    type: "Contradiction",
  }));

  const nodes = [...claimNodes, ...contradictionNodes];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Entity Graph Workspace</h1>
        <p style={{ color: "#555" }}>
          Relationship-oriented operational surface for claims and contradictions.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/entity-graph">Entity Graph</Link>
          <Link to="/case-explorer">Case Explorer</Link>
          <Link to="/truth-review-studio">Truth Review Studio</Link>
        </nav>
      </header>

      <EntityGraphPanel nodes={nodes} />
    </div>
  );
}
'@

Write-File (Join-Path $web "pages\ReviewAuditWorkspacePage.tsx") @'
import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";
import { useContradictions } from "../hooks/useContradictions";
import { ReviewAuditPanel } from "../components/ReviewAuditPanel";

export function ReviewAuditWorkspacePage() {
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  const items = [
    ...(claimsQuery.data?.items ?? []).slice(0, 4).map((item) => ({
      id: item.id,
      label: item.topic,
      outcome: item.status,
    })),
    ...(contradictionsQuery.data?.items ?? []).slice(0, 4).map((item) => ({
      id: item.id,
      label: item.topic,
      outcome: item.status,
    })),
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Review Audit Workspace</h1>
        <p style={{ color: "#555" }}>
          Audit-oriented view across review outcomes, contradictions, and decision readiness.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/review-audit">Review Audit</Link>
          <Link to="/review-decision-board">Review Decision Board</Link>
          <Link to="/decision-log">Decision Log</Link>
        </nav>
      </header>

      <ReviewAuditPanel items={items} />
    </div>
  );
}
'@

$main = Join-Path $web "main.tsx"
$content = Get-Content $main -Raw

$content = Ensure-ImportLine -Content $content -Anchor 'import { DashboardPage } from "./pages/DashboardPage";' -ImportLine 'import { UnifiedSearchWorkspacePage } from "./pages/UnifiedSearchWorkspacePage";'
$content = Ensure-ImportLine -Content $content -Anchor 'import { UnifiedSearchWorkspacePage } from "./pages/UnifiedSearchWorkspacePage";' -ImportLine 'import { SourceIntelligencePage } from "./pages/SourceIntelligencePage";'
$content = Ensure-ImportLine -Content $content -Anchor 'import { SourceIntelligencePage } from "./pages/SourceIntelligencePage";' -ImportLine 'import { EntityGraphWorkspacePage } from "./pages/EntityGraphWorkspacePage";'
$content = Ensure-ImportLine -Content $content -Anchor 'import { EntityGraphWorkspacePage } from "./pages/EntityGraphWorkspacePage";' -ImportLine 'import { ReviewAuditWorkspacePage } from "./pages/ReviewAuditWorkspacePage";'

$content = Ensure-NavBlock -Content $content -Anchor '<Link to="/truth-review-studio">Truth Review Studio</Link>' -NavBlock '<Link to="/global-search-workspace">Global Search Workspace</Link>
          <Link to="/source-intelligence">Source Intelligence</Link>
          <Link to="/entity-graph">Entity Graph</Link>
          <Link to="/review-audit">Review Audit</Link>' -PresencePattern 'to="/global-search-workspace"'

$content = Ensure-RouteBlock -Content $content -AnchorRoute '{ path: "/truth-review-studio", element: <TruthReviewStudioPage /> },' -RouteBlock '{ path: "/global-search-workspace", element: <UnifiedSearchWorkspacePage /> },
  { path: "/source-intelligence", element: <SourceIntelligencePage /> },
  { path: "/entity-graph", element: <EntityGraphWorkspacePage /> },
  { path: "/review-audit", element: <ReviewAuditWorkspacePage /> },' -PresencePattern 'path: "/global-search-workspace"'

Write-File $main $content

Write-Host "Building..." -ForegroundColor Cyan
Build-All -RootDir $RootDir

Write-Host "Phase 6.27 DONE" -ForegroundColor Green
