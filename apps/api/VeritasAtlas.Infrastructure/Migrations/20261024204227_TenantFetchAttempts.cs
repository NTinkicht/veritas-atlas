using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TenantFetchAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_tenant_raw_retrievals_tenant_source_entry_id",
                table: "tenant_raw_retrievals",
                columns: new[] { "tenant_id", "source_entry_id", "id" });

            migrationBuilder.CreateTable(
                name: "tenant_fetch_attempts",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    requested_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    failure_class = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    http_status = table.Column<int>(type: "integer", nullable: true),
                    raw_retrieval_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attempted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_fetch_attempts", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_tenant_fetch_attempts_attempt_key_not_blank", "length(btrim(attempt_key)) > 0");
                    table.CheckConstraint("ck_tenant_fetch_attempts_failure_class", "failure_class IS NULL OR failure_class IN ('network', 'timeout', 'http_status', 'size_limit', 'policy_denied')");
                    table.CheckConstraint("ck_tenant_fetch_attempts_http_status_failure_has_status", "failure_class IS DISTINCT FROM 'http_status' OR http_status IS NOT NULL");
                    table.CheckConstraint("ck_tenant_fetch_attempts_http_status_range", "http_status IS NULL OR http_status BETWEEN 100 AND 599");
                    table.CheckConstraint("ck_tenant_fetch_attempts_outcome", "outcome IN ('succeeded', 'failed')");
                    table.CheckConstraint("ck_tenant_fetch_attempts_outcome_shape", "(outcome = 'succeeded' AND raw_retrieval_id IS NOT NULL AND failure_class IS NULL) OR (outcome = 'failed' AND raw_retrieval_id IS NULL AND failure_class IS NOT NULL)");
                    table.CheckConstraint("ck_tenant_fetch_attempts_requested_uri_not_blank", "length(btrim(requested_uri)) > 0");
                    table.CheckConstraint("ck_tenant_fetch_attempts_tenant_id_not_empty", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "fk_tenant_fetch_attempts_tenant_source_entry",
                        columns: x => new { x.tenant_id, x.source_entry_id },
                        principalTable: "tenant_source_registry_entries",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_fetch_attempts_tenant_source_raw_retrieval",
                        columns: x => new { x.tenant_id, x.source_entry_id, x.raw_retrieval_id },
                        principalTable: "tenant_raw_retrievals",
                        principalColumns: new[] { "tenant_id", "source_entry_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_fetch_attempts_tenant_source_attempted_at",
                table: "tenant_fetch_attempts",
                columns: new[] { "tenant_id", "source_entry_id", "attempted_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_fetch_attempts_tenant_source_raw_retrieval",
                table: "tenant_fetch_attempts",
                columns: new[] { "tenant_id", "source_entry_id", "raw_retrieval_id" });

            migrationBuilder.CreateIndex(
                name: "ux_tenant_fetch_attempts_tenant_attempt_key",
                table: "tenant_fetch_attempts",
                columns: new[] { "tenant_id", "attempt_key" },
                unique: true);

            // Fetch-attempt history is append-only (VER-P0-03 slice 3, #89).
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_fetch_attempts_append_only() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'tenant_fetch_attempts rows are append-only' USING ERRCODE = '55000';
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_fetch_attempts_append_only
BEFORE UPDATE OR DELETE ON tenant_fetch_attempts
FOR EACH ROW EXECUTE FUNCTION tenant_fetch_attempts_append_only();");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_fetch_attempts_no_truncate
BEFORE TRUNCATE ON tenant_fetch_attempts
FOR EACH STATEMENT EXECUTE FUNCTION tenant_fetch_attempts_append_only();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_fetch_attempts_no_truncate ON tenant_fetch_attempts;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_fetch_attempts_append_only ON tenant_fetch_attempts;");
            migrationBuilder.DropTable(
                name: "tenant_fetch_attempts");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_fetch_attempts_append_only();");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_tenant_raw_retrievals_tenant_source_entry_id",
                table: "tenant_raw_retrievals");
        }
    }
}
