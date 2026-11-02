using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VeritasAtlas.Infrastructure.Persistence;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    // The generated Designer file is omitted to keep the PR reviewable; the model snapshot in
    // VeritasAtlasDbContextModelSnapshot.cs stays authoritative.
    [DbContext(typeof(VeritasAtlasDbContext))]
    [Migration("20261102173914_TenantEvidenceVerifications")]
    public partial class TenantEvidenceVerifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_evidence_verifications",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parse_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raw_retrieval_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attestation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_evidence_ref = table.Column<string>(type: "character varying(68)", maxLength: 68, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_evidence_verifications", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_tenant_evidence_verifications_parent_evidence_ref_shape", "parent_evidence_ref ~ '^ev1:[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_tenant_evidence_verifications_tenant_id_not_empty", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "fk_tenant_evidence_verifications_tenant_parse_run",
                        columns: x => new { x.tenant_id, x.parse_run_id },
                        principalTable: "tenant_parse_runs",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_tenant_evidence_verifications_tenant_run",
                table: "tenant_evidence_verifications",
                columns: new[] { "tenant_id", "parse_run_id" },
                unique: true);

            // Append-only, and a row may only prove a parsed_unverified run of the same tenant AND
            // retrieval, through an authenticated attestation of that same retrieval (VER-P0-07
            // slice 3, #174).
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_evidence_verifications_append_only() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'tenant_evidence_verifications rows are append-only' USING ERRCODE = '55000';
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_evidence_verifications_append_only
BEFORE UPDATE OR DELETE ON tenant_evidence_verifications
FOR EACH ROW EXECUTE FUNCTION tenant_evidence_verifications_append_only();");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_evidence_verifications_no_truncate
BEFORE TRUNCATE ON tenant_evidence_verifications
FOR EACH STATEMENT EXECUTE FUNCTION tenant_evidence_verifications_append_only();");
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_evidence_verifications_guard() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM tenant_parse_runs r
    WHERE r.tenant_id = NEW.tenant_id AND r.id = NEW.parse_run_id
      AND r.raw_retrieval_id = NEW.raw_retrieval_id AND r.outcome = 'parsed_unverified') THEN
    RAISE EXCEPTION 'verification must belong to a parsed_unverified run of the same retrieval' USING ERRCODE = '23514';
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM tenant_retrieval_attestations a
    WHERE a.tenant_id = NEW.tenant_id AND a.id = NEW.attestation_id
      AND a.raw_retrieval_id = NEW.raw_retrieval_id AND a.authentication_result = 'authenticated') THEN
    RAISE EXCEPTION 'verification requires an authenticated attestation of the same retrieval' USING ERRCODE = '23514';
  END IF;
  RETURN NEW;
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_evidence_verifications_guard
BEFORE INSERT ON tenant_evidence_verifications
FOR EACH ROW EXECUTE FUNCTION tenant_evidence_verifications_guard();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_evidence_verifications_guard ON tenant_evidence_verifications;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_evidence_verifications_no_truncate ON tenant_evidence_verifications;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_evidence_verifications_append_only ON tenant_evidence_verifications;");

            migrationBuilder.DropTable(
                name: "tenant_evidence_verifications");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_evidence_verifications_guard();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_evidence_verifications_append_only();");
        }
    }
}
