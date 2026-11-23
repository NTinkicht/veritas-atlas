using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VeritasAtlas.Infrastructure.Persistence;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    // The generated Designer file is omitted to keep the PR reviewable; the model snapshot stays authoritative.
    [DbContext(typeof(VeritasAtlasDbContext))]
    [Migration("20261114090000_SavedSearchVersions")]
    public partial class SavedSearchVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "tenant_saved_searches",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenant_saved_searches_version_positive",
                table: "tenant_saved_searches",
                sql: "version >= 1");

            migrationBuilder.CreateTable(
                name: "tenant_saved_search_versions",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    saved_search_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_tenant_saved_search_versions", x => new { x.tenant_id, x.saved_search_id, x.version });
                    table.CheckConstraint("ck_tenant_saved_search_versions_freshness_known", "freshness IS NULL OR freshness IN ('Current', 'Stale', 'Unknown', 'Superseded')");
                    table.CheckConstraint("ck_tenant_saved_search_versions_name_shape", "length(btrim(name)) > 0 AND name = btrim(name) AND name !~ '[\\x01-\\x1f\\x7f-\\x9f]'");
                    table.CheckConstraint("ck_tenant_saved_search_versions_query_shape", "length(btrim(query)) > 0 AND query !~ '[\\x01-\\x1f\\x7f-\\x9f]'");
                    table.CheckConstraint("ck_tenant_saved_search_versions_version_positive", "version >= 1");
                    table.ForeignKey(
                        name: "fk_tenant_saved_search_versions_saved_search",
                        columns: x => new { x.tenant_id, x.saved_search_id },
                        principalTable: "tenant_saved_searches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            // Existing saved searches become version 1 of their own history.
            migrationBuilder.Sql(
                "INSERT INTO tenant_saved_search_versions (tenant_id, saved_search_id, version, name, query, source_entry_id, jurisdiction, source_type, freshness, parser_name, parser_version, subject_term, created_at_utc) " +
                "SELECT tenant_id, id, 1, name, query, source_entry_id, jurisdiction, source_type, freshness, parser_name, parser_version, subject_term, created_at_utc FROM tenant_saved_searches");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_saved_search_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tenant_saved_searches_version_positive",
                table: "tenant_saved_searches");

            migrationBuilder.DropColumn(
                name: "version",
                table: "tenant_saved_searches");
        }
    }
}
