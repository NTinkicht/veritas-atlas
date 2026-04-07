param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir
)

$ErrorActionPreference = "Stop"

function Ensure-Dir {
    param([Parameter(Mandatory = $true)][string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { throw "Ensure-Dir received an empty path." }
    if (-not (Test-Path $Path)) { New-Item -ItemType Directory -Path $Path -Force | Out-Null }
}

function Write-File {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )
    $parent = Split-Path -Parent $Path
    Ensure-Dir -Path $parent
    $enc = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $enc)
    Write-Host "Wrote: $Path"
}

function Git-Checkpoint {
    param([Parameter(Mandatory = $true)][string]$Message)
    Push-Location $RootDir
    try {
        if (Test-Path ".git") {
            git add -A | Out-Null
            git commit -m $Message 2>$null | Out-Null
            if ($LASTEXITCODE -eq 0) { Write-Host "Created git commit: $Message" }
            else { Write-Host "No new commit created. Continuing." }
        }
    }
    finally { Pop-Location }
}

function Build-All {
    param([Parameter(Mandatory = $true)][string]$RootDir)

    Push-Location $RootDir
    try {
        dotnet build
        if ($LASTEXITCODE -ne 0) { throw "Backend failed" }
    }
    finally { Pop-Location }

    $webDir = Join-Path $RootDir "apps\web\veritas-atlas-web"
    if (Test-Path $webDir) {
        Push-Location $webDir
        try {
            npm run build
            if ($LASTEXITCODE -ne 0) { throw "Frontend failed" }
        }
        finally { Pop-Location }
    }
}

function Ensure-ImportLine {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$Anchor,
        [Parameter(Mandatory = $true)][string]$ImportLine
    )
    if ($Content -match [regex]::Escape($ImportLine)) { return $Content }
    return $Content -replace [regex]::Escape($Anchor), ($Anchor + [Environment]::NewLine + $ImportLine)
}

function Ensure-RouteBlock {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$AnchorRoute,
        [Parameter(Mandatory = $true)][string]$RouteBlock,
        [Parameter(Mandatory = $true)][string]$PresencePattern
    )
    if ($Content -match $PresencePattern) { return $Content }
    return $Content -replace [regex]::Escape($AnchorRoute), ($AnchorRoute + [Environment]::NewLine + $RouteBlock)
}

function Ensure-NavBlock {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$Anchor,
        [Parameter(Mandatory = $true)][string]$NavBlock,
        [Parameter(Mandatory = $true)][string]$PresencePattern
    )
    if ($Content -match $PresencePattern) { return $Content }
    return $Content -replace [regex]::Escape($Anchor), ($Anchor + [Environment]::NewLine + $NavBlock)
}

function Add-AuthorizeToController {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path $Path)) { return }
    $content = Get-Content $Path -Raw
    if ($content -notmatch 'using Microsoft.AspNetCore.Authorization;') {
        $content = $content -replace 'using Microsoft.AspNetCore.Mvc;', "using Microsoft.AspNetCore.Authorization;`r`nusing Microsoft.AspNetCore.Mvc;"
    }
    if ($content -notmatch '\[Authorize\]') {
        $content = $content -replace '\[ApiController\]\r?\n', "[ApiController]`r`n[Authorize]`r`n"
    }
    Write-File -Path $Path -Content $content
}

function Replace-InFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Replacement
    )
    if (-not (Test-Path $Path)) { return }
    $content = Get-Content $Path -Raw
    $updated = [regex]::Replace($content, $Pattern, $Replacement, [System.Text.RegularExpressions.RegexOptions]::Singleline)
    Write-File -Path $Path -Content $updated
}

function Invoke-Phase11Tests {
    param([Parameter(Mandatory = $true)][string]$RootDir)
    $runner = Join-Path $RootDir "tools\tests\Run-Phase11-Auth-Verification.ps1"
    if (-not (Test-Path $runner)) { throw "Phase 11 verification runner not found: $runner" }

    Push-Location $RootDir
    try {
        & powershell -ExecutionPolicy Bypass -File $runner -RootDir $RootDir
        if ($LASTEXITCODE -ne 0) { throw "Phase 11 auth verification runner failed." }
    }
    finally { Pop-Location }
}

Write-Host "Checkpointing current code with git..." -ForegroundColor Cyan
Git-Checkpoint -Message ("checkpoint before phase 11 auth bundle - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying Phase 11 auth bundle..." -ForegroundColor Cyan

$api = Join-Path $RootDir "apps\api"
$apiProj = Join-Path $api "VeritasAtlas.Api"
$web = Join-Path $RootDir "apps\web\veritas-atlas-web\src"
$tools = Join-Path $RootDir "tools"
$diag = Join-Path $RootDir "_diagnostics\phase-11-auth"
Ensure-Dir -Path $diag

# Auth contracts and services
Write-File -Path (Join-Path $apiProj "Contracts\Auth\AuthContracts.cs") -Content @'
namespace VeritasAtlas.Api.Contracts.Auth;

public sealed record LoginRequest(string Username, string Password);
public sealed record LoginResponse(string AccessToken, string TokenType, DateTime ExpiresAtUtc, string Username, string Role);
public sealed record CurrentUserResponse(string Username, string Role, bool IsAuthenticated, DateTime TimestampUtc);
public sealed record AuthErrorResponse(string Code, string Message, DateTime TimestampUtc);
'@

Write-File -Path (Join-Path $apiProj "Infrastructure\Auth\JwtTokenService.cs") -Content @'
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace VeritasAtlas.Api.Infrastructure.Auth;

public sealed class JwtTokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public (string Token, DateTime ExpiresAtUtc) CreateToken(string username, string role)
    {
        var secret = _configuration["Auth:Jwt:Secret"] ?? "veritas-atlas-dev-secret-key-change-in-production-123456";
        var issuer = _configuration["Auth:Jwt:Issuer"] ?? "VeritasAtlas";
        var audience = _configuration["Auth:Jwt:Audience"] ?? "VeritasAtlasUsers";
        var expiresMinutes = int.TryParse(_configuration["Auth:Jwt:ExpiresMinutes"], out var parsedMinutes) ? parsedMinutes : 480;

        var expiresAtUtc = DateTime.UtcNow.AddMinutes(expiresMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Role, role),
            new("preferred_username", username),
            new("role", role)
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}
'@

Write-File -Path (Join-Path $apiProj "Infrastructure\Auth\DevUserStore.cs") -Content @'
namespace VeritasAtlas.Api.Infrastructure.Auth;

public sealed class DevUserStore
{
    private static readonly IReadOnlyList<DevUserRecord> Users = new List<DevUserRecord>
    {
        new("operator1", "password123", "operator"),
        new("reviewer1", "password123", "reviewer"),
        new("publisher1", "password123", "publisher"),
        new("admin1", "password123", "admin")
    };

    public DevUserRecord? Validate(string username, string password)
    {
        return Users.FirstOrDefault(x =>
            string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase) &&
            x.Password == password);
    }
}

public sealed record DevUserRecord(string Username, string Password, string Role);
'@

Write-File -Path (Join-Path $apiProj "Infrastructure\Auth\AuthRequestContext.cs") -Content @'
using System.Security.Claims;

namespace VeritasAtlas.Api.Infrastructure.Auth;

public sealed class AuthRequestContext
{
    public string GetRole(HttpContext context)
    {
        return context.User.FindFirstValue(ClaimTypes.Role)
            ?? context.User.FindFirstValue("role")
            ?? "anonymous";
    }

    public string GetUsername(HttpContext context)
    {
        return context.User.FindFirstValue(ClaimTypes.Name)
            ?? context.User.FindFirstValue("preferred_username")
            ?? "anonymous";
    }

    public bool IsAuthenticated(HttpContext context)
    {
        return context.User.Identity?.IsAuthenticated == true;
    }
}
'@

Write-File -Path (Join-Path $apiProj "Controllers\AuthController.cs") -Content @'
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Auth;
using VeritasAtlas.Api.Infrastructure.Auth;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly DevUserStore _devUserStore;
    private readonly JwtTokenService _jwtTokenService;
    private readonly AuthRequestContext _authRequestContext;

    public AuthController(
        DevUserStore devUserStore,
        JwtTokenService jwtTokenService,
        AuthRequestContext authRequestContext)
    {
        _devUserStore = devUserStore;
        _jwtTokenService = jwtTokenService;
        _authRequestContext = authRequestContext;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public ActionResult<LoginResponse> Login([FromBody] LoginRequest request)
    {
        var user = _devUserStore.Validate(request.Username, request.Password);
        if (user is null)
        {
            return Unauthorized(new AuthErrorResponse("invalid_credentials", "The supplied username or password is invalid.", DateTime.UtcNow));
        }

        var token = _jwtTokenService.CreateToken(user.Username, user.Role);

        return Ok(new LoginResponse(token.Token, "Bearer", token.ExpiresAtUtc, user.Username, user.Role));
    }

    [HttpGet("me")]
    [Authorize]
    public ActionResult<CurrentUserResponse> Me()
    {
        return Ok(new CurrentUserResponse(
            _authRequestContext.GetUsername(HttpContext),
            _authRequestContext.GetRole(HttpContext),
            _authRequestContext.IsAuthenticated(HttpContext),
            DateTime.UtcNow));
    }
}
'@

# Program.cs patch
$programPath = Join-Path $apiProj "Program.cs"
if (Test-Path $programPath) {
    $program = Get-Content $programPath -Raw

    if ($program -notmatch 'using Microsoft.AspNetCore.Authentication.JwtBearer;') {
        $program = "using Microsoft.AspNetCore.Authentication.JwtBearer;`r`nusing Microsoft.IdentityModel.Tokens;`r`nusing System.Text;`r`n" + $program
    }

    if ($program -notmatch 'AddSingleton<VeritasAtlas.Api.Infrastructure.Auth.JwtTokenService>') {
        $authBlock = @'

builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.Auth.JwtTokenService>();
builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.Auth.DevUserStore>();
builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.Auth.AuthRequestContext>();

var jwtSecret = builder.Configuration["Auth:Jwt:Secret"] ?? "veritas-atlas-dev-secret-key-change-in-production-123456";
var jwtIssuer = builder.Configuration["Auth:Jwt:Issuer"] ?? "VeritasAtlas";
var jwtAudience = builder.Configuration["Auth:Jwt:Audience"] ?? "VeritasAtlasUsers";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });

builder.Services.AddAuthorization();
'@
        $program = $program -replace '(var app = builder\.Build\(\);)', ($authBlock + "`r`n" + '$1')
    }

    if ($program -notmatch 'app.UseAuthentication\(\);') {
        $program = $program -replace 'app\.UseAuthorization\(\);', "app.UseAuthentication();`r`napp.UseAuthorization();"
    }

    Write-File -Path $programPath -Content $program
}

# Protect controllers
Add-AuthorizeToController -Path (Join-Path $apiProj "Controllers\ActionsController.cs")
Add-AuthorizeToController -Path (Join-Path $apiProj "Controllers\ReviewWorkflowController.cs")
Add-AuthorizeToController -Path (Join-Path $apiProj "Controllers\PublicationWorkflowController.cs")
Add-AuthorizeToController -Path (Join-Path $apiProj "Controllers\WorkflowAuditController.cs")

# Frontend auth client
Write-File -Path (Join-Path $web "api\auth.ts") -Content @'
export type LoginRequest = {
  username: string;
  password: string;
};

export type LoginResponse = {
  accessToken: string;
  tokenType: string;
  expiresAtUtc: string;
  username: string;
  role: string;
};

export type CurrentUserResponse = {
  username: string;
  role: string;
  isAuthenticated: boolean;
  timestampUtc: string;
};

const TOKEN_KEY = "veritas-auth-token";
const USER_KEY = "veritas-auth-user";

export function getAccessToken(): string | null {
  if (typeof window === "undefined") return null;
  return window.localStorage.getItem(TOKEN_KEY);
}

export function setAccessToken(token: string) {
  if (typeof window === "undefined") return;
  window.localStorage.setItem(TOKEN_KEY, token);
}

export function clearAuthState() {
  if (typeof window === "undefined") return;
  window.localStorage.removeItem(TOKEN_KEY);
  window.localStorage.removeItem(USER_KEY);
}

export async function login(request: LoginRequest): Promise<LoginResponse> {
  const response = await fetch("/api/v1/auth/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  const data = (await response.json()) as LoginResponse;
  setAccessToken(data.accessToken);
  return data;
}

export async function fetchCurrentUser(): Promise<CurrentUserResponse> {
  const token = getAccessToken();

  const response = await fetch("/api/v1/auth/me", {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  const data = (await response.json()) as CurrentUserResponse;

  if (typeof window !== "undefined") {
    window.localStorage.setItem(USER_KEY, JSON.stringify(data));
  }

  return data;
}
'@

Write-File -Path (Join-Path $web "api\httpAuth.ts") -Content @'
import { getAccessToken } from "./auth";

export function getAuthHeaders(extra?: HeadersInit): HeadersInit {
  const token = getAccessToken();
  return {
    ...(extra ?? {}),
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
  };
}
'@

Write-File -Path (Join-Path $web "hooks\useAuth.ts") -Content @'
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { clearAuthState, fetchCurrentUser, login, type LoginRequest } from "../api/auth";

export function useCurrentUser() {
  return useQuery({
    queryKey: ["current-user"],
    queryFn: () => fetchCurrentUser(),
    retry: false,
  });
}

export function useLogin() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: LoginRequest) => login(request),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["current-user"] });
    },
  });
}

export function useLogout() {
  const queryClient = useQueryClient();

  return () => {
    clearAuthState();
    queryClient.removeQueries({ queryKey: ["current-user"] });
  };
}
'@

Write-File -Path (Join-Path $web "components\AuthStatusPanel.tsx") -Content @'
import type { CurrentUserResponse } from "../api/auth";
import { useLogout } from "../hooks/useAuth";

export function AuthStatusPanel({
  user,
}: {
  user: CurrentUserResponse | null;
}) {
  const logout = useLogout();

  return (
    <div style={panelStyle}>
      <strong>Auth Status</strong>
      <span>{user ? `${user.username} (${user.role})` : "Not authenticated"}</span>
      {user && (
        <button onClick={logout} style={buttonStyle}>
          Logout
        </button>
      )}
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 12,
  padding: 12,
  display: "flex",
  gap: 12,
  alignItems: "center",
  flexWrap: "wrap",
};

const buttonStyle: React.CSSProperties = {
  border: "1px solid #bbb",
  borderRadius: 8,
  padding: "8px 10px",
  background: "white",
  cursor: "pointer",
  font: "inherit",
};
'@

Write-File -Path (Join-Path $web "pages\LoginPage.tsx") -Content @'
import { useState } from "react";
import { useLogin } from "../hooks/useAuth";

export function LoginPage() {
  const loginMutation = useLogin();
  const [username, setUsername] = useState("admin1");
  const [password, setPassword] = useState("password123");
  const error = (loginMutation.error as Error | null)?.message;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24, maxWidth: 520 }}>
      <h1 style={{ marginTop: 0 }}>Login</h1>
      <p style={{ color: "#555" }}>Development authentication entry point for Veritas Atlas.</p>

      <div style={panelStyle}>
        <label style={labelStyle}>
          Username
          <input value={username} onChange={(e) => setUsername(e.target.value)} style={inputStyle} />
        </label>

        <label style={labelStyle}>
          Password
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} style={inputStyle} />
        </label>

        <button
          onClick={() => loginMutation.mutate({ username, password })}
          disabled={loginMutation.isPending}
          style={buttonStyle}
        >
          {loginMutation.isPending ? "Signing in..." : "Login"}
        </button>

        {loginMutation.data && <p style={{ margin: 0 }}>Logged in as {loginMutation.data.username} ({loginMutation.data.role})</p>}
        {error && <p style={{ margin: 0, color: "crimson" }}>{error}</p>}
      </div>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  display: "grid",
  gap: 12,
};

const labelStyle: React.CSSProperties = {
  display: "grid",
  gap: 6,
};

const inputStyle: React.CSSProperties = {
  border: "1px solid #ccc",
  borderRadius: 10,
  padding: "10px 12px",
  font: "inherit",
};

const buttonStyle: React.CSSProperties = {
  border: "1px solid #bbb",
  borderRadius: 10,
  padding: "10px 14px",
  background: "white",
  cursor: "pointer",
  font: "inherit",
};
'@

Write-File -Path (Join-Path $web "pages\AuthMePage.tsx") -Content @'
import { AuthStatusPanel } from "../components/AuthStatusPanel";
import { useCurrentUser } from "../hooks/useAuth";

export function AuthMePage() {
  const query = useCurrentUser();

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Loading current user...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24, color: "crimson" }}>Failed to load current user.</div>;
  }

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Current User</h1>
      <AuthStatusPanel user={query.data ?? null} />
    </div>
  );
}
'@

Write-File -Path (Join-Path $web "pages\Phase11AuthCenterPage.tsx") -Content @'
import { Link } from "react-router-dom";

export function Phase11AuthCenterPage() {
  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Phase 11 Auth Center</h1>
      <p style={{ color: "#555" }}>
        Authentication and authorization hub for Veritas Atlas.
      </p>

      <div style={gridStyle}>
        <Link to="/login" style={cardStyle}>Login</Link>
        <Link to="/auth-me" style={cardStyle}>Current User</Link>
        <Link to="/role-policy" style={cardStyle}>Role Policy</Link>
        <Link to="/phase-10-hardening-center" style={cardStyle}>Phase 10 Hardening</Link>
      </div>
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))",
  gap: 16,
};

const cardStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  textDecoration: "none",
  color: "inherit",
};
'@

# patch auth headers into existing api files
Replace-InFile -Path (Join-Path $web "api\actions.ts") -Pattern 'import \{ getWorkflowRoleHeaders \} from "\./workflowRoleContext";' -Replacement 'import { getAuthHeaders } from "./httpAuth";'
Replace-InFile -Path (Join-Path $web "api\actions.ts") -Pattern 'headers: getWorkflowRoleHeaders\(\),' -Replacement 'headers: getAuthHeaders(),'

Replace-InFile -Path (Join-Path $web "api\workflowActions.ts") -Pattern 'import \{ getWorkflowRoleHeaders \} from "\./workflowRoleContext";' -Replacement 'import { getAuthHeaders } from "./httpAuth";'
Replace-InFile -Path (Join-Path $web "api\workflowActions.ts") -Pattern 'headers: getWorkflowRoleHeaders\(\),' -Replacement 'headers: getAuthHeaders(),'

Replace-InFile -Path (Join-Path $web "api\workflowSeed.ts") -Pattern 'import \{ getWorkflowRoleHeaders \} from "\./workflowRoleContext";' -Replacement 'import { getAuthHeaders } from "./httpAuth";'
Replace-InFile -Path (Join-Path $web "api\workflowSeed.ts") -Pattern 'headers: getWorkflowRoleHeaders\(\),' -Replacement 'headers: getAuthHeaders(),'

# main.tsx patch
$main = Join-Path $web "main.tsx"
if (Test-Path $main) {
    $mainContent = Get-Content $main -Raw
    $mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { DashboardPage } from "./pages/DashboardPage";' -ImportLine 'import { LoginPage } from "./pages/LoginPage";'
    $mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { LoginPage } from "./pages/LoginPage";' -ImportLine 'import { AuthMePage } from "./pages/AuthMePage";'
    $mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { AuthMePage } from "./pages/AuthMePage";' -ImportLine 'import { Phase11AuthCenterPage } from "./pages/Phase11AuthCenterPage";'

    $mainContent = Ensure-RouteBlock -Content $mainContent -AnchorRoute '{ path: "/dashboard", element: <DashboardPage /> },' -RouteBlock '{ path: "/login", element: <LoginPage /> },
  { path: "/auth-me", element: <AuthMePage /> },
  { path: "/phase-11-auth-center", element: <Phase11AuthCenterPage /> },' -PresencePattern 'path: "/login"'

    $mainContent = Ensure-NavBlock -Content $mainContent -Anchor '<Link to="/workflow-audit">Workflow Audit</Link>' -NavBlock '<Link to="/login">Login</Link>
          <Link to="/auth-me">Current User</Link>
          <Link to="/phase-11-auth-center">Phase 11 Auth</Link>' -PresencePattern 'to="/login"'

    Write-File -Path $main -Content $mainContent
}

# auth test runner
Write-File -Path (Join-Path $tools "tests\Run-Phase11-Auth-Verification.ps1") -Content @'
param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir,
    [string]$BaseUrl = "http://localhost:5209"
)

$ErrorActionPreference = "Stop"

function Ensure-Dir {
    param([string]$Path)
    if (-not (Test-Path $Path)) { New-Item -ItemType Directory -Path $Path -Force | Out-Null }
}

function Add-Result {
    param(
        [string]$ReportPath,
        [string]$Name,
        [bool]$Passed,
        [string]$Detail
    )
    Add-Content -Path $ReportPath -Value ("## " + $Name)
    Add-Content -Path $ReportPath -Value ("- Result: " + ($(if ($Passed) { "PASS" } else { "FAIL" })))
    Add-Content -Path $ReportPath -Value ("- Detail: " + $Detail)
    Add-Content -Path $ReportPath -Value ""
}

function Invoke-Json {
    param(
        [string]$Url,
        [string]$Method = "GET",
        [object]$Body = $null,
        [hashtable]$Headers = @{}
    )

    try {
        $params = @{
            Uri = $Url
            Method = $Method
            Headers = $Headers
            TimeoutSec = 20
        }

        if ($null -ne $Body) {
            $params["ContentType"] = "application/json"
            $params["Body"] = ($Body | ConvertTo-Json)
        }

        $result = Invoke-RestMethod @params
        return @{
            Success = $true
            Data = $result
            Message = "OK"
        }
    }
    catch {
        return @{
            Success = $false
            Data = $null
            Message = $_.Exception.Message
        }
    }
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$diag = Join-Path $RootDir "_diagnostics\phase11-tests-$timestamp"
Ensure-Dir $diag
$report = Join-Path $diag "phase11-auth-report.md"
$stdoutLog = Join-Path $diag "api-stdout.log"
$stderrLog = Join-Path $diag "api-stderr.log"

Set-Content -Path $report -Value "# Phase 11 Auth Verification Report`r`n" -Encoding UTF8
Add-Content -Path $report -Value ("Generated: " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
Add-Content -Path $report -Value ""

$apiProcess = $null
$failed = $false

Push-Location (Join-Path $RootDir "apps\api\VeritasAtlas.Api")
try {
    $apiProcess = Start-Process "dotnet" -ArgumentList "run" -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog -PassThru
    Start-Sleep -Seconds 8

    $badLogin = Invoke-Json -Url "$BaseUrl/api/v1/auth/login" -Method POST -Body @{ username = "admin1"; password = "wrong" }
    Add-Result -ReportPath $report -Name "Invalid login rejected" -Passed (-not $badLogin.Success) -Detail $badLogin.Message
    if ($badLogin.Success) { $failed = $true }

    $goodLogin = Invoke-Json -Url "$BaseUrl/api/v1/auth/login" -Method POST -Body @{ username = "admin1"; password = "password123" }
    Add-Result -ReportPath $report -Name "Valid login accepted" -Passed $goodLogin.Success -Detail $goodLogin.Message
    if (-not $goodLogin.Success) { $failed = $true; throw "Valid login failed." }

    $token = $goodLogin.Data.accessToken
    $headers = @{ Authorization = "Bearer $token" }

    $me = Invoke-Json -Url "$BaseUrl/api/v1/auth/me" -Method GET -Headers $headers
    Add-Result -ReportPath $report -Name "Current user endpoint works" -Passed $me.Success -Detail ($(if ($me.Success) { "$($me.Data.username) / $($me.Data.role)" } else { $me.Message }))
    if (-not $me.Success) { $failed = $true }

    $protectedNoToken = Invoke-Json -Url "$BaseUrl/api/v1/actions/seed/lifecycle" -Method POST
    Add-Result -ReportPath $report -Name "Protected workflow blocked without token" -Passed (-not $protectedNoToken.Success) -Detail $protectedNoToken.Message
    if ($protectedNoToken.Success) { $failed = $true }

    $seed = Invoke-Json -Url "$BaseUrl/api/v1/actions/seed/lifecycle" -Method POST -Headers $headers
    Add-Result -ReportPath $report -Name "Protected workflow allowed with token" -Passed $seed.Success -Detail $seed.Message
    if (-not $seed.Success) { $failed = $true }

    $audit = Invoke-Json -Url "$BaseUrl/api/v1/workflow-audit/entries" -Method GET -Headers $headers
    Add-Result -ReportPath $report -Name "Workflow audit protected endpoint works" -Passed $audit.Success -Detail $audit.Message
    if (-not $audit.Success) { $failed = $true }
}
finally {
    Pop-Location
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force
    }
}

if ($failed) {
    Write-Error "Phase 11 verification failed. See report: $report"
    exit 1
}
else {
    Write-Host "Phase 11 verification passed. Report: $report" -ForegroundColor Green
}
'@

Write-File -Path (Join-Path $diag "phase-11-auth-summary.md") -Content @'
# Phase 11 Auth Summary

## Included
- JWT-based auth infrastructure
- dev user store for controlled login
- auth controller with login and current user endpoints
- auth middleware configuration in Program.cs
- workflow controllers marked as authorized
- frontend login, current user, and auth center pages
- bearer token propagation into workflow API calls
- comprehensive Phase 11 auth verification runner

## Goal
Move the platform from header-role simulation toward actual authenticated access with JWT.
'@

Write-Host "Building..." -ForegroundColor Cyan
Build-All -RootDir $RootDir

Write-Host "Running Phase 11 auth verification..." -ForegroundColor Cyan
Invoke-Phase11Tests -RootDir $RootDir

Write-Host "Phase 11 auth bundle DONE" -ForegroundColor Green
