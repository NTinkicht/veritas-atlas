using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VeritasAtlas.Infrastructure.Persistence;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    // The generated Designer file is omitted to keep the PR reviewable; the model snapshot stays authoritative.
    [DbContext(typeof(VeritasAtlasDbContext))]
    [Migration("20261107130000_PublicReaderFacts")]
    public partial class PublicReaderFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // VER-P0-16 slice 6 (#200): defaults are the restrictive values, so every existing and new revision stays hidden
            // from the public reader until a later authorised write path sets the facts truthfully.
            migrationBuilder.AddColumn<bool>(name: "restricted", table: "tenant_publication_revisions", type: "boolean", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<bool>(name: "superseded", table: "tenant_publication_revisions", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<DateTime>(name: "expires_at_utc", table: "tenant_publication_revisions", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "freshness_required", table: "tenant_publication_revisions", type: "boolean", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<string>(name: "evidence_currency", table: "tenant_publication_revisions", type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Unknown");
            migrationBuilder.AddColumn<bool>(name: "historical_label_present", table: "tenant_publication_revisions", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<string>(name: "public_citations", table: "tenant_publication_revisions", type: "jsonb", nullable: true);
            migrationBuilder.AddCheckConstraint(
                name: "ck_tenant_publication_revisions_evidence_currency",
                table: "tenant_publication_revisions",
                sql: "evidence_currency IN ('Current','NonCurrent','Unknown')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "ck_tenant_publication_revisions_evidence_currency", table: "tenant_publication_revisions");
            foreach (var column in new[] { "restricted", "superseded", "expires_at_utc", "freshness_required", "evidence_currency", "historical_label_present", "public_citations" })
            {
                migrationBuilder.DropColumn(name: column, table: "tenant_publication_revisions");
            }
        }
    }
}
