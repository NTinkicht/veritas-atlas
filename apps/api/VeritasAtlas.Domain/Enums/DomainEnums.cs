namespace VeritasAtlas.Domain.Enums
{
    public enum AliasType
    {
        Other = 0,
        BirthName = 1,
        ShortName = 2,
        Nickname = 3,
        Honorific = 4,
        Transliteration = 5,
        AlternateSpelling = 6
    }

    public enum SourceType
    {
        Unknown = 0,
        Website = 1,
        Pdf = 2,
        Video = 3,
        Audio = 4,
        SocialPost = 5,
        Interview = 6,
        Archive = 7,
        Database = 8,
        Book = 9,
        Article = 10
    }

    public enum SourceStatus
    {
        Draft = 0,
        Active = 1,
        Suspended = 2,
        Archived = 3
    }

    public enum SourceTrustTier
    {
        Unknown = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        Critical = 4
    }

    public enum DocumentType
    {
        Unknown = 0,
        Transcript = 1,
        Article = 2,
        Report = 3,
        WebPage = 4,
        Post = 5,
        Filing = 6,
        Speech = 7,
        Interview = 8,
        Video = 9,
        Audio = 10
    }

    public enum DocumentStatus
    {
        Draft = 0,
        Active = 1,
        Superseded = 2,
        Archived = 3
    }

    public enum EvidenceType
    {
        Quote = 0,
        TranscriptExcerpt = 1,
        DocumentExcerpt = 2,
        Metadata = 3,
        ImageFrame = 4,
        AudioExcerpt = 5
    }

    public enum EvidenceStatus
    {
        Pending = 0,
        Accepted = 1,
        Rejected = 2,
        Archived = 3
    }

    public enum StatementPolarity
    {
        Unknown = 0,
        Affirmative = 1,
        Negative = 2,
        Qualified = 3
    }

    public enum StatementStatus
    {
        Draft = 0,
        Extracted = 1,
        Normalized = 2,
        Eligible = 3,
        Rejected = 4,
        Archived = 5
    }

    public enum ClaimType
    {
        Unknown = 0,
        Factual = 1,
        Temporal = 2,
        Quantitative = 3,
        Categorical = 4,
        Attribution = 5
    }

    public enum ClaimStatus
    {
        Draft = 0,
        Active = 1,
        Disputed = 2,
        Rejected = 3,
        Archived = 4
    }

    public enum ContradictionType
    {
        Unknown = 0,
        Direct = 1,
        Temporal = 2,
        Quantitative = 3,
        Categorical = 4,
        Attribution = 5,
        Scope = 6
    }

    public enum ContradictionSeverity
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Critical = 3
    }

    public enum ContradictionStatus
    {
        Draft = 0,
        Active = 1,
        Resolved = 2,
        Rejected = 3,
        Archived = 4
    }

    public enum CaseType
    {
        Unknown = 0,
        ContradictionReview = 1,
        PublicationDecision = 2,
        Escalation = 3,
        Appeal = 4
    }

    public enum CaseStatus
    {
        Open = 0,
        InReview = 1,
        OnHold = 2,
        Approved = 3,
        Rejected = 4,
        Published = 5,
        Closed = 6
    }

    public enum ScoreTargetType
    {
        Unknown = 0,
        Evidence = 1,
        Statement = 2,
        Claim = 3,
        Contradiction = 4,
        Case = 5,
        Publication = 6
    }

    public enum ConfidenceBand
    {
        VeryLow = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        VeryHigh = 4
    }

    public enum ConfidenceScoreStatus
    {
        Draft = 0,
        Active = 1,
        Superseded = 2,
        Archived = 3
    }

    public enum AgentType
    {
        Ingestion = 0,
        EvidenceEligibility = 1,
        StatementNormalization = 2,
        ClaimExtraction = 3,
        ContradictionDetection = 4,
        ConfidenceCalibration = 5,
        ReviewRouting = 6,
        PublicationPreparation = 7
    }

    public enum AgentRunStatus
    {
        Pending = 0,
        Running = 1,
        Succeeded = 2,
        Failed = 3,
        Cancelled = 4
    }

    public enum ReviewType
    {
        Editorial = 0,
        Legal = 1,
        Policy = 2,
        Quality = 3,
        HumanOverride = 4
    }

    public enum ReviewStatus
    {
        Pending = 0,
        InProgress = 1,
        Completed = 2,
        Cancelled = 3
    }

    public enum ReviewDecision
    {
        None = 0,
        Approved = 1,
        Rejected = 2,
        NeedsChanges = 3,
        Escalated = 4
    }

    public enum PublicationChannel
    {
        Internal = 0,
        Web = 1,
        Api = 2,
        Report = 3,
        Export = 4
    }

    public enum PublicationStatus
    {
        Draft = 0,
        Approved = 1,
        Scheduled = 2,
        Published = 3,
        Retracted = 4,
        Archived = 5
    }
}
