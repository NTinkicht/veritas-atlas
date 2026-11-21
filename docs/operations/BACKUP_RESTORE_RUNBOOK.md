# Backup and restore runbook (VER-P0-18)

Scope: the Veritas Atlas PostgreSQL database. Backup scheduling and storage are provider/infrastructure concerns and need a separate owner decision before any paid service is used; this runbook defines the procedure and the proof a restore must meet.

## Backup

1. Take a logical backup with `pg_dump --format=custom --no-owner --file <backup>.dump <database>` using a read-only role. Never put the password on the command line; use `PGPASSWORD` or a `.pgpass` file.
2. Store the dump encrypted and outside the database host. It holds tenant evidence and audit data: treat it as production data, never commit it, and never copy it into a test or staging environment.
3. Record the dump time, the `__EFMigrationsHistory` head and the row count of `tenant_audit_events`.

## Restore

1. Create an empty database (never restore over a live one): `CREATE DATABASE <restored>`.
2. `pg_restore --no-owner --exit-on-error --dbname <restored> <backup>.dump`.
3. Do not run `dotnet ef database update` before verifying; a restore must need no further migration.

## Verification (must all hold before the restored copy is used)

- `__EFMigrationsHistory` equals the source.
- `tenant_audit_events` row count and a content checksum equal the source.
- `GetPendingMigrations()` is empty for the restored database.
- The append-only triggers are present: `UPDATE`, `DELETE` and `TRUNCATE` on `tenant_audit_events` fail with SQLSTATE `55000`.
- Tenant-scoped reads still return only the owning tenant's rows.

The automated drill is `BackupRestoreDrillTests` in `tests/VeritasAtlas.Api.Tests`. Run it with `VERITAS_TEST_POSTGRES` pointing at a disposable PostgreSQL (the role needs `CREATEDB`) and `pg_dump`/`pg_restore` on `PATH`; it creates and drops its own `*_drill_src` and `*_drill_restored` databases.

## Rollback

If verification fails, discard the restored database and keep serving from the source; do not patch the restored copy. If a forward migration must be rolled back, restore the last verified backup into a new database, verify it as above, then repoint the application.
