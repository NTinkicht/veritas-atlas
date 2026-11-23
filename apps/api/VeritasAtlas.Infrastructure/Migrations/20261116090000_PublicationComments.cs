using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VeritasAtlas.Infrastructure.Persistence;

#nullable disable

namespace VeritasAtlas.Infrastructure.Migrations
{
    // The generated Designer file is omitted to keep the PR reviewable; the model snapshot stays authoritative.
    [DbContext(typeof(VeritasAtlasDbContext))]
    [Migration("20261116090000_PublicationComments")]
    public partial class PublicationComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_publication_comments",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_key = table.Column<Guid>(type: "uuid", nullable: false),
                    author_subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_publication_comments", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_tenant_publication_comments_tenant_id_not_empty", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_tenant_publication_comments_client_key_not_empty", "client_key <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_tenant_publication_comments_author_shape", "length(btrim(author_subject)) > 0 AND author_subject !~ '[\\x01-\\x1f\\x7f-\\x9f]'");
                    table.CheckConstraint("ck_tenant_publication_comments_body_shape", "length(btrim(body)) > 0 AND char_length(body) <= 2000 AND body !~ '[\\x01-\\x08\\x0b\\x0c\\x0e-\\x1f\\x7f-\\x9f]'");
                });

            migrationBuilder.CreateIndex(
                name: "ux_tenant_publication_comments_revision_client_key",
                table: "tenant_publication_comments",
                columns: new[] { "tenant_id", "revision_id", "client_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_publication_comments_revision_created",
                table: "tenant_publication_comments",
                columns: new[] { "tenant_id", "revision_id", "created_at_utc" });

            // A comment may only reference a revision of the same tenant, and the history is append-only
            // (VER-P1-02 slice 2): no update, delete or truncate.
            migrationBuilder.Sql(@"CREATE FUNCTION tenant_publication_comments_guard() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  IF TG_OP = 'INSERT' THEN
    IF NOT EXISTS (
      SELECT 1 FROM tenant_publication_revisions r
      WHERE r.tenant_id = NEW.tenant_id AND r.id = NEW.revision_id) THEN
      RAISE EXCEPTION 'publication comment requires a same-tenant revision' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
  END IF;
  RAISE EXCEPTION 'tenant_publication_comments rows are append-only' USING ERRCODE = '55000';
END;
$$;");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_publication_comments_guard
BEFORE INSERT ON tenant_publication_comments
FOR EACH ROW EXECUTE FUNCTION tenant_publication_comments_guard();");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_publication_comments_append_only
BEFORE UPDATE OR DELETE ON tenant_publication_comments
FOR EACH ROW EXECUTE FUNCTION tenant_publication_comments_guard();");
            migrationBuilder.Sql(@"CREATE TRIGGER tr_tenant_publication_comments_no_truncate
BEFORE TRUNCATE ON tenant_publication_comments
FOR EACH STATEMENT EXECUTE FUNCTION tenant_publication_comments_guard();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_publication_comments_no_truncate ON tenant_publication_comments;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_publication_comments_append_only ON tenant_publication_comments;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_tenant_publication_comments_guard ON tenant_publication_comments;");
            migrationBuilder.DropTable(
                name: "tenant_publication_comments");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_publication_comments_guard();");
        }
    }
}
