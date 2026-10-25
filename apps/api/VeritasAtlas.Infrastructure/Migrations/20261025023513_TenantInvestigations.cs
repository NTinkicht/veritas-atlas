using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TenantInvestigations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_investigations",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    creation_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_by_subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_investigations", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_tenant_investigations_created_by_subject_not_blank", "length(btrim(created_by_subject)) > 0");
                    table.CheckConstraint("ck_tenant_investigations_creation_key_not_blank", "length(btrim(creation_key)) > 0");
                    table.CheckConstraint("ck_tenant_investigations_tenant_id_not_empty", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_tenant_investigations_title_shape", "length(title) > 0 AND title = btrim(title) AND title !~ '[\\x01-\\x1f\\x7f-\\x9f]'");
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_investigations_tenant_created_at",
                table: "tenant_investigations",
                columns: new[] { "tenant_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_tenant_investigations_tenant_creation_key",
                table: "tenant_investigations",
                columns: new[] { "tenant_id", "creation_key" },
                unique: true);

            // Investigation creation records are immutable (VER-P0-03 slice 5, #93).
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_investigations_immutable() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'tenant_investigations rows are immutable' USING ERRCODE = '55000';
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_investigations_immutable
BEFORE UPDATE OR DELETE ON tenant_investigations
FOR EACH ROW EXECUTE FUNCTION tenant_investigations_immutable();");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_investigations_no_truncate
BEFORE TRUNCATE ON tenant_investigations
FOR EACH STATEMENT EXECUTE FUNCTION tenant_investigations_immutable();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_investigations_no_truncate ON tenant_investigations;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_investigations_immutable ON tenant_investigations;");
            migrationBuilder.DropTable(
                name: "tenant_investigations");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_investigations_immutable();");
        }
    }
}
