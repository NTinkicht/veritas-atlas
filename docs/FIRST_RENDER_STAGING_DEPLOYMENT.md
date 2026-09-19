# First Render staging deployment — governed and human-operated

Source: `NTinkicht/veritas-atlas`. This is NOT an OneCompany dispatch or cutover.
Confirmed Render workspace: NTinkicht (`tea-d9cm2fnavr4c739vvl50`).
Existing HUMAN and lego-teddy services are unrelated and MUST remain untouched.
Dedicated Neon project: **Veritas Atlas Staging**, ID `quiet-bar-44039561`,
PostgreSQL 17, Frankfurt. Never use Project HUMAN's database or credentials.

## Preconditions
- Merge the deployment PR only after exact-head backend/frontend/container CI is
  green and an independent human has approved. Verify branch protection (H1).
- This is a staging-only single-operator login, NOT production SSO/MFA.
- Do not commit or paste secrets, real Neon connection strings, or Render
  environment variables containing credentials into GitHub/OneCompany/chat.
- This Blueprint requests a free Docker API and a free static frontend. If
  Render asks for a paid resource, STOP; do not approve additional spend.
- Auto deploy is OFF. A code push/merge must not automatically release services.

## Initial Blueprint sync — owner action
Render Dashboard → NTinkicht workspace → New → Blueprint. Connect the private
`NTinkicht/veritas-atlas` GitHub repository and select the reviewed `main`
`render.yaml`. The connected Render integration currently cannot create Docker
web services or trigger Blueprint sync, so an owner must initiate the first sync.

Expect only two new Veritas services:
- `veritas-atlas-api-ntinkicht` (free Docker web, Frankfurt; `/health/live`).
- `veritas-atlas-web-ntinkicht` (static; canonical Vite app; SPA rewrite).

Verify actual Render-generated domains before trying login. If a name is
unavailable and the Render URL differs, update API
`Cors__AllowedOrigins__0` to the actual **web** origin and change static-site
`VITE_API_BASE_URL` to the actual **API** origin. Rebuild the static frontend
deliberately because Vite embeds the URL at build time.

On first sync Render should request two `sync: false` API-only secrets:
- `Auth__Bootstrap__Password`: an independent RANDOM 20+ character password,
  recorded only in your password manager. The username is `stagingadmin`.
- `ConnectionStrings__DefaultConnection`: the Npgsql/ADO.NET connection string
  assembled privately from the **Veritas Atlas Staging** Neon Connection
  Details: `Host=<veritas-host>;Port=5432;Database=veritas_atlas;
  Username=<veritas-role>;Password=<veritas-secret>;SSL Mode=Require`.
  These placeholders are NOT literal credential values.

JWT signing secret is created independently by Render using generateValue.
The API is forced to `ASPNETCORE_ENVIRONMENT=Production`, not Development;
the committed development users and development-only diagnostics stay disabled.
The API listens on port 10000.

## Database initialization and proof
No database migration or seed runs automatically in the Docker startup script.
`/health` and `/health/live` currently prove liveness only, NOT database
connectivity. Before using data operations, review EF Core migration IDs and
perform a separately authorized FIRST migration targeting *only* the Veritas
Neon project. Record operator, schema version and recovery/backup evidence in
OneCompany H8. Never infer data readiness from an HTTP 200 health result.

Record both Render service IDs, actual URLs, deployed commit/digest, successful
`GET /health/live`, non-destructive database read, a successful bootstrap
login/`GET /api/v1/auth/me`, rejection of development fixture login, and a
deep-link frontend reload. Do not put bearer tokens or secrets in tickets.

## Rollback
Save deploy IDs and known-good compatible schema before every further release.
With auto deploy disabled, the human operator chooses the previous verified
service deploy on failure; a code rollback is NOT a database rollback. On a
failed first deployment (no previous service), disable or remove only the two
new Veritas services after saving logs. Preserve the new dedicated staging
database for forensic review or delete it only with owner authorization.
Do not change HUMAN or lego-teddy resources. H7/H8 remain blocked until a
reviewed OneCompany target-specific gate transition records the live evidence.

## Reproducible schema migration SQL (no automatic apply)

A dedicated PR-only CI job `Staging migration SQL (review only)` uses the
same checked-out revision and EF Core 10.0.5 to generate the exact idempotent
migration script with no staging Neon/Render secrets. Download the
`veritas-staging-ef-migration-sql` artifact from the successful PR Actions
run and review its SHA-256, expected `__EFMigrationsHistory` updates, tables,
indexes and foreign keys before any database write.

The current source contains migrations `20260401172450_InitialCreate` and
`20260406222302_ContradictionSlice` (the latter has an empty Up method).
The separately provisioned Neon `Veritas Atlas Staging` database was observed
to have no app tables as of the first Render release. A reviewed SQL artifact is
**not approval to apply it**. Only after an explicit owner go/no-go should an
authorized operator apply it to Neon project `quiet-bar-44039561`, database
`veritas_atlas`; confirm `__EFMigrationsHistory`, table counts and
non-destructive API read access. Never run this SQL against Project HUMAN.

Because the initial Neon role credential and staging bootstrap credential were
shared in a chat during setup, rotate BOTH privately in Neon/Render before
enabling staging for real users or importing any data. Do not copy replacement
values to GitHub, OneCompany evidence, source, frontend build variables, or
this chat.
