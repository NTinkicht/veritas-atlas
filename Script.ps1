param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir
)

$ErrorActionPreference = "Stop"

function Ensure-Directory {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Write-Utf8File {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $dir = Split-Path -Parent $Path
    Ensure-Directory -Path $dir

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
        }
    }
    finally { Pop-Location }
}

function Build-All {
    Push-Location $RootDir
    try {
        dotnet build
        if ($LASTEXITCODE -ne 0) { throw "Backend failed" }
    }
    finally { Pop-Location }

    $frontend = Join-Path $RootDir "apps\web\veritas-atlas-web"
    Push-Location $frontend
    try {
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "Frontend failed" }
    }
    finally { Pop-Location }
}

Write-Host "Checkpoint..."
Git-Checkpoint -Message ("checkpoint phase 6.25 " + (Get-Date))

$web = Join-Path $RootDir "apps\web\veritas-atlas-web\src"

# GLOBAL SEARCH PAGE
Write-Utf8File -Path (Join-Path $web "pages\GlobalSearchPage.tsx") -Content @'
import { useState } from "react";

export function GlobalSearchPage() {
  const [query, setQuery] = useState("");

  return (
    <div style={{ padding: 24 }}>
      <h1>Global Search</h1>

      <input
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        placeholder="Search claims, contradictions, evidence..."
        style={{ width: "100%", padding: 10, marginBottom: 20 }}
      />

      <div>
        <p>Search query: {query}</p>
        <p>Results coming from unified index (future backend integration)</p>
      </div>
    </div>
  );
}
'@

# ACTIVITY TIMELINE
Write-Utf8File -Path (Join-Path $web "components\ActivityTimelinePanel.tsx") -Content @'
export function ActivityTimelinePanel({
  items,
}: {
  items: { id: string; text: string }[];
}) {
  return (
    <div style={{ border: "1px solid #ddd", padding: 16, borderRadius: 12 }}>
      <h3>Activity Timeline</h3>
      <ul>
        {items.map((i) => (
          <li key={i.id}>{i.text}</li>
        ))}
      </ul>
    </div>
  );
}
'@

# OBSERVABILITY DASHBOARD
Write-Utf8File -Path (Join-Path $web "pages\ObservabilityDashboardPage.tsx") -Content @'
import { ActivityTimelinePanel } from "../components/ActivityTimelinePanel";

export function ObservabilityDashboardPage() {
  const activity = [
    { id: "1", text: "Claim created" },
    { id: "2", text: "Contradiction detected" },
    { id: "3", text: "Review submitted" },
  ];

  return (
    <div style={{ padding: 24 }}>
      <h1>Observability Dashboard</h1>
      <ActivityTimelinePanel items={activity} />
    </div>
  );
}
'@

# ADMIN CONTROL PAGE
Write-Utf8File -Path (Join-Path $web "pages\AdminControlTowerPage.tsx") -Content @'
export function AdminControlTowerPage() {
  return (
    <div style={{ padding: 24 }}>
      <h1>Admin Control Tower</h1>

      <ul>
        <li>System health: OK</li>
        <li>Agent runs: active</li>
        <li>Pending reviews: 12</li>
      </ul>
    </div>
  );
}
'@

# ROUTER PATCH
$main = Join-Path $web "main.tsx"
$content = Get-Content $main -Raw

if ($content -notmatch "GlobalSearchPage") {
    $content = $content -replace 'import .*DashboardPage.*', '$0
import { GlobalSearchPage } from "./pages/GlobalSearchPage";
import { ObservabilityDashboardPage } from "./pages/ObservabilityDashboardPage";
import { AdminControlTowerPage } from "./pages/AdminControlTowerPage";'
}

if ($content -notmatch "/global-search") {
    $content = $content -replace 'createBrowserRouter\(\[', 'createBrowserRouter([
  { path: "/global-search", element: <GlobalSearchPage /> },
  { path: "/observability", element: <ObservabilityDashboardPage /> },
  { path: "/admin", element: <AdminControlTowerPage /> },'
}

Write-Utf8File -Path $main -Content $content

Write-Host "Building..."
Build-All

Write-Host "Phase 6.25 DONE" -ForegroundColor Green
