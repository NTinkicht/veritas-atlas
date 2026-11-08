using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VeritasAtlas.Infrastructure.Persistence;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    // The generated Designer file is omitted to keep the PR reviewable; the model snapshot stays authoritative.
    [DbContext(typeof(VeritasAtlasDbContext))]
    [Migration("20261108080000_InvestigationShares")]
    public partial class InvestigationShares : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_investigation_shares",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    investigation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grantee_subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    granted_by_subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    granted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_by_subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_investigation_shares", x => new { x.tenant_id, x.investigation_id, x.grantee_subject });
                    table.CheckConstraint("ck_tenant_investigation_shares_not_self", "grantee_subject <> granted_by_subject");
                    table.CheckConstraint("ck_tenant_investigation_shares_revocation_shape", "(revoked_at_utc IS NULL) = (revoked_by_subject IS NULL) AND (revoked_by_subject IS NULL OR (length(btrim(revoked_by_subject)) > 0 AND revoked_by_subject !~ '[\\x01-\\x1f\\x7f-\\x9f]'))");
                    table.CheckConstraint("ck_tenant_investigation_shares_subject_shape", "length(btrim(grantee_subject)) > 0 AND grantee_subject !~ '[\\x01-\\x1f\\x7f-\\x9f]' AND length(btrim(granted_by_subject)) > 0 AND granted_by_subject !~ '[\\x01-\\x1f\\x7f-\\x9f]'");
                    table.CheckConstraint("ck_tenant_investigation_shares_tenant_id_not_empty", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_investigation_shares_grantee",
                table: "tenant_investigation_shares",
                columns: new[] { "tenant_id", "grantee_subject", "investigation_id" });

            // A grant needs an investigation of the same tenant and may only be made by its creator.
            // Identity columns and the grantor are immutable; only revocation state may change; a
            // grant is never deleted so the revocation history stays (VER-P0-10 slice 6).
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_investigation_shares_guard() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  IF TG_OP = 'UPDATE' THEN
    IF NEW.tenant_id IS DISTINCT FROM OLD.tenant_id
       OR NEW.investigation_id IS DISTINCT FROM OLD.investigation_id
       OR NEW.grantee_subject IS DISTINCT FROM OLD.grantee_subject
       OR NEW.granted_by_subject IS DISTINCT FROM OLD.granted_by_subject THEN
      RAISE EXCEPTION 'tenant_investigation_shares identity columns are immutable' USING ERRCODE = '55000';
    END IF;
    RETURN NEW;
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM tenant_investigations i
    WHERE i.tenant_id = NEW.tenant_id AND i.id = NEW.investigation_id
      AND i.created_by_subject = NEW.granted_by_subject) THEN
    RAISE EXCEPTION 'a share requires an investigation of the same tenant granted by its creator' USING ERRCODE = '23514';
  END IF;
  RETURN NEW;
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_investigation_shares_guard
BEFORE INSERT OR UPDATE ON tenant_investigation_shares
FOR EACH ROW EXECUTE FUNCTION tenant_investigation_shares_guard();");
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_investigation_shares_no_delete() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'tenant_investigation_shares rows are never deleted; revoke instead' USING ERRCODE = '55000';
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_investigation_shares_no_delete
BEFORE DELETE OR TRUNCATE ON tenant_investigation_shares
FOR EACH STATEMENT EXECUTE FUNCTION tenant_investigation_shares_no_delete();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_investigation_shares_no_delete ON tenant_investigation_shares;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_investigation_shares_guard ON tenant_investigation_shares;");

            migrationBuilder.DropTable(
                name: "tenant_investigation_shares");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_investigation_shares_no_delete();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_investigation_shares_guard();");
        }
    }
}
