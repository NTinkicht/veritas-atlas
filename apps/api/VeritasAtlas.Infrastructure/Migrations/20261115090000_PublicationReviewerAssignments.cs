using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VeritasAtlas.Infrastructure.Persistence;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    // The generated Designer file is omitted to keep the PR reviewable; the model snapshot stays authoritative.
    [DbContext(typeof(VeritasAtlasDbContext))]
    [Migration("20261115090000_PublicationReviewerAssignments")]
    public partial class PublicationReviewerAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_publication_reviewer_assignments",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    assigned_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    assigned_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_publication_reviewer_assignments", x => new { x.tenant_id, x.revision_id });
                    table.CheckConstraint("ck_tenant_publication_reviewer_assignments_tenant_id_not_empty", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_tenant_publication_reviewer_assignments_reviewer_shape", "length(btrim(reviewer_subject)) > 0 AND reviewer_subject !~ '[\\x01-\\x1f\\x7f-\\x9f]'");
                    table.CheckConstraint("ck_tenant_publication_reviewer_assignments_assigned_by_shape", "length(btrim(assigned_by)) > 0 AND assigned_by !~ '[\\x01-\\x1f\\x7f-\\x9f]'");
                });

            // A row may only reference a revision of the same tenant whose author is not the assigned reviewer
            // (separation of duties backstop), and its key may not be re-pointed (VER-P1-02 slice 1).
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_publication_reviewer_assignments_guard() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  IF TG_OP = 'UPDATE' AND (NEW.tenant_id <> OLD.tenant_id OR NEW.revision_id <> OLD.revision_id) THEN
    RAISE EXCEPTION 'tenant_publication_reviewer_assignments keys are immutable' USING ERRCODE = '55000';
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM tenant_publication_revisions r
    WHERE r.tenant_id = NEW.tenant_id AND r.id = NEW.revision_id AND r.author_id <> NEW.reviewer_subject) THEN
    RAISE EXCEPTION 'reviewer assignment requires a same-tenant revision not authored by the reviewer' USING ERRCODE = '23514';
  END IF;
  RETURN NEW;
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_publication_reviewer_assignments_guard
BEFORE INSERT OR UPDATE ON tenant_publication_reviewer_assignments
FOR EACH ROW EXECUTE FUNCTION tenant_publication_reviewer_assignments_guard();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_publication_reviewer_assignments_guard ON tenant_publication_reviewer_assignments;");
            migrationBuilder.DropTable(
                name: "tenant_publication_reviewer_assignments");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_publication_reviewer_assignments_guard();");
        }
    }
}
