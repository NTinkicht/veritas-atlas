using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "persons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    given_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    middle_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    family_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    display_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Nationality = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BornAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_persons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TrustTier = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reference_external_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    reference_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    reference_domain = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    reference_language_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "aliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LanguageCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_aliases_persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubjectPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cases_persons_SubjectPersonId",
                        column: x => x.SubjectPersonId,
                        principalTable: "persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LanguageCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetrievedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_documents_sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agent_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AgentName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AgentVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    InputHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OutputHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_runs_cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evidences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LanguageCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    span = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evidences_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_evidences_sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "confidence_scores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    score_value = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    Band = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Explanation = table.Column<string>(type: "text", nullable: true),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    AgentRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_confidence_scores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_confidence_scores_agent_runs_AgentRunId",
                        column: x => x.AgentRunId,
                        principalTable: "agent_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_confidence_scores_cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Decision = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reviewer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reviews_agent_runs_AgentRunId",
                        column: x => x.AgentRunId,
                        principalTable: "agent_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reviews_cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    text_raw = table.Column<string>(type: "text", nullable: false),
                    text_normalized = table.Column<string>(type: "text", nullable: false),
                    Polarity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Topic = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Predicate = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ObjectValue = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    StatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_statements_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_statements_evidences_EvidenceId",
                        column: x => x.EvidenceId,
                        principalTable: "evidences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_statements_persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "publications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: true),
                    Channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Slug = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Body = table.Column<string>(type: "text", nullable: true),
                    ScheduledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetractedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_publications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_publications_cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_publications_reviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "claims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StatementId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Topic = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    NormalizedText = table.Column<string>(type: "text", nullable: false),
                    IsMaterial = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_claims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_claims_cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_claims_persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_claims_statements_StatementId",
                        column: x => x.StatementId,
                        principalTable: "statements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contradictions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeftClaimId = table.Column<Guid>(type: "uuid", nullable: false),
                    RightClaimId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    Rationale = table.Column<string>(type: "text", nullable: true),
                    ConfidenceScoreId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contradictions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contradictions_cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contradictions_claims_LeftClaimId",
                        column: x => x.LeftClaimId,
                        principalTable: "claims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contradictions_claims_RightClaimId",
                        column: x => x.RightClaimId,
                        principalTable: "claims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contradictions_confidence_scores_ConfidenceScoreId",
                        column: x => x.ConfidenceScoreId,
                        principalTable: "confidence_scores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_runs_AgentName",
                table: "agent_runs",
                column: "AgentName");

            migrationBuilder.CreateIndex(
                name: "IX_agent_runs_CaseId",
                table: "agent_runs",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_runs_Status",
                table: "agent_runs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_aliases_PersonId",
                table: "aliases",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_aliases_PersonId_Value",
                table: "aliases",
                columns: new[] { "PersonId", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cases_SubjectPersonId",
                table: "cases",
                column: "SubjectPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_claims_CaseId",
                table: "claims",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_claims_PersonId",
                table: "claims",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_claims_StatementId",
                table: "claims",
                column: "StatementId");

            migrationBuilder.CreateIndex(
                name: "IX_claims_Topic",
                table: "claims",
                column: "Topic");

            migrationBuilder.CreateIndex(
                name: "IX_confidence_scores_AgentRunId",
                table: "confidence_scores",
                column: "AgentRunId");

            migrationBuilder.CreateIndex(
                name: "IX_confidence_scores_CaseId",
                table: "confidence_scores",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_confidence_scores_TargetType_TargetId",
                table: "confidence_scores",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_contradictions_CaseId",
                table: "contradictions",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_contradictions_ConfidenceScoreId",
                table: "contradictions",
                column: "ConfidenceScoreId");

            migrationBuilder.CreateIndex(
                name: "IX_contradictions_LeftClaimId_RightClaimId",
                table: "contradictions",
                columns: new[] { "LeftClaimId", "RightClaimId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contradictions_RightClaimId",
                table: "contradictions",
                column: "RightClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_contradictions_Type",
                table: "contradictions",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_documents_ExternalId",
                table: "documents",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_documents_PublishedAtUtc",
                table: "documents",
                column: "PublishedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_documents_SourceId",
                table: "documents",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_evidences_ContentHash",
                table: "evidences",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_evidences_DocumentId",
                table: "evidences",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_evidences_SourceId",
                table: "evidences",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_publications_CaseId",
                table: "publications",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_publications_ReviewId",
                table: "publications",
                column: "ReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_publications_Slug",
                table: "publications",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_publications_Status",
                table: "publications",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_reviews_AgentRunId",
                table: "reviews",
                column: "AgentRunId");

            migrationBuilder.CreateIndex(
                name: "IX_reviews_CaseId",
                table: "reviews",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_reviews_Reviewer",
                table: "reviews",
                column: "Reviewer");

            migrationBuilder.CreateIndex(
                name: "IX_statements_DocumentId",
                table: "statements",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_statements_EvidenceId",
                table: "statements",
                column: "EvidenceId");

            migrationBuilder.CreateIndex(
                name: "IX_statements_PersonId",
                table: "statements",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_statements_Topic",
                table: "statements",
                column: "Topic");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aliases");

            migrationBuilder.DropTable(
                name: "contradictions");

            migrationBuilder.DropTable(
                name: "publications");

            migrationBuilder.DropTable(
                name: "claims");

            migrationBuilder.DropTable(
                name: "confidence_scores");

            migrationBuilder.DropTable(
                name: "reviews");

            migrationBuilder.DropTable(
                name: "statements");

            migrationBuilder.DropTable(
                name: "agent_runs");

            migrationBuilder.DropTable(
                name: "evidences");

            migrationBuilder.DropTable(
                name: "cases");

            migrationBuilder.DropTable(
                name: "documents");

            migrationBuilder.DropTable(
                name: "persons");

            migrationBuilder.DropTable(
                name: "sources");
        }
    }
}
