using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TenantSourceFetchPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_source_fetch_policies",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_registry_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    policy_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    jurisdiction = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    allowed_hosts = table.Column<string[]>(type: "text[]", nullable: false),
                    allowed_schemes = table.Column<string[]>(type: "text[]", nullable: false),
                    allowed_ports = table.Column<int[]>(type: "integer[]", nullable: false),
                    max_redirects = table.Column<int>(type: "integer", nullable: false),
                    plaintext_http_exception = table.Column<bool>(type: "boolean", nullable: false),
                    widening_approved = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_source_fetch_policies", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_tenant_source_fetch_policies_allowed_hosts", "array_ndims(allowed_hosts) = 1 AND cardinality(allowed_hosts) >= 1 AND array_position(allowed_hosts, NULL) IS NULL AND cardinality(allowed_hosts) <= 32 AND array_to_string(allowed_hosts, ',') ~ '^[a-z0-9][a-z0-9.-]*(,[a-z0-9][a-z0-9.-]*)*$' AND cardinality(string_to_array(array_to_string(allowed_hosts, ','), ',')) = cardinality(allowed_hosts)");
                    table.CheckConstraint("ck_tenant_source_fetch_policies_allowed_ports", "array_ndims(allowed_ports) = 1 AND cardinality(allowed_ports) >= 1 AND array_position(allowed_ports, NULL) IS NULL AND 1 <= ALL (allowed_ports) AND 65535 >= ALL (allowed_ports)");
                    table.CheckConstraint("ck_tenant_source_fetch_policies_allowed_schemes", "array_ndims(allowed_schemes) = 1 AND cardinality(allowed_schemes) >= 1 AND array_position(allowed_schemes, NULL) IS NULL AND cardinality(allowed_schemes) <= 2 AND allowed_schemes <@ ARRAY['https', 'http']::text[]");
                    table.CheckConstraint("ck_tenant_source_fetch_policies_http_requires_exception", "NOT ('http' = ANY (allowed_schemes)) OR plaintext_http_exception");
                    table.CheckConstraint("ck_tenant_source_fetch_policies_jurisdiction_shape", "jurisdiction ~ '^[a-z0-9][a-z0-9-]{0,63}$'");
                    table.CheckConstraint("ck_tenant_source_fetch_policies_max_redirects_range", "max_redirects BETWEEN 0 AND 10");
                    table.CheckConstraint("ck_tenant_source_fetch_policies_policy_version_shape", "policy_version ~ '^[a-z0-9][a-z0-9._-]{0,63}$'");
                    table.CheckConstraint("ck_tenant_source_fetch_policies_revision_positive", "revision >= 1");
                    table.CheckConstraint("ck_tenant_source_fetch_policies_source_type_shape", "source_type ~ '^[a-z][a-z0-9_]{0,63}$'");
                    table.CheckConstraint("ck_tenant_source_fetch_policies_tenant_id_not_empty", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "fk_tenant_source_fetch_policies_tenant_source_registry_entry",
                        columns: x => new { x.tenant_id, x.source_registry_entry_id },
                        principalTable: "tenant_source_registry_entries",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_tenant_source_fetch_policies_tenant_source_policy_version",
                table: "tenant_source_fetch_policies",
                columns: new[] { "tenant_id", "source_registry_entry_id", "policy_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_tenant_source_fetch_policies_tenant_source_revision",
                table: "tenant_source_fetch_policies",
                columns: new[] { "tenant_id", "source_registry_entry_id", "revision" },
                unique: true);

            // Fetch policy revisions are append-only (VER-P0-04 slice 5a, #119):
            // a change is a new revision, never an in-place edit.
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_source_fetch_policies_append_only() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'tenant_source_fetch_policies rows are append-only' USING ERRCODE = '55000';
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_source_fetch_policies_append_only
BEFORE UPDATE OR DELETE ON tenant_source_fetch_policies
FOR EACH ROW EXECUTE FUNCTION tenant_source_fetch_policies_append_only();");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_source_fetch_policies_no_truncate
BEFORE TRUNCATE ON tenant_source_fetch_policies
FOR EACH STATEMENT EXECUTE FUNCTION tenant_source_fetch_policies_append_only();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_source_fetch_policies_no_truncate ON tenant_source_fetch_policies;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_source_fetch_policies_append_only ON tenant_source_fetch_policies;");
            migrationBuilder.DropTable(
                name: "tenant_source_fetch_policies");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_source_fetch_policies_append_only();");
        }
    }
}
