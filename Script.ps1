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
Git-Checkpoint -Message ("checkpoint before phase 6.29 - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying Phase 6.29 - Evidence-to-Decision Flow and Workspace Coordination pack..." -ForegroundColor Cyan

$web = Join-Path $RootDir "apps\web\veritas-atlas-web\src"

Write-File (Join-Path $web "components\FlowHealthPanel.tsx") @'
export function FlowHealthPanel({
  evidenceCount,
  statementCount,
  claimCount,
  contradictionCount,
}: {
  evidenceCount: number;
  statementCount: number;
  claimCount: number;
  contradictionCount: number;
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Flow Health</h3>
      <ul style={{ marginBottom: 0 }}>
        <li>Evidence items: {evidenceCount}</li>
        <li>Statements: {statementCount}</li>
        <li>Claims: {claimCount}</li>
        <li>Contradictions: {contradictionCount}</li>
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

Write-File (Join-Path $web "components\WorkspaceCoordinationPanel.tsx") @'
import { Link } from "react-router-dom";

export function WorkspaceCoordinationPanel() {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Workspace Coordination</h3>
      <div style={{ display: "flex", gap: 12, flexWrap: "wrap" }}>
        <Link to="/evidence-flow-studio">Evidence Flow Studio</Link>
        <Link to="/truth-review-studio">Truth Review Studio</Link>
        <Link to="/publication-pipeline">Publication Pipeline</Link>
        <Link to="/decision-intelligence">Decision Intelligence</Link>
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

Write-File (Join-Path $web "pages\EvidenceFlowStudioPage.tsx") @'
import { Link } from "react-router-dom";
import { useEvidenceList } from "../hooks/useEvidenceList";
import { useStatements } from "../hooks/useStatements";
import { useClaims } from "../hooks/useClaims";
import { useContradictions } from "../hooks/useContradictions";
import { FlowHealthPanel } from "../components/FlowHealthPanel";
import { WorkspaceCoordinationPanel } from "../components/WorkspaceCoordinationPanel";

export function EvidenceFlowStudioPage() {
  const evidenceQuery = useEvidenceList();
  const statementsQuery = useStatements();
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  const evidenceCount = evidenceQuery.data?.items.length ?? 0;
  const statementCount = statementsQuery.data?.items.length ?? 0;
  const claimCount = claimsQuery.data?.items.length ?? 0;
  const contradictionCount = contradictionsQuery.data?.items.length ?? 0;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Evidence Flow Studio</h1>
        <p style={{ color: "#555" }}>
          End-to-end operational view from evidence intake to contradiction handling and decision readiness.
        </p>

        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/evidence-flow-studio">Evidence Flow Studio</Link>
          <Link to="/evidence-trace">Evidence Trace</Link>
          <Link to="/decision-intelligence">Decision Intelligence</Link>
        </nav>
      </header>

      <div style={gridStyle}>
        <FlowHealthPanel
          evidenceCount={evidenceCount}
          statementCount={statementCount}
          claimCount={claimCount}
          contradictionCount={contradictionCount}
        />
        <WorkspaceCoordinationPanel />
      </div>

      <div style={{ ...panelStyle, marginTop: 20 }}>
        <h3 style={{ marginTop: 0 }}>Flow Summary</h3>
        <ol style={{ marginBottom: 0, paddingLeft: 20 }}>
          <li>Evidence intake and trace verification</li>
          <li>Statement extraction and normalization</li>
          <li>Claim formulation and review</li>
          <li>Contradiction creation and resolution</li>
          <li>Decision and publication routing</li>
        </ol>
      </div>
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "1fr 1fr",
  gap: 16,
};

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
'@

Write-File (Join-Path $web "pages\OperationalHandoffPage.tsx") @'
import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";
import { useContradictions } from "../hooks/useContradictions";

export function OperationalHandoffPage() {
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  const claims = claimsQuery.data?.items ?? [];
  const contradictions = contradictionsQuery.data?.items ?? [];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Operational Handoff</h1>
        <p style={{ color: "#555" }}>
          Cross-workspace handoff view between review, contradiction resolution, and publication preparation.
        </p>

        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/operational-handoff">Operational Handoff</Link>
          <Link to="/review-decision-board">Review Decision Board</Link>
          <Link to="/publication-pipeline">Publication Pipeline</Link>
        </nav>
      </header>

      <div style={gridStyle}>
        <div style={panelStyle}>
          <h3 style={{ marginTop: 0 }}>Claims Ready For Review</h3>
          {claims.length === 0 && <p>No claims.</p>}
          {claims.length > 0 && (
            <ul style={{ marginBottom: 0 }}>
              {claims.slice(0, 8).map((item) => (
                <li key={item.id}>
                  <Link to={`/claims/${item.id}`}>{item.topic}</Link> - {item.status}
                </li>
              ))}
            </ul>
          )}
        </div>

        <div style={panelStyle}>
          <h3 style={{ marginTop: 0 }}>Contradictions Ready For Resolution</h3>
          {contradictions.length === 0 && <p>No contradictions.</p>}
          {contradictions.length > 0 && (
            <ul style={{ marginBottom: 0 }}>
              {contradictions.slice(0, 8).map((item) => (
                <li key={item.id}>
                  <Link to={`/contradiction-resolution/${item.id}`}>{item.topic}</Link> - {item.status}
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "1fr 1fr",
  gap: 16,
};

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
'@

Write-File (Join-Path $web "pages\DecisionQueuePage.tsx") @'
import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";

export function DecisionQueuePage() {
  const claimsQuery = useClaims();
  const claims = claimsQuery.data?.items ?? [];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Decision Queue</h1>
        <p style={{ color: "#555" }}>
          Queue view for items approaching final decision and publication readiness.
        </p>

        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/decision-queue">Decision Queue</Link>
          <Link to="/decision-intelligence">Decision Intelligence</Link>
          <Link to="/publication-pipeline">Publication Pipeline</Link>
        </nav>
      </header>

      <div style={panelStyle}>
        <h3 style={{ marginTop: 0 }}>Queued Items</h3>
        {claims.length === 0 && <p>No queued items.</p>}
        {claims.length > 0 && (
          <ul style={{ marginBottom: 0 }}>
            {claims.slice(0, 12).map((item) => (
              <li key={item.id}>
                <Link to={`/claims/${item.id}`}>{item.topic}</Link> - {item.status}
              </li>
            ))}
          </ul>
        )}
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

$main = Join-Path $web "main.tsx"
$content = Get-Content $main -Raw

$content = Ensure-ImportLine -Content $content -Anchor 'import { DashboardPage } from "./pages/DashboardPage";' -ImportLine 'import { EvidenceFlowStudioPage } from "./pages/EvidenceFlowStudioPage";'
$content = Ensure-ImportLine -Content $content -Anchor 'import { EvidenceFlowStudioPage } from "./pages/EvidenceFlowStudioPage";' -ImportLine 'import { OperationalHandoffPage } from "./pages/OperationalHandoffPage";'
$content = Ensure-ImportLine -Content $content -Anchor 'import { OperationalHandoffPage } from "./pages/OperationalHandoffPage";' -ImportLine 'import { DecisionQueuePage } from "./pages/DecisionQueuePage";'

$content = Ensure-NavBlock -Content $content -Anchor '<Link to="/decision-intelligence">Decision Intelligence</Link>' -NavBlock '<Link to="/evidence-flow-studio">Evidence Flow Studio</Link>
          <Link to="/operational-handoff">Operational Handoff</Link>
          <Link to="/decision-queue">Decision Queue</Link>' -PresencePattern 'to="/evidence-flow-studio"'

$content = Ensure-RouteBlock -Content $content -AnchorRoute '{ path: "/decision-intelligence", element: <DecisionIntelligencePage /> },' -RouteBlock '{ path: "/evidence-flow-studio", element: <EvidenceFlowStudioPage /> },
  { path: "/operational-handoff", element: <OperationalHandoffPage /> },
  { path: "/decision-queue", element: <DecisionQueuePage /> },' -PresencePattern 'path: "/evidence-flow-studio"'

Write-File $main $content

Write-Host "Building..." -ForegroundColor Cyan
Build-All -RootDir $RootDir

Write-Host "Phase 6.29 DONE" -ForegroundColor Green
