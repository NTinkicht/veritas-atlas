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

function Ensure-NavLinks {
    param([Parameter(Mandatory = $true)][string]$Content)

    if ($Content -notmatch 'to="/review-queue"') {
        $Content = $Content -replace '<Link to="/reviews">Reviews</Link>', '<Link to="/reviews">Reviews</Link>
          <Link to="/review-queue">Review Queue</Link>
          <Link to="/review-workspace">Review Workspace</Link>
          <Link to="/publication-desk">Publication Desk</Link>'
    }

    return $Content
}

function Ensure-Routes {
    param([Parameter(Mandatory = $true)][string]$Content)

    $patterns = @(
        'path: "/review-queue"',
        'path: "/review-workspace"',
        'path: "/publication-desk"'
    )

    $allExist = $true
    foreach ($pattern in $patterns) {
        if ($Content -notmatch $pattern) {
            $allExist = $false
        }
    }

    if (-not $allExist) {
        $Content = $Content -replace '\{ path: "/reviews", element: <ReviewsPage /> \},', '{ path: "/reviews", element: <ReviewsPage /> },
  { path: "/review-queue", element: <ReviewQueuePage /> },
  { path: "/review-workspace", element: <ReviewWorkspacePage /> },
  { path: "/publication-desk", element: <PublicationDeskPage /> },'
    }

    return $Content
}

Write-Host "Checkpointing current code with git..."
Git-Checkpoint -Message ("checkpoint before phase 6.17 - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying Phase 6.17 - Large Scope - Review and Publication Operations Pack..."

$webRoot = Join-Path $RootDir "apps\web\veritas-atlas-web\src"

$reviewQueuePath = Join-Path $webRoot "pages\ReviewQueuePage.tsx"
$reviewWorkspacePath = Join-Path $webRoot "pages\ReviewWorkspacePage.tsx"
$publicationDeskPath = Join-Path $webRoot "pages\PublicationDeskPage.tsx"
$mainPath = Join-Path $webRoot "main.tsx"

Write-Utf8File -Path $reviewQueuePath -Content @'
import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";

export function ReviewQueuePage() {
  const claimsQuery = useClaims();

  const queueItems = (claimsQuery.data?.items ?? []).slice(0, 12);

  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Review Queue</h1>
      <p>Operational queue for human review across claims and contradiction preparation.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/reviews">Reviews</Link>
        <Link to="/review-workspace">Review Workspace</Link>
        <Link to="/publication-desk">Publication Desk</Link>
        <Link to="/claims">Claims</Link>
      </div>

      <section style={panelStyle}>
        <h2 style={{ marginTop: 0 }}>Queue summary</h2>
        <div style={summaryGridStyle}>
          <SummaryCard title="Claims available" value={claimsQuery.data?.items.length ?? 0} />
          <SummaryCard title="Ready for review" value={queueItems.length} />
          <SummaryCard title="Escalation candidates" value={Math.min(queueItems.length, 3)} />
        </div>
      </section>

      <section style={panelStyle}>
        <h2 style={{ marginTop: 0 }}>Current queue</h2>
        {claimsQuery.isLoading && <p>Loading queue...</p>}
        {claimsQuery.isError && <p style={{ color: "crimson" }}>Failed to load queue.</p>}
        {claimsQuery.isSuccess && queueItems.length === 0 && <p>No review items yet.</p>}
        {claimsQuery.isSuccess && queueItems.length > 0 && (
          <ul style={{ marginBottom: 0 }}>
            {queueItems.map((item) => (
              <li key={item.id}>
                <Link to={`/claims/${item.id}`}>{item.topic}</Link> - {item.type} - {item.status}
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}

function SummaryCard({ title, value }: { title: string; value: number }) {
  return (
    <div style={summaryCardStyle}>
      <span style={{ color: "#666" }}>{title}</span>
      <strong style={{ fontSize: 28 }}>{value}</strong>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  marginBottom: 20,
};

const summaryGridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))",
  gap: 16,
};

const summaryCardStyle: React.CSSProperties = {
  border: "1px solid #eee",
  borderRadius: 12,
  padding: 16,
  display: "grid",
  gap: 8,
};
'@

Write-Utf8File -Path $reviewWorkspacePath -Content @'
import { Link, useSearchParams } from "react-router-dom";
import { useClaimDetail } from "../hooks/useClaimDetail";

export function ReviewWorkspacePage() {
  const [params] = useSearchParams();
  const claimId = params.get("claimId") ?? "";
  const claimQuery = useClaimDetail(claimId || undefined);

  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif", maxWidth: 980 }}>
      <h1>Review Workspace</h1>
      <p>Focused review surface for claim validation, escalation, and publication readiness.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/review-queue">Review Queue</Link>
        <Link to="/publication-desk">Publication Desk</Link>
        {claimId && <Link to={`/claims/${claimId}`}>Claim</Link>}
      </div>

      <div style={panelStyle}>
        <Row label="Claim Id" value={claimId || "N/A"} />
        <Row label="Workspace Status" value="Ready for structured human review" />
      </div>

      {claimId && claimQuery.isLoading && <p>Loading claim context...</p>}
      {claimId && claimQuery.isError && <p style={{ color: "crimson" }}>Failed to load claim context.</p>}

      {claimId && claimQuery.isSuccess && claimQuery.data && (
        <>
          <div style={panelStyle}>
            <h2 style={{ marginTop: 0 }}>Claim Context</h2>
            <Row label="Topic" value={claimQuery.data.topic} />
            <Row label="Type" value={claimQuery.data.type} />
            <Row label="Status" value={claimQuery.data.status} />
            <Row label="Material" value={claimQuery.data.isMaterial ? "Yes" : "No"} />
            <Row label="Normalized Text" value={claimQuery.data.normalizedText} />
          </div>

          <div style={panelStyle}>
            <h2 style={{ marginTop: 0 }}>Review actions</h2>
            <ul style={{ marginBottom: 0 }}>
              <li>Validate claim formulation</li>
              <li>Check evidence alignment</li>
              <li>Escalate to contradiction comparison</li>
              <li>Mark ready for publication flow</li>
            </ul>
          </div>
        </>
      )}
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "180px 1fr", gap: 12, padding: "6px 0" }}>
      <strong>{label}</strong>
      <span>{value}</span>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  marginBottom: 20,
};
'@

Write-Utf8File -Path $publicationDeskPath -Content @'
import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";

export function PublicationDeskPage() {
  const claimsQuery = useClaims();
  const publicationCandidates = (claimsQuery.data?.items ?? []).slice(0, 8);

  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Publication Desk</h1>
      <p>Operational surface for publication-ready outputs and final publication checks.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/review-queue">Review Queue</Link>
        <Link to="/review-workspace">Review Workspace</Link>
        <Link to="/claims">Claims</Link>
      </div>

      <section style={panelStyle}>
        <h2 style={{ marginTop: 0 }}>Publication readiness</h2>
        <ul style={{ marginBottom: 0 }}>
          <li>Claim quality and wording review</li>
          <li>Evidence alignment check</li>
          <li>Contradiction notes attached</li>
          <li>Publication routing placeholder</li>
        </ul>
      </section>

      <section style={panelStyle}>
        <h2 style={{ marginTop: 0 }}>Candidate items</h2>
        {claimsQuery.isLoading && <p>Loading publication candidates...</p>}
        {claimsQuery.isError && <p style={{ color: "crimson" }}>Failed to load publication candidates.</p>}
        {claimsQuery.isSuccess && publicationCandidates.length === 0 && <p>No publication candidates yet.</p>}
        {claimsQuery.isSuccess && publicationCandidates.length > 0 && (
          <ul style={{ marginBottom: 0 }}>
            {publicationCandidates.map((item) => (
              <li key={item.id}>
                <Link to={`/claims/${item.id}`}>{item.topic}</Link> - {item.type} - {item.status}
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  marginBottom: 20,
};
'@

$mainContent = Get-Content $mainPath -Raw

$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { ReviewsPage } from "./pages/ReviewsPage";' -ImportLine 'import { ReviewQueuePage } from "./pages/ReviewQueuePage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { ReviewQueuePage } from "./pages/ReviewQueuePage";' -ImportLine 'import { ReviewWorkspacePage } from "./pages/ReviewWorkspacePage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { ReviewWorkspacePage } from "./pages/ReviewWorkspacePage";' -ImportLine 'import { PublicationDeskPage } from "./pages/PublicationDeskPage";'

$mainContent = Ensure-NavLinks -Content $mainContent
$mainContent = Ensure-Routes -Content $mainContent

Write-Utf8File -Path $mainPath -Content $mainContent

Write-Host "Building backend..."
Build-Backend -RootDir $RootDir

Write-Host "Building frontend..."
Build-Frontend -RootDir $RootDir

Write-Host "Phase 6.17 applied successfully."
