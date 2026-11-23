using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VeritasAtlas.Infrastructure.Persistence;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    // The generated Designer file is omitted to keep the PR reviewable; the model snapshot stays authoritative.
    [DbContext(typeof(VeritasAtlasDbContext))]
    [Migration("20261113090000_InvestigationFolders")]
    public partial class InvestigationFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_investigation_folders",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    investigation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_investigation_folders", x => new { x.tenant_id, x.investigation_id, x.subject });
                    table.CheckConstraint("ck_tenant_investigation_folders_name_shape", "name ~ '^[A-Za-z0-9_.-]+( [A-Za-z0-9_.-]+)*$'");
                    table.CheckConstraint("ck_tenant_investigation_folders_subject_shape", "length(btrim(subject)) > 0 AND subject !~ '[\\x01-\\x1f\\x7f-\\x9f]'");
                    table.CheckConstraint("ck_tenant_investigation_folders_tenant_id_not_empty", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                });

            // A row may only reference an investigation of the same tenant; its key may not be
            // re-pointed (only the folder name may change). Deleting is allowed (VER-P1-01 slice 3).
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_investigation_folders_guard() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  IF TG_OP = 'UPDATE' AND (NEW.tenant_id <> OLD.tenant_id
      OR NEW.investigation_id <> OLD.investigation_id OR NEW.subject <> OLD.subject) THEN
    RAISE EXCEPTION 'tenant_investigation_folders keys are immutable' USING ERRCODE = '55000';
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM tenant_investigations i
    WHERE i.tenant_id = NEW.tenant_id AND i.id = NEW.investigation_id) THEN
    RAISE EXCEPTION 'folder requires an investigation of the same tenant' USING ERRCODE = '23514';
  END IF;
  RETURN NEW;
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_investigation_folders_guard
BEFORE INSERT OR UPDATE ON tenant_investigation_folders
FOR EACH ROW EXECUTE FUNCTION tenant_investigation_folders_guard();");

            // Closed investigations refuse folder changes (same backstop as tags, bookmarks, annotations).
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_investigation_folders_closed_guard
BEFORE INSERT OR UPDATE OR DELETE ON tenant_investigation_folders
FOR EACH ROW EXECUTE FUNCTION tenant_investigation_closed_guard();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_investigation_folders_closed_guard ON tenant_investigation_folders;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_investigation_folders_guard ON tenant_investigation_folders;");

            migrationBuilder.DropTable(
                name: "tenant_investigation_folders");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_investigation_folders_guard();");
        }
    }
}
