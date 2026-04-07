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


