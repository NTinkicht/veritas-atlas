using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.ValueObjects;

namespace VeritasAtlas.Infrastructure.Configurations;

internal static class ValueConversionHelpers
{
    public static string? DocumentSpanToString(DocumentSpan? value)
    {
        return value.HasValue ? $"{value.Value.StartOffset}:{value.Value.EndOffset}" : null;
    }

    public static DocumentSpan? StringToDocumentSpan(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split(':');
        if (parts.Length != 2)
        {
            return null;
        }

        if (!int.TryParse(parts[0], out var start))
        {
            return null;
        }

        if (!int.TryParse(parts[1], out var end))
        {
            return null;
        }

        return new DocumentSpan(start, end);
    }

    public static decimal ConfidenceValueToDecimal(ConfidenceValue value)
    {
        return value.Value;
    }

    public static ConfidenceValue DecimalToConfidenceValue(decimal value)
    {
        return new ConfidenceValue(value);
    }
}

public sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("persons");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.OwnsOne(x => x.Name, owned =>
        {
            owned.Property(x => x.GivenName)
                .HasColumnName("given_name")
                .HasMaxLength(200)
                .IsRequired();

            owned.Property(x => x.MiddleName)
                .HasColumnName("middle_name")
                .HasMaxLength(200);

            owned.Property(x => x.FamilyName)
                .HasColumnName("family_name")
                .HasMaxLength(200)
                .IsRequired();

            owned.Property(x => x.DisplayName)
                .HasColumnName("display_name")
                .HasMaxLength(400)
                .IsRequired();
        });

        builder.Property(x => x.Description).HasColumnType("text");
        builder.Property(x => x.Nationality).HasMaxLength(200);
        builder.Property(x => x.IsActive).IsRequired();

        builder.HasMany(x => x.Aliases)
            .WithOne(x => x.Person)
            .HasForeignKey(x => x.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Statements)
            .WithOne(x => x.Person)
            .HasForeignKey(x => x.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Claims)
            .WithOne(x => x.Person)
            .HasForeignKey(x => x.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Cases)
            .WithOne(x => x.SubjectPerson)
            .HasForeignKey(x => x.SubjectPersonId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AliasConfiguration : IEntityTypeConfiguration<Alias>
{
    public void Configure(EntityTypeBuilder<Alias> builder)
    {
        builder.ToTable("aliases");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.Property(x => x.PersonId).IsRequired();

        builder.Property(x => x.Value)
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.LanguageCode).HasMaxLength(16);
        builder.Property(x => x.IsPrimary).IsRequired();

        builder.HasIndex(x => x.PersonId);
        builder.HasIndex(x => new { x.PersonId, x.Value }).IsUnique();

        builder.HasOne(x => x.Person)
            .WithMany(x => x.Aliases)
            .HasForeignKey(x => x.PersonId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}

public sealed class SourceConfiguration : IEntityTypeConfiguration<Source>
{
    public void Configure(EntityTypeBuilder<Source> builder)
    {
        builder.ToTable("sources");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(x => x.Description).HasColumnType("text");

        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.TrustTier)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.OwnsOne(x => x.Reference, owned =>
        {
            owned.Property(x => x.ExternalId)
                .HasColumnName("reference_external_id")
                .HasMaxLength(256);

            owned.Property(x => x.Url)
                .HasColumnName("reference_url")
                .HasMaxLength(2048);

            owned.Property(x => x.Domain)
                .HasColumnName("reference_domain")
                .HasMaxLength(256);

            owned.Property(x => x.LanguageCode)
                .HasColumnName("reference_language_code")
                .HasMaxLength(16);
        });


        builder.HasMany(x => x.Documents)
            .WithOne(x => x.Source)
            .HasForeignKey(x => x.SourceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Evidences)
            .WithOne(x => x.Source)
            .HasForeignKey(x => x.SourceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.Property(x => x.SourceId).IsRequired();

        builder.Property(x => x.Title)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.LanguageCode).HasMaxLength(16);
        builder.Property(x => x.ExternalId).HasMaxLength(256);
        builder.Property(x => x.Url).HasMaxLength(2048);
        builder.Property(x => x.ContentHash).HasMaxLength(128);

        builder.HasIndex(x => x.SourceId);
        builder.HasIndex(x => x.ExternalId);
        builder.HasIndex(x => x.PublishedAtUtc);

        builder.HasOne(x => x.Source)
            .WithMany(x => x.Documents)
            .HasForeignKey(x => x.SourceId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasMany(x => x.Evidences)
            .WithOne(x => x.Document)
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Statements)
            .WithOne(x => x.Document)
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class EvidenceConfiguration : IEntityTypeConfiguration<Evidence>
{
    public void Configure(EntityTypeBuilder<Evidence> builder)
    {
        var spanConverter = new ValueConverter<DocumentSpan?, string?>(
            v => ValueConversionHelpers.DocumentSpanToString(v),
            v => ValueConversionHelpers.StringToDocumentSpan(v));

        builder.ToTable("evidences");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.Property(x => x.SourceId).IsRequired();

        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Content)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.ContentHash).HasMaxLength(128);
        builder.Property(x => x.LanguageCode).HasMaxLength(16);

        builder.Property(x => x.Span)
            .HasConversion(spanConverter)
            .HasColumnName("span")
            .HasMaxLength(64);

        builder.HasIndex(x => x.SourceId);
        builder.HasIndex(x => x.DocumentId);
        builder.HasIndex(x => x.ContentHash);

        builder.HasOne(x => x.Source)
            .WithMany(x => x.Evidences)
            .HasForeignKey(x => x.SourceId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(x => x.Document)
            .WithMany(x => x.Evidences)
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Statements)
            .WithOne(x => x.Evidence)
            .HasForeignKey(x => x.EvidenceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StatementConfiguration : IEntityTypeConfiguration<Statement>
{
    public void Configure(EntityTypeBuilder<Statement> builder)
    {
        builder.ToTable("statements");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.Property(x => x.EvidenceId).IsRequired();

        builder.OwnsOne(x => x.Text, owned =>
        {
            owned.Property(x => x.Raw)
                .HasColumnName("text_raw")
                .HasColumnType("text")
                .IsRequired();

            owned.Property(x => x.Normalized)
                .HasColumnName("text_normalized")
                .HasColumnType("text")
                .IsRequired();
        });

        builder.Property(x => x.Polarity)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Topic).HasMaxLength(256);
        builder.Property(x => x.Predicate).HasMaxLength(256);
        builder.Property(x => x.ObjectValue).HasMaxLength(512);

        builder.HasIndex(x => x.PersonId);
        builder.HasIndex(x => x.DocumentId);
        builder.HasIndex(x => x.EvidenceId);
        builder.HasIndex(x => x.Topic);

        builder.HasOne(x => x.Evidence)
            .WithMany(x => x.Statements)
            .HasForeignKey(x => x.EvidenceId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(x => x.Document)
            .WithMany(x => x.Statements)
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Person)
            .WithMany(x => x.Statements)
            .HasForeignKey(x => x.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Claims)
            .WithOne(x => x.Statement)
            .HasForeignKey(x => x.StatementId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ClaimConfiguration : IEntityTypeConfiguration<Claim>
{
    public void Configure(EntityTypeBuilder<Claim> builder)
    {
        builder.ToTable("claims");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.Property(x => x.StatementId).IsRequired();

        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Topic)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.NormalizedText)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.IsMaterial).IsRequired();

        builder.HasIndex(x => x.PersonId);
        builder.HasIndex(x => x.CaseId);
        builder.HasIndex(x => x.StatementId);
        builder.HasIndex(x => x.Topic);

        builder.HasOne(x => x.Statement)
            .WithMany(x => x.Claims)
            .HasForeignKey(x => x.StatementId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(x => x.Person)
            .WithMany(x => x.Claims)
            .HasForeignKey(x => x.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Case)
            .WithMany(x => x.Claims)
            .HasForeignKey(x => x.CaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.ContradictionsAsLeft)
            .WithOne(x => x.LeftClaim)
            .HasForeignKey(x => x.LeftClaimId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.ContradictionsAsRight)
            .WithOne(x => x.RightClaim)
            .HasForeignKey(x => x.RightClaimId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ContradictionConfiguration : IEntityTypeConfiguration<Contradiction>
{
    public void Configure(EntityTypeBuilder<Contradiction> builder)
    {
        builder.ToTable("contradictions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.Property(x => x.LeftClaimId).IsRequired();
        builder.Property(x => x.RightClaimId).IsRequired();

        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Severity)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Summary)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.Rationale).HasColumnType("text");

        builder.HasIndex(x => x.CaseId);
        builder.HasIndex(x => x.Type);
        builder.HasIndex(x => new { x.LeftClaimId, x.RightClaimId }).IsUnique();

        builder.HasOne(x => x.Case)
            .WithMany(x => x.Contradictions)
            .HasForeignKey(x => x.CaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.LeftClaim)
            .WithMany(x => x.ContradictionsAsLeft)
            .HasForeignKey(x => x.LeftClaimId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(x => x.RightClaim)
            .WithMany(x => x.ContradictionsAsRight)
            .HasForeignKey(x => x.RightClaimId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(x => x.ConfidenceScore)
            .WithMany(x => x.Contradictions)
            .HasForeignKey(x => x.ConfidenceScoreId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CaseConfiguration : IEntityTypeConfiguration<Case>
{
    public void Configure(EntityTypeBuilder<Case> builder)
    {
        builder.ToTable("cases");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.Property(x => x.Title)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.Summary).HasColumnType("text");

        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(x => x.SubjectPersonId);

        builder.HasOne(x => x.SubjectPerson)
            .WithMany(x => x.Cases)
            .HasForeignKey(x => x.SubjectPersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Claims)
            .WithOne(x => x.Case)
            .HasForeignKey(x => x.CaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Contradictions)
            .WithOne(x => x.Case)
            .HasForeignKey(x => x.CaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.ConfidenceScores)
            .WithOne(x => x.Case)
            .HasForeignKey(x => x.CaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.AgentRuns)
            .WithOne(x => x.Case)
            .HasForeignKey(x => x.CaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Reviews)
            .WithOne(x => x.Case)
            .HasForeignKey(x => x.CaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Publications)
            .WithOne(x => x.Case)
            .HasForeignKey(x => x.CaseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ConfidenceScoreConfiguration : IEntityTypeConfiguration<ConfidenceScore>
{
    public void Configure(EntityTypeBuilder<ConfidenceScore> builder)
    {
        var confidenceValueConverter = new ValueConverter<ConfidenceValue, decimal>(
            v => ValueConversionHelpers.ConfidenceValueToDecimal(v),
            v => ValueConversionHelpers.DecimalToConfidenceValue(v));

        builder.ToTable("confidence_scores");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.Property(x => x.TargetType)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.TargetId).IsRequired();

        builder.Property(x => x.Value)
            .HasConversion(confidenceValueConverter)
            .HasColumnName("score_value")
            .HasPrecision(5, 4)
            .IsRequired();

        builder.Property(x => x.Band)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.ModelVersion).HasMaxLength(128);
        builder.Property(x => x.Explanation).HasColumnType("text");

        builder.HasIndex(x => x.CaseId);
        builder.HasIndex(x => x.AgentRunId);
        builder.HasIndex(x => new { x.TargetType, x.TargetId });

        builder.HasOne(x => x.Case)
            .WithMany(x => x.ConfidenceScores)
            .HasForeignKey(x => x.CaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.AgentRun)
            .WithMany(x => x.ConfidenceScores)
            .HasForeignKey(x => x.AgentRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Contradictions)
            .WithOne(x => x.ConfidenceScore)
            .HasForeignKey(x => x.ConfidenceScoreId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AgentRunConfiguration : IEntityTypeConfiguration<AgentRun>
{
    public void Configure(EntityTypeBuilder<AgentRun> builder)
    {
        builder.ToTable("agent_runs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.AgentName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.AgentVersion).HasMaxLength(128);
        builder.Property(x => x.InputHash).HasMaxLength(128);
        builder.Property(x => x.OutputHash).HasMaxLength(128);
        builder.Property(x => x.Error).HasColumnType("text");
        builder.Property(x => x.StartedAtUtc).IsRequired();

        builder.HasIndex(x => x.CaseId);
        builder.HasIndex(x => x.AgentName);
        builder.HasIndex(x => x.Status);

        builder.HasOne(x => x.Case)
            .WithMany(x => x.AgentRuns)
            .HasForeignKey(x => x.CaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.ConfidenceScores)
            .WithOne(x => x.AgentRun)
            .HasForeignKey(x => x.AgentRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Reviews)
            .WithOne(x => x.AgentRun)
            .HasForeignKey(x => x.AgentRunId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("reviews");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.Property(x => x.CaseId).IsRequired();

        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Decision)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Reviewer)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Notes).HasColumnType("text");

        builder.HasIndex(x => x.CaseId);
        builder.HasIndex(x => x.AgentRunId);
        builder.HasIndex(x => x.Reviewer);

        builder.HasOne(x => x.Case)
            .WithMany(x => x.Reviews)
            .HasForeignKey(x => x.CaseId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(x => x.AgentRun)
            .WithMany(x => x.Reviews)
            .HasForeignKey(x => x.AgentRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Publications)
            .WithOne(x => x.Review)
            .HasForeignKey(x => x.ReviewId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PublicationConfiguration : IEntityTypeConfiguration<Publication>
{
    public void Configure(EntityTypeBuilder<Publication> builder)
    {
        builder.ToTable("publications");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.Property(x => x.CaseId).IsRequired();

        builder.Property(x => x.Channel)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Title)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.Slug).HasMaxLength(300);
        builder.Property(x => x.Body).HasColumnType("text");

        builder.HasIndex(x => x.CaseId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.Slug).IsUnique();

        builder.HasOne(x => x.Case)
            .WithMany(x => x.Publications)
            .HasForeignKey(x => x.CaseId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(x => x.Review)
            .WithMany(x => x.Publications)
            .HasForeignKey(x => x.ReviewId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}


public sealed class RevokedAccessTokenConfiguration : IEntityTypeConfiguration<VeritasAtlas.Infrastructure.Persistence.RevokedAccessToken>
{
    public void Configure(EntityTypeBuilder<VeritasAtlas.Infrastructure.Persistence.RevokedAccessToken> builder)
    {
        builder.ToTable("revoked_access_tokens", table => table.HasCheckConstraint(
            "ck_revoked_access_tokens_jti_sha256_hex",
            "jti_sha256 ~ '^[0-9a-f]{64}$'"));

        builder.HasKey(x => x.JtiSha256);

        builder.Property(x => x.JtiSha256)
            .HasColumnName("jti_sha256")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();

        builder.Property(x => x.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .IsRequired();

        builder.Property(x => x.RevokedAtUtc)
            .HasColumnName("revoked_at_utc")
            .IsRequired();

        builder.HasIndex(x => x.ExpiresAtUtc)
            .HasDatabaseName("ix_revoked_access_tokens_expires_at_utc");
    }
}

public sealed class TenantSourceRegistryEntryConfiguration : IEntityTypeConfiguration<VeritasAtlas.Infrastructure.Persistence.TenantSourceRegistryEntry>
{
    public void Configure(EntityTypeBuilder<VeritasAtlas.Infrastructure.Persistence.TenantSourceRegistryEntry> builder)
    {
        builder.ToTable("tenant_source_registry_entries", table =>
        {
            table.HasCheckConstraint(
                "ck_tenant_source_registry_entries_tenant_id_not_empty",
                "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint(
                "ck_tenant_source_registry_entries_canonical_key_not_blank",
                "length(btrim(canonical_key)) > 0");
        });

        builder.HasKey(x => new { x.TenantId, x.Id })
            .HasName("pk_tenant_source_registry_entries");

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(x => x.CanonicalKey)
            .HasColumnName("canonical_key")
            .HasMaxLength(VeritasAtlas.Application.Interfaces.TenantSourceRegistryLimits.MaxCanonicalKeyLength)
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.CanonicalKey })
            .IsUnique()
            .HasDatabaseName("ux_tenant_source_registry_entries_tenant_canonical_key");
    }
}

public sealed class TenantRawRetrievalConfiguration : IEntityTypeConfiguration<VeritasAtlas.Infrastructure.Persistence.TenantRawRetrieval>
{
    public void Configure(EntityTypeBuilder<VeritasAtlas.Infrastructure.Persistence.TenantRawRetrieval> builder)
    {
        builder.ToTable("tenant_raw_retrievals", table =>
        {
            table.HasCheckConstraint(
                "ck_tenant_raw_retrievals_tenant_id_not_empty",
                "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint(
                "ck_tenant_raw_retrievals_idempotency_key_not_blank",
                "length(btrim(idempotency_key)) > 0");
            table.HasCheckConstraint(
                "ck_tenant_raw_retrievals_final_uri_not_blank",
                "length(btrim(final_uri)) > 0");
            table.HasCheckConstraint(
                "ck_tenant_raw_retrievals_content_sha256_matches_bytes",
                "content_sha256 = encode(sha256(raw_bytes), 'hex')");
            table.HasCheckConstraint(
                "ck_tenant_raw_retrievals_byte_length_matches_bytes",
                "byte_length = octet_length(raw_bytes)");
            table.HasCheckConstraint(
                "ck_tenant_raw_retrievals_no_pre_parser_evidence_reference",
                "evidence_reference IS NULL");
        });

        builder.HasKey(x => new { x.TenantId, x.Id })
            .HasName("pk_tenant_raw_retrievals");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever().IsRequired();
        builder.Property(x => x.SourceEntryId).HasColumnName("source_entry_id").IsRequired();
        builder.Property(x => x.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(VeritasAtlas.Application.Interfaces.TenantRawRetrievalLimits.MaxIdempotencyKeyLength)
            .IsRequired();
        builder.Property(x => x.FinalUri)
            .HasColumnName("final_uri")
            .HasMaxLength(VeritasAtlas.Application.Interfaces.TenantRawRetrievalLimits.MaxFinalUriLength)
            .IsRequired();
        builder.Property(x => x.RawBytes).HasColumnName("raw_bytes").HasColumnType("bytea").IsRequired();
        builder.Property(x => x.ContentSha256)
            .HasColumnName("content_sha256")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(x => x.ByteLength).HasColumnName("byte_length").IsRequired();
        builder.Property(x => x.EvidenceReference).HasColumnName("evidence_reference").HasMaxLength(128);
        builder.Property(x => x.RetrievedAtUtc).HasColumnName("retrieved_at_utc").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_tenant_raw_retrievals_tenant_idempotency_key");
        builder.HasIndex(x => new { x.TenantId, x.SourceEntryId })
            .HasDatabaseName("ix_tenant_raw_retrievals_tenant_source_entry");

        // Target of the fetch-attempt link (#89): a successful attempt can only
        // point at a retrieval of the same tenant and the same source.
        builder.HasAlternateKey(x => new { x.TenantId, x.SourceEntryId, x.Id })
            .HasName("ak_tenant_raw_retrievals_tenant_source_entry_id");

        // Composite key: a retrieval can only point at its own tenant's source.
        builder.HasOne<VeritasAtlas.Infrastructure.Persistence.TenantSourceRegistryEntry>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SourceEntryId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .HasConstraintName("fk_tenant_raw_retrievals_tenant_source_entry")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TenantFetchAttemptConfiguration : IEntityTypeConfiguration<VeritasAtlas.Infrastructure.Persistence.TenantFetchAttempt>
{
    public void Configure(EntityTypeBuilder<VeritasAtlas.Infrastructure.Persistence.TenantFetchAttempt> builder)
    {
        builder.ToTable("tenant_fetch_attempts", table =>
        {
            table.HasCheckConstraint(
                "ck_tenant_fetch_attempts_tenant_id_not_empty",
                "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint(
                "ck_tenant_fetch_attempts_attempt_key_not_blank",
                "length(btrim(attempt_key)) > 0");
            table.HasCheckConstraint(
                "ck_tenant_fetch_attempts_requested_uri_not_blank",
                "length(btrim(requested_uri)) > 0");
            table.HasCheckConstraint(
                "ck_tenant_fetch_attempts_outcome",
                "outcome IN ('succeeded', 'failed')");
            table.HasCheckConstraint(
                "ck_tenant_fetch_attempts_failure_class",
                "failure_class IS NULL OR failure_class IN ('network', 'timeout', 'http_status', 'size_limit', 'policy_denied')");
            table.HasCheckConstraint(
                "ck_tenant_fetch_attempts_http_status_range",
                "http_status IS NULL OR http_status BETWEEN 100 AND 599");
            table.HasCheckConstraint(
                "ck_tenant_fetch_attempts_outcome_shape",
                "(outcome = 'succeeded' AND raw_retrieval_id IS NOT NULL AND failure_class IS NULL) OR (outcome = 'failed' AND raw_retrieval_id IS NULL AND failure_class IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_tenant_fetch_attempts_http_status_failure_has_status",
                "failure_class IS DISTINCT FROM 'http_status' OR http_status IS NOT NULL");
        });

        builder.HasKey(x => new { x.TenantId, x.Id })
            .HasName("pk_tenant_fetch_attempts");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever().IsRequired();
        builder.Property(x => x.SourceEntryId).HasColumnName("source_entry_id").IsRequired();
        builder.Property(x => x.AttemptKey)
            .HasColumnName("attempt_key")
            .HasMaxLength(VeritasAtlas.Application.Interfaces.TenantFetchAttemptLimits.MaxAttemptKeyLength)
            .IsRequired();
        builder.Property(x => x.RequestedUri)
            .HasColumnName("requested_uri")
            .HasMaxLength(VeritasAtlas.Application.Interfaces.TenantFetchAttemptLimits.MaxRequestedUriLength)
            .IsRequired();
        builder.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(16).IsRequired();
        builder.Property(x => x.FailureClass).HasColumnName("failure_class").HasMaxLength(32);
        builder.Property(x => x.HttpStatus).HasColumnName("http_status");
        builder.Property(x => x.RawRetrievalId).HasColumnName("raw_retrieval_id");
        builder.Property(x => x.AttemptedAtUtc).HasColumnName("attempted_at_utc").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.AttemptKey })
            .IsUnique()
            .HasDatabaseName("ux_tenant_fetch_attempts_tenant_attempt_key");
        builder.HasIndex(x => new { x.TenantId, x.SourceEntryId, x.AttemptedAtUtc })
            .HasDatabaseName("ix_tenant_fetch_attempts_tenant_source_attempted_at");
        builder.HasIndex(x => new { x.TenantId, x.SourceEntryId, x.RawRetrievalId })
            .HasDatabaseName("ix_tenant_fetch_attempts_tenant_source_raw_retrieval");

        builder.HasOne<VeritasAtlas.Infrastructure.Persistence.TenantSourceRegistryEntry>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SourceEntryId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .HasConstraintName("fk_tenant_fetch_attempts_tenant_source_entry")
            .OnDelete(DeleteBehavior.Restrict);

        // Composite link: same tenant AND same source as the attempt. With a
        // NULL raw_retrieval_id (failed attempt) the FK is not enforced.
        builder.HasOne<VeritasAtlas.Infrastructure.Persistence.TenantRawRetrieval>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SourceEntryId, x.RawRetrievalId })
            .HasPrincipalKey(x => new { x.TenantId, x.SourceEntryId, x.Id })
            .HasConstraintName("fk_tenant_fetch_attempts_tenant_source_raw_retrieval")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TenantParseRunConfiguration : IEntityTypeConfiguration<VeritasAtlas.Infrastructure.Persistence.TenantParseRun>
{
    public void Configure(EntityTypeBuilder<VeritasAtlas.Infrastructure.Persistence.TenantParseRun> builder)
    {
        builder.ToTable("tenant_parse_runs", table =>
        {
            table.HasCheckConstraint(
                "ck_tenant_parse_runs_tenant_id_not_empty",
                "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint(
                "ck_tenant_parse_runs_run_key_not_blank",
                "length(btrim(run_key)) > 0");
            table.HasCheckConstraint(
                "ck_tenant_parse_runs_parser_name_identity",
                "parser_name ~ '^[a-z0-9][a-z0-9._-]*$'");
            table.HasCheckConstraint(
                "ck_tenant_parse_runs_parser_version_identity",
                "parser_version ~ '^[a-z0-9][a-z0-9._-]*$'");
            table.HasCheckConstraint(
                "ck_tenant_parse_runs_outcome",
                "outcome IN ('failed', 'parsed_unverified')");
            table.HasCheckConstraint(
                "ck_tenant_parse_runs_failure_class",
                "failure_class IS NULL OR failure_class IN ('unsupported_format', 'malformed', 'oversized', 'parser_error')");
            table.HasCheckConstraint(
                "ck_tenant_parse_runs_outcome_shape",
                "(outcome = 'failed' AND failure_class IS NOT NULL) OR (outcome = 'parsed_unverified' AND failure_class IS NULL)");
            // Only VER-P0-07's versioned parser may populate or verify this slot,
            // through its own migration: no pre-parser ev1: reference.
            table.HasCheckConstraint(
                "ck_tenant_parse_runs_no_pre_parser_evidence_reference",
                "evidence_reference IS NULL");
        });

        builder.HasKey(x => new { x.TenantId, x.Id })
            .HasName("pk_tenant_parse_runs");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever().IsRequired();
        builder.Property(x => x.RawRetrievalId).HasColumnName("raw_retrieval_id").IsRequired();
        builder.Property(x => x.RunKey)
            .HasColumnName("run_key")
            .HasMaxLength(VeritasAtlas.Application.Interfaces.TenantParseRunLimits.MaxRunKeyLength)
            .IsRequired();
        builder.Property(x => x.ParserName)
            .HasColumnName("parser_name")
            .HasMaxLength(VeritasAtlas.Application.Interfaces.TenantParseRunLimits.MaxParserIdentityLength)
            .IsRequired();
        builder.Property(x => x.ParserVersion)
            .HasColumnName("parser_version")
            .HasMaxLength(VeritasAtlas.Application.Interfaces.TenantParseRunLimits.MaxParserIdentityLength)
            .IsRequired();
        builder.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(24).IsRequired();
        builder.Property(x => x.FailureClass).HasColumnName("failure_class").HasMaxLength(32);
        builder.Property(x => x.EvidenceReference).HasColumnName("evidence_reference").HasMaxLength(128);
        builder.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.RunKey })
            .IsUnique()
            .HasDatabaseName("ux_tenant_parse_runs_tenant_run_key");
        builder.HasIndex(x => new { x.TenantId, x.RawRetrievalId, x.StartedAtUtc })
            .HasDatabaseName("ix_tenant_parse_runs_tenant_raw_retrieval_started_at");

        // Composite key: a run can only point at its own tenant's retrieval.
        builder.HasOne<VeritasAtlas.Infrastructure.Persistence.TenantRawRetrieval>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RawRetrievalId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .HasConstraintName("fk_tenant_parse_runs_tenant_raw_retrieval")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TenantRetrievalAttestationConfiguration : IEntityTypeConfiguration<VeritasAtlas.Infrastructure.Persistence.TenantRetrievalAttestation>
{
    // Stored exactly as given: not blank, no surrounding whitespace and no
    // control characters (C0, DEL, C1).
    private static string ExactText(string column) =>
        $"length({column}) > 0 AND {column} = btrim({column}) AND {column} !~ '[\\x01-\\x1f\\x7f-\\x9f]'";

    public void Configure(EntityTypeBuilder<VeritasAtlas.Infrastructure.Persistence.TenantRetrievalAttestation> builder)
    {
        builder.ToTable("tenant_retrieval_attestations", table =>
        {
            table.HasCheckConstraint(
                "ck_tenant_retrieval_attestations_tenant_id_not_empty",
                "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint(
                "ck_tenant_retrieval_attestations_attestation_key_shape",
                ExactText("attestation_key"));
            table.HasCheckConstraint(
                "ck_tenant_retrieval_attestations_retriever_identity_shape",
                ExactText("retriever_identity"));
            table.HasCheckConstraint(
                "ck_tenant_retrieval_attestations_publisher_identity_shape",
                ExactText("publisher_identity"));
            table.HasCheckConstraint(
                "ck_tenant_retrieval_attestations_binding_reference_shape",
                ExactText("binding_reference"));
            table.HasCheckConstraint(
                "ck_tenant_retrieval_attestations_binding_mechanism",
                "binding_mechanism IN ('source_registry', 'publisher_signature')");
            table.HasCheckConstraint(
                "ck_tenant_retrieval_attestations_authentication_result",
                "authentication_result IN ('authenticated', 'unauthenticated', 'failed')");
            table.HasCheckConstraint(
                "ck_tenant_retrieval_attestations_jurisdiction_code",
                "jurisdiction ~ '^[a-z0-9][a-z0-9._-]*$'");
            table.HasCheckConstraint(
                "ck_tenant_retrieval_attestations_source_type",
                "source_type IN ('court_record', 'statute', 'regulation', 'register', 'filing', 'other_public_record')");
        });

        builder.HasKey(x => new { x.TenantId, x.Id })
            .HasName("pk_tenant_retrieval_attestations");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever().IsRequired();
        builder.Property(x => x.RawRetrievalId).HasColumnName("raw_retrieval_id").IsRequired();
        builder.Property(x => x.AttestationKey)
            .HasColumnName("attestation_key")
            .HasMaxLength(VeritasAtlas.Application.Interfaces.TenantRetrievalAttestationLimits.MaxAttestationKeyLength)
            .IsRequired();
        builder.Property(x => x.RetrieverIdentity)
            .HasColumnName("retriever_identity")
            .HasMaxLength(VeritasAtlas.Application.Interfaces.TenantRetrievalAttestationLimits.MaxIdentityLength)
            .IsRequired();
        builder.Property(x => x.PublisherIdentity)
            .HasColumnName("publisher_identity")
            .HasMaxLength(VeritasAtlas.Application.Interfaces.TenantRetrievalAttestationLimits.MaxIdentityLength)
            .IsRequired();
        builder.Property(x => x.BindingMechanism).HasColumnName("binding_mechanism").HasMaxLength(32).IsRequired();
        builder.Property(x => x.BindingReference)
            .HasColumnName("binding_reference")
            .HasMaxLength(VeritasAtlas.Application.Interfaces.TenantRetrievalAttestationLimits.MaxBindingReferenceLength)
            .IsRequired();
        builder.Property(x => x.AuthenticationResult).HasColumnName("authentication_result").HasMaxLength(24).IsRequired();
        builder.Property(x => x.Jurisdiction)
            .HasColumnName("jurisdiction")
            .HasMaxLength(VeritasAtlas.Application.Interfaces.TenantRetrievalAttestationLimits.MaxJurisdictionLength)
            .IsRequired();
        builder.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(32).IsRequired();
        builder.Property(x => x.AttestedAtUtc).HasColumnName("attested_at_utc").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.AttestationKey })
            .IsUnique()
            .HasDatabaseName("ux_tenant_retrieval_attestations_tenant_attestation_key");
        builder.HasIndex(x => new { x.TenantId, x.RawRetrievalId, x.AttestedAtUtc })
            .HasDatabaseName("ix_tenant_retrieval_attestations_tenant_raw_retrieval_attested_at");

        // Composite key: an attestation can only point at its own tenant's retrieval.
        builder.HasOne<VeritasAtlas.Infrastructure.Persistence.TenantRawRetrieval>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RawRetrievalId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .HasConstraintName("fk_tenant_retrieval_attestations_tenant_raw_retrieval")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TenantInvestigationConfiguration : IEntityTypeConfiguration<VeritasAtlas.Infrastructure.Persistence.TenantInvestigation>
{
    public void Configure(EntityTypeBuilder<VeritasAtlas.Infrastructure.Persistence.TenantInvestigation> builder)
    {
        builder.ToTable("tenant_investigations", table =>
        {
            table.HasCheckConstraint(
                "ck_tenant_investigations_tenant_id_not_empty",
                "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint(
                "ck_tenant_investigations_creation_key_not_blank",
                "length(btrim(creation_key)) > 0");
            // Stored exactly as given: not blank, no surrounding whitespace and
            // no control characters (C0, DEL, C1).
            table.HasCheckConstraint(
                "ck_tenant_investigations_title_shape",
                "length(title) > 0 AND title = btrim(title) AND title !~ '[\\x01-\\x1f\\x7f-\\x9f]'");
            table.HasCheckConstraint(
                "ck_tenant_investigations_created_by_subject_not_blank",
                "length(btrim(created_by_subject)) > 0");
        });

        builder.HasKey(x => new { x.TenantId, x.Id })
            .HasName("pk_tenant_investigations");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever().IsRequired();
        builder.Property(x => x.CreationKey)
            .HasColumnName("creation_key")
            .HasMaxLength(VeritasAtlas.Application.Interfaces.TenantInvestigationLimits.MaxCreationKeyLength)
            .IsRequired();
        builder.Property(x => x.Title)
            .HasColumnName("title")
            .HasMaxLength(VeritasAtlas.Application.Interfaces.TenantInvestigationLimits.MaxTitleLength)
            .IsRequired();
        builder.Property(x => x.CreatedBySubject)
            .HasColumnName("created_by_subject")
            .HasMaxLength(VeritasAtlas.Application.Interfaces.TenantInvestigationLimits.MaxSubjectLength)
            .IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.CreationKey })
            .IsUnique()
            .HasDatabaseName("ux_tenant_investigations_tenant_creation_key");
        builder.HasIndex(x => new { x.TenantId, x.CreatedAtUtc })
            .HasDatabaseName("ix_tenant_investigations_tenant_created_at");
    }
}

public sealed class TenantAuditEventConfiguration : IEntityTypeConfiguration<VeritasAtlas.Infrastructure.Persistence.TenantAuditEvent>
{
    public void Configure(EntityTypeBuilder<VeritasAtlas.Infrastructure.Persistence.TenantAuditEvent> builder)
    {
        builder.ToTable("tenant_audit_events", table =>
        {
            table.HasCheckConstraint(
                "ck_tenant_audit_events_tenant_id_not_empty",
                "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint(
                "ck_tenant_audit_events_event_key_not_blank",
                "length(btrim(event_key)) > 0");
            // Lowercase dotted identifiers only: no free text in the audit trail.
            table.HasCheckConstraint(
                "ck_tenant_audit_events_action_shape",
                "action ~ '^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)*$'");
            table.HasCheckConstraint(
                "ck_tenant_audit_events_entity_type_shape",
                "entity_type ~ '^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)*$'");
            table.HasCheckConstraint(
                "ck_tenant_audit_events_entity_id_shape",
                "length(btrim(entity_id)) > 0 AND entity_id !~ '[\\x01-\\x1f\\x7f-\\x9f]'");
            table.HasCheckConstraint(
                "ck_tenant_audit_events_actor_subject_shape",
                "length(btrim(actor_subject)) > 0 AND actor_subject !~ '[\\x01-\\x1f\\x7f-\\x9f]'");
        });

        builder.HasKey(x => new { x.TenantId, x.Id })
            .HasName("pk_tenant_audit_events");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever().IsRequired();
        builder.Property(x => x.EventKey)
            .HasColumnName("event_key")
            .HasMaxLength(VeritasAtlas.Application.Interfaces.TenantAuditEventLimits.MaxEventKeyLength)
            .IsRequired();
        builder.Property(x => x.Action)
            .HasColumnName("action")
            .HasMaxLength(VeritasAtlas.Application.Interfaces.TenantAuditEventLimits.MaxActionLength)
            .IsRequired();
        builder.Property(x => x.EntityType)
            .HasColumnName("entity_type")
            .HasMaxLength(VeritasAtlas.Application.Interfaces.TenantAuditEventLimits.MaxEntityTypeLength)
            .IsRequired();
        builder.Property(x => x.EntityId)
            .HasColumnName("entity_id")
            .HasMaxLength(VeritasAtlas.Application.Interfaces.TenantAuditEventLimits.MaxEntityIdLength)
            .IsRequired();
        builder.Property(x => x.ActorSubject)
            .HasColumnName("actor_subject")
            .HasMaxLength(VeritasAtlas.Application.Interfaces.TenantAuditEventLimits.MaxSubjectLength)
            .IsRequired();
        builder.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.EventKey })
            .IsUnique()
            .HasDatabaseName("ux_tenant_audit_events_tenant_event_key");
        builder.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId, x.OccurredAtUtc })
            .HasDatabaseName("ix_tenant_audit_events_tenant_entity_occurred_at");
    }
}
