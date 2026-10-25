using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TenantParseRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_parse_runs",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    raw_retrieval_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    parser_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    parser_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    failure_class = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    evidence_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_parse_runs", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_tenant_parse_runs_failure_class", "failure_class IS NULL OR failure_class IN ('unsupported_format', 'malformed', 'oversized', 'parser_error')");
                    table.CheckConstraint("ck_tenant_parse_runs_no_pre_parser_evidence_reference", "evidence_reference IS NULL");
                    table.CheckConstraint("ck_tenant_parse_runs_outcome", "outcome IN ('failed', 'parsed_unverified')");
                    table.CheckConstraint("ck_tenant_parse_runs_outcome_shape", "(outcome = 'failed' AND failure_class IS NOT NULL) OR (outcome = 'parsed_unverified' AND failure_class IS NULL)");
                    table.CheckConstraint("ck_tenant_parse_runs_parser_name_identity", "parser_name ~ '^[a-z0-9][a-z0-9._-]*$'");
                    table.CheckConstraint("ck_tenant_parse_runs_parser_version_identity", "parser_version ~ '^[a-z0-9][a-z0-9._-]*$'");
                    table.CheckConstraint("ck_tenant_parse_runs_run_key_not_blank", "length(btrim(run_key)) > 0");
                    table.CheckConstraint("ck_tenant_parse_runs_tenant_id_not_empty", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "fk_tenant_parse_runs_tenant_raw_retrieval",
                        columns: x => new { x.tenant_id, x.raw_retrieval_id },
                        principalTable: "tenant_raw_retrievals",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_parse_runs_tenant_raw_retrieval_started_at",
                table: "tenant_parse_runs",
                columns: new[] { "tenant_id", "raw_retrieval_id", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_tenant_parse_runs_tenant_run_key",
                table: "tenant_parse_runs",
                columns: new[] { "tenant_id", "run_key" },
                unique: true);

            // Parse-run history is append-only (VER-P0-03 slice 4, #91).
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_parse_runs_append_only() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'tenant_parse_runs rows are append-only' USING ERRCODE = '55000';
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_parse_runs_append_only
BEFORE UPDATE OR DELETE ON tenant_parse_runs
FOR EACH ROW EXECUTE FUNCTION tenant_parse_runs_append_only();");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_parse_runs_no_truncate
BEFORE TRUNCATE ON tenant_parse_runs
FOR EACH STATEMENT EXECUTE FUNCTION tenant_parse_runs_append_only();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_parse_runs_no_truncate ON tenant_parse_runs;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_parse_runs_append_only ON tenant_parse_runs;");
            migrationBuilder.DropTable(
                name: "tenant_parse_runs");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_parse_runs_append_only();");
        }
    }
}
