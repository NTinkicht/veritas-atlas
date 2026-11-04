using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TenantPublicationRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_publication_revisions",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_digest = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    approved_digest = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    policy_permits_public_display = table.Column<bool>(type: "boolean", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    last_operation_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    last_action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_publication_revisions", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_tenant_publication_revisions_author_not_blank", "length(btrim(author_id)) > 0");
                    table.CheckConstraint("ck_tenant_publication_revisions_digest_not_blank", "length(btrim(content_digest)) > 0");
                    table.CheckConstraint("ck_tenant_publication_revisions_published_bound", "state NOT IN ('Approved','Published') OR (approved_digest IS NOT NULL AND approved_digest = content_digest)");
                    table.CheckConstraint("ck_tenant_publication_revisions_published_permitted", "state <> 'Published' OR policy_permits_public_display");
                    table.CheckConstraint("ck_tenant_publication_revisions_state", "state IN ('Draft','Submitted','ChangesRequested','Approved','Published','Withdrawn')");
                    table.CheckConstraint("ck_tenant_publication_revisions_tenant_id_not_empty", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_tenant_publication_revisions_version_nonnegative", "version >= 0");
                });

            migrationBuilder.CreateTable(
                name: "tenant_publication_transitions",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    operation_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    from_state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    to_state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    actor_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_publication_transitions", x => new { x.tenant_id, x.revision_id, x.version });
                    table.CheckConstraint("ck_tenant_publication_transitions_text_not_blank", "length(btrim(operation_id)) > 0 AND length(btrim(actor_id)) > 0 AND length(btrim(actor_role)) > 0");
                    table.CheckConstraint("ck_tenant_publication_transitions_version_positive", "version >= 1");
                    table.ForeignKey(
                        name: "fk_tenant_publication_transitions_tenant_revision",
                        columns: x => new { x.tenant_id, x.revision_id },
                        principalTable: "tenant_publication_revisions",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_tenant_publication_transitions_operation",
                table: "tenant_publication_transitions",
                columns: new[] { "tenant_id", "revision_id", "operation_id" },
                unique: true);

            // Second line of defence behind the store (VER-P0-16 slice 4, #196): revisions start as Draft v0, advance one
            // version at a time with immutable identity and content, and are never deleted; transitions are append-only.
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_publication_revisions_guard() RETURNS trigger
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
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_publication_revisions_guard
BEFORE INSERT OR UPDATE OR DELETE ON tenant_publication_revisions
FOR EACH ROW EXECUTE FUNCTION tenant_publication_revisions_guard();");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_publication_revisions_no_truncate
BEFORE TRUNCATE ON tenant_publication_revisions
FOR EACH STATEMENT EXECUTE FUNCTION tenant_publication_revisions_guard();");
            // Every revision change must be backed, at commit, by the audit row of the exact transition it claims.
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_publication_revisions_audit_check() RETURNS trigger
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
            migrationBuilder.Sql(@"CREATE CONSTRAINT TRIGGER tr_tenant_publication_revisions_audit_check
AFTER UPDATE ON tenant_publication_revisions
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION tenant_publication_revisions_audit_check();");
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_publication_transitions_append_only() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  IF TG_OP = 'INSERT' THEN
    RETURN NEW;
  END IF;
  RAISE EXCEPTION 'tenant_publication_transitions is append-only' USING ERRCODE = '55000';
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_publication_transitions_append_only
BEFORE INSERT OR UPDATE OR DELETE ON tenant_publication_transitions
FOR EACH ROW EXECUTE FUNCTION tenant_publication_transitions_append_only();");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_publication_transitions_no_truncate
BEFORE TRUNCATE ON tenant_publication_transitions
FOR EACH STATEMENT EXECUTE FUNCTION tenant_publication_transitions_append_only();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_publication_revisions_audit_check ON tenant_publication_revisions;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_publication_transitions_no_truncate ON tenant_publication_transitions;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_publication_transitions_append_only ON tenant_publication_transitions;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_publication_revisions_no_truncate ON tenant_publication_revisions;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_publication_revisions_guard ON tenant_publication_revisions;");
            migrationBuilder.DropTable(
                name: "tenant_publication_transitions");

            migrationBuilder.DropTable(
                name: "tenant_publication_revisions");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_publication_transitions_append_only();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_publication_revisions_guard();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_publication_revisions_audit_check();");
        }
    }
}
