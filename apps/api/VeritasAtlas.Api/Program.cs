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
