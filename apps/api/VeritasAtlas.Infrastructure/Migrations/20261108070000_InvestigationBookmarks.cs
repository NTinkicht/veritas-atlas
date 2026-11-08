using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VeritasAtlas.Infrastructure.Persistence;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    // The generated Designer file is omitted to keep the PR reviewable; the model snapshot stays authoritative.
    [DbContext(typeof(VeritasAtlasDbContext))]
    [Migration("20261108070000_InvestigationBookmarks")]
    public partial class InvestigationBookmarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_investigation_bookmarks",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    investigation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_investigation_bookmarks", x => new { x.tenant_id, x.evidence_item_id, x.subject });
                    table.CheckConstraint("ck_tenant_investigation_bookmarks_subject_shape", "length(btrim(subject)) > 0 AND subject !~ '[\\x01-\\x1f\\x7f-\\x9f]'");
                    table.CheckConstraint("ck_tenant_investigation_bookmarks_tenant_id_not_empty", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_investigation_bookmarks_subject",
                table: "tenant_investigation_bookmarks",
                columns: new[] { "tenant_id", "investigation_id", "subject", "created_at_utc" });

            // A row may only reference an evidence item of the same tenant and investigation, and
            // may not be re-pointed (VER-P0-10 slice 5). Deleting a bookmark is allowed.
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_investigation_bookmarks_guard() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  IF TG_OP = 'UPDATE' THEN
    RAISE EXCEPTION 'tenant_investigation_bookmarks rows are immutable until removed' USING ERRCODE = '55000';
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM tenant_investigation_evidence_items i
    WHERE i.tenant_id = NEW.tenant_id AND i.investigation_id = NEW.investigation_id
      AND i.id = NEW.evidence_item_id) THEN
    RAISE EXCEPTION 'bookmark requires an evidence item of the same tenant and investigation' USING ERRCODE = '23514';
  END IF;
  RETURN NEW;
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_investigation_bookmarks_guard
BEFORE INSERT OR UPDATE ON tenant_investigation_bookmarks
FOR EACH ROW EXECUTE FUNCTION tenant_investigation_bookmarks_guard();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_investigation_bookmarks_guard ON tenant_investigation_bookmarks;");

            migrationBuilder.DropTable(
                name: "tenant_investigation_bookmarks");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_investigation_bookmarks_guard();");
        }
    }
}
