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

function Remove-DuplicateLines {
    param([Parameter(Mandatory = $true)][string]$Content)

    $lines = $Content -split "`r?`n"
    $seen = New-Object System.Collections.Generic.HashSet[string]
    $result = New-Object System.Collections.Generic.List[string]

    foreach ($line in $lines) {
        $trimmed = $line.Trim()
        if ($trimmed.StartsWith("import {") -or $trimmed.StartsWith('{ path: "/')) {
            if (-not $seen.Add($trimmed)) {
                continue
            }
        }
        $result.Add($line)
    }

    return [string]::Join([Environment]::NewLine, $result)
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

function Get-StatusClass {
    param([string]$Text)

    if ($Text -match 'placeholder|ready for|future|planned|monitor|board|console|control|summary|status|timeline|radar|overview') {
        return "Operational shell / placeholder-heavy"
    }

    if ($Text -match 'create|list|detail|workspace|browse|claims|statements|evidence|documents|sources') {
        return "Likely data-driven operational page"
    }

    return "Unknown / inspect manually"
}

Write-Host "Checkpointing current code with git..."
Git-Checkpoint -Message ("checkpoint before stabilization audit - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying stabilization and audit phase..."

$webRoot = Join-Path $RootDir "apps\web\veritas-atlas-web\src"
$mainPath = Join-Path $webRoot "main.tsx"
$pagesDir = Join-Path $webRoot "pages"

$mainContent = Get-Content $mainPath -Raw

$mainContent = Remove-DuplicateLines -Content $mainContent

$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { DashboardPage } from "./pages/DashboardPage";' -ImportLine 'import { OperationsIntelligencePage } from "./pages/OperationsIntelligencePage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { OperationsIntelligencePage } from "./pages/OperationsIntelligencePage";' -ImportLine 'import { InvestigationNavigatorPage } from "./pages/InvestigationNavigatorPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { InvestigationNavigatorPage } from "./pages/InvestigationNavigatorPage";' -ImportLine 'import { GovernanceConsolePage } from "./pages/GovernanceConsolePage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { GovernanceConsolePage } from "./pages/GovernanceConsolePage";' -ImportLine 'import { PublicationReadinessBoardPage } from "./pages/PublicationReadinessBoardPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { PublicationReadinessBoardPage } from "./pages/PublicationReadinessBoardPage";' -ImportLine 'import { DecisionLogPage } from "./pages/DecisionLogPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { DecisionLogPage } from "./pages/DecisionLogPage";' -ImportLine 'import { ExecutiveOverviewPage } from "./pages/ExecutiveOverviewPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { ExecutiveOverviewPage } from "./pages/ExecutiveOverviewPage";' -ImportLine 'import { DeliveryControlTowerPage } from "./pages/DeliveryControlTowerPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { DeliveryControlTowerPage } from "./pages/DeliveryControlTowerPage";' -ImportLine 'import { WorkstreamBoardPage } from "./pages/WorkstreamBoardPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { WorkstreamBoardPage } from "./pages/WorkstreamBoardPage";' -ImportLine 'import { EscalationCenterPage } from "./pages/EscalationCenterPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { EscalationCenterPage } from "./pages/EscalationCenterPage";' -ImportLine 'import { AnalyticsCenterPage } from "./pages/AnalyticsCenterPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { AnalyticsCenterPage } from "./pages/AnalyticsCenterPage";' -ImportLine 'import { AgentRunsBoardPage } from "./pages/AgentRunsBoardPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { AgentRunsBoardPage } from "./pages/AgentRunsBoardPage";' -ImportLine 'import { CaseFlowMapPage } from "./pages/CaseFlowMapPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { CaseFlowMapPage } from "./pages/CaseFlowMapPage";' -ImportLine 'import { QualityRadarPage } from "./pages/QualityRadarPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { QualityRadarPage } from "./pages/QualityRadarPage";' -ImportLine 'import { ReviewQueuePage } from "./pages/ReviewQueuePage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { ReviewQueuePage } from "./pages/ReviewQueuePage";' -ImportLine 'import { ReviewWorkspacePage } from "./pages/ReviewWorkspacePage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { ReviewWorkspacePage } from "./pages/ReviewWorkspacePage";' -ImportLine 'import { PublicationDeskPage } from "./pages/PublicationDeskPage";'

$mainContent = Ensure-NavBlock -Content $mainContent -Anchor '<Link to="/dashboard">Dashboard</Link>' -NavBlock '<Link to="/operations-intelligence">Operations Intelligence</Link>
          <Link to="/investigation-navigator">Investigation Navigator</Link>
          <Link to="/analytics-center">Analytics Center</Link>
          <Link to="/executive-overview">Executive Overview</Link>' -PresencePattern 'to="/operations-intelligence"'

$mainContent = Ensure-NavBlock -Content $mainContent -Anchor '<Link to="/reviews">Reviews</Link>' -NavBlock '<Link to="/review-queue">Review Queue</Link>
          <Link to="/review-workspace">Review Workspace</Link>
          <Link to="/publication-desk">Publication Desk</Link>
          <Link to="/governance-console">Governance Console</Link>
          <Link to="/publication-readiness">Publication Readiness</Link>
          <Link to="/decision-log">Decision Log</Link>' -PresencePattern 'to="/review-queue"'

$mainContent = Ensure-NavBlock -Content $mainContent -Anchor '<Link to="/claims/workspace">Claims Workspace</Link>' -NavBlock '<Link to="/contradictions/workspace">Contradictions Workspace</Link>
          <Link to="/delivery-control-tower">Delivery Control Tower</Link>
          <Link to="/workstream-board">Workstream Board</Link>
          <Link to="/escalation-center">Escalation Center</Link>
          <Link to="/agent-runs-board">Agent Runs Board</Link>
          <Link to="/case-flow-map">Case Flow Map</Link>
          <Link to="/quality-radar">Quality Radar</Link>' -PresencePattern 'to="/delivery-control-tower"'

$mainContent = Ensure-RouteBlock -Content $mainContent -AnchorRoute '{ path: "/dashboard", element: <DashboardPage /> },' -RouteBlock '{ path: "/operations-intelligence", element: <OperationsIntelligencePage /> },
  { path: "/investigation-navigator", element: <InvestigationNavigatorPage /> },
  { path: "/analytics-center", element: <AnalyticsCenterPage /> },
  { path: "/executive-overview", element: <ExecutiveOverviewPage /> },' -PresencePattern 'path: "/operations-intelligence"'

$mainContent = Ensure-RouteBlock -Content $mainContent -AnchorRoute '{ path: "/reviews", element: <ReviewsPage /> },' -RouteBlock '{ path: "/review-queue", element: <ReviewQueuePage /> },
  { path: "/review-workspace", element: <ReviewWorkspacePage /> },
  { path: "/publication-desk", element: <PublicationDeskPage /> },
  { path: "/governance-console", element: <GovernanceConsolePage /> },
  { path: "/publication-readiness", element: <PublicationReadinessBoardPage /> },
  { path: "/decision-log", element: <DecisionLogPage /> },' -PresencePattern 'path: "/review-queue"'

$mainContent = Ensure-RouteBlock -Content $mainContent -AnchorRoute '{ path: "/claims/workspace", element: <ClaimsWorkspacePage /> },' -RouteBlock '{ path: "/contradictions/workspace", element: <ContradictionsWorkspacePage /> },
  { path: "/delivery-control-tower", element: <DeliveryControlTowerPage /> },
  { path: "/workstream-board", element: <WorkstreamBoardPage /> },
  { path: "/escalation-center", element: <EscalationCenterPage /> },
  { path: "/agent-runs-board", element: <AgentRunsBoardPage /> },
  { path: "/case-flow-map", element: <CaseFlowMapPage /> },
  { path: "/quality-radar", element: <QualityRadarPage /> },' -PresencePattern 'path: "/delivery-control-tower"'

Write-Utf8File -Path $mainPath -Content $mainContent

Write-Host "Building backend..."
Build-Backend -RootDir $RootDir

Write-Host "Building frontend..."
Build-Frontend -RootDir $RootDir

Write-Host "Generating implementation audit report..."

$diagnosticsDir = Join-Path $RootDir "_diagnostics"
Ensure-Directory -Path $diagnosticsDir
$auditPath = Join-Path $diagnosticsDir ("stabilization-audit-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".md")

$pageFiles = Get-ChildItem -Path $pagesDir -Filter *.tsx | Sort-Object Name

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# Veritas Atlas Stabilization Audit")
$lines.Add("")
$lines.Add("Generated: " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
$lines.Add("")
$lines.Add("## Build status")
$lines.Add("")
$lines.Add("- Backend build: passed")
$lines.Add("- Frontend build: passed")
$lines.Add("")
$lines.Add("## Page inventory")
$lines.Add("")

foreach ($file in $pageFiles) {
    $content = Get-Content $file.FullName -Raw
    $statusClass = Get-StatusClass -Text $content

    $signals = @()
    if ($content -match 'useClaims|useStatements|useEvidenceList|useSources|useDocuments|useClaimDetail|useStatementDetail|useCaseDetail') {
        $signals += "uses data hooks"
    }
    if ($content -match 'placeholder') {
        $signals += "contains placeholder text"
    }
    if ($content -match 'Workspace') {
        $signals += "workspace surface"
    }
    if ($content -match 'Board|Console|Overview|Center|Tower|Radar|Map|Desk|Queue|Log') {
        $signals += "control or oversight surface"
    }

    if ($signals.Count -eq 0) {
        $signals += "no strong heuristic signals"
    }

    $lines.Add("### " + $file.Name)
    $lines.Add("- Classification: " + $statusClass)
    $lines.Add("- Signals: " + ($signals -join ", "))
    $lines.Add("")
}

$lines.Add("## Summary")
$lines.Add("")
$lines.Add("- Core data-driven slices are present around sources, documents, evidence, statements, claims, and related workspaces.")
$lines.Add("- Many later-stage pages are operational surfaces or governance shells and should be treated as partially complete until deeper backend wiring is added.")
$lines.Add("- Router and navigation were normalized in this stabilization pass.")
$lines.Add("")

Write-Utf8File -Path $auditPath -Content ([string]::Join([Environment]::NewLine, $lines))

Write-Host "Stabilization and audit phase completed successfully."
Write-Host "Audit report: $auditPath"
