using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VeritasAtlas.Infrastructure.Persistence;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    // The generated Designer file (target model snapshot, ~85 KB) is omitted to keep the PR
    // reviewable; the model snapshot in VeritasAtlasDbContextModelSnapshot.cs stays authoritative.
    [DbContext(typeof(VeritasAtlasDbContext))]
    [Migration("20261102083908_TenantEvidenceChunks")]
    public partial class TenantEvidenceChunks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_evidence_chunks",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parse_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raw_retrieval_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_evidence_ref = table.Column<string>(type: "character varying(68)", maxLength: 68, nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    element_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    text_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_evidence_chunks", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_tenant_evidence_chunks_element_id_not_blank", "length(btrim(element_id)) > 0");
                    table.CheckConstraint("ck_tenant_evidence_chunks_ordinal", "ordinal >= 0");
                    table.CheckConstraint("ck_tenant_evidence_chunks_parent_evidence_ref_shape", "parent_evidence_ref ~ '^ev1:[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_tenant_evidence_chunks_tenant_id_not_empty", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_tenant_evidence_chunks_text_not_blank", "length(btrim(text)) > 0");
                    table.CheckConstraint("ck_tenant_evidence_chunks_text_sha256_shape", "text_sha256 ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_tenant_evidence_chunks_tenant_parse_run",
                        columns: x => new { x.tenant_id, x.parse_run_id },
                        principalTable: "tenant_parse_runs",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_tenant_evidence_chunks_tenant_run_element",
                table: "tenant_evidence_chunks",
                columns: new[] { "tenant_id", "parse_run_id", "element_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_tenant_evidence_chunks_tenant_run_ordinal",
                table: "tenant_evidence_chunks",
                columns: new[] { "tenant_id", "parse_run_id", "ordinal" },
                unique: true);

            // Chunks are append-only and may only hang off a parsed_unverified run of the
            // same tenant AND the same raw retrieval (VER-P0-07 slice 2, #157).
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_evidence_chunks_append_only() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'tenant_evidence_chunks rows are append-only' USING ERRCODE = '55000';
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_evidence_chunks_append_only
BEFORE UPDATE OR DELETE ON tenant_evidence_chunks
FOR EACH ROW EXECUTE FUNCTION tenant_evidence_chunks_append_only();");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_evidence_chunks_no_truncate
BEFORE TRUNCATE ON tenant_evidence_chunks
FOR EACH STATEMENT EXECUTE FUNCTION tenant_evidence_chunks_append_only();");
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_evidence_chunks_run_guard() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM tenant_parse_runs r
    WHERE r.tenant_id = NEW.tenant_id AND r.id = NEW.parse_run_id
      AND r.raw_retrieval_id = NEW.raw_retrieval_id AND r.outcome = 'parsed_unverified') THEN
    RAISE EXCEPTION 'chunk must belong to a parsed_unverified run of the same retrieval' USING ERRCODE = '23514';
  END IF;
  RETURN NEW;
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_evidence_chunks_run_guard
BEFORE INSERT ON tenant_evidence_chunks
FOR EACH ROW EXECUTE FUNCTION tenant_evidence_chunks_run_guard();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_evidence_chunks_run_guard ON tenant_evidence_chunks;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_evidence_chunks_no_truncate ON tenant_evidence_chunks;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_evidence_chunks_append_only ON tenant_evidence_chunks;");

            migrationBuilder.DropTable(
                name: "tenant_evidence_chunks");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_evidence_chunks_run_guard();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_evidence_chunks_append_only();");
        }
    }
}
