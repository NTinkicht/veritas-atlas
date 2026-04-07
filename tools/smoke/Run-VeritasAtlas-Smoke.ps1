param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir,
    [string]$BaseUrl = "http://localhost:5209"
)

$ErrorActionPreference = "Stop"

function Ensure-Dir {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Append-Line {
    param(
        [string]$Path,
        [string]$Text
    )
    Add-Content -Path $Path -Value $Text
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$diag = Join-Path $RootDir "_diagnostics\smoke-$timestamp"
Ensure-Dir $diag
$report = Join-Path $diag "smoke-report.md"
$apiLog = Join-Path $diag "api.log"

Set-Content -Path $report -Value "# Veritas Atlas Smoke Report`r`n" -Encoding UTF8
Append-Line -Path $report -Text ("Generated: " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
Append-Line -Path $report -Text ""

Push-Location (Join-Path $RootDir "apps\api\VeritasAtlas.Api")
$apiProcess = $null
try {
    $apiProcess = Start-Process "dotnet" -ArgumentList "run" -RedirectStandardOutput $apiLog -RedirectStandardError $apiLog -PassThru
    Start-Sleep -Seconds 8

    $checks = @(
        @{ Name = "Health"; Url = "$BaseUrl/health" },
        @{ Name = "Database Health"; Url = "$BaseUrl/health/db" },
        @{ Name = "Cases"; Url = "$BaseUrl/api/v1/cases?page=1&pageSize=5" },
        @{ Name = "Sources"; Url = "$BaseUrl/api/v1/sources?page=1&pageSize=5" },
        @{ Name = "Documents"; Url = "$BaseUrl/api/v1/documents?page=1&pageSize=5" },
        @{ Name = "Evidence"; Url = "$BaseUrl/api/v1/evidence?page=1&pageSize=5" },
        @{ Name = "Statements"; Url = "$BaseUrl/api/v1/statements?page=1&pageSize=5" },
        @{ Name = "Claims"; Url = "$BaseUrl/api/v1/claims?page=1&pageSize=5" },
        @{ Name = "Contradictions"; Url = "$BaseUrl/api/v1/contradictions?page=1&pageSize=5" }
    )

    foreach ($check in $checks) {
        try {
            $response = Invoke-WebRequest -Uri $check.Url -Method Get -UseBasicParsing -TimeoutSec 15
            Append-Line -Path $report -Text ("## " + $check.Name)
            Append-Line -Path $report -Text ("- StatusCode: " + $response.StatusCode)
            Append-Line -Path $report -Text ("- Url: " + $check.Url)
            Append-Line -Path $report -Text ""
        }
        catch {
            Append-Line -Path $report -Text ("## " + $check.Name)
            Append-Line -Path $report -Text ("- FAILED: " + $_.Exception.Message)
            Append-Line -Path $report -Text ("- Url: " + $check.Url)
            Append-Line -Path $report -Text ""
        }
    }

    Append-Line -Path $report -Text "## API log"
    Append-Line -Path $report -Text ""
    if (Test-Path $apiLog) {
        Append-Line -Path $report -Text '```'
        Append-Line -Path $report -Text (Get-Content $apiLog -Raw)
        Append-Line -Path $report -Text '```'
    }
}
finally {
    Pop-Location
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force
    }
}