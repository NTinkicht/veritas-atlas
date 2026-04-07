# Auth Debug Collection

# Environment

# RootDir

C:\Projects\veritas-atlas

# Git Status

 M Script.ps1
 M apps/api/VeritasAtlas.Api/Program.cs
 M apps/api/VeritasAtlas.Api/appsettings.Development.json
 M apps/web/veritas-atlas-web/src/main.tsx
 M tools/tests/Run-Phase12-Verification.ps1
?? _diagnostics/auth-debug-collect-20260407-205337/
?? _diagnostics/phase-11-1-auth-debug/
?? _diagnostics/phase12-tests-20260407-203448/
?? apps/api/VeritasAtlas.Api/Controllers/AuthDiagnosticsController.cs
?? apps/web/veritas-atlas-web/src/api/authDiagnostics.ts
?? apps/web/veritas-atlas-web/src/hooks/useAuthDiagnostics.ts
?? apps/web/veritas-atlas-web/src/pages/AuthDiagnosticsPage.tsx
?? tools/tests/Run-Phase11_1-Auth-Debug-Verification.ps1


# Recent Git Log

4a5e758 checkpoint before phase 12 bundle - 2026-04-07 20:34:29
694aafe checkpoint before phase 12 bundle - 2026-04-07 20:30:27
8b2dc7e checkpoint before phase 11 auth bundle - 2026-04-07 20:13:57
ff03a16 checkpoint before phase 10 bundle - 2026-04-07 19:57:22
55a753d checkpoint before phase 9 bundle - 2026-04-07 19:48:06
810a604 checkpoint before phase 9 bundle - 2026-04-07 19:46:44
1cc6787 checkpoint before phase 9 bundle - 2026-04-07 19:39:14
37438da checkpoint before phase 8.1-8.5 bundle - 2026-04-07 19:21:41


# Relevant Files

## FILE: C:\Projects\veritas-atlas\Directory.Packages.props
```
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.5" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.0.5" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.5" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.5" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Tools" Version="10.0.5">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageVersion>
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.1" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.5" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.Abstractions" Version="10.0.5" />
    <PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.0" />
    <PackageVersion Include="Swashbuckle.AspNetCore" Version="10.0.1" />
    <PackageVersion Include="Microsoft.Extensions.Configuration" Version="10.0.5" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.Json" Version="10.0.5" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.FileExtensions" Version="10.0.5" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="10.0.5" />
    <PackageVersion Include="AspNetCore.HealthChecks.NpgSql" Version="9.0.0" />
    <PackageVersion Include="System.IdentityModel.Tokens.Jwt" Version="8.17.0" />
  </ItemGroup>
</Project>
```

## FILE: C:\Projects\veritas-atlas\apps\api\VeritasAtlas.Api\Program.cs
```
var jwtSecret = builder.Configuration["Auth:Jwt:Secret"] ?? "veritas-atlas-dev-secret-key-change-in-production-123456";
var jwtIssuer = builder.Configuration["Auth:Jwt:Issuer"] ?? "VeritasAtlas";
var jwtAudience = builder.Configuration["Auth:Jwt:Audience"] ?? "VeritasAtlasUsers";
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using VeritasAtlas.Api.Infrastructure;
using VeritasAtlas.Application.Extensions;
using VeritasAtlas.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.WorkflowRequestContext>();
builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.WorkflowAuthorizationService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.WorkflowRequestContext>();
builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.WorkflowAuthorizationService>();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.WorkflowRequestContext>();
builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.WorkflowAuthorizationService>();

builder.Services.AddVeritasAtlasApplication();
builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.WorkflowRequestContext>();
builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.WorkflowAuthorizationService>();
builder.Services.AddVeritasAtlasInfrastructure(builder.Configuration);
builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.WorkflowRequestContext>();
builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.WorkflowAuthorizationService>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

builder.Services.AddHealthChecks()
    .AddNpgSql(
        connectionString,
        name: "postgresql",
        tags: new[] { "db", "postgres", "ready" });


builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.Auth.JwtTokenService>();
builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.Auth.DevUserStore>();
builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.Auth.AuthRequestContext>();




builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.Auth.JwtTokenService>();
builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.Auth.DevUserStore>();
builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.Auth.AuthRequestContext>();


builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };
    });

builder.Services.AddAuthorization();
var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapHealthChecks("/health");

app.MapHealthChecks("/health/db", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("db")
});

app.Run();

```

## FILE: C:\Projects\veritas-atlas\apps\api\VeritasAtlas.Api\VeritasAtlas.Api.csproj
```
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="AspNetCore.HealthChecks.NpgSql" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" />
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Swashbuckle.AspNetCore" />
    <PackageReference Include="System.IdentityModel.Tokens.Jwt" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\VeritasAtlas.Application\VeritasAtlas.Application.csproj" />
    <ProjectReference Include="..\VeritasAtlas.Infrastructure\VeritasAtlas.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

## FILE: C:\Projects\veritas-atlas\apps\api\VeritasAtlas.Api\appsettings.json
```
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

```

## FILE: C:\Projects\veritas-atlas\apps\api\VeritasAtlas.Api\appsettings.Development.json
```
{
  "Auth": {
    "Jwt": {
      "Secret": "veritas-atlas-dev-secret-key-change-in-production-123456",
      "Issuer": "VeritasAtlas",
      "Audience": "VeritasAtlasUsers",
      "ExpiresMinutes": 480
    }
  }
}
```

## FILE: C:\Projects\veritas-atlas\apps\api\VeritasAtlas.Api\Controllers\AuthController.cs
```
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
```

## FILE: C:\Projects\veritas-atlas\apps\api\VeritasAtlas.Api\Controllers\AuthDiagnosticsController.cs
```
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/auth-diagnostics")]
public class AuthDiagnosticsController : ControllerBase
{
    private readonly AuthRequestContext _authRequestContext;
    private readonly IConfiguration _configuration;

    public AuthDiagnosticsController(
        AuthRequestContext authRequestContext,
        IConfiguration configuration)
    {
        _authRequestContext = authRequestContext;
        _configuration = configuration;
    }

    [HttpGet("public")]
    [AllowAnonymous]
    public IActionResult Public()
    {
        return Ok(new
        {
            Mode = "public",
            JwtIssuer = _configuration["Auth:Jwt:Issuer"] ?? "VeritasAtlas",
            JwtAudience = _configuration["Auth:Jwt:Audience"] ?? "VeritasAtlasUsers",
            TimestampUtc = DateTime.UtcNow
        });
    }

    [HttpGet("protected")]
    [Authorize]
    public IActionResult Protected()
    {
        return Ok(new
        {
            Mode = "protected",
            Username = _authRequestContext.GetUsername(HttpContext),
            Role = _authRequestContext.GetRole(HttpContext),
            IsAuthenticated = _authRequestContext.IsAuthenticated(HttpContext),
            Claims = User.Claims.Select(x => new { x.Type, x.Value }).ToList(),
            TimestampUtc = DateTime.UtcNow
        });
    }
}
```

## FILE: C:\Projects\veritas-atlas\apps\api\VeritasAtlas.Api\Controllers\ActionsController.cs
```
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Workflow;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/actions")]
public class ActionsController : ControllerBase
{
    private readonly WorkflowOrchestratorService _workflowOrchestratorService;

    public ActionsController(WorkflowOrchestratorService workflowOrchestratorService)
    {
        _workflowOrchestratorService = workflowOrchestratorService;
    }

    [HttpPost("cases/{caseId}/submit")]
    public async Task<ActionResult<WorkflowTransitionResponse>> SubmitCase(Guid caseId, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Case",
            caseId,
            () => _workflowOrchestratorService.SubmitCaseAsync(caseId, Request.Headers["X-Role"], cancellationToken),
            "Case submitted.");
    }

    [HttpPost("cases/{caseId}/approve")]
    public async Task<ActionResult<WorkflowTransitionResponse>> ApproveCase(Guid caseId, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Case",
            caseId,
            () => _workflowOrchestratorService.ApproveCaseAsync(caseId, Request.Headers["X-Role"], cancellationToken),
            "Case approved.");
    }

    [HttpPost("cases/{caseId}/reject")]
    public async Task<ActionResult<WorkflowTransitionResponse>> RejectCase(Guid caseId, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Case",
            caseId,
            () => _workflowOrchestratorService.RejectCaseAsync(caseId, Request.Headers["X-Role"], cancellationToken),
            "Case rejected.");
    }

    [HttpPost("contradictions/{id}/resolve")]
    public async Task<ActionResult<WorkflowTransitionResponse>> ResolveContradiction(Guid id, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Contradiction",
            id,
            () => _workflowOrchestratorService.ResolveContradictionAsync(id, Request.Headers["X-Role"], cancellationToken),
            "Contradiction resolved.");
    }

    [HttpPost("reviews/{id}/complete")]
    public async Task<ActionResult<WorkflowTransitionResponse>> CompleteReview(Guid id, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Review",
            id,
            () => _workflowOrchestratorService.CompleteReviewAsync(id, Request.Headers["X-Role"], cancellationToken),
            "Review completed.");
    }

    [HttpPost("seed/lifecycle")]
    public async Task<ActionResult<WorkflowSeedResponse>> SeedLifecycle(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _workflowOrchestratorService.SeedLifecycleAsync(Request.Headers["X-Role"], cancellationToken);

            return Ok(new WorkflowSeedResponse(
                result.CaseId,
                result.ClaimAId,
                result.ClaimBId,
                result.ContradictionId,
                result.CaseStatus,
                result.ContradictionStatus,
                DateTime.UtcNow));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private async Task<ActionResult<WorkflowTransitionResponse>> ExecuteTransition(
        string entityType,
        Guid entityId,
        Func<Task<(Guid Id, string Status)>> action,
        string message)
    {
        try
        {
            var result = await action();
            return Ok(new WorkflowTransitionResponse(entityType, result.Id, result.Status, DateTime.UtcNow, message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new WorkflowValidationFailureResponse(entityType, entityId, message, ex.Message, DateTime.UtcNow));
        }
    }
}
```

## FILE: C:\Projects\veritas-atlas\apps\api\VeritasAtlas.Api\Controllers\ReviewWorkflowController.cs
```
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Workflow;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/review-workflow")]
public class ReviewWorkflowController : ControllerBase
{
    private readonly WorkflowOrchestratorService _workflowOrchestratorService;

    public ReviewWorkflowController(WorkflowOrchestratorService workflowOrchestratorService)
    {
        _workflowOrchestratorService = workflowOrchestratorService;
    }

    [HttpPost("claims/{claimId}/send-to-review")]
    public async Task<ActionResult<WorkflowTransitionResponse>> SendClaimToReview(Guid claimId, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Claim",
            claimId,
            () => _workflowOrchestratorService.SendClaimToReviewAsync(claimId, Request.Headers["X-Role"], cancellationToken),
            "Claim sent to review.");
    }

    [HttpPost("claims/{claimId}/return-for-edit")]
    public async Task<ActionResult<WorkflowTransitionResponse>> ReturnClaimForEdit(Guid claimId, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Claim",
            claimId,
            () => _workflowOrchestratorService.ReturnClaimForEditAsync(claimId, Request.Headers["X-Role"], cancellationToken),
            "Claim returned for edit.");
    }

    [HttpPost("contradictions/{id}/escalate")]
    public async Task<ActionResult<WorkflowTransitionResponse>> EscalateContradiction(Guid id, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Contradiction",
            id,
            () => _workflowOrchestratorService.EscalateContradictionAsync(id, Request.Headers["X-Role"], cancellationToken),
            "Contradiction escalated.");
    }

    [HttpPost("reviews/{id}/reopen")]
    public async Task<ActionResult<WorkflowTransitionResponse>> ReopenReview(Guid id, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Review",
            id,
            () => _workflowOrchestratorService.ReopenReviewAsync(id, Request.Headers["X-Role"], cancellationToken),
            "Review reopened.");
    }

    private async Task<ActionResult<WorkflowTransitionResponse>> ExecuteTransition(
        string entityType,
        Guid entityId,
        Func<Task<(Guid Id, string Status)>> action,
        string message)
    {
        try
        {
            var result = await action();
            return Ok(new WorkflowTransitionResponse(entityType, result.Id, result.Status, DateTime.UtcNow, message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new WorkflowValidationFailureResponse(entityType, entityId, message, ex.Message, DateTime.UtcNow));
        }
    }
}
```

## FILE: C:\Projects\veritas-atlas\apps\api\VeritasAtlas.Api\Controllers\PublicationWorkflowController.cs
```
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Workflow;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/publication-workflow")]
public class PublicationWorkflowController : ControllerBase
{
    private readonly WorkflowOrchestratorService _workflowOrchestratorService;

    public PublicationWorkflowController(WorkflowOrchestratorService workflowOrchestratorService)
    {
        _workflowOrchestratorService = workflowOrchestratorService;
    }

    [HttpPost("cases/{caseId}/prepare")]
    public async Task<ActionResult<WorkflowTransitionResponse>> PreparePublication(Guid caseId, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Case",
            caseId,
            () => _workflowOrchestratorService.PreparePublicationAsync(caseId, Request.Headers["X-Role"], cancellationToken),
            "Case prepared for publication.");
    }

    [HttpPost("cases/{caseId}/publish")]
    public async Task<ActionResult<WorkflowTransitionResponse>> PublishCase(Guid caseId, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Case",
            caseId,
            () => _workflowOrchestratorService.PublishCaseAsync(caseId, Request.Headers["X-Role"], cancellationToken),
            "Case published.");
    }

    [HttpPost("cases/{caseId}/hold")]
    public async Task<ActionResult<WorkflowTransitionResponse>> HoldCase(Guid caseId, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Case",
            caseId,
            () => _workflowOrchestratorService.HoldCaseAsync(caseId, Request.Headers["X-Role"], cancellationToken),
            "Case put on hold.");
    }

    private async Task<ActionResult<WorkflowTransitionResponse>> ExecuteTransition(
        string entityType,
        Guid entityId,
        Func<Task<(Guid Id, string Status)>> action,
        string message)
    {
        try
        {
            var result = await action();
            return Ok(new WorkflowTransitionResponse(entityType, result.Id, result.Status, DateTime.UtcNow, message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new WorkflowValidationFailureResponse(entityType, entityId, message, ex.Message, DateTime.UtcNow));
        }
    }
}
```

## FILE: C:\Projects\veritas-atlas\apps\api\VeritasAtlas.Api\Controllers\WorkflowAuditController.cs
```
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Workflow;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/workflow-audit")]
public class WorkflowAuditController : ControllerBase
{
    private readonly WorkflowAuditStore _workflowAuditStore;
    private readonly WorkflowIntegrityService _workflowIntegrityService;

    public WorkflowAuditController(
        WorkflowAuditStore workflowAuditStore,
        WorkflowIntegrityService workflowIntegrityService)
    {
        _workflowAuditStore = workflowAuditStore;
        _workflowIntegrityService = workflowIntegrityService;
    }

    [HttpGet("entries")]
    public async Task<ActionResult<IReadOnlyList<WorkflowAuditEntryResponse>>> GetEntries(CancellationToken cancellationToken)
    {
        var entries = (await _workflowAuditStore.GetAllAsync(cancellationToken))
            .Select(x => new WorkflowAuditEntryResponse(
                x.Id,
                x.EntityType,
                x.EntityId,
                x.ActionName,
                x.PreviousStatus,
                x.NextStatus,
                x.Role,
                x.Success,
                x.Message,
                x.TimestampUtc))
            .ToList();

        return Ok(entries);
    }

    [HttpPost("clear")]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
    {
        await _workflowAuditStore.ClearAsync(cancellationToken);
        return Ok(new { Cleared = true, TimestampUtc = DateTime.UtcNow });
    }

    [HttpGet("rules")]
    public IActionResult GetRules()
    {
        return Ok(new
        {
            Transitions = _workflowIntegrityService.GetRules(),
            Roles = _workflowIntegrityService.GetRolePolicies(),
            TimestampUtc = DateTime.UtcNow
        });
    }
}
```

## FILE: C:\Projects\veritas-atlas\apps\api\VeritasAtlas.Api\Infrastructure\Auth\JwtTokenService.cs
```
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
```

## FILE: C:\Projects\veritas-atlas\apps\api\VeritasAtlas.Api\Infrastructure\Auth\DevUserStore.cs
```
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
```

## FILE: C:\Projects\veritas-atlas\apps\api\VeritasAtlas.Api\Infrastructure\Auth\AuthRequestContext.cs
```
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
```

## FILE: C:\Projects\veritas-atlas\apps\api\VeritasAtlas.Infrastructure\Extensions\ServiceCollectionExtensions.cs
```
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVeritasAtlasInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

        services.AddDbContext<VeritasAtlasDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IPersonService, PersonService>();
        services.AddScoped<ISourceService, SourceService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IEvidenceService, EvidenceService>();
        services.AddScoped<IStatementService, StatementService>();
        services.AddScoped<StatementService>();
        services.AddScoped<IClaimService, ClaimService>();
        services.AddScoped<ContradictionSliceService>();
        services.AddScoped<WorkflowTransitionService>();
        services.AddSingleton<PersistentWorkflowAuditService>();
        services.AddSingleton<ScenarioPersistenceService>();
        services.AddSingleton<WorkflowAuditStore>();
        services.AddSingleton<WorkflowIntegrityService>();
        services.AddScoped<WorkflowOrchestratorService>();
        services.AddScoped<ClaimSliceService>();
        services.AddScoped<IContradictionService, ContradictionService>();
        services.AddScoped<ICaseService, CaseService>();
        services.AddScoped<IConfidenceService, ConfidenceService>();
        services.AddScoped<IAgentRunService, AgentRunService>();
        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<IPublicationService, PublicationService>();

        return services;
    }
}



```

## FILE: C:\Projects\veritas-atlas\apps\api\VeritasAtlas.Infrastructure\Services\WorkflowOrchestratorService.cs
```
using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class WorkflowOrchestratorService
{
    private readonly VeritasAtlasDbContext _dbContext;
    private readonly WorkflowTransitionService _workflowTransitionService;
    private readonly WorkflowIntegrityService _workflowIntegrityService;
    private readonly WorkflowAuditStore _workflowAuditStore;

    public WorkflowOrchestratorService(
        VeritasAtlasDbContext dbContext,
        WorkflowTransitionService workflowTransitionService,
        WorkflowIntegrityService workflowIntegrityService,
        WorkflowAuditStore workflowAuditStore)
    {
        _dbContext = dbContext;
        _workflowTransitionService = workflowTransitionService;
        _workflowIntegrityService = workflowIntegrityService;
        _workflowAuditStore = workflowAuditStore;
    }

    public async Task<(Guid Id, string Status)> SubmitCaseAsync(Guid caseId, string? role, CancellationToken cancellationToken = default)
        => await RunCaseActionAsync("SubmitCase", caseId, "InReview", role, _workflowTransitionService.SubmitCaseAsync, cancellationToken);

    public async Task<(Guid Id, string Status)> ApproveCaseAsync(Guid caseId, string? role, CancellationToken cancellationToken = default)
        => await RunCaseActionAsync("ApproveCase", caseId, "Approved", role, _workflowTransitionService.ApproveCaseAsync, cancellationToken);

    public async Task<(Guid Id, string Status)> RejectCaseAsync(Guid caseId, string? role, CancellationToken cancellationToken = default)
        => await RunCaseActionAsync("RejectCase", caseId, "Rejected", role, _workflowTransitionService.RejectCaseAsync, cancellationToken);

    public async Task<(Guid Id, string Status)> PreparePublicationAsync(Guid caseId, string? role, CancellationToken cancellationToken = default)
        => await RunCaseActionAsync("PreparePublication", caseId, "ReadyForPublication", role, _workflowTransitionService.PreparePublicationAsync, cancellationToken);

    public async Task<(Guid Id, string Status)> PublishCaseAsync(Guid caseId, string? role, CancellationToken cancellationToken = default)
        => await RunCaseActionAsync("PublishCase", caseId, "Published", role, _workflowTransitionService.PublishCaseAsync, cancellationToken);

    public async Task<(Guid Id, string Status)> HoldCaseAsync(Guid caseId, string? role, CancellationToken cancellationToken = default)
        => await RunCaseActionAsync("HoldCase", caseId, "OnHold", role, _workflowTransitionService.HoldCaseAsync, cancellationToken);

    public async Task<(Guid Id, string Status)> ResolveContradictionAsync(Guid contradictionId, string? role, CancellationToken cancellationToken = default)
        => await RunContradictionActionAsync("ResolveContradiction", contradictionId, "Resolved", role, _workflowTransitionService.ResolveContradictionAsync, cancellationToken);

    public async Task<(Guid Id, string Status)> EscalateContradictionAsync(Guid contradictionId, string? role, CancellationToken cancellationToken = default)
        => await RunContradictionActionAsync("EscalateContradiction", contradictionId, "UnderReview", role, _workflowTransitionService.EscalateContradictionAsync, cancellationToken);

    public async Task<(Guid Id, string Status)> SendClaimToReviewAsync(Guid claimId, string? role, CancellationToken cancellationToken = default)
        => await RunClaimActionAsync("SendClaimToReview", claimId, "InReview", role, _workflowTransitionService.SendClaimToReviewAsync, cancellationToken);

    public async Task<(Guid Id, string Status)> ReturnClaimForEditAsync(Guid claimId, string? role, CancellationToken cancellationToken = default)
        => await RunClaimActionAsync("ReturnClaimForEdit", claimId, "Draft", role, _workflowTransitionService.ReturnClaimForEditAsync, cancellationToken);

    public async Task<(Guid Id, string Status)> CompleteReviewAsync(Guid reviewId, string? role, CancellationToken cancellationToken = default)
        => await RunReviewActionAsync("CompleteReview", reviewId, "Completed", role, _workflowTransitionService.CompleteReviewAsync, cancellationToken);

    public async Task<(Guid Id, string Status)> ReopenReviewAsync(Guid reviewId, string? role, CancellationToken cancellationToken = default)
        => await RunReviewActionAsync("ReopenReview", reviewId, "Open", role, _workflowTransitionService.ReopenReviewAsync, cancellationToken);

    public async Task<(Guid CaseId, Guid ClaimAId, Guid ClaimBId, Guid ContradictionId, string CaseStatus, string ContradictionStatus)> SeedLifecycleAsync(string? role, CancellationToken cancellationToken = default)
    {
        _workflowIntegrityService.EnsureRoleAllowed("SeedLifecycle", role);

        var seeded = await _workflowTransitionService.SeedLifecycleAsync(cancellationToken);

        await _workflowAuditStore.AddAsync(new WorkflowAuditRecord(
            Guid.NewGuid(),
            "Seed",
            seeded.CaseId,
            "SeedLifecycle",
            null,
            seeded.CaseStatus,
            NormalizeRole(role),
            true,
            "Seeded lifecycle scenario.",
            DateTime.UtcNow), cancellationToken);

        return seeded;
    }

    private async Task<(Guid Id, string Status)> RunCaseActionAsync(string actionName, Guid caseId, string nextStatus, string? role, Func<Guid, CancellationToken, Task<(Guid Id, string Status)>> operation, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Cases.FirstOrDefaultAsync(x => x.Id == caseId, cancellationToken)
            ?? throw new InvalidOperationException($"Case '{caseId}' was not found.");

        var previous = entity.Status.ToString();
        _workflowIntegrityService.EnsureRoleAllowed(actionName, role);
        _workflowIntegrityService.EnsureTransitionAllowed("Case", previous, nextStatus);

        try
        {
            var result = await operation(caseId, cancellationToken);
            await _workflowAuditStore.AddAsync(new WorkflowAuditRecord(Guid.NewGuid(), "Case", caseId, actionName, previous, result.Status, NormalizeRole(role), true, "Transition succeeded.", DateTime.UtcNow), cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            await _workflowAuditStore.AddAsync(new WorkflowAuditRecord(Guid.NewGuid(), "Case", caseId, actionName, previous, nextStatus, NormalizeRole(role), false, ex.Message, DateTime.UtcNow), cancellationToken);
            throw;
        }
    }

    private async Task<(Guid Id, string Status)> RunClaimActionAsync(string actionName, Guid claimId, string nextStatus, string? role, Func<Guid, CancellationToken, Task<(Guid Id, string Status)>> operation, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Claims.FirstOrDefaultAsync(x => x.Id == claimId, cancellationToken)
            ?? throw new InvalidOperationException($"Claim '{claimId}' was not found.");

        var previous = entity.Status.ToString();
        _workflowIntegrityService.EnsureRoleAllowed(actionName, role);
        _workflowIntegrityService.EnsureTransitionAllowed("Claim", previous, nextStatus);

        try
        {
            var result = await operation(claimId, cancellationToken);
            await _workflowAuditStore.AddAsync(new WorkflowAuditRecord(Guid.NewGuid(), "Claim", claimId, actionName, previous, result.Status, NormalizeRole(role), true, "Transition succeeded.", DateTime.UtcNow), cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            await _workflowAuditStore.AddAsync(new WorkflowAuditRecord(Guid.NewGuid(), "Claim", claimId, actionName, previous, nextStatus, NormalizeRole(role), false, ex.Message, DateTime.UtcNow), cancellationToken);
            throw;
        }
    }

    private async Task<(Guid Id, string Status)> RunContradictionActionAsync(string actionName, Guid contradictionId, string nextStatus, string? role, Func<Guid, CancellationToken, Task<(Guid Id, string Status)>> operation, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Contradictions.FirstOrDefaultAsync(x => x.Id == contradictionId, cancellationToken)
            ?? throw new InvalidOperationException($"Contradiction '{contradictionId}' was not found.");

        var previous = entity.Status.ToString();
        _workflowIntegrityService.EnsureRoleAllowed(actionName, role);
        _workflowIntegrityService.EnsureTransitionAllowed("Contradiction", previous, nextStatus);

        try
        {
            var result = await operation(contradictionId, cancellationToken);
            await _workflowAuditStore.AddAsync(new WorkflowAuditRecord(Guid.NewGuid(), "Contradiction", contradictionId, actionName, previous, result.Status, NormalizeRole(role), true, "Transition succeeded.", DateTime.UtcNow), cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            await _workflowAuditStore.AddAsync(new WorkflowAuditRecord(Guid.NewGuid(), "Contradiction", contradictionId, actionName, previous, nextStatus, NormalizeRole(role), false, ex.Message, DateTime.UtcNow), cancellationToken);
            throw;
        }
    }

    private async Task<(Guid Id, string Status)> RunReviewActionAsync(string actionName, Guid reviewId, string nextStatus, string? role, Func<Guid, CancellationToken, Task<(Guid Id, string Status)>> operation, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Reviews.FirstOrDefaultAsync(x => x.Id == reviewId, cancellationToken)
            ?? throw new InvalidOperationException($"Review '{reviewId}' was not found.");

        var previous = entity.Status.ToString();
        _workflowIntegrityService.EnsureRoleAllowed(actionName, role);
        _workflowIntegrityService.EnsureTransitionAllowed("Review", previous, nextStatus);

        try
        {
            var result = await operation(reviewId, cancellationToken);
            await _workflowAuditStore.AddAsync(new WorkflowAuditRecord(Guid.NewGuid(), "Review", reviewId, actionName, previous, result.Status, NormalizeRole(role), true, "Transition succeeded.", DateTime.UtcNow), cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            await _workflowAuditStore.AddAsync(new WorkflowAuditRecord(Guid.NewGuid(), "Review", reviewId, actionName, previous, nextStatus, NormalizeRole(role), false, ex.Message, DateTime.UtcNow), cancellationToken);
            throw;
        }
    }

    private static string NormalizeRole(string? role)
    {
        return string.IsNullOrWhiteSpace(role) ? "anonymous" : role.Trim().ToLowerInvariant();
    }
}
```

## FILE: C:\Projects\veritas-atlas\apps\api\VeritasAtlas.Infrastructure\Services\WorkflowAuditStore.cs
```
namespace VeritasAtlas.Infrastructure.Services;

public sealed class WorkflowAuditStore
{
    private readonly PersistentWorkflowAuditService _persistentWorkflowAuditService;

    public WorkflowAuditStore(PersistentWorkflowAuditService persistentWorkflowAuditService)
    {
        _persistentWorkflowAuditService = persistentWorkflowAuditService;
    }

    public async Task AddAsync(WorkflowAuditRecord record, CancellationToken cancellationToken = default)
    {
        await _persistentWorkflowAuditService.AppendAsync(record, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowAuditRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _persistentWorkflowAuditService.GetAllAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _persistentWorkflowAuditService.ClearAsync(cancellationToken);
    }
}

public sealed record WorkflowAuditRecord(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string ActionName,
    string? PreviousStatus,
    string NextStatus,
    string Role,
    bool Success,
    string Message,
    DateTime TimestampUtc);
```

## FILE: C:\Projects\veritas-atlas\apps\api\VeritasAtlas.Infrastructure\Services\PersistentWorkflowAuditService.cs
```
using System.Text.Json;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class PersistentWorkflowAuditService
{
    private readonly string _auditPath;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public PersistentWorkflowAuditService()
    {
        var root = AppContext.BaseDirectory;
        _auditPath = Path.Combine(root, "workflow-audit-store.json");
    }

    public async Task AppendAsync(WorkflowAuditRecord record, CancellationToken cancellationToken = default)
    {
        var records = await ReadAllAsync(cancellationToken);
        records.Add(record);

        await using var stream = File.Create(_auditPath);
        await JsonSerializer.SerializeAsync(stream, records, JsonOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowAuditRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var records = await ReadAllAsync(cancellationToken);
        return records.OrderByDescending(x => x.TimestampUtc).ToList();
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(_auditPath);
        await JsonSerializer.SerializeAsync(stream, new List<WorkflowAuditRecord>(), JsonOptions, cancellationToken);
    }

    private async Task<List<WorkflowAuditRecord>> ReadAllAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_auditPath))
        {
            return new List<WorkflowAuditRecord>();
        }

        await using var stream = File.OpenRead(_auditPath);
        var result = await JsonSerializer.DeserializeAsync<List<WorkflowAuditRecord>>(stream, JsonOptions, cancellationToken);
        return result ?? new List<WorkflowAuditRecord>();
    }
}
```

## FILE: C:\Projects\veritas-atlas\tools\tests\Run-Phase11-Auth-Verification.ps1
```
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
```

## FILE: C:\Projects\veritas-atlas\tools\tests\Run-Phase11_1-Auth-Debug-Verification.ps1
```
param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir,
    [string]$BaseUrl = "http://localhost:5091"
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
$diag = Join-Path $RootDir "_diagnostics\phase11_1-tests-$timestamp"
Ensure-Dir $diag
$report = Join-Path $diag "phase11_1-auth-debug-report.md"
$stdoutLog = Join-Path $diag "api-stdout.log"
$stderrLog = Join-Path $diag "api-stderr.log"

Set-Content -Path $report -Value "# Phase 11.1 Auth Debug Verification Report`r`n" -Encoding UTF8
Add-Content -Path $report -Value ("Generated: " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
Add-Content -Path $report -Value ("BaseUrl: " + $BaseUrl)
Add-Content -Path $report -Value ""

$apiProcess = $null
$failed = $false

Push-Location (Join-Path $RootDir "apps\api\VeritasAtlas.Api")
try {
    $env:ASPNETCORE_URLS = $BaseUrl
    $apiProcess = Start-Process "dotnet" -ArgumentList "run" -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog -PassThru
    Start-Sleep -Seconds 8

    $publicDiag = Invoke-Json -Url "$BaseUrl/api/v1/auth-diagnostics/public"
    Add-Result -ReportPath $report -Name "Public diagnostics endpoint works" -Passed $publicDiag.Success -Detail $(if ($publicDiag.Success) { "Issuer=$($publicDiag.Data.jwtIssuer), Audience=$($publicDiag.Data.jwtAudience)" } else { $publicDiag.Message })
    if (-not $publicDiag.Success) { $failed = $true }

    $badLogin = Invoke-Json -Url "$BaseUrl/api/v1/auth/login" -Method POST -Body @{ username = "admin1"; password = "wrong" }
    Add-Result -ReportPath $report -Name "Invalid login rejected" -Passed (-not $badLogin.Success) -Detail $badLogin.Message
    if ($badLogin.Success) { $failed = $true }

    $goodLogin = Invoke-Json -Url "$BaseUrl/api/v1/auth/login" -Method POST -Body @{ username = "admin1"; password = "password123" }
    Add-Result -ReportPath $report -Name "Valid login accepted" -Passed $goodLogin.Success -Detail $goodLogin.Message
    if (-not $goodLogin.Success) { $failed = $true; throw "Valid login failed." }

    $token = $goodLogin.Data.accessToken
    $headers = @{ Authorization = "Bearer $token" }

    $me = Invoke-Json -Url "$BaseUrl/api/v1/auth/me" -Method GET -Headers $headers
    Add-Result -ReportPath $report -Name "Auth me works" -Passed $me.Success -Detail $(if ($me.Success) { "$($me.Data.username) / $($me.Data.role)" } else { $me.Message })
    if (-not $me.Success) { $failed = $true }

    $protectedDiag = Invoke-Json -Url "$BaseUrl/api/v1/auth-diagnostics/protected" -Method GET -Headers $headers
    Add-Result -ReportPath $report -Name "Protected diagnostics works" -Passed $protectedDiag.Success -Detail $(if ($protectedDiag.Success) { "Claims=$($protectedDiag.Data.claims.Count)" } else { $protectedDiag.Message })
    if (-not $protectedDiag.Success) { $failed = $true }

    $seed = Invoke-Json -Url "$BaseUrl/api/v1/actions/seed/lifecycle" -Method POST -Headers $headers
    Add-Result -ReportPath $report -Name "Protected workflow accepts token" -Passed $seed.Success -Detail $seed.Message
    if (-not $seed.Success) { $failed = $true }

    $audit = Invoke-Json -Url "$BaseUrl/api/v1/workflow-audit/entries" -Method GET -Headers $headers
    Add-Result -ReportPath $report -Name "Protected audit accepts token" -Passed $audit.Success -Detail $audit.Message
    if (-not $audit.Success) { $failed = $true }
}
finally {
    Pop-Location
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force
    }
}

if ($failed) {
    Write-Error "Phase 11.1 auth debug verification failed. See report: $report"
    exit 1
}
else {
    Write-Host "Phase 11.1 auth debug verification passed. Report: $report" -ForegroundColor Green
}
```

## FILE: C:\Projects\veritas-atlas\tools\tests\Run-Phase12-Verification.ps1
```
param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir,
    [string]$BaseUrl = "http://localhost:5091"
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

function Invoke-Api {
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
$diag = Join-Path $RootDir "_diagnostics\phase12-tests-$timestamp"
Ensure-Dir $diag
$report = Join-Path $diag "phase12-report.md"
$stdoutLog = Join-Path $diag "api-stdout.log"
$stderrLog = Join-Path $diag "api-stderr.log"

Set-Content -Path $report -Value "# Phase 12 Verification Report`r`n" -Encoding UTF8
Add-Content -Path $report -Value ("Generated: " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
Add-Content -Path $report -Value ""

$apiProcess = $null
$failed = $false

Push-Location (Join-Path $RootDir "apps\api\VeritasAtlas.Api")
try {
    $apiProcess = Start-Process "dotnet" -ArgumentList "run" -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog -PassThru
    Start-Sleep -Seconds 8

    $login = Invoke-Api -Url "$BaseUrl/api/v1/auth/login" -Method POST -Body @{ username = "admin1"; password = "password123" }
    Add-Result -ReportPath $report -Name "Login works" -Passed $login.Success -Detail $login.Message
    if (-not $login.Success) { $failed = $true; throw "Login failed." }

    $headers = @{ Authorization = "Bearer $($login.Data.accessToken)" }

    $seed = Invoke-Api -Url "$BaseUrl/api/v1/actions/seed/lifecycle" -Method POST -Headers $headers
    Add-Result -ReportPath $report -Name "Seed lifecycle works" -Passed $seed.Success -Detail $seed.Message
    if (-not $seed.Success) { $failed = $true }

    $snapshot = Invoke-Api -Url "$BaseUrl/api/v1/persistence/snapshot" -Method GET -Headers $headers
    $snapshotPass = $snapshot.Success -and $snapshot.Data.exists -eq $true
    Add-Result -ReportPath $report -Name "Snapshot exists after seed" -Passed $snapshotPass -Detail $(if ($snapshot.Success) { "Exists: $($snapshot.Data.exists)" } else { $snapshot.Message })
    if (-not $snapshotPass) { $failed = $true }

    if ($seed.Success) {
        $prepare = Invoke-Api -Url "$BaseUrl/api/v1/publication-workflow/cases/$($seed.Data.caseId)/prepare" -Method POST -Headers $headers
        Add-Result -ReportPath $report -Name "Prepare publication works" -Passed $prepare.Success -Detail $prepare.Message
        if (-not $prepare.Success) { $failed = $true }

        $resolve = Invoke-Api -Url "$BaseUrl/api/v1/actions/contradictions/$($seed.Data.contradictionId)/resolve" -Method POST -Headers $headers
        Add-Result -ReportPath $report -Name "Resolve contradiction works" -Passed $resolve.Success -Detail $resolve.Message
        if (-not $resolve.Success) { $failed = $true }

        $snapshotAfter = Invoke-Api -Url "$BaseUrl/api/v1/persistence/snapshot" -Method GET -Headers $headers
        $snapshotAfterPass = $snapshotAfter.Success -and $snapshotAfter.Data.caseStatus -ne $null -and $snapshotAfter.Data.contradictionStatus -eq "Resolved"
        Add-Result -ReportPath $report -Name "Snapshot updates after transitions" -Passed $snapshotAfterPass -Detail $(if ($snapshotAfter.Success) { "CaseStatus=$($snapshotAfter.Data.caseStatus), ContradictionStatus=$($snapshotAfter.Data.contradictionStatus)" } else { $snapshotAfter.Message })
        if (-not $snapshotAfterPass) { $failed = $true }
    }

    $reset = Invoke-Api -Url "$BaseUrl/api/v1/persistence/reset" -Method POST -Headers $headers
    Add-Result -ReportPath $report -Name "Reset works" -Passed $reset.Success -Detail $reset.Message
    if (-not $reset.Success) { $failed = $true }

    $snapshotCleared = Invoke-Api -Url "$BaseUrl/api/v1/persistence/snapshot" -Method GET -Headers $headers
    $snapshotClearedPass = $snapshotCleared.Success -and $snapshotCleared.Data.exists -eq $false
    Add-Result -ReportPath $report -Name "Snapshot cleared after reset" -Passed $snapshotClearedPass -Detail $(if ($snapshotCleared.Success) { "Exists: $($snapshotCleared.Data.exists)" } else { $snapshotCleared.Message })
    if (-not $snapshotClearedPass) { $failed = $true }

    $audit = Invoke-Api -Url "$BaseUrl/api/v1/workflow-audit/entries" -Method GET -Headers $headers
    Add-Result -ReportPath $report -Name "Audit endpoint accessible" -Passed $audit.Success -Detail $audit.Message
    if (-not $audit.Success) { $failed = $true }
}
finally {
    Pop-Location
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force
    }
}

if ($failed) {
    Write-Error "Phase 12 verification failed. See report: $report"
    exit 1
}
else {
    Write-Host "Phase 12 verification passed. Report: $report" -ForegroundColor Green
}
```

# Package References

# dotnet list package

  Identification des projets ├á restaurer...
  Tous les projets sont ├á jour pour la restauration.
Le projet 'VeritasAtlas.Api' a les r├⌐f├⌐rences de package suivantes
   [net10.0]: 
   Package de niveau sup├⌐rieur                          Demand├⌐   R├⌐solu
   > AspNetCore.HealthChecks.NpgSql                     9.0.0     9.0.0 
   > Microsoft.AspNetCore.Authentication.JwtBearer      10.0.5    10.0.5
   > Microsoft.AspNetCore.OpenApi                       10.0.0    10.0.0
   > Microsoft.EntityFrameworkCore.Design               10.0.5    10.0.5
   > Swashbuckle.AspNetCore                             10.0.1    10.0.1
   > System.IdentityModel.Tokens.Jwt                    8.17.0    8.17.0



# Program.cs Line Numbers

```
   1: var jwtSecret = builder.Configuration["Auth:Jwt:Secret"] ?? "veritas-atlas-dev-secret-key-change-in-production-123456";
   2: var jwtIssuer = builder.Configuration["Auth:Jwt:Issuer"] ?? "VeritasAtlas";
   3: var jwtAudience = builder.Configuration["Auth:Jwt:Audience"] ?? "VeritasAtlasUsers";
   4: using Microsoft.AspNetCore.Authentication.JwtBearer;
   5: using Microsoft.IdentityModel.Tokens;
   6: using System.Text;
   7: using Microsoft.AspNetCore.Diagnostics.HealthChecks;
   8: using VeritasAtlas.Api.Infrastructure;
   9: using VeritasAtlas.Application.Extensions;
  10: using VeritasAtlas.Infrastructure.Extensions;
  11: 
  12: var builder = WebApplication.CreateBuilder(args);
  13: 
  14: builder.Services.AddControllers();
  15: builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.WorkflowRequestContext>();
  16: builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.WorkflowAuthorizationService>();
  17: builder.Services.AddEndpointsApiExplorer();
  18: builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.WorkflowRequestContext>();
  19: builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.WorkflowAuthorizationService>();
  20: builder.Services.AddSwaggerGen();
  21: builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.WorkflowRequestContext>();
  22: builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.WorkflowAuthorizationService>();
  23: 
  24: builder.Services.AddVeritasAtlasApplication();
  25: builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.WorkflowRequestContext>();
  26: builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.WorkflowAuthorizationService>();
  27: builder.Services.AddVeritasAtlasInfrastructure(builder.Configuration);
  28: builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.WorkflowRequestContext>();
  29: builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.WorkflowAuthorizationService>();
  30: 
  31: var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
  32:     ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");
  33: 
  34: builder.Services.AddHealthChecks()
  35:     .AddNpgSql(
  36:         connectionString,
  37:         name: "postgresql",
  38:         tags: new[] { "db", "postgres", "ready" });
  39: 
  40: 
  41: builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.Auth.JwtTokenService>();
  42: builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.Auth.DevUserStore>();
  43: builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.Auth.AuthRequestContext>();
  44: 
  45: 
  46: 
  47: 
  48: builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.Auth.JwtTokenService>();
  49: builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.Auth.DevUserStore>();
  50: builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.Auth.AuthRequestContext>();
  51: 
  52: 
  53: builder.Services
  54:     .AddAuthentication(options =>
  55:     {
  56:         options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
  57:         options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
  58:         options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
  59:     })
  60:     .AddJwtBearer(options =>
  61:     {
  62:         options.RequireHttpsMetadata = false;
  63:         options.SaveToken = true;
  64:         options.TokenValidationParameters = new TokenValidationParameters
  65:         {
  66:             ValidateIssuer = true,
  67:             ValidIssuer = jwtIssuer,
  68:             ValidateAudience = true,
  69:             ValidAudience = jwtAudience,
  70:             ValidateIssuerSigningKey = true,
  71:             IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
  72:             ValidateLifetime = true,
  73:             ClockSkew = TimeSpan.Zero,
  74:             NameClaimType = System.Security.Claims.ClaimTypes.Name,
  75:             RoleClaimType = System.Security.Claims.ClaimTypes.Role
  76:         };
  77:     });
  78: 
  79: builder.Services.AddAuthorization();
  80: var app = builder.Build();
  81: 
  82: app.UseMiddleware<CorrelationIdMiddleware>();
  83: app.UseMiddleware<RequestLoggingMiddleware>();
  84: app.UseMiddleware<ExceptionHandlingMiddleware>();
  85: 
  86: if (app.Environment.IsDevelopment())
  87: {
  88:     app.UseSwagger();
  89:     app.UseSwaggerUI();
  90: }
  91: 
  92: app.UseHttpsRedirection();
  93: app.UseAuthentication();
  94: app.UseAuthorization();
  95: app.MapControllers();
  96: 
  97: app.MapHealthChecks("/health");
  98: 
  99: app.MapHealthChecks("/health/db", new HealthCheckOptions
 100: {
 101:     Predicate = registration => registration.Tags.Contains("db")
 102: });
 103: 
 104: app.Run();
```

