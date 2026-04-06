using System;
using System.Collections.Generic;
using VeritasAtlas.Domain.Common;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Domain.ValueObjects;

namespace VeritasAtlas.Domain.Entities
{
    public class Person : Entity
    {
        public required PersonName Name { get; set; }
        public string? Description { get; set; }
        public string? Nationality { get; set; }
        public DateTime? BornAtUtc { get; set; }
        public DateTime? DiedAtUtc { get; set; }
        public bool IsActive { get; set; } = true;

        public ICollection<Alias> Aliases { get; set; } = new List<Alias>();
        public ICollection<Statement> Statements { get; set; } = new List<Statement>();
        public ICollection<Claim> Claims { get; set; } = new List<Claim>();
        public ICollection<Case> Cases { get; set; } = new List<Case>();
    }

    public class Alias : Entity
    {
        public Guid PersonId { get; set; }
        public Person? Person { get; set; }

        public required string Value { get; set; }
        public AliasType Type { get; set; }
        public string? LanguageCode { get; set; }
        public bool IsPrimary { get; set; }
    }

    public class Source : Entity
    {
        public required string Name { get; set; }
        public string? Description { get; set; }
        public SourceType Type { get; set; }
        public SourceStatus Status { get; set; }
        public SourceTrustTier TrustTier { get; set; }
        public SourceReference? Reference { get; set; }

        public ICollection<Document> Documents { get; set; } = new List<Document>();
        public ICollection<Evidence> Evidences { get; set; } = new List<Evidence>();
    }

    public class Document : Entity
    {
        public Guid SourceId { get; set; }
        public Source? Source { get; set; }

        public required string Title { get; set; }
        public DocumentType Type { get; set; }
        public DocumentStatus Status { get; set; }
        public string? LanguageCode { get; set; }
        public string? ExternalId { get; set; }
        public string? Url { get; set; }
        public string? ContentHash { get; set; }
        public DateTime? PublishedAtUtc { get; set; }
        public DateTime? RetrievedAtUtc { get; set; }

        public ICollection<Evidence> Evidences { get; set; } = new List<Evidence>();
        public ICollection<Statement> Statements { get; set; } = new List<Statement>();
    }

    public class Evidence : Entity
    {
        public Guid SourceId { get; set; }
        public Source? Source { get; set; }

        public Guid? DocumentId { get; set; }
        public Document? Document { get; set; }

        public EvidenceType Type { get; set; }
        public EvidenceStatus Status { get; set; }
        public required string Content { get; set; }
        public string? ContentHash { get; set; }
        public string? LanguageCode { get; set; }
        public DocumentSpan? Span { get; set; }
        public DateTime? CapturedAtUtc { get; set; }

        public ICollection<Statement> Statements { get; set; } = new List<Statement>();
    }

    public class Statement : Entity
    {
        public Guid EvidenceId { get; set; }
        public Evidence? Evidence { get; set; }

        public Guid? DocumentId { get; set; }
        public Document? Document { get; set; }

        public Guid? PersonId { get; set; }
        public Person? Person { get; set; }

        public required StatementText Text { get; set; }
        public StatementPolarity Polarity { get; set; }
        public StatementStatus Status { get; set; }
        public string? Topic { get; set; }
        public string? Predicate { get; set; }
        public string? ObjectValue { get; set; }
        public DateTime? StatedAtUtc { get; set; }

        public ICollection<Claim> Claims { get; set; } = new List<Claim>();
    }

    public class Claim : Entity
    {
        public Guid StatementId { get; set; }
        public Statement? Statement { get; set; }

        public Guid? PersonId { get; set; }
        public Person? Person { get; set; }

        public Guid? CaseId { get; set; }
        public Case? Case { get; set; }

        public ClaimType Type { get; set; }
        public ClaimStatus Status { get; set; }
        public required string Topic { get; set; }
        public required string NormalizedText { get; set; }
        public bool IsMaterial { get; set; }

        public ICollection<Contradiction> ContradictionsAsLeft { get; set; } = new List<Contradiction>();
        public ICollection<Contradiction> ContradictionsAsRight { get; set; } = new List<Contradiction>();
    }

    public class Contradiction : Entity
    {
        public Guid CaseId { get; set; }
        public Case? Case { get; set; }

        public Guid LeftClaimId { get; set; }
        public Claim? LeftClaim { get; set; }

        public Guid RightClaimId { get; set; }
        public Claim? RightClaim { get; set; }

        public ContradictionType Type { get; set; }
        public ContradictionSeverity Severity { get; set; }
        public ContradictionStatus Status { get; set; }
        public required string Summary { get; set; }
        public string? Rationale { get; set; }

        public Guid? ConfidenceScoreId { get; set; }
        public ConfidenceScore? ConfidenceScore { get; set; }
    }

    public class Case : Entity
    {
        public required string Title { get; set; }
        public string? Summary { get; set; }
        public CaseType Type { get; set; }
        public CaseStatus Status { get; set; }

        public Guid? SubjectPersonId { get; set; }
        public Person? SubjectPerson { get; set; }

        public ICollection<Claim> Claims { get; set; } = new List<Claim>();
        public ICollection<Contradiction> Contradictions { get; set; } = new List<Contradiction>();
        public ICollection<ConfidenceScore> ConfidenceScores { get; set; } = new List<ConfidenceScore>();
        public ICollection<AgentRun> AgentRuns { get; set; } = new List<AgentRun>();
        public ICollection<Review> Reviews { get; set; } = new List<Review>();
        public ICollection<Publication> Publications { get; set; } = new List<Publication>();
    }

    public class ConfidenceScore : Entity
    {
        public ScoreTargetType TargetType { get; set; }
        public Guid TargetId { get; set; }
        public ConfidenceValue Value { get; set; }
        public ConfidenceBand Band { get; set; }
        public ConfidenceScoreStatus Status { get; set; }
        public string? ModelVersion { get; set; }
        public string? Explanation { get; set; }

        public Guid? CaseId { get; set; }
        public Case? Case { get; set; }

        public Guid? AgentRunId { get; set; }
        public AgentRun? AgentRun { get; set; }

        public ICollection<Contradiction> Contradictions { get; set; } = new List<Contradiction>();
    }

    public class AgentRun : Entity
    {
        public Guid? CaseId { get; set; }
        public Case? Case { get; set; }

        public AgentType Type { get; set; }
        public AgentRunStatus Status { get; set; }
        public required string AgentName { get; set; }
        public string? AgentVersion { get; set; }
        public string? InputHash { get; set; }
        public string? OutputHash { get; set; }
        public string? Error { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }

        public ICollection<ConfidenceScore> ConfidenceScores { get; set; } = new List<ConfidenceScore>();
        public ICollection<Review> Reviews { get; set; } = new List<Review>();
    }

    public class Review : Entity
    {
        public Guid CaseId { get; set; }
        public Case? Case { get; set; }

        public Guid? AgentRunId { get; set; }
        public AgentRun? AgentRun { get; set; }

        public ReviewType Type { get; set; }
        public ReviewStatus Status { get; set; }
        public ReviewDecision Decision { get; set; }
        public required string Reviewer { get; set; }
        public string? Notes { get; set; }
        public DateTime? ReviewedAtUtc { get; set; }

        public ICollection<Publication> Publications { get; set; } = new List<Publication>();
    }

    public class Publication : Entity
    {
        public Guid CaseId { get; set; }
        public Case? Case { get; set; }

        public Guid? ReviewId { get; set; }
        public Review? Review { get; set; }

        public PublicationChannel Channel { get; set; }
        public PublicationStatus Status { get; set; }
        public required string Title { get; set; }
        public string? Slug { get; set; }
        public string? Body { get; set; }
        public DateTime? ScheduledAtUtc { get; set; }
        public DateTime? PublishedAtUtc { get; set; }
        public DateTime? RetractedAtUtc { get; set; }
    }
}
