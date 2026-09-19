using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.RateLimiting;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHealthChecks();

builder.Services.AddSingleton<DevUserStore>();
builder.Services.AddSingleton<ProductionBootstrapAuthenticator>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("auth-login", limiter =>
    {
        limiter.PermitLimit = 6;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
});
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<AuthRequestContext>();

var corsOrigins = RuntimeSecurity.ResolveCorsOrigins(
    builder.Configuration,
    builder.Environment.EnvironmentName);

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (corsOrigins.Length > 0)
        {
            policy
                .WithOrigins(corsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

builder.Services.AddVeritasAtlasInfrastructure(builder.Configuration);

ProductionBootstrapAuthenticator.ValidateConfiguration(
    builder.Configuration, builder.Environment.EnvironmentName);

var jwtSecret = RuntimeSecurity.ResolveJwtSecret(
    builder.Configuration,
    builder.Environment.EnvironmentName);

var jwtIssuer =
    builder.Configuration["Auth:Jwt:Issuer"]
    ?? "VeritasAtlas";

var jwtAudience =
    builder.Configuration["Auth:Jwt:Audience"]
    ?? "VeritasAtlasUsers";

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.SaveToken = true;
        options.MapInboundClaims = true;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseRouting();
app.UseRateLimiter();
app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live");
app.MapControllers();

app.Run();
