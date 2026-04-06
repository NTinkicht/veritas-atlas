param(
    [string]$RootDir = ((Get-Location).Path + "\veritas-atlas"),
    [string]$SolutionName = "VeritasAtlas"
)

$ErrorActionPreference = "Stop"

$apiProjectName = "VeritasAtlas.Api"
$appProjectName = "VeritasAtlas.Application"
$domainProjectName = "VeritasAtlas.Domain"
$infraProjectName = "VeritasAtlas.Infrastructure"

$srcRoot = Join-Path $RootDir "apps\api"
$apiDir = Join-Path $srcRoot $apiProjectName
$appDir = Join-Path $srcRoot $appProjectName
$domainDir = Join-Path $srcRoot $domainProjectName
$infraDir = Join-Path $srcRoot $infraProjectName

Write-Host "Bootstrapping backend solution in: $RootDir"

if (-not (Test-Path $RootDir)) {
    throw "Root directory does not exist: $RootDir"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK is not installed or not available in PATH."
}

New-Item -ItemType Directory -Force -Path $srcRoot | Out-Null

Push-Location $RootDir

$solutionPath = Join-Path $RootDir "$SolutionName.sln"
if (-not (Test-Path $solutionPath)) {
    dotnet new sln -n $SolutionName
}

if (-not (Test-Path $apiDir)) {
    dotnet new webapi -n $apiProjectName -o $apiDir --use-controllers
}

if (-not (Test-Path $appDir)) {
    dotnet new classlib -n $appProjectName -o $appDir
}

if (-not (Test-Path $domainDir)) {
    dotnet new classlib -n $domainProjectName -o $domainDir
}

if (-not (Test-Path $infraDir)) {
    dotnet new classlib -n $infraProjectName -o $infraDir
}

$apiCsproj = Join-Path $apiDir "$apiProjectName.csproj"
$appCsproj = Join-Path $appDir "$appProjectName.csproj"
$domainCsproj = Join-Path $domainDir "$domainProjectName.csproj"
$infraCsproj = Join-Path $infraDir "$infraProjectName.csproj"

dotnet sln $solutionPath add $apiCsproj 2>$null
dotnet sln $solutionPath add $appCsproj 2>$null
dotnet sln $solutionPath add $domainCsproj 2>$null
dotnet sln $solutionPath add $infraCsproj 2>$null

dotnet add $apiCsproj reference $appCsproj
dotnet add $apiCsproj reference $infraCsproj

dotnet add $appCsproj reference $domainCsproj

dotnet add $infraCsproj reference $appCsproj
dotnet add $infraCsproj reference $domainCsproj

dotnet add $infraCsproj package Microsoft.EntityFrameworkCore
dotnet add $infraCsproj package Microsoft.EntityFrameworkCore.Design
dotnet add $infraCsproj package Npgsql.EntityFrameworkCore.PostgreSQL

dotnet add $apiCsproj package Microsoft.AspNetCore.OpenApi
dotnet add $apiCsproj package Swashbuckle.AspNetCore

$directoryBuildPropsPath = Join-Path $RootDir "Directory.Build.props"
$directoryBuildProps = @"
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
</Project>
"@
Set-Content -Path $directoryBuildPropsPath -Value $directoryBuildProps -Encoding UTF8

$defaultFiles = @(
    (Join-Path $appDir "Class1.cs"),
    (Join-Path $domainDir "Class1.cs"),
    (Join-Path $infraDir "Class1.cs")
)

foreach ($file in $defaultFiles) {
    if (Test-Path $file) {
        Remove-Item $file -Force
    }
}

$foldersToCreate = @(
    "$domainDir\Common",
    "$domainDir\Entities",
    "$domainDir\Enums",
    "$domainDir\ValueObjects",

    "$appDir\Interfaces",
    "$appDir\Services",
    "$appDir\DTOs",
    "$appDir\Commands",
    "$appDir\Queries",

    "$infraDir\Persistence",
    "$infraDir\Repositories",
    "$infraDir\Configurations",

    "$apiDir\Controllers",
    "$apiDir\Contracts",
    "$apiDir\Infrastructure"
)

foreach ($folder in $foldersToCreate) {
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
}

$appsettingsPath = Join-Path $apiDir "appsettings.json"
$appsettingsDevPath = Join-Path $apiDir "appsettings.Development.json"

$appsettings = @"
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=veritas_atlas;Username=postgres;Password=postgres"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
"@

$appsettingsDev = @"
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
"@

Set-Content -Path $appsettingsPath -Value $appsettings -Encoding UTF8
Set-Content -Path $appsettingsDevPath -Value $appsettingsDev -Encoding UTF8

$programCsPath = Join-Path $apiDir "Program.cs"
$programCs = @"
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();
"@
Set-Content -Path $programCsPath -Value $programCs -Encoding UTF8

dotnet restore $solutionPath
dotnet build $solutionPath

Pop-Location

Write-Host ""
Write-Host "Phase 1 complete."
Write-Host "Solution: $solutionPath"
Write-Host "Projects created:"
Write-Host " - $apiProjectName"
Write-Host " - $appProjectName"
Write-Host " - $domainProjectName"
Write-Host " - $infraProjectName"