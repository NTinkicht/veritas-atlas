using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TenantAuditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_audit_events",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    actor_subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_audit_events", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_tenant_audit_events_action_shape", "action ~ '^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)*$'");
                    table.CheckConstraint("ck_tenant_audit_events_actor_subject_shape", "length(btrim(actor_subject)) > 0 AND actor_subject !~ '[\\x01-\\x1f\\x7f-\\x9f]'");
                    table.CheckConstraint("ck_tenant_audit_events_entity_id_shape", "length(btrim(entity_id)) > 0 AND entity_id !~ '[\\x01-\\x1f\\x7f-\\x9f]'");
                    table.CheckConstraint("ck_tenant_audit_events_entity_type_shape", "entity_type ~ '^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)*$'");
                    table.CheckConstraint("ck_tenant_audit_events_event_key_not_blank", "length(btrim(event_key)) > 0");
                    table.CheckConstraint("ck_tenant_audit_events_tenant_id_not_empty", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_audit_events_tenant_entity_occurred_at",
                table: "tenant_audit_events",
                columns: new[] { "tenant_id", "entity_type", "entity_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_tenant_audit_events_tenant_event_key",
                table: "tenant_audit_events",
                columns: new[] { "tenant_id", "event_key" },
                unique: true);

            // Audit events are append-only (VER-P0-03 slice 6, #95).
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_audit_events_append_only() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'tenant_audit_events rows are append-only' USING ERRCODE = '55000';
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_audit_events_append_only
BEFORE UPDATE OR DELETE ON tenant_audit_events
FOR EACH ROW EXECUTE FUNCTION tenant_audit_events_append_only();");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_audit_events_no_truncate
BEFORE TRUNCATE ON tenant_audit_events
FOR EACH STATEMENT EXECUTE FUNCTION tenant_audit_events_append_only();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_audit_events_no_truncate ON tenant_audit_events;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_audit_events_append_only ON tenant_audit_events;");
            migrationBuilder.DropTable(
                name: "tenant_audit_events");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_audit_events_append_only();");
        }
    }
}
