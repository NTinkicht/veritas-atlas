param(
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl
)

$ErrorActionPreference = "Stop"

Write-Host "Logging in..." -ForegroundColor Cyan
$login = Invoke-RestMethod -Uri "$BaseUrl/api/v1/auth/login" `
    -Method POST `
    -Body '{"username":"admin1","password":"password123"}' `
    -ContentType "application/json"

if (-not $login.accessToken) {
    throw "Login succeeded but no accessToken was returned."
}

$token = $login.accessToken
Write-Host "Token received." -ForegroundColor Green

Write-Host "Calling /api/v1/auth/me ..." -ForegroundColor Cyan
$me = Invoke-RestMethod -Uri "$BaseUrl/api/v1/auth/me" `
    -Headers @{ Authorization = "Bearer $token" }

$me | ConvertTo-Json -Depth 6