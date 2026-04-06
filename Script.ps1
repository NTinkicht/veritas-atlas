param(
    [string]$RootDir = "C:\Projects\veritas-atlas"
)

$ErrorActionPreference = "Stop"

function Ensure-Directory {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Force -Path $Path | Out-Null
    }
}

function Add-Section {
    param(
        [string]$OutputPath,
        [string]$Title,
        [string]$Content
    )

    Add-Content -Path $OutputPath -Value ""
    Add-Content -Path $OutputPath -Value ("=" * 120)
    Add-Content -Path $OutputPath -Value $Title
    Add-Content -Path $OutputPath -Value ("=" * 120)
    Add-Content -Path $OutputPath -Value $Content
}

function Add-FileSection {
    param(
        [string]$OutputPath,
        [string]$RootDir,
        [string]$Path
    )

    if (Test-Path $Path) {
        $relative = Resolve-Path -Path $Path | ForEach-Object {
            $_.Path.Substring($RootDir.Length).TrimStart('\')
        }

        $content = Get-Content -Path $Path -Raw
        Add-Section -OutputPath $OutputPath -Title "FILE: $relative" -Content $content
    }
    else {
        Add-Section -OutputPath $OutputPath -Title "FILE: $Path" -Content "[MISSING]"
    }
}

function Find-ByName {
    param(
        [string]$BasePath,
        [string]$FileName
    )

    Get-ChildItem -Path $BasePath -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq $FileName } |
        Select-Object -First 1
}

function Find-ContainingText {
    param(
        [string]$BasePath,
        [string]$Pattern
    )

    $files = Get-ChildItem -Path $BasePath -Recurse -File -Include *.cs,*.tsx,*.ts -ErrorAction SilentlyContinue

    foreach ($file in $files) {
        $content = Get-Content -Path $file.FullName -Raw -ErrorAction SilentlyContinue
        if ($null -ne $content -and $content -match $Pattern) {
            $file
        }
    }
}

$solutionPath = Join-Path $RootDir "VeritasAtlas.slnx"
$apiRoot = Join-Path $RootDir "apps\api"
$webRoot = Join-Path $RootDir "apps\web\veritas-atlas-web"

if (-not (Test-Path $solutionPath)) {
    throw "Solution file not found: $solutionPath"
}

Push-Location $RootDir

Write-Host ""
Write-Host "Checkpointing current code with git..." -ForegroundColor Cyan

git add -A
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "git add failed."
}

$commitMessage = "checkpoint before phase 6.6 - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
git commit -m $commitMessage
if ($LASTEXITCODE -ne 0) {
    Write-Host "No new commit created. Continuing with phase 6.6 capture." -ForegroundColor Yellow
}
else {
    Write-Host "Created git commit: $commitMessage" -ForegroundColor Green
}

Write-Host ""
Write-Host "Running clean / restore / build..." -ForegroundColor Cyan

dotnet clean $solutionPath
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "dotnet clean failed."
}

dotnet restore $solutionPath
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "dotnet restore failed."
}

dotnet build $solutionPath
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "dotnet build failed."
}

if (Test-Path $webRoot) {
    Push-Location $webRoot
    npm run build
    if ($LASTEXITCODE -ne 0) {
        Pop-Location
        Pop-Location
        throw "npm run build failed."
    }
    Pop-Location
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$diagDir = Join-Path $RootDir "_diagnostics\phase-6-6-capture-$timestamp"
Ensure-Directory $diagDir

$reportPath = Join-Path $diagDir "phase-6-6-capture-report.txt"
Set-Content -Path $reportPath -Value "Phase 6.6 statement slice capture`r`nGenerated: $(Get-Date -Format s)`r`nRoot: $RootDir" -Encoding UTF8

$targets = @()

# Core likely files by name
$targets += Find-ByName -BasePath $apiRoot -FileName "IStatementService.cs"
$targets += Find-ByName -BasePath $apiRoot -FileName "StatementService.cs"
$targets += Find-ByName -BasePath $apiRoot -FileName "StatementsController.cs"
$targets += Find-ByName -BasePath $apiRoot -FileName "StatementsContracts.cs"
$targets += Find-ByName -BasePath $apiRoot -FileName "DomainEntities.cs"
$targets += Find-ByName -BasePath $apiRoot -FileName "EntityConfigurations.cs"
$targets += Find-ByName -BasePath $apiRoot -FileName "VeritasAtlasDbContext.cs"
$targets += Find-ByName -BasePath $apiRoot -FileName "ServiceCollectionExtensions.cs"

# Frontend statement files
$targets += Find-ByName -BasePath $webRoot -FileName "createStatement.ts"
$targets += Find-ByName -BasePath $webRoot -FileName "useCreateStatement.ts"
$targets += Find-ByName -BasePath $webRoot -FileName "CreateStatementPage.tsx"
$targets += Find-ByName -BasePath $webRoot -FileName "main.tsx"

# Additional text-based finds
$targets += Find-ContainingText -BasePath $apiRoot -Pattern 'class\s+Statement\b'
$targets += Find-ContainingText -BasePath $apiRoot -Pattern 'StatementConfiguration'
$targets += Find-ContainingText -BasePath $apiRoot -Pattern 'IStatementService'
$targets += Find-ContainingText -BasePath $apiRoot -Pattern 'AddStatementAsync'
$targets += Find-ContainingText -BasePath $apiRoot -Pattern 'GetStatementsAsync'
$targets += Find-ContainingText -BasePath $apiRoot -Pattern 'GetStatement'
$targets += Find-ContainingText -BasePath $apiRoot -Pattern 'StatementText'
$targets += Find-ContainingText -BasePath $apiRoot -Pattern 'StatementPolarity'
$targets += Find-ContainingText -BasePath $apiRoot -Pattern 'StatementStatus'

$targets = $targets |
    Where-Object { $null -ne $_ } |
    Select-Object -Unique

$gitStatus = & git status --short --branch 2>&1 | Out-String
Add-Section -OutputPath $reportPath -Title "GIT STATUS" -Content $gitStatus

$buildSummary = "dotnet clean / restore / build: SUCCESS`r`nnpm run build: SUCCESS"
Add-Section -OutputPath $reportPath -Title "BUILD STATUS" -Content $buildSummary

$fileList = $targets | ForEach-Object { $_.FullName }
Add-Section -OutputPath $reportPath -Title "CAPTURED FILE LIST" -Content ($fileList -join [Environment]::NewLine)

foreach ($target in $targets) {
    Add-FileSection -OutputPath $reportPath -RootDir $RootDir -Path $target.FullName

    $safeName = $target.FullName.Substring($RootDir.Length).TrimStart('\').Replace('\', '__')
    Copy-Item -Path $target.FullName -Destination (Join-Path $diagDir $safeName) -Force
}

Pop-Location

Write-Host ""
Write-Host "Phase 6.6 capture completed successfully." -ForegroundColor Green
Write-Host "Report: $reportPath" -ForegroundColor Cyan
Write-Host ""
Write-Host "Send me the generated report file next."