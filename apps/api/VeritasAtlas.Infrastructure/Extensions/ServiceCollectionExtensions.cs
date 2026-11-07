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
        services.AddScoped<IAccessTokenRevocationStore, EfAccessTokenRevocationStore>();
        services.AddScoped<ITenantSourceRegistry, EfTenantSourceRegistry>();
        services.AddScoped<ITenantSourceFetchPolicyRegistry, EfTenantSourceFetchPolicyRegistry>();
        services.AddScoped<ITenantRawRetrievalLedger, EfTenantRawRetrievalLedger>();
        services.AddScoped<ITenantFetchAttemptLedger, EfTenantFetchAttemptLedger>();
        services.AddSingleton<IHostAddressResolver, SystemHostAddressResolver>();
        services.AddSingleton<RawSourceHttpClient>(sp =>
        {
            var resolver = sp.GetRequiredService<IHostAddressResolver>();
            return new RawSourceHttpClient(new HttpClient(new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                ConnectCallback = async (context, ct) =>
                    await GuardedConnect.ConnectAsync(context.DnsEndPoint, resolver, ct),
            }) { Timeout = Timeout.InfiniteTimeSpan });
        });
        services.AddScoped<IRawSourceFetcher>(sp => new HttpRawSourceFetcher(
            sp.GetRequiredService<RawSourceHttpClient>().Client,
            sp.GetRequiredService<IHostAddressResolver>(),
            sp.GetRequiredService<ITenantSourceFetchPolicyRegistry>(),
            sp.GetRequiredService<ITenantFetchAttemptLedger>(),
            sp.GetRequiredService<ITenantRawRetrievalLedger>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System));
        services.AddScoped<ITenantParseRunLedger, EfTenantParseRunLedger>();
        services.AddScoped<ITenantRetrievalAttestationLedger, EfTenantRetrievalAttestationLedger>();
        services.AddScoped<ITenantRetrievalCheckpointStore, EfTenantRetrievalCheckpointStore>();
        services.AddScoped<ITenantPublicationRevisionStore>(sp => new EfTenantPublicationRevisionStore(
            sp.GetRequiredService<VeritasAtlasDbContext>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System));
        services.AddScoped<IPublicPublicationReader>(sp => new EfPublicPublicationReader(
            sp.GetRequiredService<VeritasAtlasDbContext>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System));
        services.AddScoped<ITenantInvestigationStore, EfTenantInvestigationStore>();
        services.AddSingleton<ISourceFreshnessPolicyResolver, FailClosedSourceFreshnessPolicyResolver>();
        services.AddSingleton<ISourceRevisionEvidenceProvider, FailClosedSourceRevisionEvidenceProvider>();
        services.AddScoped<IEvidenceFreshnessService>(sp => new EfEvidenceFreshnessService(
            sp.GetRequiredService<VeritasAtlasDbContext>(),
            sp.GetRequiredService<ISourceFreshnessPolicyResolver>(),
            sp.GetRequiredService<ISourceRevisionEvidenceProvider>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System));
        services.AddScoped<IEvidenceSearchService, EfEvidenceSearchService>();
        services.AddScoped<IClaimEvidenceValidationService, EfClaimEvidenceValidationService>();
        services.AddScoped<ITenantAuditEventStore, EfTenantAuditEventStore>();

        return services;
    }
}


