using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VeritasAtlas.Infrastructure.Persistence;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    // The generated Designer file is omitted to keep the PR reviewable; the model snapshot stays authoritative.
    [DbContext(typeof(VeritasAtlasDbContext))]
    [Migration("20261109090000_InvestigationStateEvents")]
    public partial class InvestigationStateEvents : Migration
    {
        private static readonly string[] GuardedTables =
        [
            "tenant_investigation_evidence_items",
            "tenant_investigation_annotations",
            "tenant_investigation_bookmarks",
            "tenant_investigation_shares"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_investigation_state_events",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    investigation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    actor_subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_investigation_state_events", x => new { x.tenant_id, x.investigation_id, x.version });
                    table.CheckConstraint("ck_tenant_investigation_state_events_actor_subject_shape", "length(btrim(actor_subject)) > 0 AND actor_subject !~ '[\\x01-\\x1f\\x7f-\\x9f]'");
                    table.CheckConstraint("ck_tenant_investigation_state_events_state", "state IN ('closed', 'open')");
                    table.CheckConstraint("ck_tenant_investigation_state_events_tenant_id_not_empty", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_tenant_investigation_state_events_version_positive", "version >= 1");
                });

            // Append-only: the investigation must exist in the tenant, versions are gapless from 1
            // and states alternate starting with closed (an investigation starts open).
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_investigation_state_events_guard() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
  prev_version integer;
  prev_state text;
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM tenant_investigations i
    WHERE i.tenant_id = NEW.tenant_id AND i.id = NEW.investigation_id) THEN
    RAISE EXCEPTION 'a state event requires an investigation of the same tenant' USING ERRCODE = '23514';
  END IF;
  SELECT e.version, e.state INTO prev_version, prev_state
  FROM tenant_investigation_state_events e
  WHERE e.tenant_id = NEW.tenant_id AND e.investigation_id = NEW.investigation_id
  ORDER BY e.version DESC LIMIT 1;
  IF NEW.version IS DISTINCT FROM COALESCE(prev_version, 0) + 1
     OR NEW.state IS NOT DISTINCT FROM COALESCE(prev_state, 'open') THEN
    RAISE EXCEPTION 'state events must follow the previous version with a changed state' USING ERRCODE = '23514';
  END IF;
  RETURN NEW;
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_investigation_state_events_guard
BEFORE INSERT ON tenant_investigation_state_events
FOR EACH ROW EXECUTE FUNCTION tenant_investigation_state_events_guard();");
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_investigation_state_events_immutable() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'tenant_investigation_state_events rows are append-only' USING ERRCODE = '55000';
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_investigation_state_events_row_immutable
BEFORE UPDATE OR DELETE ON tenant_investigation_state_events
FOR EACH ROW EXECUTE FUNCTION tenant_investigation_state_events_immutable();");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_investigation_state_events_truncate_immutable
BEFORE TRUNCATE ON tenant_investigation_state_events
FOR EACH STATEMENT EXECUTE FUNCTION tenant_investigation_state_events_immutable();");

            // Database backstop for the application check: while the latest state event is
            // closed, evidence items, annotations, bookmarks (incl. removal) and shares (incl.
            // revocation) refuse every write. Fail closed with SQLSTATE P0001 (not 55xxx, which Npgsql retries as transient).
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_investigation_closed_guard() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
  t uuid := COALESCE(NEW.tenant_id, OLD.tenant_id);
  i uuid := COALESCE(NEW.investigation_id, OLD.investigation_id);
BEGIN
  IF (SELECT e.state FROM tenant_investigation_state_events e
      WHERE e.tenant_id = t AND e.investigation_id = i
      ORDER BY e.version DESC LIMIT 1) = 'closed' THEN
    RAISE EXCEPTION 'the investigation is closed' USING ERRCODE = 'P0001';
  END IF;
  RETURN COALESCE(NEW, OLD);
END;
$$;");
            foreach (var table in GuardedTables)
            {
                migrationBuilder.Sql($@"CREATE TRIGGER tr_{table}_closed_guard
BEFORE INSERT OR UPDATE OR DELETE ON {table}
FOR EACH ROW EXECUTE FUNCTION tenant_investigation_closed_guard();");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in GuardedTables)
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS tr_{table}_closed_guard ON {table};");
            }

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_investigation_closed_guard();");
            migrationBuilder.DropTable(
                name: "tenant_investigation_state_events");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_investigation_state_events_immutable();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_investigation_state_events_guard();");
        }
    }
}
