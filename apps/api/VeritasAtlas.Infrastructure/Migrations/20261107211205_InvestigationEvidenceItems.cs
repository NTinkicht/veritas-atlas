using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VeritasAtlas.Infrastructure.Persistence;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    // The generated Designer file is omitted to keep the PR reviewable; the model snapshot stays authoritative.
    [DbContext(typeof(VeritasAtlasDbContext))]
    [Migration("20261107211205_InvestigationEvidenceItems")]
    public partial class InvestigationEvidenceItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_investigation_evidence_items",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    investigation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_verification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    collected_by_subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_investigation_evidence_items", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_tenant_investigation_evidence_items_collected_by_shape", "length(btrim(collected_by_subject)) > 0 AND collected_by_subject !~ '[\\x01-\\x1f\\x7f-\\x9f]'");
                    table.CheckConstraint("ck_tenant_investigation_evidence_items_note_shape", "note IS NULL OR (length(note) > 0 AND note = btrim(note) AND note !~ '[\\x01-\\x1f\\x7f-\\x9f]')");
                    table.CheckConstraint("ck_tenant_investigation_evidence_items_tenant_id_not_empty", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                });

            migrationBuilder.CreateIndex(
                name: "ux_tenant_investigation_evidence_items_collected",
                table: "tenant_investigation_evidence_items",
                columns: new[] { "tenant_id", "investigation_id", "evidence_verification_id" },
                unique: true);
            // Append-only, and a row may only reference an investigation and a verified evidence
            // record of the same tenant (VER-P0-10 slice 2, #210).
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_investigation_evidence_items_append_only() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'tenant_investigation_evidence_items rows are append-only' USING ERRCODE = '55000';
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_investigation_evidence_items_append_only
BEFORE UPDATE OR DELETE ON tenant_investigation_evidence_items
FOR EACH ROW EXECUTE FUNCTION tenant_investigation_evidence_items_append_only();");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_investigation_evidence_items_no_truncate
BEFORE TRUNCATE ON tenant_investigation_evidence_items
FOR EACH STATEMENT EXECUTE FUNCTION tenant_investigation_evidence_items_append_only();");
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_investigation_evidence_items_guard() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM tenant_investigations i
    WHERE i.tenant_id = NEW.tenant_id AND i.id = NEW.investigation_id) THEN
    RAISE EXCEPTION 'evidence item requires an investigation of the same tenant' USING ERRCODE = '23514';
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM tenant_evidence_verifications v
    WHERE v.tenant_id = NEW.tenant_id AND v.id = NEW.evidence_verification_id) THEN
    RAISE EXCEPTION 'evidence item requires verified evidence of the same tenant' USING ERRCODE = '23514';
  END IF;
  RETURN NEW;
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_investigation_evidence_items_guard
BEFORE INSERT ON tenant_investigation_evidence_items
FOR EACH ROW EXECUTE FUNCTION tenant_investigation_evidence_items_guard();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_investigation_evidence_items_guard ON tenant_investigation_evidence_items;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_investigation_evidence_items_no_truncate ON tenant_investigation_evidence_items;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_investigation_evidence_items_append_only ON tenant_investigation_evidence_items;");

            migrationBuilder.DropTable(
                name: "tenant_investigation_evidence_items");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_investigation_evidence_items_guard();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_investigation_evidence_items_append_only();");
        }
    }
}
