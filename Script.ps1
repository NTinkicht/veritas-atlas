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
Git-Checkpoint -Message ("checkpoint before phase 6.24 - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying Phase 6.24 - Review Decision and Publication Pipeline UI pack..."

$webRoot = Join-Path $RootDir "apps\web\veritas-atlas-web\src"

$decisionBoardPath = Join-Path $webRoot "pages\ReviewDecisionBoardPage.tsx"
$publicationPipelinePath = Join-Path $webRoot "pages\PublicationPipelinePage.tsx"
$evidenceTracePath = Join-Path $webRoot "pages\EvidenceTracePage.tsx"
$narrativeBuilderPath = Join-Path $webRoot "pages\NarrativeBuilderPage.tsx"
$publicationChecklistPath = Join-Path $webRoot "components\PublicationChecklistPanel.tsx"
$evidenceTracePanelPath = Join-Path $webRoot "components\EvidenceTracePanel.tsx"
$mainPath = Join-Path $webRoot "main.tsx"

Write-Utf8File -Path $publicationChecklistPath -Content @'
export function PublicationChecklistPanel({
  items,
}: {
  items: Array<{ label: string; done: boolean }>;
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Publication Checklist</h3>
      <ul style={{ marginBottom: 0 }}>
        {items.map((item) => (
          <li key={item.label}>
            {item.done ? "✓" : "•"} {item.label}
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

Write-Utf8File -Path $evidenceTracePanelPath -Content @'
type EvidenceTraceNode = {
  id: string;
  label: string;
  detail: string;
};

export function EvidenceTracePanel({
  nodes,
}: {
  nodes: EvidenceTraceNode[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Evidence Trace</h3>
      {nodes.length === 0 && <p>No trace nodes available.</p>}
      {nodes.length > 0 && (
        <ul style={{ marginBottom: 0 }}>
          {nodes.map((node) => (
            <li key={node.id}>
              <strong>{node.label}</strong>: {node.detail}
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

Write-Utf8File -Path $decisionBoardPath -Content @'
import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";
import { useContradictions } from "../hooks/useContradictions";

export function ReviewDecisionBoardPage() {
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  const claims = claimsQuery.data?.items ?? [];
  const contradictions = contradictionsQuery.data?.items ?? [];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Review Decision Board</h1>
        <p style={{ color: "#555" }}>
          Operational board for decisions across claims, contradictions, and publication readiness.
        </p>

        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/truth-review-studio">Truth Review Studio</Link>
          <Link to="/review-decision-board">Review Decision Board</Link>
          <Link to="/publication-pipeline">Publication Pipeline</Link>
        </nav>
      </header>

      <div style={gridStyle}>
        <Lane
          title="Needs Review"
          items={claims.slice(0, 4).map((x) => ({ id: x.id, label: x.topic, href: `/claims/${x.id}` }))}
        />
        <Lane
          title="Needs Resolution"
          items={contradictions.slice(0, 4).map((x) => ({ id: x.id, label: x.topic, href: `/contradiction-resolution/${x.id}` }))}
        />
        <Lane
          title="Ready for Publication"
          items={claims.slice(4, 8).map((x) => ({ id: x.id, label: x.topic, href: `/claims/${x.id}` }))}
        />
      </div>
    </div>
  );
}

function Lane({
  title,
  items,
}: {
  title: string;
  items: Array<{ id: string; label: string; href: string }>;
}) {
  return (
    <div style={laneStyle}>
      <h3 style={{ marginTop: 0 }}>{title}</h3>
      {items.length === 0 && <p>No items.</p>}
      {items.length > 0 && (
        <ul style={{ marginBottom: 0 }}>
          {items.map((item) => (
            <li key={item.id}>
              <Link to={item.href}>{item.label}</Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
  gap: 16,
};

const laneStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  minHeight: 220,
};
'@

Write-Utf8File -Path $publicationPipelinePath -Content @'
import { Link } from "react-router-dom";
import { PublicationChecklistPanel } from "../components/PublicationChecklistPanel";

export function PublicationPipelinePage() {
  const checklist = [
    { label: "Claim reviewed", done: true },
    { label: "Contradictions reviewed", done: true },
    { label: "Decision logged", done: false },
    { label: "Publication narrative prepared", done: false },
    { label: "Final publication routing", done: false },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Publication Pipeline</h1>
        <p style={{ color: "#555" }}>
          Working surface for publication preparation, gating, and final release flow.
        </p>

        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/review-decision-board">Review Decision Board</Link>
          <Link to="/publication-pipeline">Publication Pipeline</Link>
          <Link to="/narrative-builder">Narrative Builder</Link>
          <Link to="/publication-desk">Publication Desk</Link>
        </nav>
      </header>

      <PublicationChecklistPanel items={checklist} />

      <div style={{ ...panelStyle, marginTop: 20 }}>
        <h3 style={{ marginTop: 0 }}>Pipeline Stages</h3>
        <ol style={{ marginBottom: 0, paddingLeft: 20 }}>
          <li>Review complete</li>
          <li>Contradiction resolution complete</li>
          <li>Decision logged</li>
          <li>Narrative drafted</li>
          <li>Publication desk handoff</li>
        </ol>
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

Write-Utf8File -Path $evidenceTracePath -Content @'
import { Link } from "react-router-dom";
import { useStatements } from "../hooks/useStatements";
import { useClaims } from "../hooks/useClaims";
import { EvidenceTracePanel } from "../components/EvidenceTracePanel";

export function EvidenceTracePage() {
  const statementsQuery = useStatements();
  const claimsQuery = useClaims();

  const statementNodes = (statementsQuery.data?.items ?? []).slice(0, 4).map((item) => ({
    id: item.id,
    label: "Statement",
    detail: item.text,
  }));

  const claimNodes = (claimsQuery.data?.items ?? []).slice(0, 4).map((item) => ({
    id: item.id,
    label: "Claim",
    detail: item.topic,
  }));

  const nodes = [...statementNodes, ...claimNodes];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Evidence Trace</h1>
        <p style={{ color: "#555" }}>
          Trace surface from extracted statements into claims and downstream contradiction work.
        </p>

        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/evidence">Evidence</Link>
          <Link to="/statements">Statements</Link>
          <Link to="/claims">Claims</Link>
          <Link to="/evidence-trace">Evidence Trace</Link>
        </nav>
      </header>

      <EvidenceTracePanel nodes={nodes} />
    </div>
  );
}
'@

Write-Utf8File -Path $narrativeBuilderPath -Content @'
import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";
import { useContradictions } from "../hooks/useContradictions";

export function NarrativeBuilderPage() {
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  const claims = claimsQuery.data?.items ?? [];
  const contradictions = contradictionsQuery.data?.items ?? [];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Narrative Builder</h1>
        <p style={{ color: "#555" }}>
          Structured drafting surface for building publication-ready narratives from reviewed items.
        </p>

        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/publication-pipeline">Publication Pipeline</Link>
          <Link to="/narrative-builder">Narrative Builder</Link>
          <Link to="/decision-log">Decision Log</Link>
        </nav>
      </header>

      <div style={panelStyle}>
        <h3 style={{ marginTop: 0 }}>Narrative Inputs</h3>
        <ul style={{ marginBottom: 0 }}>
          <li>Claims available: {claims.length}</li>
          <li>Contradictions available: {contradictions.length}</li>
          <li>Decision log available: yes</li>
        </ul>
      </div>

      <div style={{ ...panelStyle, marginTop: 20 }}>
        <h3 style={{ marginTop: 0 }}>Draft Structure</h3>
        <ol style={{ marginBottom: 0, paddingLeft: 20 }}>
          <li>Opening claim context</li>
          <li>Evidence trace and supporting statements</li>
          <li>Contradiction analysis</li>
          <li>Decision rationale</li>
          <li>Publication-ready summary</li>
        </ol>
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

$mainContent = Get-Content $mainPath -Raw

$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { DashboardPage } from "./pages/DashboardPage";' -ImportLine 'import { ReviewDecisionBoardPage } from "./pages/ReviewDecisionBoardPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { ReviewDecisionBoardPage } from "./pages/ReviewDecisionBoardPage";' -ImportLine 'import { PublicationPipelinePage } from "./pages/PublicationPipelinePage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { PublicationPipelinePage } from "./pages/PublicationPipelinePage";' -ImportLine 'import { EvidenceTracePage } from "./pages/EvidenceTracePage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { EvidenceTracePage } from "./pages/EvidenceTracePage";' -ImportLine 'import { NarrativeBuilderPage } from "./pages/NarrativeBuilderPage";'

$mainContent = Ensure-NavBlock -Content $mainContent -Anchor '<Link to="/truth-review-studio">Truth Review Studio</Link>' -NavBlock '<Link to="/review-decision-board">Review Decision Board</Link>
          <Link to="/publication-pipeline">Publication Pipeline</Link>
          <Link to="/evidence-trace">Evidence Trace</Link>
          <Link to="/narrative-builder">Narrative Builder</Link>' -PresencePattern 'to="/review-decision-board"'

$mainContent = Ensure-RouteBlock -Content $mainContent -AnchorRoute '{ path: "/truth-review-studio", element: <TruthReviewStudioPage /> },' -RouteBlock '{ path: "/review-decision-board", element: <ReviewDecisionBoardPage /> },
  { path: "/publication-pipeline", element: <PublicationPipelinePage /> },
  { path: "/evidence-trace", element: <EvidenceTracePage /> },
  { path: "/narrative-builder", element: <NarrativeBuilderPage /> },' -PresencePattern 'path: "/review-decision-board"'

Write-Utf8File -Path $mainPath -Content $mainContent

Write-Host "Building backend..."
Build-Backend -RootDir $RootDir

Write-Host "Building frontend..."
Build-Frontend -RootDir $RootDir

Write-Host "Phase 6.24 completed successfully." -ForegroundColor Green
