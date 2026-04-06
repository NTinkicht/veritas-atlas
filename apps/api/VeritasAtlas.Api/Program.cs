using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using VeritasAtlas.Api.Infrastructure;
using VeritasAtlas.Application.Extensions;
using VeritasAtlas.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddVeritasAtlasApplication();
builder.Services.AddVeritasAtlasInfrastructure(builder.Configuration);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

builder.Services.AddHealthChecks()
    .AddNpgSql(
        connectionString,
        name: "postgresql",
        tags: new[] { "db", "postgres", "ready" });

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
app.UseAuthorization();
app.MapControllers();

app.MapHealthChecks("/health");

app.MapHealthChecks("/health/db", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("db")
});

app.Run();
