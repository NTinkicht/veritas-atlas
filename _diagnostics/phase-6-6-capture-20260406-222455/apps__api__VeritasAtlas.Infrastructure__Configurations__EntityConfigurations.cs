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

