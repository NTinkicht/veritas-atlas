using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TenantRetrievalAttestations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_retrieval_attestations",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    raw_retrieval_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attestation_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    retriever_identity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    publisher_identity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    binding_mechanism = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    binding_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    authentication_result = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    jurisdiction = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_retrieval_attestations", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_tenant_retrieval_attestations_attestation_key_not_blank", "length(btrim(attestation_key)) > 0");
                    table.CheckConstraint("ck_tenant_retrieval_attestations_authentication_result", "authentication_result IN ('authenticated', 'unauthenticated', 'failed')");
                    table.CheckConstraint("ck_tenant_retrieval_attestations_binding_mechanism", "binding_mechanism IN ('source_registry', 'publisher_signature')");
                    table.CheckConstraint("ck_tenant_retrieval_attestations_binding_reference_shape", "length(binding_reference) > 0 AND binding_reference = btrim(binding_reference) AND binding_reference !~ '[\\x01-\\x1f\\x7f-\\x9f]'");
                    table.CheckConstraint("ck_tenant_retrieval_attestations_jurisdiction_code", "jurisdiction ~ '^[a-z0-9][a-z0-9._-]*$'");
                    table.CheckConstraint("ck_tenant_retrieval_attestations_publisher_identity_shape", "length(publisher_identity) > 0 AND publisher_identity = btrim(publisher_identity) AND publisher_identity !~ '[\\x01-\\x1f\\x7f-\\x9f]'");
                    table.CheckConstraint("ck_tenant_retrieval_attestations_retriever_identity_shape", "length(retriever_identity) > 0 AND retriever_identity = btrim(retriever_identity) AND retriever_identity !~ '[\\x01-\\x1f\\x7f-\\x9f]'");
                    table.CheckConstraint("ck_tenant_retrieval_attestations_source_type", "source_type IN ('court_record', 'statute', 'regulation', 'register', 'filing', 'other_public_record')");
                    table.CheckConstraint("ck_tenant_retrieval_attestations_tenant_id_not_empty", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "fk_tenant_retrieval_attestations_tenant_raw_retrieval",
                        columns: x => new { x.tenant_id, x.raw_retrieval_id },
                        principalTable: "tenant_raw_retrievals",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_retrieval_attestations_tenant_raw_retrieval_attested_at",
                table: "tenant_retrieval_attestations",
                columns: new[] { "tenant_id", "raw_retrieval_id", "attested_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_tenant_retrieval_attestations_tenant_attestation_key",
                table: "tenant_retrieval_attestations",
                columns: new[] { "tenant_id", "attestation_key" },
                unique: true);

            // Retrieval attestations are append-only (VER-P0-03 slice 12, #107).
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_retrieval_attestations_append_only() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'tenant_retrieval_attestations rows are append-only' USING ERRCODE = '55000';
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_retrieval_attestations_append_only
BEFORE UPDATE OR DELETE ON tenant_retrieval_attestations
FOR EACH ROW EXECUTE FUNCTION tenant_retrieval_attestations_append_only();");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_retrieval_attestations_no_truncate
BEFORE TRUNCATE ON tenant_retrieval_attestations
FOR EACH STATEMENT EXECUTE FUNCTION tenant_retrieval_attestations_append_only();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_retrieval_attestations_no_truncate ON tenant_retrieval_attestations;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_retrieval_attestations_append_only ON tenant_retrieval_attestations;");
            migrationBuilder.DropTable(
                name: "tenant_retrieval_attestations");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_retrieval_attestations_append_only();");
        }
    }
}
