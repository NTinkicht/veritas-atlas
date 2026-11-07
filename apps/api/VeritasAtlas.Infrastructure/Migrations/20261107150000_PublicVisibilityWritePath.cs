using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VeritasAtlas.Infrastructure.Persistence;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    // The generated Designer file is omitted to keep the PR reviewable; the model snapshot stays authoritative.
    [DbContext(typeof(VeritasAtlasDbContext))]
    [Migration("20261107150000_PublicVisibilityWritePath")]
    public partial class PublicVisibilityWritePath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // VER-P0-16 slice 7 (#202): audited, version-checked write path for the public-reader facts.
            migrationBuilder.AddColumn<long>(name: "facts_version", table: "tenant_publication_revisions", type: "bigint", nullable: false, defaultValue: 0L);
            migrationBuilder.AddCheckConstraint(
                name: "ck_tenant_publication_revisions_facts_version_nonnegative", table: "tenant_publication_revisions",
                sql: "facts_version >= 0");
            migrationBuilder.AddCheckConstraint(
                name: "ck_tenant_publication_revisions_facts_restrictive", table: "tenant_publication_revisions",
                sql: "state IN ('Approved','Published') OR (restricted AND NOT superseded AND expires_at_utc IS NULL AND freshness_required AND evidence_currency = 'Unknown' AND NOT historical_label_present AND public_citations IS NULL)");

            migrationBuilder.CreateTable(
                name: "tenant_publication_visibility_changes",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facts_version = table.Column<long>(type: "bigint", nullable: false),
                    revision_version = table.Column<long>(type: "bigint", nullable: false),
                    operation_id = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    request_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    actor_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_publication_visibility_changes", x => new { x.tenant_id, x.revision_id, x.facts_version });
                    table.CheckConstraint("ck_tenant_publication_visibility_changes_digest", "request_digest ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_tenant_publication_visibility_changes_kind", "kind IN ('Set','Reset')");
                    table.CheckConstraint("ck_tenant_publication_visibility_changes_text_not_blank", "length(btrim(operation_id)) > 0 AND length(btrim(actor_id)) > 0 AND length(btrim(actor_role)) > 0");
                    table.CheckConstraint("ck_tenant_publication_visibility_changes_versions", "facts_version >= 1 AND revision_version >= 0");
                    table.ForeignKey(
                        name: "fk_tenant_publication_visibility_changes_tenant_revision",
                        columns: x => new { x.tenant_id, x.revision_id },
                        principalTable: "tenant_publication_revisions",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_tenant_publication_visibility_changes_operation",
                table: "tenant_publication_visibility_changes",
                columns: new[] { "tenant_id", "revision_id", "operation_id" },
                unique: true);

            migrationBuilder.Sql(@"CREATE FUNCTION tenant_publication_visibility_changes_append_only() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  IF TG_OP = 'INSERT' THEN
    RETURN NEW;
  END IF;
  RAISE EXCEPTION 'tenant_publication_visibility_changes is append-only' USING ERRCODE = '55000';
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_publication_visibility_changes_append_only
BEFORE INSERT OR UPDATE OR DELETE ON tenant_publication_visibility_changes
FOR EACH ROW EXECUTE FUNCTION tenant_publication_visibility_changes_append_only();");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_publication_visibility_changes_no_truncate
BEFORE TRUNCATE ON tenant_publication_visibility_changes
FOR EACH STATEMENT EXECUTE FUNCTION tenant_publication_visibility_changes_append_only();");

            // Guard: a lifecycle transition (one step, facts unchanged on the Approved/Published line, or reset to the restrictive
            // defaults with the facts version stepped) or a facts-only update on Approved/Published (lifecycle fields untouched,
            // facts version stepped by one). Nothing else may update a revision.
            migrationBuilder.Sql(@"CREATE OR REPLACE FUNCTION tenant_publication_revisions_guard() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
  facts_same boolean;
BEGIN
  IF TG_OP = 'INSERT' AND NEW.state = 'Draft' AND NEW.version = 0 AND NEW.approved_digest IS NULL
     AND NEW.last_operation_id IS NULL AND NEW.last_action IS NULL AND NEW.facts_version = 0 THEN
    RETURN NEW;
  END IF;
  IF TG_OP = 'UPDATE'
     AND NEW.tenant_id = OLD.tenant_id AND NEW.id = OLD.id AND NEW.author_id = OLD.author_id
     AND NEW.content_digest = OLD.content_digest
     AND NEW.policy_permits_public_display = OLD.policy_permits_public_display THEN
    facts_same := NEW.restricted = OLD.restricted AND NEW.superseded = OLD.superseded
      AND NEW.expires_at_utc IS NOT DISTINCT FROM OLD.expires_at_utc
      AND NEW.freshness_required = OLD.freshness_required AND NEW.evidence_currency = OLD.evidence_currency
      AND NEW.historical_label_present = OLD.historical_label_present
      AND NEW.public_citations IS NOT DISTINCT FROM OLD.public_citations;
    IF NEW.version = OLD.version + 1 AND NEW.last_operation_id IS NOT NULL AND NEW.last_action IS NOT NULL
       AND (OLD.state, NEW.last_action, NEW.state) IN (
         ('Draft','Submit','Submitted'), ('Submitted','RequestChanges','ChangesRequested'),
         ('Submitted','Approve','Approved'), ('ChangesRequested','Revise','Draft'),
         ('Approved','Publish','Published'), ('Approved','Withdraw','Withdrawn'),
         ('Published','Withdraw','Withdrawn'))
       AND ((facts_same AND NEW.facts_version = OLD.facts_version)
            OR (NOT facts_same AND NEW.state NOT IN ('Approved','Published') AND NEW.facts_version = OLD.facts_version + 1)) THEN
      RETURN NEW;
    END IF;
    IF NEW.version = OLD.version AND NEW.state = OLD.state AND NEW.state IN ('Approved','Published')
       AND NEW.approved_digest IS NOT DISTINCT FROM OLD.approved_digest
       AND NEW.last_operation_id IS NOT DISTINCT FROM OLD.last_operation_id
       AND NEW.last_action IS NOT DISTINCT FROM OLD.last_action
       AND NEW.facts_version = OLD.facts_version + 1 THEN
      RETURN NEW;
    END IF;
  END IF;
  RAISE EXCEPTION 'tenant_publication_revisions only allows a Draft v0 insert, one-step lifecycle updates and audited facts updates' USING ERRCODE = '55000';
END;
$$;");

            migrationBuilder.Sql(@"CREATE OR REPLACE FUNCTION tenant_publication_revisions_audit_check() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  IF NEW.version <> OLD.version AND NOT EXISTS (
    SELECT 1 FROM tenant_publication_transitions t
    WHERE t.tenant_id = NEW.tenant_id AND t.revision_id = NEW.id AND t.version = NEW.version
      AND t.operation_id = NEW.last_operation_id AND t.action = NEW.last_action
      AND t.from_state = OLD.state AND t.to_state = NEW.state) THEN
    RAISE EXCEPTION 'tenant_publication_revisions change has no matching audit transition' USING ERRCODE = '55000';
  END IF;
  IF NEW.facts_version <> OLD.facts_version AND NOT EXISTS (
    SELECT 1 FROM tenant_publication_visibility_changes v
    WHERE v.tenant_id = NEW.tenant_id AND v.revision_id = NEW.id AND v.facts_version = NEW.facts_version
      AND v.revision_version = NEW.version) THEN
    RAISE EXCEPTION 'tenant_publication_revisions facts change has no matching audit row' USING ERRCODE = '55000';
  END IF;
  RETURN NULL;
END;
$$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_publication_visibility_changes_no_truncate ON tenant_publication_visibility_changes;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_publication_visibility_changes_append_only ON tenant_publication_visibility_changes;");
            migrationBuilder.DropTable(name: "tenant_publication_visibility_changes");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_publication_visibility_changes_append_only();");
            migrationBuilder.DropCheckConstraint(name: "ck_tenant_publication_revisions_facts_restrictive", table: "tenant_publication_revisions");
            migrationBuilder.DropCheckConstraint(name: "ck_tenant_publication_revisions_facts_version_nonnegative", table: "tenant_publication_revisions");
            // Restore the slice 4 guard and audit check (no facts rules); the facts column goes last because the guard references it.
            migrationBuilder.Sql(@"CREATE OR REPLACE FUNCTION tenant_publication_revisions_guard() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  IF TG_OP = 'INSERT' AND NEW.state = 'Draft' AND NEW.version = 0 AND NEW.approved_digest IS NULL
     AND NEW.last_operation_id IS NULL AND NEW.last_action IS NULL THEN
    RETURN NEW;
  END IF;
  IF TG_OP = 'UPDATE'
     AND NEW.tenant_id = OLD.tenant_id AND NEW.id = OLD.id AND NEW.author_id = OLD.author_id
     AND NEW.content_digest = OLD.content_digest
     AND NEW.policy_permits_public_display = OLD.policy_permits_public_display
     AND NEW.version = OLD.version + 1 AND NEW.last_operation_id IS NOT NULL AND NEW.last_action IS NOT NULL
     AND (OLD.state, NEW.last_action, NEW.state) IN (
       ('Draft','Submit','Submitted'), ('Submitted','RequestChanges','ChangesRequested'),
       ('Submitted','Approve','Approved'), ('ChangesRequested','Revise','Draft'),
       ('Approved','Publish','Published'), ('Approved','Withdraw','Withdrawn'),
       ('Published','Withdraw','Withdrawn')) THEN
    RETURN NEW;
  END IF;
  RAISE EXCEPTION 'tenant_publication_revisions only allows a Draft v0 insert and one-step updates of state and version' USING ERRCODE = '55000';
END;
$$;");
            migrationBuilder.Sql(@"CREATE OR REPLACE FUNCTION tenant_publication_revisions_audit_check() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM tenant_publication_transitions t
    WHERE t.tenant_id = NEW.tenant_id AND t.revision_id = NEW.id AND t.version = NEW.version
      AND t.operation_id = NEW.last_operation_id AND t.action = NEW.last_action
      AND t.from_state = OLD.state AND t.to_state = NEW.state) THEN
    RAISE EXCEPTION 'tenant_publication_revisions change has no matching audit transition' USING ERRCODE = '55000';
  END IF;
  RETURN NULL;
END;
$$;");
            migrationBuilder.DropColumn(name: "facts_version", table: "tenant_publication_revisions");
        }
    }
}
