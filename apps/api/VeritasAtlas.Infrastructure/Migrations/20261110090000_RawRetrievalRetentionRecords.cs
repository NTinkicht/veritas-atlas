using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VeritasAtlas.Infrastructure.Persistence;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    // The generated Designer file is omitted to keep the PR reviewable; the model snapshot stays authoritative.
    [DbContext(typeof(VeritasAtlasDbContext))]
    [Migration("20261110090000_RawRetrievalRetentionRecords")]
    public partial class RawRetrievalRetentionRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_raw_retrieval_retention_records",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raw_retrieval_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    actor_subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    legal_hold = table.Column<bool>(type: "boolean", nullable: false),
                    retain_until_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_raw_retrieval_retention_records", x => new { x.tenant_id, x.raw_retrieval_id, x.version });
                    table.CheckConstraint("ck_tenant_raw_retrieval_retention_records_actor_subject_shape", "length(btrim(actor_subject)) > 0 AND actor_subject !~ '[\\x01-\\x1f\\x7f-\\x9f]'");
                    table.CheckConstraint("ck_tenant_raw_retrieval_retention_records_tenant_id_not_empty", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_tenant_raw_retrieval_retention_records_version_positive", "version >= 1");
                });

            // Append-only: the raw retrieval must exist in the same tenant, versions are gapless from 1
            // and retain_until never moves earlier (or back to NULL) once a window was recorded.
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_raw_retrieval_retention_records_guard() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
  prev_version integer;
  prev_until timestamptz;
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM tenant_raw_retrievals r
    WHERE r.tenant_id = NEW.tenant_id AND r.id = NEW.raw_retrieval_id) THEN
    RAISE EXCEPTION 'a retention record requires a raw retrieval of the same tenant' USING ERRCODE = '23514';
  END IF;
  SELECT e.version, e.retain_until_utc INTO prev_version, prev_until
  FROM tenant_raw_retrieval_retention_records e
  WHERE e.tenant_id = NEW.tenant_id AND e.raw_retrieval_id = NEW.raw_retrieval_id
  ORDER BY e.version DESC LIMIT 1;
  IF NEW.version IS DISTINCT FROM COALESCE(prev_version, 0) + 1 THEN
    RAISE EXCEPTION 'retention record versions must be gapless' USING ERRCODE = '23514';
  END IF;
  IF prev_until IS NOT NULL AND (NEW.retain_until_utc IS NULL OR NEW.retain_until_utc < prev_until) THEN
    RAISE EXCEPTION 'a retention window may only move later' USING ERRCODE = '23514';
  END IF;
  RETURN NEW;
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_raw_retrieval_retention_records_guard
BEFORE INSERT ON tenant_raw_retrieval_retention_records
FOR EACH ROW EXECUTE FUNCTION tenant_raw_retrieval_retention_records_guard();");
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_raw_retrieval_retention_records_immutable() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'tenant_raw_retrieval_retention_records rows are append-only' USING ERRCODE = '55000';
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_raw_retrieval_retention_records_row_immutable
BEFORE UPDATE OR DELETE ON tenant_raw_retrieval_retention_records
FOR EACH ROW EXECUTE FUNCTION tenant_raw_retrieval_retention_records_immutable();");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_raw_retrieval_retention_records_truncate_immutable
BEFORE TRUNCATE ON tenant_raw_retrieval_retention_records
FOR EACH STATEMENT EXECUTE FUNCTION tenant_raw_retrieval_retention_records_immutable();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_raw_retrieval_retention_records");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_raw_retrieval_retention_records_immutable();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_raw_retrieval_retention_records_guard();");
        }
    }
}
