using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Infrastructure.Configurations;

namespace VeritasAtlas.Infrastructure.Persistence;

public sealed class VeritasAtlasDbContext : DbContext
{
    public VeritasAtlasDbContext(DbContextOptions<VeritasAtlasDbContext> options) : base(options)
    {
    }

    public DbSet<Person> Persons => Set<Person>();
    public DbSet<Alias> Aliases => Set<Alias>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<Evidence> Evidences => Set<Evidence>();
    public DbSet<Statement> Statements => Set<Statement>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<Contradiction> Contradictions => Set<Contradiction>();
    public DbSet<Case> Cases => Set<Case>();
    public DbSet<ConfidenceScore> ConfidenceScores => Set<ConfidenceScore>();
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Publication> Publications => Set<Publication>();
    public DbSet<RevokedAccessToken> RevokedAccessTokens => Set<RevokedAccessToken>();
    public DbSet<TenantSourceRegistryEntry> TenantSourceRegistryEntries => Set<TenantSourceRegistryEntry>();

    public DbSet<TenantRawRetrieval> TenantRawRetrievals => Set<TenantRawRetrieval>();

    public DbSet<TenantFetchAttempt> TenantFetchAttempts => Set<TenantFetchAttempt>();

    public DbSet<TenantParseRun> TenantParseRuns => Set<TenantParseRun>();

    public DbSet<TenantRetrievalAttestation> TenantRetrievalAttestations => Set<TenantRetrievalAttestation>();

    public DbSet<TenantSourceFetchPolicy> TenantSourceFetchPolicies => Set<TenantSourceFetchPolicy>();

    public DbSet<TenantInvestigation> TenantInvestigations => Set<TenantInvestigation>();

    public DbSet<TenantAuditEvent> TenantAuditEvents => Set<TenantAuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PersonConfiguration());
        modelBuilder.ApplyConfiguration(new AliasConfiguration());
        modelBuilder.ApplyConfiguration(new SourceConfiguration());
        modelBuilder.ApplyConfiguration(new DocumentConfiguration());
        modelBuilder.ApplyConfiguration(new EvidenceConfiguration());
        modelBuilder.ApplyConfiguration(new StatementConfiguration());
        modelBuilder.ApplyConfiguration(new ClaimConfiguration());
        modelBuilder.ApplyConfiguration(new ContradictionConfiguration());
        modelBuilder.ApplyConfiguration(new CaseConfiguration());
        modelBuilder.ApplyConfiguration(new ConfidenceScoreConfiguration());
        modelBuilder.ApplyConfiguration(new AgentRunConfiguration());
        modelBuilder.ApplyConfiguration(new ReviewConfiguration());
        modelBuilder.ApplyConfiguration(new PublicationConfiguration());
        modelBuilder.ApplyConfiguration(new RevokedAccessTokenConfiguration());
        modelBuilder.ApplyConfiguration(new TenantSourceRegistryEntryConfiguration());
        modelBuilder.ApplyConfiguration(new TenantRawRetrievalConfiguration());
        modelBuilder.ApplyConfiguration(new TenantFetchAttemptConfiguration());
        modelBuilder.ApplyConfiguration(new TenantParseRunConfiguration());
        modelBuilder.ApplyConfiguration(new TenantRetrievalAttestationConfiguration());
        modelBuilder.ApplyConfiguration(new TenantSourceFetchPolicyConfiguration());
        modelBuilder.ApplyConfiguration(new TenantInvestigationConfiguration());
        modelBuilder.ApplyConfiguration(new TenantAuditEventConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
