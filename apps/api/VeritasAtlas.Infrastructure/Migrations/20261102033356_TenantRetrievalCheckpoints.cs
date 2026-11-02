using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TenantRetrievalCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_retrieval_checkpoints",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    raw_retrieval_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_retrieval_checkpoints", x => new { x.tenant_id, x.source_entry_id });
                    table.CheckConstraint("ck_tenant_retrieval_checkpoints_key_not_blank", "length(btrim(key)) > 0");
                    table.CheckConstraint("ck_tenant_retrieval_checkpoints_sequence_positive", "sequence >= 1");
                    table.CheckConstraint("ck_tenant_retrieval_checkpoints_tenant_id_not_empty", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "fk_tenant_retrieval_checkpoints_tenant_source_raw_retrieval",
                        columns: x => new { x.tenant_id, x.source_entry_id, x.raw_retrieval_id },
                        principalTable: "tenant_raw_retrievals",
                        principalColumns: new[] { "tenant_id", "source_entry_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_retrieval_checkpoints_tenant_id_source_entry_id_raw_~",
                table: "tenant_retrieval_checkpoints",
                columns: new[] { "tenant_id", "source_entry_id", "raw_retrieval_id" });

            // The checkpoint is the one mutable ledger row (VER-P0-06 follow-up, #167): only a
            // one-step, non-regressing advance of the same tenant and source is allowed.
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_retrieval_checkpoints_guard() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  IF TG_OP = 'UPDATE'
     AND NEW.tenant_id = OLD.tenant_id
     AND NEW.source_entry_id = OLD.source_entry_id
     AND NEW.sequence = OLD.sequence + 1
     AND NEW.completed_at_utc >= OLD.completed_at_utc THEN
    RETURN NEW;
  END IF;
  RAISE EXCEPTION 'tenant_retrieval_checkpoints only allows a one-step advance' USING ERRCODE = '55000';
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_retrieval_checkpoints_guard
BEFORE UPDATE OR DELETE ON tenant_retrieval_checkpoints
FOR EACH ROW EXECUTE FUNCTION tenant_retrieval_checkpoints_guard();");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_retrieval_checkpoints_no_truncate
BEFORE TRUNCATE ON tenant_retrieval_checkpoints
FOR EACH STATEMENT EXECUTE FUNCTION tenant_retrieval_checkpoints_guard();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_retrieval_checkpoints_no_truncate ON tenant_retrieval_checkpoints;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_retrieval_checkpoints_guard ON tenant_retrieval_checkpoints;");
            migrationBuilder.DropTable(
                name: "tenant_retrieval_checkpoints");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_retrieval_checkpoints_guard();");
        }
    }
}
