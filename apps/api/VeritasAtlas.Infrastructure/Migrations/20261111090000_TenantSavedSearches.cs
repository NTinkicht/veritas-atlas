using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VeritasAtlas.Infrastructure.Persistence;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    // The generated Designer file is omitted to keep the PR reviewable; the model snapshot stays authoritative.
    [DbContext(typeof(VeritasAtlasDbContext))]
    [Migration("20261111090000_TenantSavedSearches")]
    public partial class TenantSavedSearches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_saved_searches",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    query = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    source_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    jurisdiction = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    source_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    freshness = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    parser_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    parser_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    subject_term = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_saved_searches", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_tenant_saved_searches_freshness_known", "freshness IS NULL OR freshness IN ('Current', 'Stale', 'Unknown', 'Superseded')");
                    table.CheckConstraint("ck_tenant_saved_searches_name_shape", "length(btrim(name)) > 0 AND name = btrim(name) AND name !~ '[\\x01-\\x1f\\x7f-\\x9f]'");
                    table.CheckConstraint("ck_tenant_saved_searches_query_shape", "length(btrim(query)) > 0 AND query !~ '[\\x01-\\x1f\\x7f-\\x9f]'");
                    table.CheckConstraint("ck_tenant_saved_searches_subject_shape", "length(btrim(subject)) > 0 AND subject !~ '[\\x01-\\x1f\\x7f-\\x9f]'");
                    table.CheckConstraint("ck_tenant_saved_searches_tenant_id_not_empty", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_saved_searches_subject",
                table: "tenant_saved_searches",
                columns: new[] { "tenant_id", "subject", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_tenant_saved_searches_name",
                table: "tenant_saved_searches",
                columns: new[] { "tenant_id", "subject", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_saved_searches");
        }
    }
}
