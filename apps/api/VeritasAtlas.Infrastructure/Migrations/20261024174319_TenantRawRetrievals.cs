using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TenantRawRetrievals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_raw_retrievals",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    final_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    raw_bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    content_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    retrieved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_raw_retrievals", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_tenant_raw_retrievals_byte_length_matches_bytes", "byte_length = octet_length(raw_bytes)");
                    table.CheckConstraint("ck_tenant_raw_retrievals_content_sha256_matches_bytes", "content_sha256 = encode(sha256(raw_bytes), 'hex')");
                    table.CheckConstraint("ck_tenant_raw_retrievals_final_uri_not_blank", "length(btrim(final_uri)) > 0");
                    table.CheckConstraint("ck_tenant_raw_retrievals_idempotency_key_not_blank", "length(btrim(idempotency_key)) > 0");
                    table.CheckConstraint("ck_tenant_raw_retrievals_no_pre_parser_evidence_reference", "evidence_reference IS NULL");
                    table.CheckConstraint("ck_tenant_raw_retrievals_tenant_id_not_empty", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "fk_tenant_raw_retrievals_tenant_source_entry",
                        columns: x => new { x.tenant_id, x.source_entry_id },
                        principalTable: "tenant_source_registry_entries",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_raw_retrievals_tenant_source_entry",
                table: "tenant_raw_retrievals",
                columns: new[] { "tenant_id", "source_entry_id" });

            migrationBuilder.CreateIndex(
                name: "ux_tenant_raw_retrievals_tenant_idempotency_key",
                table: "tenant_raw_retrievals",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            // Raw retrieval history is immutable (VER-P0-03 slice 2, #87).
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_raw_retrievals_immutable() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'tenant_raw_retrievals rows are immutable' USING ERRCODE = '55000';
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_raw_retrievals_immutable
BEFORE UPDATE OR DELETE ON tenant_raw_retrievals
FOR EACH ROW EXECUTE FUNCTION tenant_raw_retrievals_immutable();");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_raw_retrievals_no_truncate
BEFORE TRUNCATE ON tenant_raw_retrievals
FOR EACH STATEMENT EXECUTE FUNCTION tenant_raw_retrievals_immutable();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_raw_retrievals_no_truncate ON tenant_raw_retrievals;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_raw_retrievals_immutable ON tenant_raw_retrievals;");
            migrationBuilder.DropTable(
                name: "tenant_raw_retrievals");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_raw_retrievals_immutable();");
        }
    }
}
