using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VeritasAtlas.Infrastructure.Persistence;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    // The generated Designer file is omitted to keep the PR reviewable; the model snapshot stays authoritative.
    [DbContext(typeof(VeritasAtlasDbContext))]
    [Migration("20261108060000_InvestigationAnnotations")]
    public partial class InvestigationAnnotations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_investigation_annotations",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    investigation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    author_subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_investigation_annotations", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_tenant_investigation_annotations_author_shape", "length(btrim(author_subject)) > 0 AND author_subject !~ '[\\x01-\\x1f\\x7f-\\x9f]'");
                    table.CheckConstraint("ck_tenant_investigation_annotations_tenant_id_not_empty", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_tenant_investigation_annotations_text_shape", "length(text) > 0 AND text = btrim(text) AND text !~ '[\\x01-\\x09\\x0b-\\x1f\\x7f-\\x9f]'");
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_investigation_annotations_item",
                table: "tenant_investigation_annotations",
                columns: new[] { "tenant_id", "evidence_item_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_tenant_investigation_annotations_replay",
                table: "tenant_investigation_annotations",
                columns: new[] { "tenant_id", "evidence_item_id", "author_subject", "text" },
                unique: true);
            // Append-only, and a row may only reference an evidence item of the same tenant and
            // investigation (VER-P0-10 slice 4).
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_investigation_annotations_append_only() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'tenant_investigation_annotations rows are append-only' USING ERRCODE = '55000';
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_investigation_annotations_append_only
BEFORE UPDATE OR DELETE ON tenant_investigation_annotations
FOR EACH ROW EXECUTE FUNCTION tenant_investigation_annotations_append_only();");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_investigation_annotations_no_truncate
BEFORE TRUNCATE ON tenant_investigation_annotations
FOR EACH STATEMENT EXECUTE FUNCTION tenant_investigation_annotations_append_only();");
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_investigation_annotations_guard() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM tenant_investigation_evidence_items i
    WHERE i.tenant_id = NEW.tenant_id AND i.investigation_id = NEW.investigation_id
      AND i.id = NEW.evidence_item_id) THEN
    RAISE EXCEPTION 'annotation requires an evidence item of the same tenant and investigation' USING ERRCODE = '23514';
  END IF;
  RETURN NEW;
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_investigation_annotations_guard
BEFORE INSERT ON tenant_investigation_annotations
FOR EACH ROW EXECUTE FUNCTION tenant_investigation_annotations_guard();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_investigation_annotations_guard ON tenant_investigation_annotations;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_investigation_annotations_no_truncate ON tenant_investigation_annotations;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_investigation_annotations_append_only ON tenant_investigation_annotations;");

            migrationBuilder.DropTable(
                name: "tenant_investigation_annotations");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_investigation_annotations_guard();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_investigation_annotations_append_only();");
        }
    }
}
